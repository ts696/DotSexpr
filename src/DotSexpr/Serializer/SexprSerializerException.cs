using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DotSexpr.Serializer;

public class SexprSerializerException : SexprException
{
    // Error paths are collected from leaf to root.
    private readonly List<object>? _pathSegments;
    private string? _path;
    private readonly System.Type? _valueType;
    private readonly string? _expected;
    private string _targetName = "value";

    public SexprSerializerException(string message) : base(message)
    {
    }

    public SexprSerializerException(string message, System.Exception? innerException) : base(message, innerException)
    {
    }

    private SexprSerializerException(string message, List<object> pathSegments, System.Exception? innerException)
        : base(message, innerException)
    {
        _pathSegments = pathSegments;
    }

    internal static SexprSerializerException ForSerialization(string message, System.Exception? innerException = null) =>
        new(message, new List<object>(), innerException);

    /// <summary>Creates an input error at a node, retaining its location after the document is disposed.</summary>
    public SexprSerializerException(string message, SNode node, System.Exception? innerException = null)
        : base(message, node.Document.Source.Span, node.Token.Offset, innerException)
    {
        _pathSegments = new List<object>();
    }

    internal SexprSerializerException(SNode node, System.Type valueType, string? expected,
        System.Exception? innerException = null)
        : this($"Cannot deserialize value as '{valueType.Name}'.", node, innerException)
    {
        _valueType = valueType;
        _expected = expected;
    }

    /// <summary>Gets the path to the input value, starting at '$', if known.</summary>
    public string? Path => _pathSegments == null ? null : _path ??= BuildPath();

    public override string Message => FormatMessage(Path, _valueType == null ? null :
        $"Cannot deserialize {_targetName} as '{_valueType.Name}'." +
        (_expected == null ? "" : $" Expected {_expected}."));

    internal void PrependProperty(string name)
    {
        if (_pathSegments?.Count == 0)
        {
            _targetName = $"property ':{name}'";
        }
        _pathSegments?.Add(name);
        _path = null;
    }

    internal void PrependIndex(int index)
    {
        if (_pathSegments?.Count == 0)
        {
            _targetName = $"array element at index {index}";
        }
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
