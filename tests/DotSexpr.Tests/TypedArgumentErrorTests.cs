using DotSexpr.Emitter;
using DotSexpr.Serializer;
using DotSexpr.Serializer.Resolvers;

namespace DotSexpr.Tests;

public partial class TypedArgumentErrorTests
{
    [SexprCall("fixed", ArgumentTypes = new[] { typeof(SymbolExpression) })]
    public sealed partial record Fixed : CallExpression;

    [SexprCall("positional")]
    public sealed partial record Positional(SymbolExpression Item) : CallExpression;

    [SexprCall("variadic")]
    public sealed partial record Variadic(params SymbolExpression[] Items) : CallExpression;

    [SexprObject]
    public partial class Document
    {
        public Positional Value { get; set; } = new(new SymbolExpression("x"));
    }

    [TestCase(0, false)]
    [TestCase(1, false)]
    [TestCase(2, false)]
    [TestCase(0, true)]
    [TestCase(1, true)]
    [TestCase(2, true)]
    public void CustomFormatterErrorRetainsArgumentIndex(int kind, bool located)
    {
        var resolver = new ErrorResolver(located);
        var options = new SexprSerializerOptions { Resolver = resolver };
        switch (kind)
        {
            case 0: Check(new Fixed([new SymbolExpression("x")])); break;
            case 1: Check(new Positional(new SymbolExpression("x"))); break;
            default: Check(new Variadic(new SymbolExpression("x"))); break;
        }

        void Check<T>(T value)
        {
            var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize(value, options))!;
            Assert.That(error.Path, Is.EqualTo("$[1]"));
            if (located)
            {
                Assert.That(error, Is.SameAs(resolver.LastError));
                Assert.That(error.BytePosition, Is.Zero);
            }
            else
            {
                Assert.That(error.InnerException, Is.SameAs(resolver.LastError));
                Assert.That(error.BytePosition, Is.Null);
            }
            error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize(new[] { value }, options))!;
            Assert.That(error.Path, Is.EqualTo("$[0][1]"));
        }
    }

    [Test]
    public void UnlocatedErrorRetainsModelPropertyAndArgumentIndex()
    {
        var options = new SexprSerializerOptions { Resolver = new ErrorResolver(false) };
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize(new Document(), options))!;
        Assert.That(error.Path, Is.EqualTo("$.Value[1]"));
        Assert.That(error.Message, Does.Contain("bad"));
    }

    private sealed class ErrorResolver(bool located) : ISexprFormatterResolver, ISexprFormatter<SymbolExpression>
    {
        public SexprSerializerException? LastError { get; private set; }

        public ISexprFormatter<T>? GetFormatter<T>() => typeof(T) == typeof(SymbolExpression)
            ? (ISexprFormatter<T>)(object)this : DefaultResolver.Instance.GetFormatter<T>();

        public SymbolExpression Deserialize(in SNode node, SexprSerializerOptions options) => new(node.GetSymbol());

        public void Serialize(ref SexprEmitter emitter, SymbolExpression value, SexprSerializerOptions options)
        {
            using var document = SDocument.Parse("x");
            LastError = located ? new SexprSerializerException("bad", document.RootNode) : new SexprSerializerException("bad");
            throw LastError;
        }
    }
}
