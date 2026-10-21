using DotSexpr.Serializer.Formatters;

namespace DotSexpr.Serializer.Resolvers;

public class BuiltinResolver : ISexprFormatterResolver
{
    public static BuiltinResolver Instance { get; } = new();

    private static class Cache<T>
    {
        internal static readonly ISexprFormatter<T>? Formatter = Create();

        private static ISexprFormatter<T>? Create()
        {
            var type = typeof(T);
            object? formatter = null;
            if (type == typeof(string))
            {
                formatter = new StringFormatter();
            }
            else if (type == typeof(int))
            {
                formatter = new Int32Formatter();
            }
            else if (type == typeof(long))
            {
                formatter = new Int64Formatter();
            }
            else if (type == typeof(float))
            {
                formatter = new SingleFormatter();
            }
            else if (type == typeof(double))
            {
                formatter = new DoubleFormatter();
            }
            else if (type == typeof(Expression))
            {
                formatter = new ExpressionFormatter();
            }
            else if (type == typeof(string[]))
            {
                formatter = new ArrayFormatter<string>();
            }
            else if (type == typeof(int[]))
            {
                formatter = new ArrayFormatter<int>();
            }
            else if (type == typeof(long[]))
            {
                formatter = new ArrayFormatter<long>();
            }
            else if (type == typeof(float[]))
            {
                formatter = new ArrayFormatter<float>();
            }
            else if (type == typeof(double[]))
            {
                formatter = new ArrayFormatter<double>();
            }
            else if (type == typeof(Expression[]))
            {
                formatter = new ArrayFormatter<Expression>();
            }
            return (ISexprFormatter<T>?)formatter;
        }
    }

    public ISexprFormatter<T>? GetFormatter<T>() => Cache<T>.Formatter;
}
