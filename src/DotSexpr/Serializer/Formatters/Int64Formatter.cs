using DotSexpr.Emitter;

namespace DotSexpr.Serializer.Formatters;

public class Int64Formatter : ISexprFormatter<long>
{
    public void Serialize(ref SexprEmitter emitter, long value, SexprSerializerOptions options) =>
        emitter.WriteNumber(value);

    public long Deserialize(in SNode node, SexprSerializerOptions options) =>
        NumberFormatter.Deserialize<long>(in node, static n => n.GetInt64());
}
