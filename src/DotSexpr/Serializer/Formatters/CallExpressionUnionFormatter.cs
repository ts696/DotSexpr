using System;
using System.Collections.Generic;
using DotSexpr.Emitter;

namespace DotSexpr.Serializer.Formatters;

/// <summary>Dispatches a call to one of the explicitly registered derived formatters.</summary>
public class CallExpressionUnionFormatter<T> : ISexprFormatter<T> where T : CallExpression
{
    private readonly Dictionary<string, IEntry> _names = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, IEntry> _types = new();
    private Func<string, IReadOnlyList<Expression>, T>? _fallback;
    private Type? _fallbackType;

    protected void Register<TCall>(string name, ISexprFormatter<TCall>? formatter = null) where TCall : T
    {
        var entry = new Entry<TCall>(formatter);
        _names.Add(name, entry);
        _types.Add(typeof(TCall), entry);
    }

    protected void RegisterFallback<TCall>(Func<string, IReadOnlyList<Expression>, TCall> create) where TCall : T
    {
        _fallback = (name, arguments) => create(name, arguments);
        _fallbackType = typeof(TCall);
    }

    public void Serialize(ref SexprEmitter emitter, T value, SexprSerializerOptions options)
    {
        options = ScopeOptions(options);
        if (!_types.TryGetValue(value.GetType(), out var entry))
        {
            if (value.GetType() == _fallbackType)
            {
                new ExpressionFormatter().Serialize(ref emitter, value, options);
                return;
            }
            throw SexprSerializerException.ForSerialization($"Call type '{value.GetType()}' is not registered for '{typeof(T)}'.");
        }
        entry.Serialize(ref emitter, value, options);
    }

    public T Deserialize(in SNode node, SexprSerializerOptions options)
    {
        var name = ExpressionFormatter.ReadCallName(in node);
        if (!_names.TryGetValue(name, out var entry) && _fallback == null)
        {
            var error = new SexprSerializerException($"Call name '{name}' is not registered for '{typeof(T)}'.", node[0]);
            error.PrependIndex(0);
            throw error;
        }
        options = ScopeOptions(options);
        if (entry != null)
        {
            return entry.Deserialize(in node, options);
        }
        var call = ExpressionFormatter.DeserializeCall(in node, options);
        return _fallback!(call.Name, call.Arguments);
    }

    private SexprSerializerOptions ScopeOptions(SexprSerializerOptions options)
    {
        if (!(options.Resolver is ArgumentResolver scoped && ReferenceEquals(scoped.Owner, this)))
        {
            // Scope the union to this call's arguments without mutating the caller's options.
            options = new SexprSerializerOptions
            {
                Resolver = new ArgumentResolver(this, options.Resolver),
                MaxDepth = options.MaxDepth
            };
        }
        return options;
    }

    private sealed class ArgumentResolver : ISexprFormatterResolver
    {
        internal CallExpressionUnionFormatter<T> Owner { get; }
        private readonly ISexprFormatterResolver _fallback;
        private readonly ArgumentFormatter _formatter;

        internal ArgumentResolver(CallExpressionUnionFormatter<T> owner, ISexprFormatterResolver fallback)
        {
            Owner = owner;
            _fallback = fallback;
            _formatter = new ArgumentFormatter(owner);
        }

        public ISexprFormatter<TValue>? GetFormatter<TValue>()
        {
            if (typeof(TValue) == typeof(Expression) || typeof(TValue) == typeof(CallExpression))
            {
                return (ISexprFormatter<TValue>)(object)_formatter;
            }
            // A consumer resolver need not expose another assembly's internal case types.
            return _fallback.GetFormatter<TValue>() ??
                (Owner._types.TryGetValue(typeof(TValue), out var entry)
                    ? (ISexprFormatter<TValue>?)entry.Formatter : null);
        }
    }

    private sealed class ArgumentFormatter(CallExpressionUnionFormatter<T> owner)
        : ISexprFormatter<Expression>, ISexprFormatter<CallExpression>
    {
        public Expression Deserialize(in SNode node, SexprSerializerOptions options) => node.Token.IsList
            ? ReadCall(in node, options) : new ExpressionFormatter().Deserialize(in node, options);

        CallExpression ISexprFormatter<CallExpression>.Deserialize(in SNode node, SexprSerializerOptions options) =>
            ReadCall(in node, options);

        private CallExpression ReadCall(in SNode node, SexprSerializerOptions options)
        {
            var name = ExpressionFormatter.ReadCallName(in node);
            if (owner._names.TryGetValue(name, out var entry))
            {
                return entry.Deserialize(in node, options);
            }
            if (owner._fallback == null)
            {
                var error = new SexprSerializerException($"Call name '{name}' is not registered for '{typeof(T)}'.", node[0]);
                error.PrependIndex(0);
                throw error;
            }
            var call = ExpressionFormatter.DeserializeCall(in node, options);
            return owner._fallback(call.Name, call.Arguments);
        }

        public void Serialize(ref SexprEmitter emitter, Expression value, SexprSerializerOptions options)
        {
            if (value is T registeredCall)
            {
                owner.Serialize(ref emitter, registeredCall, options);
                return;
            }
            if (value is CallExpression call)
            {
                if (owner._names.ContainsKey(call.Name) || call.GetType() != typeof(CallExpression))
                {
                    throw SexprSerializerException.ForSerialization($"Call type '{call.GetType()}' is not registered for '{typeof(T)}'.");
                }
                if (owner._fallback == null)
                {
                    var error = SexprSerializerException.ForSerialization($"Call name '{call.Name}' is not registered for '{typeof(T)}'.");
                    error.PrependIndex(0);
                    throw error;
                }
            }
            new ExpressionFormatter().Serialize(ref emitter, value, options);
        }

        public void Serialize(ref SexprEmitter emitter, CallExpression value, SexprSerializerOptions options) =>
            Serialize(ref emitter, (Expression)value, options);
    }

    private interface IEntry
    {
        object? Formatter { get; }
        T Deserialize(in SNode node, SexprSerializerOptions options);
        void Serialize(ref SexprEmitter emitter, T value, SexprSerializerOptions options);
    }

    private sealed class Entry<TCall>(ISexprFormatter<TCall>? formatter) : IEntry where TCall : T
    {
        public object? Formatter => formatter;

        public T Deserialize(in SNode node, SexprSerializerOptions options) =>
            SexprSerializer.Deserialize<TCall>(in node, options);

        public void Serialize(ref SexprEmitter emitter, T value, SexprSerializerOptions options) =>
            SexprSerializer.Serialize(ref emitter, (TCall)value, options);
    }
}
