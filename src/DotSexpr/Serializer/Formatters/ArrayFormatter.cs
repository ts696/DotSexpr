using DotSexpr.Emitter;

namespace DotSexpr.Serializer.Formatters;

public class ArrayFormatter<T> : ISexprFormatter<T[]>
{
    public void Serialize(ref SexprEmitter emitter, T[] value, SexprSerializerOptions options)
    {
        emitter.BeginList();
        for (var i = 0; i < value.Length; i++)
        {
            try
            {
                SexprSerializer.Serialize(ref emitter, value[i], options);
            }
            catch (SexprSerializerException exception)
            {
                exception.PrependIndex(i);
                throw;
            }
        }
        emitter.EndList();
    }

    public T[] Deserialize(in SNode node, SexprSerializerOptions options)
    {
        if (!node.Token.IsList)
        {
            throw new SexprSerializerException("An array value must be a list.", node);
        }
        var values = new T[node.Count];
        var index = 0;
        try
        {
            foreach (var child in node)
            {
                values[index] = SexprSerializer.Deserialize<T>(in child, options);
                index++;
            }
        }
        catch (SexprSerializerException exception)
        {
            exception.PrependIndex(index);
            throw;
        }
        return values;
    }
}
