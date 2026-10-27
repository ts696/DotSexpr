using System.Buffers;
using System.Globalization;
using System.Text;
using DotSexpr.Emitter;
using DotSexpr.Serializer;
using DotSexpr.Serializer.Formatters;

namespace DotSexpr.Tests;

public partial class SerializeTests
{
    [SexprObject]
    public partial class Person
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
    }

    [SexprObject]
    public partial class TaggedNames
    {
        [SexprTag("full-name")]
        public string[] Names { get; set; } = [];
    }

    [SexprObject]
    public partial class Accessors
    {
        private int _value = 7;
        public int WriteOnly { set { _value = value; } }
        public int ReadOnly => _value;
        public int PrivateSetter { get; private set; } = 8;
        public int PrivateGetter { private get; set; } = 9;
        internal int InternalField = 10;
        public int Field;

        public int ReadPrivateGetter() => PrivateGetter;
    }

    [Test]
    public void Serialize_TagsAndPublicFields_UseDeserializationMemberSet()
    {
        var value = SexprSerializer.Deserialize<FormatterTests.Tagged>("(:full-name Alice :年齢 20 :event 3)");
        var text = SexprSerializer.Serialize(value);

        Assert.That(text, Is.EqualTo("(:full-name \"Alice\" :年齢 20 :event 3)"));
    }

    [Test]
    public void Formatter_NonPublicAndIncompleteAccessors_AreSkipped()
    {
        var value = SexprSerializer.Deserialize<Accessors>(
            "(:_value 11 :WriteOnly 12 :ReadOnly 13 :PrivateSetter 14 :PrivateGetter 15 :InternalField 16 :Field 3)");
        Assert.That(value.ReadOnly, Is.EqualTo(7));
        Assert.That(value.PrivateSetter, Is.EqualTo(8));
        Assert.That(value.ReadPrivateGetter(), Is.EqualTo(9));
        Assert.That(value.InternalField, Is.EqualTo(10));
        Assert.That(SexprSerializer.Serialize(value), Is.EqualTo("(:Field 3)"));
    }

    [Test]
    public void Serialize_StructRecordAndInheritedGenericMembers_RoundTrip()
    {
        Assert.That(SexprSerializer.Serialize(new FormatterTests.WithInitializer()), Is.EqualTo("(:Value 42)"));
        var record = new FormatterTests.MutableRecord { Value = 7 };
        Assert.That(SexprSerializer.Deserialize<FormatterTests.MutableRecord>(SexprSerializer.Serialize(record)), Is.EqualTo(record));
        var derived = new FormatterTests.Derived { Name = "child", Inherited = 8 };
        var restored = SexprSerializer.Deserialize<FormatterTests.Derived>(SexprSerializer.Serialize(derived));
        Assert.That(restored.Name, Is.EqualTo("child"));
        Assert.That(restored.Inherited, Is.EqualTo(8));
        var generic = new FormatterTests.Container<string>.Item<long> { Names = ["a", "b"], Value = long.MaxValue };
        var restoredGeneric = SexprSerializer.Deserialize<FormatterTests.Container<string>.Item<long>>(SexprSerializer.Serialize(generic));
        Assert.That(restoredGeneric.Names, Is.EqualTo(generic.Names));
        Assert.That(restoredGeneric.Value, Is.EqualTo(long.MaxValue));
    }

    [Test]
    public void Serialize_RecursiveObjectsAndArrays_RoundTrip()
    {
        var value = new FormatterTests.Recursive { Name = "root", Children = [new() { Name = "leaf" }] };
        var result = SexprSerializer.Deserialize<FormatterTests.Recursive>(SexprSerializer.Serialize(value));
        Assert.That(result.Name, Is.EqualTo("root"));
        Assert.That(result.Children[0].Name, Is.EqualTo("leaf"));
        Assert.That(result.Children[0].Children, Is.Empty);
        Assert.That(SexprSerializer.Serialize(new[] { new[] { 1, 2 }, Array.Empty<int>(), new[] { 3 } }), Is.EqualTo("((1 2) () (3))"));
        Assert.That(SexprSerializer.Serialize(Array.Empty<int>()), Is.EqualTo("()"));
    }

    [TestCase(0)]
    [TestCase(int.MinValue)]
    [TestCase(int.MaxValue)]
    public void Serialize_Int32_RoundTrips(int value)
    {
        Assert.That(SexprSerializer.Deserialize<int>(SexprSerializer.Serialize(value)), Is.EqualTo(value));
    }

    [TestCase(long.MinValue)]
    [TestCase(long.MaxValue)]
    public void Serialize_Int64_RoundTrips(long value)
    {
        Assert.That(SexprSerializer.Deserialize<long>(SexprSerializer.Serialize(value)), Is.EqualTo(value));
    }

    [Test]
    public void Serialize_FloatingPointValues_PreserveBitsAndIgnoreCurrentCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.That(SexprSerializer.Serialize(1.25), Is.EqualTo("1.25"));
            foreach (var value in new[] { 0d, -0d, 0.1, double.Epsilon, double.MinValue, double.MaxValue, 1e-200, 1e200 })
            {
                var text = SexprSerializer.Serialize(value);
                var result = SexprSerializer.Deserialize<double>(text);
                Assert.That(BitConverter.DoubleToInt64Bits(result), Is.EqualTo(BitConverter.DoubleToInt64Bits(value)), text);
            }
            foreach (var value in new[] { 0f, -0f, 0.1f, float.Epsilon, float.MinValue, float.MaxValue, 1e-30f, 1e30f })
            {
                var text = SexprSerializer.Serialize(value);
                var result = SexprSerializer.Deserialize<float>(text);
                Assert.That(BitConverter.SingleToInt32Bits(result), Is.EqualTo(BitConverter.SingleToInt32Bits(value)), text);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [TestCase("")]
    [TestCase("123")]
    [TestCase("a b;()#|comment|#")]
    [TestCase("quote\" and slash\\")]
    [TestCase("東京😀")]
    [TestCase("a\"東京😀")]
    public void Serialize_String_QuotesEscapesAndRoundTrips(string value)
    {
        var text = SexprSerializer.Serialize(value);
        using var document = SDocument.Parse(text);
        Assert.That(document.RootNode.Token.IsString, Is.True);
        Assert.That(SexprSerializer.Deserialize<string>(text), Is.EqualTo(value));
    }

    [TestCase(64)]
    [TestCase(65)]
    public void Serialize_EscapedAsciiString_ProducesExactOutput(int length)
    {
        var prefix = new string('a', length - 2);
        var value = prefix + "\"\n";
        var expected = "\"" + prefix + "\\\"\\n\"";
        var writer = new ArrayBufferWriter<byte>();
        SexprSerializer.Serialize(writer, value);
        Assert.That(SexprSerializer.Serialize(value), Is.EqualTo(expected));
        Assert.That(Encoding.UTF8.GetString(writer.WrittenSpan), Is.EqualTo(expected));
    }

    [Test]
    public void Serialize_AllControlCharacters_RoundTrip()
    {
        var value = string.Concat(Enumerable.Range(0, 32).Select(i => (char)i));
        var text = SexprSerializer.Serialize(value);
        Assert.That(text, Does.Contain("\\u0000"));
        Assert.That(text, Does.Contain("\\u001f"));
        Assert.That(SexprSerializer.Deserialize<string>(text), Is.EqualTo(value));
    }

    [TestCase(16)]
    [TestCase(4096)]
    public void Serialize_Utf8AndBufferWriter_ProduceSameOutputAsString(int length)
    {
        var value = new Person { Name = new string('a', length) + "東京😀", Age = 3 };
        var writer = new ArrayBufferWriter<byte>();
        SexprSerializer.Serialize(writer, value);
        Assert.That(writer.WrittenSpan.ToArray(), Is.EqualTo(SexprSerializer.SerializeToUtf8Bytes(value)));
        Assert.That(Encoding.UTF8.GetString(writer.WrittenSpan), Is.EqualTo(SexprSerializer.Serialize(value)));
    }

    [Test]
    public void Serialize_Utf8Result_RemainsValidAfterAnotherSerialization()
    {
        var value = new string('a', 4096) + "東京😀";
        var bytes = SexprSerializer.SerializeToUtf8Bytes(value);

        SexprSerializer.SerializeToUtf8Bytes(new string('b', 6000));

        using var document = SDocument.Parse(bytes);
        Assert.That(document.RootNode.GetString(), Is.EqualTo(value));
    }

    [Test]
    public void Serialize_ExistingExpressionTree_RoundTripsCallsAndSymbols()
    {
        const string text = "(and (reachable ?from ?to) (not (Place_Visited ?to)))";
        var expression = SexprSerializer.Deserialize<Expression>(text);
        Assert.That(SexprSerializer.Serialize(expression), Is.EqualTo(text));
        Assert.That(SexprSerializer.Serialize<Expression>(new SymbolExpression("?from")), Is.EqualTo("?from"));
    }

    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(double.NegativeInfinity)]
    public void Serialize_NonFiniteNumbers_ReportNestedPath(double value)
    {
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize(new[] { new[] { value } }))!;
        Assert.That(error.Path, Is.EqualTo("$[0][0]"));
        Assert.That(error.BytePosition, Is.Null);
        Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize((float)value));
    }

    [Test]
    public void Serialize_NullValues_ReportPropertyAndArrayPath()
    {
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize(new TaggedNames { Names = ["ok", null!] }))!;
        Assert.That(error.Path, Is.EqualTo("$['full-name'][1]"));
        Assert.That(Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize<string>(null!))!.Path, Is.EqualTo("$"));
    }

    [TestCase("")]
    [TestCase("\"")]
    public void Serialize_InvalidUtf16_PreservesCauseAndPath(string prefix)
    {
        var value = new Person { Name = prefix + new string((char)0xd800, 1) };
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize(value))!;
        Assert.That(error.Path, Is.EqualTo("$.Name"));
        Assert.That(error.InnerException, Is.TypeOf<EncoderFallbackException>());
    }

    [TestCase("")]
    [TestCase("10")]
    [TestCase("bad name")]
    [TestCase("a;b")]
    [TestCase("#|comment|#")]
    public void Serialize_InvalidCallNames_AreRejectedAtNameIndex(string name)
    {
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize<Expression>(new CallExpression(name, [])))!;
        Assert.That(error.Path, Is.EqualTo("$[0]"));
    }

    [Test]
    public void Serialize_UnsupportedTypes_ReportRootAndNestedPath()
    {
        Assert.That(Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize(new FormatterTests.Unannotated()))!.Path, Is.EqualTo("$"));
        var value = new FormatterTests.Container<FormatterTests.Unannotated>.Item<int> { Names = [new()] };
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize(value))!;
        Assert.That(error.Path, Is.EqualTo("$.Names[0]"));
    }

    [Test]
    public void Serialize_MaxDepth_StopsCyclesAndAllowsConfiguredDepth()
    {
        var value = new FormatterTests.Recursive();
        value.Children = [value];
        var options = new SexprSerializerOptions { MaxDepth = 4 };
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize(value, options))!;
        Assert.That(error.Path, Is.EqualTo("$.Children[0].Children[0]"));
        Assert.That(error.Message, Does.Contain("maximum depth"));
        Assert.That(SexprSerializer.Serialize(new[] { new[] { 1 } }, new SexprSerializerOptions { MaxDepth = 2 }), Is.EqualTo("((1))"));
        Assert.Throws<ArgumentOutOfRangeException>(() => SexprSerializer.Serialize(1, new SexprSerializerOptions { MaxDepth = 0 }));
    }

    [Test]
    public void Serialize_CustomResolver_IsUsedForPropertiesArraysAndExpressionChildren()
    {
        var options = new SexprSerializerOptions { Resolver = new CustomResolver() };
        Assert.That(SexprSerializer.Serialize(new Person { Name = "a", Age = 2 }, options), Is.EqualTo("(:Name \"a\" :Age 102)"));
        Assert.That(SexprSerializer.Serialize(new[] { 1, 2 }, options), Is.EqualTo("(101 102)"));
        var expression = SexprSerializer.Deserialize<Expression>("(f x (g y))");
        Assert.That(SexprSerializer.Serialize(expression, options), Is.EqualTo("(f custom-x (g custom-y))"));
    }

    private sealed class CustomResolver : ISexprFormatterResolver
    {
        public ISexprFormatter<T>? GetFormatter<T>()
        {
            if (typeof(T) == typeof(int))
            {
                return (ISexprFormatter<T>)(object)new CustomIntFormatter();
            }
            if (typeof(T) == typeof(Expression))
            {
                return (ISexprFormatter<T>)(object)new CustomExpressionFormatter();
            }
            return DotSexpr.Serializer.Resolvers.DefaultResolver.Instance.GetFormatter<T>();
        }
    }

    private sealed class CustomIntFormatter : ISexprFormatter<int>
    {
        public void Serialize(ref SexprEmitter emitter, int value, SexprSerializerOptions options) => emitter.WriteNumber(value + 100);
        public int Deserialize(in SNode node, SexprSerializerOptions options) => node.GetInt32() - 100;
    }

    private sealed class CustomExpressionFormatter : ISexprFormatter<Expression>
    {
        public void Serialize(ref SexprEmitter emitter, Expression value, SexprSerializerOptions options)
        {
            if (value is SymbolExpression symbol)
            {
                emitter.WriteSymbol("custom-" + symbol.Name);
                return;
            }
            new ExpressionFormatter().Serialize(ref emitter, value, options);
        }

        public Expression Deserialize(in SNode node, SexprSerializerOptions options) => new ExpressionFormatter().Deserialize(in node, options);
    }
}
