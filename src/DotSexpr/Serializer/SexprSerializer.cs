using System;
using System.Buffers;
using System.Text;
using DotSexpr.Emitter;

namespace DotSexpr.Serializer;

public static class SexprSerializer
{
    public static string Serialize<T>(T value, SexprSerializerOptions? options = null)
    {
        using var buffer = new PooledBufferWriter(typeof(T) != typeof(string) && !typeof(T).IsPrimitive);
        Serialize(buffer, value, options);
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    public static byte[] SerializeToUtf8Bytes<T>(T value, SexprSerializerOptions? options = null)
    {
        using var buffer = new PooledBufferWriter(typeof(T) != typeof(string) && !typeof(T).IsPrimitive);
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
        SerializeResolved(ref emitter, value, options, formatter);
    }

    internal static void SerializeResolved<T>(ref SexprEmitter emitter, T value,
        SexprSerializerOptions options, ISexprFormatter<T>? formatter)
    {
        if (value is null)
        {
            throw SexprSerializerException.ForSerialization("Null values cannot be represented as an S-expression.");
        }
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
        return DeserializeResolved(in node, options, formatter);
    }

    internal static T DeserializeResolved<T>(in SNode node, SexprSerializerOptions options,
        ISexprFormatter<T>? formatter)
    {
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

    private sealed class PooledBufferWriter : IBufferWriter<byte>, IDisposable
    {
        private const int InitialCapacity = 256;
        private const int MaxInitialCapacity = 32 * 1024;
        [ThreadStatic]
        private static int _capacityHint;
        private byte[]? _buffer;
        private int _written;
        private readonly bool _rememberCapacity;
        private readonly bool _startedWithLargeHint;

        public PooledBufferWriter(bool rememberCapacity)
        {
            _rememberCapacity = rememberCapacity;
            var capacity = rememberCapacity && _capacityHint > 0 ? _capacityHint : InitialCapacity;
            _startedWithLargeHint = rememberCapacity && capacity > InitialCapacity;
            _buffer = ArrayPool<byte>.Shared.Rent(capacity);
        }

        public ReadOnlySpan<byte> WrittenSpan =>
            (_buffer ?? throw new ObjectDisposedException(nameof(PooledBufferWriter))).AsSpan(0, _written);

        public void Advance(int count)
        {
            var buffer = _buffer ?? throw new ObjectDisposedException(nameof(PooledBufferWriter));
            if ((uint)count > (uint)(buffer.Length - _written))
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
            _written += count;
        }

        public Memory<byte> GetMemory(int sizeHint = 0) => GetBuffer(sizeHint).AsMemory(_written);

        public Span<byte> GetSpan(int sizeHint = 0) => GetBuffer(sizeHint).AsSpan(_written);

        private byte[] GetBuffer(int sizeHint)
        {
            var buffer = _buffer ?? throw new ObjectDisposedException(nameof(PooledBufferWriter));
            if (sizeHint < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sizeHint));
            }
            var required = checked(_written + Math.Max(sizeHint, 1));
            if (required <= buffer.Length)
            {
                return buffer;
            }

            var capacity = (int)Math.Min(Math.Max((long)buffer.Length * 2, required), int.MaxValue);
            var replacement = ArrayPool<byte>.Shared.Rent(capacity);
            buffer.AsSpan(0, _written).CopyTo(replacement);
            _buffer = replacement;
            if (_rememberCapacity)
            {
                _capacityHint = Math.Min(replacement.Length, MaxInitialCapacity);
            }
            ArrayPool<byte>.Shared.Return(buffer);
            return replacement;
        }

        public void Dispose()
        {
            var buffer = _buffer;
            if (buffer == null)
            {
                return;
            }
            _buffer = null;
            if (_startedWithLargeHint && _written <= InitialCapacity)
            {
                _capacityHint = InitialCapacity;
            }
            _written = 0;
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
