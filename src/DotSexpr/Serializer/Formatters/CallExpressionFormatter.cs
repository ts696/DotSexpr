using System;
using System.Collections.Generic;
using DotSexpr.Emitter;

namespace DotSexpr.Serializer.Formatters;

/// <summary>Formats a derived call type whose name is fixed by its constructor.</summary>
public class CallExpressionFormatter<T>(string name, Func<IReadOnlyList<Expression>, T> create)
    : ISexprFormatter<T> where T : CallExpression
{
    public void Serialize(ref SexprEmitter emitter, T value, SexprSerializerOptions options)
    {
        if (!string.Equals(value.Name, name, StringComparison.Ordinal))
        {
            var error = SexprSerializerException.ForSerialization($"Expected call name '{name}', but found '{value.Name}'.");
            error.PrependIndex(0);
            throw error;
        }
        new ExpressionFormatter().Serialize(ref emitter, value, options);
    }

    public T Deserialize(in SNode node, SexprSerializerOptions options)
    {
        var call = ExpressionFormatter.DeserializeCall(in node, options, name);
        return create(call.Arguments);
    }
}
