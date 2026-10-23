using System;
using System.Collections.Generic;
using DotSexpr.Emitter;

namespace DotSexpr.Serializer.Formatters;

/// <summary>Formats a derived call type whose name is fixed by its constructor.</summary>
public class CallExpressionFormatter<T>(string name, Func<IReadOnlyList<Expression>, T> create)
    : ISexprFormatter<T> where T : CallExpression
{
    private IArgument[]? _arguments;

    /// <summary>Sets the argument schema. An empty schema requires no arguments.</summary>
    protected void SetArgumentTypes(params IArgument[] arguments) => _arguments = arguments;

    /// <summary>Uses the declared expression type's formatter, with an optional fallback for external dependencies.</summary>
    protected static IArgument Argument<TArgument>(Func<ISexprFormatter<TArgument>>? fallback = null)
        where TArgument : Expression => new TypedArgument<TArgument>(fallback);

    public void Serialize(ref SexprEmitter emitter, T value, SexprSerializerOptions options)
    {
        if (!string.Equals(value.Name, name, StringComparison.Ordinal))
        {
            var error = SexprSerializerException.ForSerialization($"Expected call name '{name}', but found '{value.Name}'.");
            error.PrependIndex(0);
            throw error;
        }
        if (value.Arguments == null)
        {
            throw SexprSerializerException.ForSerialization("Call arguments cannot be null.");
        }
        if (_arguments != null && value.Arguments.Count != _arguments.Length)
        {
            throw SexprSerializerException.ForSerialization(
                $"Call '{name}' requires exactly {_arguments.Length} argument(s), but found {value.Arguments.Count}.");
        }
        emitter.BeginList();
        try
        {
            emitter.WriteSymbol(name);
        }
        catch (SexprSerializerException exception)
        {
            exception.PrependIndex(0);
            throw;
        }
        for (var i = 0; i < value.Arguments.Count; i++)
        {
            try
            {
                if (_arguments == null)
                    SexprSerializer.Serialize(ref emitter, value.Arguments[i], options);
                else
                    _arguments[i].Serialize(ref emitter, value.Arguments[i], options);
            }
            catch (SexprSerializerException exception)
            {
                exception.PrependIndex(i + 1);
                throw;
            }
        }
        emitter.EndList();
    }

    public T Deserialize(in SNode node, SexprSerializerOptions options)
    {
        var actualName = ExpressionFormatter.ReadCallName(in node);
        if (!string.Equals(actualName, name, StringComparison.Ordinal))
        {
            var error = new SexprSerializerException($"Expected call name '{name}', but found '{actualName}'.", node[0]);
            error.PrependIndex(0);
            throw error;
        }
        var count = node.Count - 1;
        if (_arguments != null && count != _arguments.Length)
        {
            throw new SexprSerializerException(
                $"Call '{name}' requires exactly {_arguments.Length} argument(s), but found {count}.", node);
        }
        var arguments = new Expression[count];
        var index = 0;
        try
        {
            foreach (var child in node)
            {
                if (index > 0)
                    arguments[index - 1] = _arguments == null
                        ? SexprSerializer.Deserialize<Expression>(in child, options)
                        : _arguments[index - 1].Deserialize(in child, options);
                index++;
            }
        }
        catch (SexprSerializerException exception)
        {
            exception.PrependIndex(index);
            throw;
        }
        return create(arguments);
    }

    protected interface IArgument
    {
        Expression Deserialize(in SNode node, SexprSerializerOptions options);
        void Serialize(ref SexprEmitter emitter, Expression value, SexprSerializerOptions options);
    }

    private sealed class TypedArgument<TArgument>(Func<ISexprFormatter<TArgument>>? fallback) : IArgument
        where TArgument : Expression
    {
        public Expression Deserialize(in SNode node, SexprSerializerOptions options)
        {
            var formatter = options.Resolver.GetFormatter<TArgument>() ?? fallback?.Invoke();
            if (formatter == null)
                throw new SexprSerializerException($"No formatter is available for '{typeof(TArgument)}'.", node);
            return formatter.Deserialize(in node, options) ??
                throw new SexprSerializerException("Call arguments cannot be null.", node);
        }

        public void Serialize(ref SexprEmitter emitter, Expression value, SexprSerializerOptions options)
        {
            if (value is not TArgument argument)
                throw SexprSerializerException.ForSerialization(
                    $"Expected argument type '{typeof(TArgument)}', but found '{value?.GetType().ToString() ?? "null"}'.");
            var formatter = options.Resolver.GetFormatter<TArgument>() ?? fallback?.Invoke();
            if (formatter == null)
                throw SexprSerializerException.ForSerialization($"No formatter is available for '{typeof(TArgument)}'.");
            formatter.Serialize(ref emitter, argument, options);
        }
    }
}
