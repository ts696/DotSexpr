using System;
using System.Buffers;
using System.Buffers.Text;
using System.Text;
using DotSexpr.Serializer;

namespace DotSexpr.Emitter;

public struct SexprEmitter
{
    private const int IntegerBatchSize = 32;
    private const int MaxEscapedAsciiStringLength = 64;

    public readonly struct EncodedSymbol
    {
        private readonly byte[]? _bytes;

        private EncodedSymbol(byte[] bytes)
        {
            _bytes = bytes;
        }

        public ReadOnlySpan<byte> Utf8Value => _bytes;

        public static EncodedSymbol Encode(string value)
        {
            if (value == null)
            {
                throw SexprSerializerException.ForSerialization("A symbol name cannot be null.");
            }
            try
            {
                var bytes = Utf8.GetBytes(value);
                ValidateSymbol(bytes);
                return new EncodedSymbol(bytes);
            }
            catch (EncoderFallbackException exception)
            {
                throw SexprSerializerException.ForSerialization("The symbol contains invalid UTF-16.", exception);
            }
        }
    }

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
        var buffer = BeginValue(1, out var written);
        buffer[0] = (byte)'(';
        _writer.Advance(written);
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
            var text = value.AsSpan();
            var firstEscape = IndexOfStringSpecialCharacter(text);
            if (firstEscape >= 0 && text.Length <= MaxEscapedAsciiStringLength && TryWriteEscapedAsciiString(text))
            {
                return;
            }
            var length = checked(Utf8.GetByteCount(text) + 2);
            for (var i = firstEscape; i >= 0;)
            {
                length = checked(length + (text[i] is '"' or '\\' or '\b' or '\f' or '\n' or '\r' or '\t' ? 1 : 5));
                var next = IndexOfStringSpecialCharacter(text.Slice(i + 1));
                i = next < 0 ? -1 : i + 1 + next;
            }

            var buffer = BeginValue(length, out var written);
            buffer[0] = (byte)'"';
            var position = 1;
            var start = 0;
            for (var i = firstEscape; i >= 0;)
            {
                position += Utf8.GetBytes(text.Slice(start, i - start), buffer.Slice(position));
                position += WriteEscape(text[i], buffer.Slice(position));
                start = i + 1;
                var next = IndexOfStringSpecialCharacter(text.Slice(start));
                i = next < 0 ? -1 : start + next;
            }
            position += Utf8.GetBytes(text.Slice(start), buffer.Slice(position));
            buffer[position] = (byte)'"';
            _writer.Advance(written);
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
        ValidateSymbol(value);
        WriteValueBytes(value);
    }

    public void WriteSymbol(EncodedSymbol value)
    {
        var bytes = value.Utf8Value;
        if (bytes.IsEmpty)
        {
            throw SexprSerializerException.ForSerialization("Initialize the encoded symbol before writing.");
        }
        WriteValueBytes(bytes);
    }

    private static void ValidateSymbol(ReadOnlySpan<byte> value)
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
        foreach (var character in value)
        {
            if (character <= (byte)' ' || character is (byte)'(' or (byte)')' or (byte)';' or (byte)'"')
            {
                throw SexprSerializerException.ForSerialization("A symbol cannot contain whitespace, control characters, parentheses, semicolons, or quotes.");
            }
        }
    }

    public void WriteNumber(int value)
    {
        Span<byte> bytes = stackalloc byte[32];
        Utf8Formatter.TryFormat(value, bytes, out var written);
        WriteValueBytes(bytes.Slice(0, written));
    }

    public void WriteNumber(long value)
    {
        Span<byte> bytes = stackalloc byte[32];
        Utf8Formatter.TryFormat(value, bytes, out var written);
        WriteValueBytes(bytes.Slice(0, written));
    }

    internal void WriteNumbers(ReadOnlySpan<int> values)
    {
        const int maxBytesPerValue = 12;
        while (!values.IsEmpty)
        {
            var count = Math.Min(values.Length, IntegerBatchSize);
            var buffer = BeginValue(count * maxBytesPerValue, out var reserved);
            var position = 0;
            for (var i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    buffer[position++] = (byte)' ';
                }
                Utf8Formatter.TryFormat(values[i], buffer.Slice(position), out var written);
                position += written;
            }
            _writer.Advance(reserved - buffer.Length + position);
            values = values.Slice(count);
        }
    }

    internal void WriteNumbers(ReadOnlySpan<long> values)
    {
        const int maxBytesPerValue = 21;
        while (!values.IsEmpty)
        {
            var count = Math.Min(values.Length, IntegerBatchSize);
            var buffer = BeginValue(count * maxBytesPerValue, out var reserved);
            var position = 0;
            for (var i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    buffer[position++] = (byte)' ';
                }
                Utf8Formatter.TryFormat(values[i], buffer.Slice(position), out var written);
                position += written;
            }
            _writer.Advance(reserved - buffer.Length + position);
            values = values.Slice(count);
        }
    }

    public void WriteNumber(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            throw SexprSerializerException.ForSerialization("Non-finite numbers cannot be represented as numeric S-expressions.");
        }
        Span<byte> bytes = stackalloc byte[64];
        Utf8Formatter.TryFormat(value, bytes, out var written);
        WriteValueBytes(bytes.Slice(0, written));
    }

    public void WriteNumber(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw SexprSerializerException.ForSerialization("Non-finite numbers cannot be represented as numeric S-expressions.");
        }
        Span<byte> bytes = stackalloc byte[64];
        Utf8Formatter.TryFormat(value, bytes, out var written);
        WriteValueBytes(bytes.Slice(0, written));
    }

    /// <summary>Checks that exactly one complete root value was emitted.</summary>
    public void Complete()
    {
        if (!_hasRoot || _depth != 0)
        {
            throw SexprSerializerException.ForSerialization("The formatter must write exactly one complete S-expression.");
        }
    }

    private Span<byte> BeginValue(int length, out int written)
    {
        if (_writer == null)
        {
            throw new InvalidOperationException("Initialize the emitter with an IBufferWriter<byte> before writing.");
        }
        var separator = _depth > 0 && _needsSeparator;
        if (_depth == 0)
        {
            if (_hasRoot)
            {
                throw SexprSerializerException.ForSerialization("Only one top-level expression can be written.");
            }
            _hasRoot = true;
        }
        _needsSeparator = true;
        written = checked(length + (separator ? 1 : 0));
        var buffer = _writer.GetSpan(written).Slice(0, written);
        if (separator)
        {
            buffer[0] = (byte)' ';
            return buffer.Slice(1);
        }
        return buffer;
    }

    private void WriteValueBytes(ReadOnlySpan<byte> bytes)
    {
        var buffer = BeginValue(bytes.Length, out var written);
        bytes.CopyTo(buffer);
        _writer.Advance(written);
    }

    private bool TryWriteEscapedAsciiString(ReadOnlySpan<char> text)
    {
        var length = text.Length + 2;
        foreach (var character in text)
        {
            if (character > 0x7f)
            {
                return false;
            }
            if (character < ' ' || character is '"' or '\\')
            {
                length += character is '"' or '\\' or '\b' or '\f' or '\n' or '\r' or '\t' ? 1 : 5;
            }
        }
        var buffer = BeginValue(length, out var written);
        buffer[0] = (byte)'"';
        var position = 1;
        foreach (var character in text)
        {
            if (character < ' ' || character is '"' or '\\')
            {
                position += WriteEscape(character, buffer.Slice(position));
            }
            else
            {
                buffer[position++] = (byte)character;
            }
        }
        buffer[position] = (byte)'"';
        _writer.Advance(written);
        return true;
    }

    private static int IndexOfStringSpecialCharacter(ReadOnlySpan<char> text)
    {
#if NET8_0_OR_GREATER
        return text.IndexOfAny(StringSpecialCharacters);
#else
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] < ' ' || text[i] is '"' or '\\')
            {
                return i;
            }
        }
        return -1;
#endif
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

    private static int WriteEscape(char value, Span<byte> buffer)
    {
        buffer[0] = (byte)'\\';
        buffer[1] = value switch
        {
            '"' => (byte)'"',
            '\\' => (byte)'\\',
            '\b' => (byte)'b',
            '\f' => (byte)'f',
            '\n' => (byte)'n',
            '\r' => (byte)'r',
            '\t' => (byte)'t',
            _ => (byte)'u'
        };
        if (buffer[1] != (byte)'u')
        {
            return 2;
        }
        ReadOnlySpan<byte> hex = "0123456789abcdef"u8;
        buffer[2] = (byte)'0';
        buffer[3] = (byte)'0';
        buffer[4] = hex[value >> 4];
        buffer[5] = hex[value & 15];
        return 6;
    }

    private void WriteByte(byte value)
    {
        _writer.GetSpan(1)[0] = value;
        _writer.Advance(1);
    }
}
