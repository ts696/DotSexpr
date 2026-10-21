using DotSexpr.Emitter;

namespace DotSexpr.Serializer.Formatters;

public class DoubleFormatter : ISexprFormatter<double>
{
    public void Serialize(ref SexprEmitter emitter, double value, SexprSerializerOptions options) =>
        emitter.WriteNumber(value);

    public double Deserialize(in SNode node, SexprSerializerOptions options) =>
        NumberFormatter.Deserialize<double>(in node, static n => n.GetDouble());
}
