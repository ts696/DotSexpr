using System;

namespace DotSexpr.Internal;

internal static class SourcePosition
{
    internal static (int LineNumber, int BytePositionInLine) GetLinePosition(
        ReadOnlySpan<byte> source, int bytePosition)
    {
        if (bytePosition < 0 || bytePosition > source.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(bytePosition));
        }

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

        return (lineNumber, bytePosition - lineStart);
    }
}
