namespace DotSexpr.Serializer;

public interface ISexprFormatterResolver
{
    ISexprFormatter<T>? GetFormatter<T>();
}
