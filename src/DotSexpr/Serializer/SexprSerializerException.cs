namespace DotSexpr;

public class SexprSerializerException : SexprException
{
    public SexprSerializerException(string message) : base(message)
    {
    }

    public SexprSerializerException(string message, System.Exception? innerException) : base(message, innerException)
    {
    }

    internal SexprSerializerException(string message, SNode node, string path, System.Exception? innerException = null)
        : base(message, node.Document.Source.Span, node.Token.Offset, innerException)
    {
        Path = path;
    }

    /// <summary>Gets the path to the input value, starting at '$', if known.</summary>
    public string? Path { get; }

    public override string Message => FormatMessage(Path);
}
