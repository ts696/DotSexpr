using System;
#if NET8_0_OR_GREATER
using System.Buffers;
#endif

namespace DotSexpr.Internal;

internal static class SymbolSyntax
{
#if NET8_0_OR_GREATER
    private static readonly SearchValues<byte> InvalidCharacters = CreateInvalidCharacters();
#endif

    internal static int IndexOfInvalidCharacter(ReadOnlySpan<byte> value)
    {
#if NET8_0_OR_GREATER
        return value.IndexOfAny(InvalidCharacters);
#else
        const ulong invalidCharacters = ((1UL << (' ' + 1)) - 1)
            | (1UL << '"') | (1UL << '(') | (1UL << ')') | (1UL << ';');
        for (var i = 0; i < value.Length; i++)
        {
            var character = value[i];
            if (character <= ';' && ((1UL << character) & invalidCharacters) != 0)
            {
                return i;
            }
        }
        return -1;
#endif
    }

#if NET8_0_OR_GREATER
    private static SearchValues<byte> CreateInvalidCharacters()
    {
        const int controlAndSpaceCount = 0x21;
        Span<byte> values = stackalloc byte[controlAndSpaceCount + 4];
        for (var i = 0; i < controlAndSpaceCount; i++)
        {
            values[i] = (byte)i;
        }
        values[controlAndSpaceCount] = (byte)'(';
        values[controlAndSpaceCount + 1] = (byte)')';
        values[controlAndSpaceCount + 2] = (byte)';';
        values[controlAndSpaceCount + 3] = (byte)'"';
        return SearchValues.Create(values);
    }
#endif
}
