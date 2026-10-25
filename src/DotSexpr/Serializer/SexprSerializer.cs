using System;
using System.Buffers;
using System.Text;
using DotSexpr.Emitter;

namespace DotSexpr.Serializer;

public static class SexprSerializer
{
    public static string Serialize<T>(T value, SexprSerializerOptions? options = null)
    {
        var buffer = new ArrayBufferWriter<byte>();
        Serialize(buffer, value, options);
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    public static byte[] SerializeToUtf8Bytes<T>(T value, SexprSerializerOptions? options = null)
    {
        var buffer = new ArrayBufferWriter<byte>();
        Serialize(buffer, value, options);
        return buffer.WrittenSpan.ToArray();
    }

    public static void Serialize<T>(IBufferWriter<byte> writer, T value, SexprSerializerOptions? options = null)
    {
        options ??= SexprSerializerOptions.Default;
        var emitter = new SexprEmitter(writer, options.MaxDepth);
        Serialize(ref emitter, value, options);
        emitter.Complete();
    }

    public static void Serialize<T>(ref SexprEmitter emitter, T value, SexprSerializerOptions? options = null)
    {
        options ??= SexprSerializerOptions.Default;
        if (value is null)
        {
            throw SexprSerializerException.ForSerialization("Null values cannot be represented as an S-expression.");
        }
        var formatter = options.Resolver.GetFormatter<T>();
        if (formatter == null)
        {
            throw SexprSerializerException.ForSerialization(MissingFormatter<T>());
        }
        try
        {
            formatter.Serialize(ref emitter, value, options);
        }
        catch (SexprSerializerException exception) when (exception.Path == null)
        {
            throw SexprSerializerException.ForSerialization(exception.Message, exception);
        }
    }

    public static void SerializeProperty<T>(ref SexprEmitter emitter, T value, SexprSerializerOptions options, string name)
    {
        try
        {
            Serialize(ref emitter, value, options);
        }
        catch (SexprSerializerException exception)
        {
            exception.PrependProperty(name);
            throw;
        }
    }

    public static T Deserialize<T>(string text, SexprSerializerOptions? options = null)
    {
        using var document = SDocument.Parse(text);
        return Deserialize<T>(document.RootNode, options);
    }

    /// <summary>Deserializes each top-level expression in order. Empty input returns an empty array.</summary>
    public static T[] DeserializeMultipleDocuments<T>(string text, SexprSerializerOptions? options = null)
    {
        using var document = SDocument.ParseMultiple(text);
        return DeserializeDocuments<T>(document.RootNode, options);
    }

    /// <summary>Deserializes each top-level UTF-8 expression in order. Empty input returns an empty array.</summary>
    public static T[] DeserializeMultipleDocuments<T>(ReadOnlySpan<byte> bytes, SexprSerializerOptions? options = null)
    {
        using var document = SDocument.ParseMultiple(bytes);
        return DeserializeDocuments<T>(document.RootNode, options);
    }

    private static T[] DeserializeDocuments<T>(in SNode root, SexprSerializerOptions? options)
    {
        options ??= SexprSerializerOptions.Default;
        var values = new T[root.Count];
        var index = 0;
        foreach (var node in root)
        {
            try
            {
                values[index] = Deserialize<T>(in node, options);
            }
            catch (SexprSerializerException exception)
            {
                exception.PrependIndex(index);
                throw;
            }
            index++;
        }
        return values;
    }

    public static T Deserialize<T>(in SNode node, SexprSerializerOptions? options = null)
    {
        options ??= SexprSerializerOptions.Default;
        var formatter = options.Resolver.GetFormatter<T>();
        if (formatter == null)
        {
            throw new SexprSerializerException(MissingFormatter<T>(), node);
        }
        return formatter.Deserialize(in node, options);
    }

    private static string MissingFormatter<T>() =>
        $"No formatter is available for '{typeof(T)}'. Initialize or register the required formatter, or provide a custom resolver.";

    public static T DeserializeProperty<T>(in SNode node, SexprSerializerOptions options, string name)
    {
        try
        {
            return Deserialize<T>(in node, options);
        }
        catch (SexprSerializerException exception)
        {
            exception.PrependProperty(name);
            throw;
        }
    }
}
