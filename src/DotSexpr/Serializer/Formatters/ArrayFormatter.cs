using DotSexpr.Emitter;

namespace DotSexpr.Serializer.Formatters;

public class ArrayFormatter<T> : ISexprFormatter<T[]>
{
    public void Serialize(ref SexprEmitter emitter, T[] value, SexprSerializerOptions options)
    {
        emitter.BeginList();
        ISexprFormatter<T>? formatter = null;
        for (var i = 0; i < value.Length; i++)
        {
            try
            {
                if (i == 0 && value[i] is not null)
                {
                    formatter = options.Resolver.GetFormatter<T>();
                }
                SexprSerializer.SerializeResolved(ref emitter, value[i], options, formatter);
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
        ISexprFormatter<T>? formatter = null;
        var index = 0;
        try
        {
            foreach (var child in node)
            {
                if (index == 0)
                {
                    formatter = options.Resolver.GetFormatter<T>();
                }
                values[index] = SexprSerializer.DeserializeResolved(in child, options, formatter);
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
