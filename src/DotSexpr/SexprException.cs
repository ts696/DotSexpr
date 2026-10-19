using System;
using DotSexpr.Internal;

namespace DotSexpr;

public class SexprException : Exception
{
    public SexprException(string message) : this(message, null)
    {
    }

    public SexprException(string message, Exception? innerException) : base(message, innerException)
    {
    }

    internal SexprException(string message, ReadOnlySpan<byte> source, int bytePosition,
        Exception? innerException = null) : base(message, innerException)
    {
        var position = SourcePosition.GetLinePosition(source, bytePosition);
        BytePosition = bytePosition;
        LineNumber = position.LineNumber;
        BytePositionInLine = position.BytePositionInLine;
    }

    /// <summary>Gets the zero-based UTF-8 byte offset from the start of the input, if known.</summary>
    public int? BytePosition { get; }

    /// <summary>Gets the one-based line number, if known.</summary>
    public int? LineNumber { get; }

    /// <summary>Gets the zero-based UTF-8 byte offset within the line, if known.</summary>
    public int? BytePositionInLine { get; }

    public override string Message => FormatMessage(null);

    protected string FormatMessage(string? path)
    {
        var context = path == null ? null : $"Path: {path}";
        if (LineNumber is { } lineNumber && BytePositionInLine is { } bytePositionInLine)
        {
            var location = $"Line {lineNumber}, byte position {bytePositionInLine}";
            context = context == null ? location : $"{context}, {location}";
        }

        return context == null ? base.Message : $"{base.Message} ({context}.)";
    }
}
