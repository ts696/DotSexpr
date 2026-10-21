using DotSexpr.Emitter;

namespace DotSexpr.Serializer.Formatters;

public class SingleFormatter : ISexprFormatter<float>
{
    public void Serialize(ref SexprEmitter emitter, float value, SexprSerializerOptions options) =>
        emitter.WriteNumber(value);

    public float Deserialize(in SNode node, SexprSerializerOptions options) =>
        NumberFormatter.Deserialize<float>(in node, static n => n.GetSingle());
}
