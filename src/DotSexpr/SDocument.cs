using System;
using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using DotSexpr.Internal;

namespace DotSexpr;

public readonly record struct SDocumentOptions
{
    public int MaxDepth { get; init; }
}

public class SDocument : IDisposable
{
    private const int UnterminatedString = -1;
    private const int InvalidStringUtf8 = -2;
    private const int InvalidStringContent = -3;
    private const int RootId = 0;
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
#if NET8_0_OR_GREATER
    private static readonly SearchValues<byte> StringSpecialCharacters = CreateStringSpecialCharacters();
#endif
    private SToken[] _tokens;
    private ReadOnlyMemory<byte> _bytes;
    private int _disposed;
    private readonly int _maxDepth;

    public SNode RootNode
    {
        get
        {
            ThrowIfDisposed();
            return new(this, RootId);
        }
    }

    public ReadOnlyMemory<byte> Source
    {
        get
        {
            ThrowIfDisposed();
            return _bytes;
        }
    }

    public ReadOnlyMemory<byte> GetRawUtf8(SToken token)
    {
        ThrowIfDisposed();
        return _bytes.Slice(token.Offset, token.Length);
    }

    private SDocument(int bytesLength, SDocumentOptions options)
    {
        if (options.MaxDepth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxDepth must be zero or greater.");
        }

        _maxDepth = options.MaxDepth;
        var capacity = Math.Clamp(bytesLength / 8, 16, 2048);
        _tokens = ArrayPool<SToken>.Shared.Rent(capacity);
    }

    internal SToken GetToken(int id)
    {
        ThrowIfDisposed();
        return _tokens[id];
    }

    internal int GetChildId(int id, int index)
    {
        var token = GetToken(id);
        if (!token.IsList)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        var currentId = id + 1;
        if (index == 0)
        {
            return currentId;
        }

        var counter = 0;
        while (counter < index)
        {
            var current = GetToken(currentId);
            currentId = current.IsList
                ? current.EndIndex
                : currentId + 1;

            counter++;
        }

        return currentId;
    }

    public string GetSymbol(SToken token)
    {
        ThrowIfDisposed();
        if (!token.IsSymbol)
        {
            throw new ArgumentOutOfRangeException(nameof(token));
        }

        return Encoding.UTF8.GetString(_bytes.Slice(token.Offset, token.Length).Span);
    }

    public string GetString(SToken token)
    {
        ThrowIfDisposed();
        if (!token.IsString)
        {
            throw new ArgumentOutOfRangeException(nameof(token));
        }

        var span = _bytes.Slice(token.Offset + 1, token.Length - 2).Span;
        if (token.IsStringEscaped)
        {
            byte[]? workRental = null;
            try
            {
                var work = span.Length < 256 ? stackalloc byte[span.Length] : default;
                if (work.Length == 0)
                {
                    workRental = ArrayPool<byte>.Shared.Rent(span.Length);
                    work = workRental.AsSpan();
                }

                var index = 0;
                var sourceIndex = 0;
                while (sourceIndex < span.Length)
                {
                    var remaining = span.Slice(sourceIndex);
                    var plainLength = remaining.IndexOf((byte)'\\');
                    if (plainLength < 0)
                    {
                        plainLength = remaining.Length;
                    }

                    remaining.Slice(0, plainLength).CopyTo(work.Slice(index));
                    sourceIndex += plainLength;
                    index += plainLength;
                    if (sourceIndex < span.Length)
                    {
                        index += ReadStringEscape(span, ref sourceIndex, work.Slice(index));
                    }
                }

                var result = Encoding.UTF8.GetString(work.Slice(0, index));
                return result;
            }
            finally
            {
                if (workRental != null)
                {
                    ArrayPool<byte>.Shared.Return(workRental);
                }
            }
        }

        var str = Encoding.UTF8.GetString(_bytes.Slice(token.Offset + 1, token.Length - 2).Span);
        return str;
    }

    public int GetInt32(SToken token)
    {
        ThrowIfDisposed();
        if (!token.IsNumber)
        {
            throw new ArgumentOutOfRangeException(nameof(token));
        }

        var bytes = _bytes.Slice(token.Offset, token.Length).Span;
        if (Utf8Parser.TryParse(bytes, out int value, out var bytesConsumed) && bytesConsumed == bytes.Length)
        {
            return value;
        }

#if NET8_0_OR_GREATER
        return int.Parse(bytes, NumberStyles.Float, CultureInfo.InvariantCulture);
#else
        return int.Parse(Encoding.UTF8.GetString(bytes), NumberStyles.Float, CultureInfo.InvariantCulture);
#endif
    }

    public long GetInt64(SToken token)
    {
        ThrowIfDisposed();
        if (!token.IsNumber)
        {
            throw new ArgumentOutOfRangeException(nameof(token));
        }

        var bytes = _bytes.Slice(token.Offset, token.Length).Span;
        if (Utf8Parser.TryParse(bytes, out long value, out var bytesConsumed) && bytesConsumed == bytes.Length)
        {
            return value;
        }

#if NET8_0_OR_GREATER
        return long.Parse(bytes, NumberStyles.Float, CultureInfo.InvariantCulture);
#else
        return long.Parse(Encoding.UTF8.GetString(bytes), NumberStyles.Float, CultureInfo.InvariantCulture);
#endif
    }

    public float GetSingle(SToken token)
    {
        ThrowIfDisposed();
        if (!token.IsNumber)
        {
            throw new ArgumentOutOfRangeException(nameof(token));
        }

        Utf8Parser.TryParse(_bytes.Slice(token.Offset, token.Length).Span, out float value, out int _);
        return value;
    }

    public double GetDouble(SToken token)
    {
        ThrowIfDisposed();
        if (!token.IsNumber)
        {
            throw new ArgumentOutOfRangeException(nameof(token));
        }

        Utf8Parser.TryParse(_bytes.Slice(token.Offset, token.Length).Span, out double value, out int _);
        return value;
    }

    public bool ValueEquals(SToken token, ReadOnlySpan<byte> value)
    {
        ThrowIfDisposed();
        if (token.IsString)
        {
            if (token.IsStringEscaped)
            {
                var source = _bytes
                    .Slice(token.Offset + 1, token.Length - 2)
                    .Span;

                var valueIndex = 0;
                var sourceIndex = 0;
                Span<byte> decoded = stackalloc byte[4];
                while (sourceIndex < source.Length)
                {
                    var remaining = source.Slice(sourceIndex);
                    var plainLength = remaining.IndexOf((byte)'\\');
                    if (plainLength < 0)
                    {
                        plainLength = remaining.Length;
                    }

                    if (plainLength > value.Length - valueIndex ||
                        !remaining.Slice(0, plainLength).SequenceEqual(value.Slice(valueIndex, plainLength)))
                    {
                        return false;
                    }

                    sourceIndex += plainLength;
                    valueIndex += plainLength;
                    if (sourceIndex == source.Length)
                    {
                        break;
                    }

                    var length = ReadStringEscape(source, ref sourceIndex, decoded);
                    if (length > value.Length - valueIndex ||
                        !decoded.Slice(0, length).SequenceEqual(value.Slice(valueIndex, length)))
                    {
                        return false;
                    }

                    valueIndex += length;
                }

                return valueIndex == value.Length;
            }

            return _bytes.Slice(token.Offset + 1, token.Length - 2).Span.SequenceEqual(value);
        }

        return _bytes.Slice(token.Offset, token.Length).Span.SequenceEqual(value);
    }

    public static bool TryParse(ReadOnlySpan<byte> bytes, [NotNullWhen(true)] out SDocument? doc)
    {
        return TryParse(bytes, default, out doc);
    }

    public static bool TryParse(ReadOnlySpan<byte> bytes, SDocumentOptions options, [NotNullWhen(true)] out SDocument? doc)
    {
        var document = new SDocument(bytes.Length, options);
        try
        {
            document.ParseSexpr(bytes);
            document._bytes = bytes.ToArray();
        }
        catch (Exception ex)
        {
            document.Dispose();
            doc = null;
            if (ex is SexprException)
            {
                return false;
            }
            throw;
        }

        doc = document;
        return true;
    }

    public static bool TryParse(string text, [NotNullWhen(true)] out SDocument? doc)
    {
        return TryParse(text, default, out doc);
    }

    public static bool TryParse(string text, SDocumentOptions options, [NotNullWhen(true)] out SDocument? doc)
    {
        ArgumentGuard.ThrowIfNull(text);
        var bytes = Encoding.UTF8.GetBytes(text);
        var document = new SDocument(bytes.Length, options);
        try
        {
            document.ParseSexpr(bytes, validateUtf8: false);
            document._bytes = bytes;
        }
        catch (Exception ex)
        {
            document.Dispose();
            doc = null;
            if (ex is SexprException)
            {
                return false;
            }
            throw;
        }

        doc = document;
        return true;
    }

    public static SDocument Parse(ReadOnlySpan<byte> bytes)
    {
        return Parse(bytes, default);
    }

    public static SDocument Parse(ReadOnlySpan<byte> bytes, SDocumentOptions options)
    {
        return Parse(bytes, options, allowMultipleDocuments: false);
    }

    /// <summary>Parses zero or more top-level expressions as children of a synthetic root list.</summary>
    public static SDocument ParseMultiple(ReadOnlySpan<byte> bytes, SDocumentOptions options = default)
    {
        return Parse(bytes, options, allowMultipleDocuments: true);
    }

    private static SDocument Parse(ReadOnlySpan<byte> bytes, SDocumentOptions options, bool allowMultipleDocuments)
    {
        var document = new SDocument(bytes.Length, options);
        try
        {
            document.ParseSexpr(bytes, allowMultipleDocuments: allowMultipleDocuments);
            document._bytes = bytes.ToArray();
        }
        catch
        {
            document.Dispose();
            throw;
        }

        return document;
    }

    public static SDocument Parse(string text)
    {
        return Parse(text, default);
    }

    public static SDocument Parse(string text, SDocumentOptions options)
    {
        return Parse(text, options, allowMultipleDocuments: false);
    }

    /// <summary>Parses zero or more top-level expressions as children of a synthetic root list.</summary>
    public static SDocument ParseMultiple(string text, SDocumentOptions options = default)
    {
        return Parse(text, options, allowMultipleDocuments: true);
    }

    private static SDocument Parse(string text, SDocumentOptions options, bool allowMultipleDocuments)
    {
        ArgumentGuard.ThrowIfNull(text);
        var bytes = Encoding.UTF8.GetBytes(text);

        var document = new SDocument(bytes.Length, options);
        try
        {
            document.ParseSexpr(bytes, validateUtf8: false, allowMultipleDocuments: allowMultipleDocuments);
            document._bytes = bytes;
        }
        catch
        {
            document.Dispose();
            throw;
        }

        return document;
    }

    private void ParseSexpr(ReadOnlySpan<byte> bytes, bool validateUtf8 = true, bool allowMultipleDocuments = false)
    {
        int[]? listStackRental = null;
        Span<int> listStack = stackalloc int[16];

        var bufferIndex = 0;
        var elementId = 0;
        var listId = -1;
        var topLevel = 0;

        if (allowMultipleDocuments)
        {
            MakeToken(STokenKind.List, RootId, 0);
            elementId++;
        }

        try
        {
            while (bufferIndex < bytes.Length)
            {
                var c = bytes[bufferIndex];
                if (c == ';')
                {
                    var end = bytes.Slice(bufferIndex).IndexOfAny("\r\n"u8);
                    bufferIndex += (end < 0 ? bytes.Length - bufferIndex : end) - 1;
                }
                else if (c == '#' && bufferIndex + 1 < bytes.Length && bytes[bufferIndex + 1] == '|')
                {
                    var length = GetBlockCommentLength(bytes.Slice(bufferIndex));
                    if (length < 0)
                    {
                        throw new SexprParserException("Unterminated block comment.", bytes, bufferIndex);
                    }

                    bufferIndex += length - 1;
                }
                else if (c == '(')
                {
                    if (_maxDepth > 0 && listId + 2 > _maxDepth)
                    {
                        throw new SexprParserException("Maximum list depth exceeded.", bytes, bufferIndex);
                    }

                    MakeToken(STokenKind.List, elementId, bufferIndex);
                    if (listStack.Length <= listId + 1)
                    {
                        var buffer = ArrayPool<int>.Shared.Rent(listStack.Length * 2);
                        listStack.CopyTo(buffer);
                        if (listStackRental != null)
                        {
                            ArrayPool<int>.Shared.Return(listStackRental);
                        }
                        listStack = buffer;
                        listStackRental = buffer;
                    }

                    if (listId >= 0)
                    {
                        IncrementCount(listStack[listId]);
                    }
                    else
                    {
                        topLevel++;
                    }

                    listStack[++listId] = elementId;
                    elementId++;
                }
                else if (c == ')')
                {
                    if (listId < 0)
                    {
                        throw new SexprParserException("Unexpected ')': no matching '(' was found.", bytes, bufferIndex);
                    }

                    ref var list = ref _tokens[listStack[listId]];
                    list.Length = bufferIndex - list.Offset + 1;
                    list.EndIndex = elementId;
                    listId--;
                }
                else if (c == '\n' || c == '\r' || c == '\t' || c == ' ')
                {
                }
                else if (c == '"')
                {
                    var strLength = GetStringLength(bytes.Slice(bufferIndex), out var hasEscape, validateUtf8);
                    if (strLength == InvalidStringUtf8)
                    {
                        throw new SexprParserException("Invalid UTF-8 in string literal.", bytes, bufferIndex);
                    }

                    if (strLength == InvalidStringContent)
                    {
                        throw new SexprParserException("Invalid escape sequence or unescaped control character in string literal.", bytes, bufferIndex);
                    }

                    if (strLength == UnterminatedString)
                    {
                        throw new SexprParserException("Unterminated string literal.", bytes, bufferIndex);
                    }

                    var stringKind = hasEscape ? StringKind.Escaped : StringKind.Plane;
                    MakeToken(STokenKind.String, elementId, bufferIndex, (int)stringKind, strLength);
                    bufferIndex += strLength - 1;
                    elementId++;

                    if (listId >= 0)
                    {
                        IncrementCount(listStack[listId]);
                    }
                    else
                    {
                        topLevel++;
                    }
                }
                else
                {
                    var elemLength = GetSymbolLength(bytes.Slice(bufferIndex), out var invalidCharacterIndex);
                    if (invalidCharacterIndex >= 0)
                    {
                        throw new SexprParserException("A symbol cannot contain control characters or quotes.",
                            bytes, bufferIndex + invalidCharacterIndex);
                    }

                    var elemSpan = bytes.Slice(bufferIndex, elemLength);
                    var kind = IsNumber(elemSpan) ? STokenKind.Number : STokenKind.Symbol;
                    if (validateUtf8 && kind == STokenKind.Symbol)
                    {
                        try
                        {
                            StrictUtf8.GetCharCount(elemSpan);
                        }
                        catch (DecoderFallbackException)
                        {
                            throw new SexprParserException("Invalid UTF-8 in symbol.", bytes, bufferIndex);
                        }
                    }
                    MakeToken(kind, elementId, bufferIndex, length: elemLength);
                    bufferIndex += elemLength - 1;
                    elementId++;

                    if (listId >= 0)
                    {
                        IncrementCount(listStack[listId]);
                    }
                    else
                    {
                        topLevel++;
                    }
                }

                bufferIndex++;

                if (!allowMultipleDocuments && topLevel > 1)
                {
                    throw new SexprParserException("Only one top-level expression is allowed.", bytes, GetToken(elementId - 1).Offset);
                }
            }

            if (!allowMultipleDocuments && topLevel == 0)
            {
                throw new SexprParserException("Unexpected end of file.", bytes, bytes.Length);
            }

            if (listId >= 0)
            {
                throw new SexprParserException("Unexpected end of input: expected ')'.", bytes, bytes.Length);
            }

            if (allowMultipleDocuments)
            {
                ref var root = ref _tokens[RootId];
                root.Count = topLevel;
                root.Length = bytes.Length;
                root.EndIndex = elementId;
            }
        }
        finally
        {
            if (listStackRental != null)
            {
                ArrayPool<int>.Shared.Return(listStackRental);
            }
        }
    }

    internal static bool IsNumber(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return false;
        }

        var index = bytes[0] == '-' ? 1 : 0;
        if (index == bytes.Length)
        {
            return false;
        }

        if (bytes[index] == '0')
        {
            index++;
        }
        else
        {
            if (bytes[index] < '1' || bytes[index] > '9')
            {
                return false;
            }

            index = SkipDigits(bytes, index);
        }

        if (index < bytes.Length && bytes[index] == '.')
        {
            var start = ++index;
            index = SkipDigits(bytes, index);
            if (index == start)
            {
                return false;
            }
        }

        if (index < bytes.Length && (bytes[index] == 'e' || bytes[index] == 'E'))
        {
            index++;
            if (index < bytes.Length && (bytes[index] == '+' || bytes[index] == '-'))
            {
                index++;
            }

            var start = index;
            index = SkipDigits(bytes, index);
            if (index == start)
            {
                return false;
            }
        }

        return index == bytes.Length;

        static int SkipDigits(ReadOnlySpan<byte> source, int index)
        {
            while (index < source.Length && source[index] >= '0' && source[index] <= '9')
            {
                index++;
            }

            return index;
        }
    }

    private static int GetSymbolLength(ReadOnlySpan<byte> bytes, out int invalidCharacterIndex)
    {
        var idx = SymbolSyntax.IndexOfInvalidCharacter(bytes);
        invalidCharacterIndex = -1;
        if (idx >= 0)
        {
            var character = bytes[idx];
            if (character == '"' || (character < ' ' && character is not ((byte)'\n' or (byte)'\r' or (byte)'\t')))
            {
                invalidCharacterIndex = idx;
            }
        }
        return idx >= 0 ? idx : bytes.Length;
    }

    private static int GetStringLength(ReadOnlySpan<byte> bytes, out bool hasEscape, bool validateUtf8)
    {
        hasEscape = false;
        var current = 1;
        Span<byte> decoded = stackalloc byte[4];

        while (current < bytes.Length)
        {
#if NET8_0_OR_GREATER
            var found = bytes.Slice(current).IndexOfAny(StringSpecialCharacters);
            if (found < 0)
            {
                return UnterminatedString;
            }

            current += found;
#endif
            var value = bytes[current];
            if (value < 0x20)
            {
                return InvalidStringContent;
            }

            if (value == '\\')
            {
                hasEscape = true;
                if (ReadStringEscape(bytes, ref current, decoded) < 0)
                {
                    return InvalidStringContent;
                }
                continue;
            }

            if (value != '"')
            {
                current++;
                continue;
            }

            var length = current + 1;
            if (validateUtf8)
            {
                try
                {
                    StrictUtf8.GetCharCount(bytes.Slice(1, length - 2));
                }
                catch (DecoderFallbackException)
                {
                    return InvalidStringUtf8;
                }
            }

            return length;
        }

        return UnterminatedString;
    }

    private static int GetBlockCommentLength(ReadOnlySpan<byte> bytes)
    {
        var depth = 1;
        for (var i = 2; i + 1 < bytes.Length; i++)
        {
            if (bytes[i] == '#' && bytes[i + 1] == '|')
            {
                depth++;
                i++;
            }
            else if (bytes[i] == '|' && bytes[i + 1] == '#')
            {
                depth--;
                if (depth == 0)
                {
                    return i + 2;
                }

                i++;
            }
        }

        return -1;
    }

    private void MakeToken(STokenKind kind, int id, int offset, int subKind = 0, int length = 0)
    {
        if (_tokens.Length <= id)
        {
            var newBuffer = ArrayPool<SToken>.Shared.Rent(_tokens.Length * 2);
            _tokens.CopyTo(newBuffer, 0);
            ArrayPool<SToken>.Shared.Return(_tokens);
            _tokens = newBuffer;
        }

        _tokens[id] = new SToken(kind, subKind, offset) { Length = length };
    }

#if NET8_0_OR_GREATER
    private static SearchValues<byte> CreateStringSpecialCharacters()
    {
        const int controlCharacterCount = 0x20;
        Span<byte> values = stackalloc byte[controlCharacterCount + 2];
        for (var i = 0; i < controlCharacterCount; i++)
        {
            values[i] = (byte)i;
        }

        values[controlCharacterCount] = (byte)'"';
        values[controlCharacterCount + 1] = (byte)'\\';
        return SearchValues.Create(values);
    }
#endif

    private static int ReadStringEscape(ReadOnlySpan<byte> source, ref int index, Span<byte> destination)
    {
        index++;
        if (index >= source.Length)
        {
            return -1;
        }

        var value = source[index++];
        switch (value)
        {
            case (byte)'"':
            case (byte)'\\':
            case (byte)'/':
                destination[0] = value;
                return 1;
            case (byte)'b':
                destination[0] = 0x08;
                return 1;
            case (byte)'f':
                destination[0] = 0x0c;
                return 1;
            case (byte)'n':
                destination[0] = 0x0a;
                return 1;
            case (byte)'r':
                destination[0] = 0x0d;
                return 1;
            case (byte)'t':
                destination[0] = 0x09;
                return 1;
            case (byte)'u':
                var code = ReadHex4(source, ref index);
                if (code < 0 || code is >= 0xdc00 and <= 0xdfff)
                {
                    return -1;
                }

                Span<char> characters = stackalloc char[2];
                characters[0] = (char)code;
                var count = 1;
                if (char.IsHighSurrogate(characters[0]))
                {
                    if (source.Length - index < 6 || source[index] != '\\' || source[index + 1] != 'u')
                    {
                        return -1;
                    }

                    index += 2;
                    var low = ReadHex4(source, ref index);
                    if (low is < 0xdc00 or > 0xdfff)
                    {
                        return -1;
                    }

                    characters[1] = (char)low;
                    count = 2;
                }

                return Encoding.UTF8.GetBytes(characters.Slice(0, count), destination);
            default:
                return -1;
        }
    }

    private static int ReadHex4(ReadOnlySpan<byte> source, ref int index)
    {
        if (source.Length - index < 4)
        {
            return -1;
        }

        var result = 0;
        for (var i = 0; i < 4; i++)
        {
            var value = source[index++];
            var digit = value switch
            {
                >= (byte)'0' and <= (byte)'9' => value - '0',
                >= (byte)'a' and <= (byte)'f' => value - 'a' + 10,
                >= (byte)'A' and <= (byte)'F' => value - 'A' + 10,
                _ => -1
            };
            if (digit < 0)
            {
                return -1;
            }

            result = result * 16 + digit;
        }

        return result;
    }

    private void IncrementCount(int id)
    {
        ref var token = ref _tokens[id];
        token.IncrementCount();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            ArrayPool<SToken>.Shared.Return(_tokens);
            _tokens = Array.Empty<SToken>();
            _bytes = default;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(SDocument));
        }
    }
}
