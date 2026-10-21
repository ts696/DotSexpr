using DotSexpr.Emitter;

namespace DotSexpr.Serializer;

public interface ISexprFormatter
{
}

public interface ISexprFormatter<T> : ISexprFormatter
{
    void Serialize(ref SexprEmitter emitter, T value, SexprSerializerOptions options);
    T Deserialize(in SNode node, SexprSerializerOptions options);
}
