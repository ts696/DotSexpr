namespace DotSexpr.Serializer;

public struct SexprObjectReader
{
    private SNode.Enumerator _enumerator;

    public SexprObjectReader(in SNode node)
    {
        if (!node.Token.IsList)
        {
            throw new SexprSerializerException("An object value must be a list.", node);
        }
        _enumerator = node.GetEnumerator();
    }

    public bool MoveNext(out SNode key, out SNode value)
    {
        key = default;
        value = default;
        if (!_enumerator.MoveNext())
        {
            return false;
        }

        key = _enumerator.Current;
        if (!key.Token.IsSymbol)
        {
            throw new SexprSerializerException("Property keys must be symbols beginning with ':'.", key);
        }

        var bytes = key.GetRawUtf8().Span;
        if (bytes.Length < 2 || bytes[0] != (byte)':')
        {
            throw new SexprSerializerException(
                $"Invalid property key '{key.GetSymbol()}'. Expected ':' followed by a property name.", key);
        }

        if (!_enumerator.MoveNext())
        {
            var name = key.GetSymbol();
            var exception = new SexprSerializerException($"Expected a value after property key '{name}'.", key);
            exception.PrependProperty(name.Substring(1));
            throw exception;
        }
        value = _enumerator.Current;
        return true;
    }
}
