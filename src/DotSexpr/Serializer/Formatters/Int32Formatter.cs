using DotSexpr.Emitter;

namespace DotSexpr.Serializer.Formatters;

public class Int32Formatter : ISexprFormatter<int>
{
    public void Serialize(ref SexprEmitter emitter, int value, SexprSerializerOptions options)
    {
        emitter.WriteNumber(value);
    }

    public int Deserialize(in SNode node, SexprSerializerOptions options)
    {
        return NumberFormatter.Deserialize<int>(in node, static n => n.GetInt32());
    }
}
