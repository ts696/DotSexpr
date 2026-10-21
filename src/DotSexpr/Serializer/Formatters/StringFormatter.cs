using DotSexpr.Emitter;

namespace DotSexpr.Serializer.Formatters;

public class StringFormatter : ISexprFormatter<string>
{
    public void Serialize(ref SexprEmitter emitter, string value, SexprSerializerOptions options)
    {
        emitter.WriteString(value);
    }

    public string Deserialize(in SNode node, SexprSerializerOptions options)
    {
        if (node.Token.IsString)
        {
            return node.GetString();
        }
        if (node.Token.IsSymbol)
        {
            return node.GetSymbol();
        }
        throw new SexprSerializerException(node, typeof(string), "a symbol or string");
    }
}
