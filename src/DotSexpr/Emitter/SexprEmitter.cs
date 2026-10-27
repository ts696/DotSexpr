using System;
using System.Buffers;
using System.Buffers.Text;
using System.Text;
using DotSexpr.Internal;
using DotSexpr.Serializer;

namespace DotSexpr.Emitter;

public struct SexprEmitter
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
#if NET8_0_OR_GREATER
    private static readonly SearchValues<char> StringSpecialCharacters = CreateStringSpecialCharacters();
#endif
    private readonly IBufferWriter<byte> _writer;
    private readonly int _maxDepth;
    private int _depth;
    private bool _hasRoot;
    private bool _needsSeparator;

    public SexprEmitter(IBufferWriter<byte> writer, int maxDepth = 64)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        if (maxDepth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDepth));
        }
        _maxDepth = maxDepth;
        _depth = 0;
        _hasRoot = false;
        _needsSeparator = false;
    }

    public void BeginList()
    {
        if (_depth >= _maxDepth)
        {
            throw SexprSerializerException.ForSerialization($"Serialization exceeds the maximum depth of {_maxDepth}.");
        }
        BeginValue();
        WriteByte((byte)'(');
        _depth++;
        _needsSeparator = false;
    }

    public void EndList()
    {
        if (_depth == 0)
        {
            throw SexprSerializerException.ForSerialization("There is no open list to close.");
        }
        WriteByte((byte)')');
        _depth--;
        _needsSeparator = true;
    }

    public void WriteString(string value)
    {
        if (value == null)
        {
            throw SexprSerializerException.ForSerialization("Null strings cannot be represented as an S-expression.");
        }
        try
        {
            Utf8.GetByteCount(value.AsSpan());
            BeginValue();
            WriteByte((byte)'"');
            var start = 0;
            for (var i = 0; i < value.Length; i++)
            {
                var character = value[i];
                if (character >= ' ' && character != '"' && character != '\\')
                {
#if NET8_0_OR_GREATER
                    // Amortize the search over long plain runs, not escape-heavy text.
                    if (i - start >= 16 && value.Length - i >= 16)
                    {
                        var found = value.AsSpan(i).IndexOfAny(StringSpecialCharacters);
                        if (found < 0)
                        {
                            break;
                        }
                        i += found;
                        character = value[i];
                    }
                    else
#endif
                    {
                        continue;
                    }
                }
                WriteText(value.AsSpan(start, i - start));
                switch (character)
                {
                    case '"': WriteBytes("\\\""u8); break;
                    case '\\': WriteBytes("\\\\"u8); break;
                    case '\b': WriteBytes("\\b"u8); break;
                    case '\f': WriteBytes("\\f"u8); break;
                    case '\n': WriteBytes("\\n"u8); break;
                    case '\r': WriteBytes("\\r"u8); break;
                    case '\t': WriteBytes("\\t"u8); break;
                    default: WriteControlCharacter(character); break;
                }
                start = i + 1;
            }
            WriteText(value.AsSpan(start));
            WriteByte((byte)'"');
        }
        catch (EncoderFallbackException exception)
        {
            throw SexprSerializerException.ForSerialization("The string contains invalid UTF-16.", exception);
        }
    }

    public void WriteSymbol(string value)
    {
        if (value == null)
        {
            throw SexprSerializerException.ForSerialization("A symbol name cannot be null.");
        }
        byte[]? rental = null;
        try
        {
            var length = Utf8.GetByteCount(value.AsSpan());
            Span<byte> bytes = length <= 256 ? stackalloc byte[256] : (rental = ArrayPool<byte>.Shared.Rent(length));
            var written = Utf8.GetBytes(value.AsSpan(), bytes);
            WriteSymbol(bytes.Slice(0, written));
        }
        catch (EncoderFallbackException exception)
        {
            throw SexprSerializerException.ForSerialization("The symbol contains invalid UTF-16.", exception);
        }
        finally
        {
            if (rental != null)
            {
                ArrayPool<byte>.Shared.Return(rental);
            }
        }
    }

    public void WriteSymbol(ReadOnlySpan<byte> value)
    {
        try
        {
            Utf8.GetCharCount(value);
        }
        catch (DecoderFallbackException exception)
        {
            throw SexprSerializerException.ForSerialization("The symbol contains invalid UTF-8.", exception);
        }
        if (value.IsEmpty || value.StartsWith("#|"u8) || SDocument.IsNumber(value))
        {
            throw SexprSerializerException.ForSerialization("A symbol must be non-empty and must not be a number or a block comment.");
        }
        if (SymbolSyntax.IndexOfInvalidCharacter(value) >= 0)
        {
            throw SexprSerializerException.ForSerialization("A symbol cannot contain whitespace, control characters, parentheses, semicolons, or quotes.");
        }
        BeginValue();
        WriteBytes(value);
    }

    public void WriteNumber(int value)
    {
        Span<byte> bytes = stackalloc byte[32];
        Utf8Formatter.TryFormat(value, bytes, out var written);
        WriteNumberBytes(bytes.Slice(0, written));
    }

    public void WriteNumber(long value)
    {
        Span<byte> bytes = stackalloc byte[32];
        Utf8Formatter.TryFormat(value, bytes, out var written);
        WriteNumberBytes(bytes.Slice(0, written));
    }

    public void WriteNumber(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            throw SexprSerializerException.ForSerialization("Non-finite numbers cannot be represented as numeric S-expressions.");
        }
        Span<byte> bytes = stackalloc byte[64];
        Utf8Formatter.TryFormat(value, bytes, out var written);
        WriteNumberBytes(bytes.Slice(0, written));
    }

    public void WriteNumber(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw SexprSerializerException.ForSerialization("Non-finite numbers cannot be represented as numeric S-expressions.");
        }
        Span<byte> bytes = stackalloc byte[64];
        Utf8Formatter.TryFormat(value, bytes, out var written);
        WriteNumberBytes(bytes.Slice(0, written));
    }

    /// <summary>Checks that exactly one complete root value was emitted.</summary>
    public void Complete()
    {
        if (!_hasRoot || _depth != 0)
        {
            throw SexprSerializerException.ForSerialization("The formatter must write exactly one complete S-expression.");
        }
    }

    private void BeginValue()
    {
        if (_writer == null)
        {
            throw new InvalidOperationException("Initialize the emitter with an IBufferWriter<byte> before writing.");
        }
        if (_depth == 0)
        {
            if (_hasRoot)
            {
                throw SexprSerializerException.ForSerialization("Only one top-level expression can be written.");
            }
            _hasRoot = true;
        }
        else if (_needsSeparator)
        {
            WriteByte((byte)' ');
        }
        _needsSeparator = true;
    }

    private void WriteNumberBytes(ReadOnlySpan<byte> bytes)
    {
        BeginValue();
        WriteBytes(bytes);
    }

    private void WriteText(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            return;
        }
        var length = Utf8.GetByteCount(text);
        var written = Utf8.GetBytes(text, _writer.GetSpan(length));
        _writer.Advance(written);
    }

#if NET8_0_OR_GREATER
    private static SearchValues<char> CreateStringSpecialCharacters()
    {
        Span<char> values = stackalloc char[34];
        for (var i = 0; i < 32; i++)
        {
            values[i] = (char)i;
        }
        values[32] = '"';
        values[33] = '\\';
        return SearchValues.Create(values);
    }
#endif

    private void WriteControlCharacter(char value)
    {
        ReadOnlySpan<byte> hex = "0123456789abcdef"u8;
        Span<byte> bytes = stackalloc byte[6];
        bytes[0] = (byte)'\\';
        bytes[1] = (byte)'u';
        bytes[2] = (byte)'0';
        bytes[3] = (byte)'0';
        bytes[4] = hex[value >> 4];
        bytes[5] = hex[value & 15];
        WriteBytes(bytes);
    }

    private void WriteByte(byte value)
    {
        _writer.GetSpan(1)[0] = value;
        _writer.Advance(1);
    }

    private void WriteBytes(ReadOnlySpan<byte> bytes)
    {
        bytes.CopyTo(_writer.GetSpan(bytes.Length));
        _writer.Advance(bytes.Length);
    }
}
