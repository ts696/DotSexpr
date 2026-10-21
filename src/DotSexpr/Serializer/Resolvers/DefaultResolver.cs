using System;
using System.Threading;
using DotSexpr.Internal;

namespace DotSexpr.Serializer.Resolvers;

public sealed class DefaultResolver : ISexprFormatterResolver
{
    public static DefaultResolver Instance { get; } = new();

    private static readonly object Gate = new();
    private static ISexprFormatterResolver[] resolvers = Array.Empty<ISexprFormatterResolver>();

    private DefaultResolver() { }

    /// <summary>Adds an assembly or custom resolver.</summary>
    public static void Register(ISexprFormatterResolver resolver)
    {
        ArgumentGuard.ThrowIfNull(resolver);
        if (ReferenceEquals(resolver, Instance))
        {
            throw new ArgumentException("The default resolver cannot register itself.", nameof(resolver));
        }
        lock (Gate)
        {
            foreach (var registered in resolvers)
            {
                if (ReferenceEquals(registered, resolver))
                {
                    return;
                }
            }
            var updated = new ISexprFormatterResolver[resolvers.Length + 1];
            Array.Copy(resolvers, updated, resolvers.Length);
            updated[resolvers.Length] = resolver;
            Volatile.Write(ref resolvers, updated);
        }
    }

    /// <summary>Adds a formatter for a type not already resolved by the default resolver.</summary>
    public static void Register<T>(ISexprFormatter<T> formatter)
    {
        ArgumentGuard.ThrowIfNull(formatter);
        Register(new FormatterResolver<T>(formatter));
    }

    public ISexprFormatter<T>? GetFormatter<T>()
    {
        var formatter = Volatile.Read(ref Cache<T>.Formatter);
        if (formatter == null)
        {
            formatter = BuiltinResolver.Instance.GetFormatter<T>() ?? Find<T>();
            if (formatter != null)
            {
                formatter = Interlocked.CompareExchange(ref Cache<T>.Formatter, formatter, null) ?? formatter;
            }
        }
        return formatter;
    }

    private static ISexprFormatter<T>? Find<T>()
    {
        foreach (var resolver in Volatile.Read(ref resolvers))
        {
            var formatter = resolver.GetFormatter<T>();
            if (formatter != null)
            {
                return formatter;
            }
        }
        return null;
    }

    private static class Cache<T>
    {
        internal static ISexprFormatter<T>? Formatter;
    }

    private sealed class FormatterResolver<TValue>(ISexprFormatter<TValue> formatter) : ISexprFormatterResolver
    {
        public ISexprFormatter<T>? GetFormatter<T>() => typeof(T) == typeof(TValue)
            ? (ISexprFormatter<T>)(object)formatter : null;
    }
}
