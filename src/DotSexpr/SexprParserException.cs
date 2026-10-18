using System;

namespace DotSexpr;

public class SexprParserException : SexprException
{
    internal SexprParserException(string message, ReadOnlySpan<byte> source, int bytePosition) : base(message)
    {
        var lineNumber = 1;
        var lineStart = 0;

        for (var i = 0; i < bytePosition; i++)
        {
            if (source[i] == '\r')
            {
                if (i + 1 < bytePosition && source[i + 1] == '\n')
                {
                    i++;
                }

                lineNumber++;
                lineStart = i + 1;
            }
            else if (source[i] == '\n')
            {
                lineNumber++;
                lineStart = i + 1;
            }
        }

        BytePosition = bytePosition;
        LineNumber = lineNumber;
        BytePositionInLine = bytePosition - lineStart;
    }

    public int? BytePosition { get; }

    public int? LineNumber { get; }

    public int? BytePositionInLine { get; }

    public override string Message => LineNumber is { } lineNumber && BytePositionInLine is { } bytePositionInLine
        ? $"{base.Message} (Line {lineNumber}, byte position {bytePositionInLine}.)"
        : base.Message;
}
