using DotSexpr.Emitter;

namespace DotSexpr.Serializer.Formatters;

internal sealed class ExpressionSubtypeFormatter<T> : ISexprFormatter<T> where T : Expression
{
    public void Serialize(ref SexprEmitter emitter, T value, SexprSerializerOptions options) =>
        new ExpressionFormatter().Serialize(ref emitter, value, options);

    public T Deserialize(in SNode node, SexprSerializerOptions options)
    {
        var expression = new ExpressionFormatter().Deserialize(in node, options);
        if (typeof(T) == typeof(NumberExpression) && expression is IntegerExpression integer)
        {
            return (T)(Expression)new NumberExpression(integer.Value);
        }
        return expression is T result ? result : throw new SexprSerializerException(node, typeof(T), "a matching expression");
    }
}
