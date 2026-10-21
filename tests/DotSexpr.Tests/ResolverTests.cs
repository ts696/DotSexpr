using System.Buffers;
using System.Text;
using DotSexpr.Emitter;
using DotSexpr.Generated;
using DotSexpr.Serializer;
using DotSexpr.Serializer.Formatters;
using DotSexpr.Serializer.Resolvers;

namespace DotSexpr.Tests;

public partial class ResolverTests
{
    [SexprObject]
    public partial class Tree
    {
        public string Name { get; set; } = "";
        public Tree[] Children { get; set; } = [];
    }

    [SexprObject]
    public partial class ArrayItem
    {
        public int Value { get; set; }
    }

    [Test]
    public void AutomaticResolver_DiscoversRecursiveMembers()
    {
        const string text = "(:Name \"root\" :Children ((:Name \"leaf\" :Children ())))";
        var tree = SexprSerializer.Deserialize<Tree>(text);
        Assert.That(tree.Children[0].Name, Is.EqualTo("leaf"));
        Assert.That(SexprSerializer.Serialize(tree), Is.EqualTo(text));
        Assert.That(Encoding.UTF8.GetString(SexprSerializer.SerializeToUtf8Bytes(tree)), Is.EqualTo(text));
        var writer = new ArrayBufferWriter<byte>();
        SexprSerializer.Serialize(writer, tree);
        Assert.That(Encoding.UTF8.GetString(writer.WrittenSpan), Is.EqualTo(text));
        writer.Clear();
        var emitter = new SexprEmitter(writer);
        SexprSerializer.Serialize(ref emitter, tree);
        emitter.Complete();
        Assert.That(Encoding.UTF8.GetString(writer.WrittenSpan), Is.EqualTo(text));
        using var document = SDocument.Parse(text);
        Assert.That(SexprSerializer.Deserialize<Tree>(document.RootNode).Children[0].Name, Is.EqualTo("leaf"));
    }

    [Test]
    public void Options_CustomDepth_UsesDefaultResolver()
    {
        var options = new SexprSerializerOptions { MaxDepth = 1 };
        var tree = SexprSerializer.Deserialize<Tree>("(:Name root)", options);
        Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize(tree, options));
        options.MaxDepth = 2;
        Assert.That(SexprSerializer.Serialize(tree, options), Is.EqualTo("(:Name \"root\" :Children ())"));
    }

    [TestCase("()", 0)]
    [TestCase("((:Value 1))", 1)]
    public void ModelArrays_WithoutArrayMembers_WorkThroughGenericMethods(string text, int count)
    {
        var values = Load<ArrayItem[]>(text);
        Assert.That(values, Has.Length.EqualTo(count));
        Assert.That(SexprSerializer.Serialize(values), Is.EqualTo(text));
        Assert.That(Encoding.UTF8.GetString(SexprSerializer.SerializeToUtf8Bytes(values)), Is.EqualTo(text));
        var writer = new ArrayBufferWriter<byte>();
        SexprSerializer.Serialize(writer, values);
        Assert.That(Encoding.UTF8.GetString(writer.WrittenSpan), Is.EqualTo(text));
        writer.Clear();
        var emitter = new SexprEmitter(writer);
        SexprSerializer.Serialize(ref emitter, values);
        emitter.Complete();
        Assert.That(Encoding.UTF8.GetString(writer.WrittenSpan), Is.EqualTo(text));
        using var document = SDocument.Parse(text);
        Assert.That(SexprSerializer.Deserialize<ArrayItem[]>(document.RootNode), Has.Length.EqualTo(count));
    }

    [Test]
    public void GenericModels_CollectedFromMembers_WorkAsValuesAndArrays()
    {
        var resolver = DotSexprGeneratedResolver.Instance;
        Assert.That(resolver.GetFormatter<FormatterTests.Container<string>.Item<int>>(), Is.Not.Null);
        var model = SexprSerializer.Deserialize<FormatterTests.GenericDocument>("(:IntItem (:Value 5))");
        Assert.That(model.IntItem.Value, Is.EqualTo(5));
        var item = Load<FormatterTests.Container<string>.Item<int>>(SexprSerializer.Serialize(model.IntItem));
        Assert.That(item.Value, Is.EqualTo(5));
        var array = Load<FormatterTests.Container<string>.Item<int>[]>(SexprSerializer.Serialize(new[] { item }));
        Assert.That(array[0].Value, Is.EqualTo(5));
    }

    [Test]
    public void BuiltinArrays_WorkWithoutGeneratedResolver()
    {
        var options = new SexprSerializerOptions { Resolver = BuiltinResolver.Instance };
        RoundTrip(new[] { 1, 2 }, "(1 2)");
        RoundTrip(new[] { 1L, 2L }, "(1 2)");
        RoundTrip(new[] { 1.5f, 2.5f }, "(1.5 2.5)");
        RoundTrip(new[] { 1.5, 2.5 }, "(1.5 2.5)");
        RoundTrip(new[] { "a", "b" }, "(\"a\" \"b\")");
        RoundTrip<Expression>(new Expression[] { new SymbolExpression("a") }, "(a)");

        void RoundTrip<T>(T[] values, string text)
        {
            Assert.That(SexprSerializer.Serialize(values, options), Is.EqualTo(text));
            Assert.That(SexprSerializer.Deserialize<T[]>(text, options), Is.EqualTo(values));
        }
    }

    [Test]
    public void Scalars_WorkThroughGenericMethods()
    {
        Assert.That(SexprSerializer.Serialize(Load<int>("5")), Is.EqualTo("5"));
        Assert.That(SexprSerializer.Serialize(Load<string>("value")), Is.EqualTo("\"value\""));
    }

    [Test]
    public void UncollectedJaggedArray_CanBeRegisteredExplicitly()
    {
        Assert.That(DefaultResolver.Instance.GetFormatter<ArrayItem[][]>(), Is.Null);
        Assert.Throws<SexprSerializerException>(() => Load<ArrayItem[][]>("()"));
        DefaultResolver.Register<ArrayItem[][]>(new ArrayFormatter<ArrayItem[]>());
        const string text = "(((:Value 7)))";
        var values = Load<ArrayItem[][]>(text);
        Assert.That(values[0][0].Value, Is.EqualTo(7));
        Assert.That(SexprSerializer.Serialize(values), Is.EqualTo(text));
    }

    [Test]
    public void MultidimensionalArrays_AreUnsupported()
    {
        Assert.That(DefaultResolver.Instance.GetFormatter<int[,]>(), Is.Null);
        Assert.That(DefaultResolver.Instance.GetFormatter<ArrayItem[,]>(), Is.Null);
        Assert.Throws<SexprSerializerException>(() => Load<int[,]>("((1))"));
        Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize(new int[1, 1]));
    }

    [Test]
    public void Resolver_RegistrationAfterMiss_ResolvesFormatter()
    {
        var resolver = SexprSerializerOptions.Default.Resolver;
        Assert.That(resolver.GetFormatter<Deferred>(), Is.Null);
        DefaultResolver.Register(new DeferredResolver());
        Parallel.For(0, 32, _ =>
        {
            var value = SexprSerializer.Deserialize<Deferred>("7");
            Assert.That(SexprSerializer.Serialize(value), Is.EqualTo("7"));
        });
    }

    private static T Load<T>(string text) => SexprSerializer.Deserialize<T>(text);

    private sealed record Deferred(int Value);

    private sealed class DeferredResolver : ISexprFormatterResolver
    {
        private static readonly DeferredFormatter Formatter = new();
        public ISexprFormatter<T>? GetFormatter<T>() => typeof(T) == typeof(Deferred)
            ? (ISexprFormatter<T>)(object)Formatter : null;
    }

    private sealed class DeferredFormatter : ISexprFormatter<Deferred>
    {
        public Deferred Deserialize(in SNode node, SexprSerializerOptions options) => new(node.GetInt32());
        public void Serialize(ref SexprEmitter emitter, Deferred value, SexprSerializerOptions options) => emitter.WriteNumber(value.Value);
    }
}
