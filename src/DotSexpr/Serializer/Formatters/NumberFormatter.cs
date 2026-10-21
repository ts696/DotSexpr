using System;

namespace DotSexpr.Serializer.Formatters;

internal static class NumberFormatter
{
    internal static T Deserialize<T>(in SNode node, Func<SNode, T> read)
    {
        if (!node.Token.IsNumber)
        {
            throw new SexprSerializerException(node, typeof(T), "a number");
        }
        try
        {
            return read(node);
        }
        catch (Exception exception) when (exception is FormatException or OverflowException)
        {
            throw new SexprSerializerException(node, typeof(T), null, exception);
        }
    }
}
