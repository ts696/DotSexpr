using DotSexpr.Emitter;
using DotSexpr.Serializer;
using DotSexpr.Serializer.Formatters;

namespace DotSexpr.Tests;

public partial class FormatterTests
{
    [SexprObject]
    public partial class Tagged
    {
        [SexprTag("full-name")]
        public string Name { get; set; } = "initial";

        [SexprTag("年齢")]
        public int Age;

        public int @event { get; set; } = 7;
        public int ReadOnly => 99;
        public static int Static { get; set; }
    }

    [SexprObject]
    public partial class Recursive
    {
        public string Name { get; set; } = "default";
        public Recursive[] Children { get; set; } = [];
    }

    [SexprObject]
    public partial class Base
    {
        public int Inherited { get; set; }
    }

    [SexprObject]
    public partial class Derived : Base
    {
        public string Name { get; set; } = "";
    }

    public class TaggedBase
    {
        [SexprTag("base-value")]
        public virtual int Value { get; set; }
    }

    public class TaggedOverride : TaggedBase
    {
        public override int Value { get; set; }
    }

    [SexprObject]
    public partial class InheritedTag : TaggedOverride
    {
        public override int Value { get; set; }
    }

    [SexprObject]
    public partial class ReplacedTag : TaggedOverride
    {
        [SexprTag("derived-value")]
        public override int Value { get; set; }
    }

    public partial class Container<T> where T : class
    {
        [SexprObject]
        public partial class Item<U> where U : struct
        {
            public T[] Names { get; set; } = [];
            public U Value { get; set; }
        }
    }

    [SexprObject]
    public partial struct WithInitializer
    {
        public int Value { get; set; } = 42;
        public WithInitializer() { }
    }

    [SexprObject]
    public partial record MutableRecord
    {
        public int Value { get; set; }
    }

    public class Unannotated { public int Value { get; set; } }

    public class Manual(int value) { public int Value { get; } = value; }

    [SexprObject]
    public partial class GenericDocument
    {
        public Container<string>.Item<int> IntItem { get; set; } = new();
        public Container<string>.Item<long> LongItem { get; set; } = new();
        public Container<Unannotated>.Item<int> Unsupported { get; set; } = new();
        public Manual[] ManualValues { get; set; } = [];
        public int[][] Ints { get; set; } = [];
        public double[][] Doubles { get; set; } = [];
    }

    [SexprObject]
    public partial class ConcurrentItem { public int Value { get; set; } }

    [Test]
    public void Deserialize_GeneratedTagsAndPublicMembers_ReturnValues()
    {
        var result = SexprSerializer.Deserialize<Tagged>("(:full-name Alice :年齢 20 :event 3 :ReadOnly 0 :Static 1)");
        Assert.That(result.Name, Is.EqualTo("Alice"));
        Assert.That(result.Age, Is.EqualTo(20));
        Assert.That(result.@event, Is.EqualTo(3));
        Assert.That(result.ReadOnly, Is.EqualTo(99));
        Assert.That(Tagged.Static, Is.Zero);
    }

    [Test]
    public void Deserialize_MissingMembers_PreservesInitializers()
    {
        var result = SexprSerializer.Deserialize<Tagged>("()");
        Assert.That(result.Name, Is.EqualTo("initial"));
        Assert.That(result.@event, Is.EqualTo(7));
        Assert.That(SexprSerializer.Deserialize<WithInitializer>("()").Value, Is.EqualTo(42));
        Assert.That(SexprSerializer.Deserialize<Recursive>("()").Children, Is.Empty);
    }

    [Test]
    public void Deserialize_RepeatedKey_LastValueWins()
    {
        Assert.That(SexprSerializer.Deserialize<Tagged>("(:event 1 :event 2)").@event, Is.EqualTo(2));
    }

    [Test]
    public void Deserialize_CustomTagError_UsesInputTagInPath()
    {
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Deserialize<Tagged>("(:full-name 20)"))!;
        Assert.That(error.Path, Is.EqualTo("$['full-name']"));
        Assert.That(error.BytePosition, Is.EqualTo(12));
    }

    [Test]
    public void Deserialize_RecursiveType_RegistersWithoutRecursingDuringResolution()
    {
        var result = SexprSerializer.Deserialize<Recursive>("(:Name root :Children ((:Name leaf)))");
        Assert.That(result.Name, Is.EqualTo("root"));
        Assert.That(result.Children[0].Name, Is.EqualTo("leaf"));
        Assert.That(result.Children[0].Children, Is.Empty);
    }

    [Test]
    public void Deserialize_InheritedMembers_ReturnPopulatedDerivedType()
    {
        var result = SexprSerializer.Deserialize<Derived>("(:Inherited 8 :Name child)");
        Assert.That(result.Inherited, Is.EqualTo(8));
        Assert.That(result.Name, Is.EqualTo("child"));
        Assert.That(SexprSerializer.Deserialize<Base>("(:Inherited 4)").Inherited, Is.EqualTo(4));
    }

    [Test]
    public void Formatter_OverriddenProperty_InheritsTagAcrossMultipleLevels()
    {
        const string text = "(:base-value 7)";
        var result = SexprSerializer.Deserialize<InheritedTag>(text);
        Assert.That(result.Value, Is.EqualTo(7));
        Assert.That(SexprSerializer.Serialize(result), Is.EqualTo(text));
        var error = Assert.Throws<SexprSerializerException>(() =>
            SexprSerializer.Deserialize<InheritedTag>("(:base-value invalid)"))!;
        Assert.That(error.Path, Is.EqualTo("$['base-value']"));
    }

    [Test]
    public void Formatter_OverriddenProperty_PrefersDeclaredTag()
    {
        const string text = "(:derived-value 8)";
        var result = SexprSerializer.Deserialize<ReplacedTag>("(:derived-value 8 :base-value 9)");
        Assert.That(result.Value, Is.EqualTo(8));
        Assert.That(SexprSerializer.Serialize(result), Is.EqualTo(text));
    }

    [Test]
    public void Deserialize_NestedConstrainedGeneric_ReturnsValues()
    {
        var result = SexprSerializer.Deserialize<Container<string>.Item<int>>("(:Names (a b) :Value 6)");
        Assert.That(result.Names, Is.EqualTo(new[] { "a", "b" }));
        Assert.That(result.Value, Is.EqualTo(6));
        Assert.That(SexprSerializer.Deserialize<Container<string>.Item<long>>("(:Value 2147483648)").Value,
            Is.EqualTo(2147483648L));
    }

    [Test]
    public void Deserialize_MutableRecord_ReturnsValue()
    {
        Assert.That(SexprSerializer.Deserialize<MutableRecord>("(:Value 9)").Value, Is.EqualTo(9));
    }

    [TestCase(":", "$", 1)]
    [TestCase(":Unknown", "$.Unknown", 1)]
    [TestCase("20 1", "$", 1)]
    [TestCase("() 1", "$", 1)]
    public void Deserialize_MalformedProperties_ThrowLocatedErrors(string body, string path, int offset)
    {
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Deserialize<Tagged>($"({body})"))!;
        Assert.That(error.Path, Is.EqualTo(path));
        Assert.That(error.BytePosition, Is.EqualTo(offset));
    }

    [Test]
    public void Deserialize_JaggedArrayError_PreservesBothIndices()
    {
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Deserialize<int[][]>("((1) (2 bad))"))!;
        Assert.That(error.Path, Is.EqualTo("$[1][1]"));
        Assert.That(error.BytePosition, Is.EqualTo(8));
    }

    [TestCase("()")]
    [TestCase("\"text\"")]
    [TestCase("(\"name\")")]
    public void Deserialize_InvalidRootExpression_ReportsRootOrName(string text)
    {
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Deserialize<Expression>(text))!;
        Assert.That(error.Path, Is.EqualTo(text.StartsWith("(\"") ? "$[0]" : "$"));
    }

    [TestCase("1.5", typeof(OverflowException))]
    [TestCase("2147483648", typeof(OverflowException))]
    public void Deserialize_InvalidInteger_PreservesCauseAfterDocumentDisposal(string text, Type cause)
    {
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Deserialize<int>(text))!;
        Assert.That(error.Path, Is.EqualTo("$"));
        Assert.That(error.BytePosition, Is.Zero);
        Assert.That(error.InnerException, Is.TypeOf(cause));
        Assert.That(error.Message, Does.Contain("Int32"));
    }

    [Test]
    public void Resolver_UnsupportedType_ReturnsNullAndSerializerReportsType()
    {
        Assert.That(DotSexpr.Generated.DotSexprGeneratedResolver.Instance.GetFormatter<Unannotated>(), Is.Null);
        Assert.That(DotSexpr.Generated.DotSexprGeneratedResolver.Instance.GetFormatter<int[,]>(), Is.Null);
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Deserialize<Unannotated>("()"))!;
        Assert.That(error.Path, Is.EqualTo("$"));
        Assert.That(error.Message, Does.Contain("Unannotated"));
    }

    [Test]
    public void Resolver_CachesBuiltinAndGeneratedFormatters()
    {
        var primitive = DotSexpr.Generated.DotSexprGeneratedResolver.Instance.GetFormatter<int>();
        var generated = DotSexpr.Generated.DotSexprGeneratedResolver.Instance.GetFormatter<Recursive>();
        Assert.That(DotSexpr.Generated.DotSexprGeneratedResolver.Instance.GetFormatter<int>(), Is.SameAs(primitive));
        Assert.That(DotSexpr.Generated.DotSexprGeneratedResolver.Instance.GetFormatter<Recursive>(), Is.SameAs(generated));
    }

    [Test]
    public void Resolver_ConcurrentFirstUse_ResolvesGeneratedFormatter()
    {
        Parallel.For(0, 32, i =>
            Assert.That(SexprSerializer.Deserialize<ConcurrentItem>("(:Value 3)").Value, Is.EqualTo(3)));
    }

    [Test]
    public void Resolver_CustomFormatter_SupportsTypeWithoutParameterlessConstructor()
    {
        Assert.That(DotSexpr.Generated.DotSexprGeneratedResolver.Instance.GetFormatter<Manual>(), Is.Null);
        var options = new SexprSerializerOptions { Resolver = new CustomResolver() };
        Assert.That(SexprSerializer.Deserialize<Manual>("8", options).Value, Is.EqualTo(8));
        Assert.That(SexprSerializer.Deserialize<Manual[]>("(9)", options)[0].Value, Is.EqualTo(9));
    }

    [Test]
    public void Deserialize_CustomResolver_IsUsedForPropertiesArraysAndExpressions()
    {
        var options = new SexprSerializerOptions { Resolver = new CustomResolver() };
        Assert.That(SexprSerializer.Deserialize<Tagged>("(:event 1)", options).@event, Is.EqualTo(101));
        Assert.That(SexprSerializer.Deserialize<int[]>("(1 2)", options), Is.EqualTo(new[] { 101, 102 }));
        Assert.That(SexprSerializer.Deserialize<SerializeTests.TaggedNames>("(:full-name (a b))", options).Names,
            Is.EqualTo(new[] { "custom" }));
        var call = (CallExpression)SexprSerializer.Deserialize<Expression>("(f x)", options);
        Assert.That(call.Arguments[0], Is.EqualTo(new SymbolExpression("custom")));
    }

    private sealed class CustomResolver : ISexprFormatterResolver
    {
        public ISexprFormatter<T>? GetFormatter<T>()
        {
            if (typeof(T) == typeof(string[]))
            {
                return (ISexprFormatter<T>)(object)new CustomStringArrayFormatter();
            }
            if (typeof(T) == typeof(Manual))
            {
                return (ISexprFormatter<T>)(object)new ManualFormatter();
            }
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

    private sealed class CustomStringArrayFormatter : ISexprFormatter<string[]>
    {
        public string[] Deserialize(in SNode node, SexprSerializerOptions options) => new[] { "custom" };
        public void Serialize(ref SexprEmitter emitter, string[] value, SexprSerializerOptions options) =>
            throw new NotImplementedException();
    }

    private sealed class CustomIntFormatter : ISexprFormatter<int>
    {
        public int Deserialize(in SNode node, SexprSerializerOptions options) => node.GetInt32() + 100;
        public void Serialize(ref SexprEmitter emitter, int value, SexprSerializerOptions options) => throw new NotImplementedException();
    }

    private sealed class ManualFormatter : ISexprFormatter<Manual>
    {
        public Manual Deserialize(in SNode node, SexprSerializerOptions options) => new(node.GetInt32());
        public void Serialize(ref SexprEmitter emitter, Manual value, SexprSerializerOptions options) => throw new NotImplementedException();
    }

    private sealed class CustomExpressionFormatter : ISexprFormatter<Expression>
    {
        public Expression Deserialize(in SNode node, SexprSerializerOptions options) => node.Token.IsList
            ? new ExpressionFormatter().Deserialize(in node, options) : new SymbolExpression("custom");
        public void Serialize(ref SexprEmitter emitter, Expression value, SexprSerializerOptions options) => throw new NotImplementedException();
    }
}
