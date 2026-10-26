using System.Buffers;
using System.Text;
using DotSexpr.Emitter;
using DotSexpr.Serializer;

namespace DotSexpr.Tests;

public partial class EmitterTests
{
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(15)]
    [TestCase(16)]
    [TestCase(17)]
    [TestCase(31)]
    [TestCase(32)]
    [TestCase(33)]
    [TestCase(63)]
    [TestCase(64)]
    [TestCase(65)]
    [TestCase(127)]
    public void WriteString_EscapesAfterPlainText_PreservesUnicodeAndTrailingText(int prefixLength)
    {
        var prefix = new string('a', prefixLength);
        var suffix = "日本語😀" + new string('b', 64);
        var value = prefix + "\0\b\f\n\r\t\"\\\u001f" + suffix;

        var text = SexprSerializer.Serialize(value);

        Assert.That(text, Is.EqualTo("\"" + prefix + "\\u0000\\b\\f\\n\\r\\t\\\"\\\\\\u001f" + suffix + "\""));
        using var document = SDocument.Parse(text);
        Assert.That(document.RootNode.GetString(), Is.EqualTo(value));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(15)]
    [TestCase(16)]
    [TestCase(17)]
    [TestCase(31)]
    [TestCase(32)]
    [TestCase(33)]
    [TestCase(4096)]
    public void WriteString_WithoutEscapes_PreservesEntireValue(int length)
    {
        var value = new string('a', length);
        Assert.That(SexprSerializer.Serialize(value), Is.EqualTo("\"" + value + "\""));
    }

    [Test]
    public void Write_NestedListsAndAtoms_SeparatesValuesWithoutExtraSpaces()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var emitter = new SexprEmitter(buffer);
        emitter.BeginList();
        emitter.WriteSymbol("f");
        emitter.BeginList();
        emitter.EndList();
        emitter.WriteString("a\nb");
        emitter.WriteNumber(10);
        emitter.EndList();
        emitter.Complete();

        Assert.That(Encoding.UTF8.GetString(buffer.WrittenSpan), Is.EqualTo("(f () \"a\\nb\" 10)"));
    }

    [Test]
    public void WriteSymbol_LongUnicodeName_RoundTrips()
    {
        var name = new string('あ', 1000);
        var buffer = new ArrayBufferWriter<byte>();
        var emitter = new SexprEmitter(buffer);
        emitter.WriteSymbol(name);
        emitter.Complete();
        using var document = SDocument.Parse(buffer.WrittenSpan);
        Assert.That(document.RootNode.GetSymbol(), Is.EqualTo(name));
    }

    [Test]
    public void WriteSymbol_InvalidUtf8_IsRejectedBeforeOutput()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var emitter = new SexprEmitter(buffer);
        var error = Assert.Throws<SexprSerializerException>(() => emitter.WriteSymbol(new byte[] { 0xff }))!;
        Assert.That(error.InnerException, Is.TypeOf<DecoderFallbackException>());
        Assert.That(buffer.WrittenCount, Is.Zero);
    }

    [Test]
    public void EndList_WithoutOpenList_IsRejected()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var emitter = new SexprEmitter(buffer);
        Assert.Throws<SexprSerializerException>(() => emitter.EndList());
    }

    public enum BadOutput { Empty, UnclosedList, MultipleRoots, Error }

    [SexprObject]
    public partial class BadOutputDocument
    {
        public BadOutput Value { get; set; }
    }

    [SexprObject]
    public partial class BadArrayDocument
    {
        public BadOutput[] Values { get; set; } = [];
    }

    [TestCase(BadOutput.Empty)]
    [TestCase(BadOutput.UnclosedList)]
    [TestCase(BadOutput.MultipleRoots)]
    public void Serialize_IncompleteFormatterOutput_ReportsRootError(BadOutput value)
    {
        var options = new SexprSerializerOptions { Resolver = new BadOutputResolver() };
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize(new BadOutputDocument { Value = value }, options))!;
        Assert.That(error.Path, Is.EqualTo("$"));
    }

    [Test]
    public void Serialize_CustomFormatterError_GainsParentArrayPath()
    {
        var options = new SexprSerializerOptions { Resolver = new BadOutputResolver() };
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize(new BadArrayDocument { Values = [BadOutput.Error] }, options))!;
        Assert.That(error.Path, Is.EqualTo("$.Values[0]"));
        Assert.That(error.InnerException, Is.TypeOf<SexprSerializerException>());
    }

    private sealed class BadOutputResolver : ISexprFormatterResolver
    {
        public ISexprFormatter<T>? GetFormatter<T>()
        {
            if (typeof(T) == typeof(BadOutputDocument))
            {
                return (ISexprFormatter<T>)(object)new BadDocumentFormatter();
            }
            if (typeof(T) == typeof(BadOutput))
            {
                return (ISexprFormatter<T>)(object)new BadOutputFormatter();
            }
            return DotSexpr.Generated.DotSexprGeneratedResolver.Instance.GetFormatter<T>();
        }
    }

    private sealed class BadDocumentFormatter : ISexprFormatter<BadOutputDocument>
    {
        public void Serialize(ref SexprEmitter emitter, BadOutputDocument value, SexprSerializerOptions options) =>
            new BadOutputFormatter().Serialize(ref emitter, value.Value, options);

        public BadOutputDocument Deserialize(in SNode node, SexprSerializerOptions options) => throw new NotSupportedException();
    }

    private sealed class BadOutputFormatter : ISexprFormatter<BadOutput>
    {
        public void Serialize(ref SexprEmitter emitter, BadOutput value, SexprSerializerOptions options)
        {
            switch (value)
            {
                case BadOutput.UnclosedList:
                    emitter.BeginList();
                    break;
                case BadOutput.MultipleRoots:
                    emitter.WriteNumber(1);
                    emitter.WriteNumber(2);
                    break;
                case BadOutput.Error:
                    throw new SexprSerializerException("Custom formatter error.");
            }
        }

        public BadOutput Deserialize(in SNode node, SexprSerializerOptions options) => throw new NotSupportedException();
    }
}
