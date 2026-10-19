using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DotSexpr;

public class SexprSerializerException : SexprException
{
    // Capture segments from leaf to root only while an input error unwinds.
    private readonly List<object>? _pathSegments;
    private string? _path;

    public SexprSerializerException(string message) : base(message)
    {
    }

    public SexprSerializerException(string message, System.Exception? innerException) : base(message, innerException)
    {
    }

    internal SexprSerializerException(string message, SNode node, System.Exception? innerException = null)
        : base(message, node.Document.Source.Span, node.Token.Offset, innerException)
    {
        _pathSegments = new List<object>();
    }

    /// <summary>Gets the path to the input value, starting at '$', if known.</summary>
    public string? Path => _pathSegments == null ? null : _path ??= BuildPath();

    public override string Message => FormatMessage(Path);

    internal void PrependProperty(string name)
    {
        _pathSegments?.Add(name);
        _path = null;
    }

    internal void PrependIndex(int index)
    {
        _pathSegments?.Add(index);
        _path = null;
    }

    private string BuildPath()
    {
        var path = new StringBuilder("$");
        for (var i = _pathSegments!.Count - 1; i >= 0; i--)
        {
            if (_pathSegments[i] is string name)
            {
                AppendProperty(path, name);
            }
            else
            {
                path.Append('[').Append(((int)_pathSegments[i]).ToString(CultureInfo.InvariantCulture)).Append(']');
            }
        }

        return path.ToString();
    }

    private static void AppendProperty(StringBuilder path, string name)
    {
        // Quote non-identifier keys so punctuation cannot be mistaken for path syntax.
        for (var i = 0; i < name.Length; i++)
        {
            if (name[i] != '_' && !(i == 0 ? char.IsLetter(name[i]) : char.IsLetterOrDigit(name[i])))
            {
                path.Append("['");
                foreach (var character in name)
                {
                    if (character is '\\' or '\'')
                    {
                        path.Append('\\');
                    }

                    path.Append(character);
                }

                path.Append("']");
                return;
            }
        }

        path.Append('.').Append(name);
    }
}
