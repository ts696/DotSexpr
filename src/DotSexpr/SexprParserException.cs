using System;

namespace DotSexpr;

public class SexprParserException : SexprException
{
    internal SexprParserException(string message, ReadOnlySpan<byte> source, int bytePosition)
        : base(message, source, bytePosition)
    {
    }
}
