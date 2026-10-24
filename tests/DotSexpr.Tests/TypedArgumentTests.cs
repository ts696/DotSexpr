using DotSexpr.Emitter;
using DotSexpr.Serializer;
using DotSexpr.Serializer.Resolvers;

namespace DotSexpr.Tests;

public partial class TypedArgumentTests
{
    [SexprCallUnion(typeof(And), typeof(Not), typeof(Repeat), typeof(Values), typeof(WithStep), AllowUnknownCalls = true)]
    public abstract partial record Condition : CallExpression;

    [SexprCall("and")]
    public sealed partial record And(params Expression[] Items) : Condition;

    [SexprCall("not")]
    public sealed partial record Not(Condition Operand) : Condition;

    [SexprCall("repeat")]
    public sealed partial record Repeat(IntegerExpression Count, CallExpression Action) : Condition;

    [SexprCall("values")]
    public sealed partial record Values(SymbolExpression Symbol, IntegerExpression Integer, NumberExpression Number, Expression Other) : Condition;

    [SexprCall("with-step")]
    public sealed partial record WithStep(Step Step) : Condition;

    [SexprCall("step")]
    public sealed partial record Step(IntegerExpression Count) : CallExpression;

    [SexprCall("open")]
    public sealed partial record Open(params Expression[] Items) : CallExpression;

    [SexprObject]
    public partial class Document
    {
        public Condition[] Conditions { get; set; } = [];
    }

    [Test]
    public void DeclaredTypesProduceTypedArgumentsAndRoundTrip()
    {
        const string text = "(values ?actor 9007199254740993 0.5 (not (ready ?actor)))";
        var call = SexprSerializer.Deserialize<Values>(text);
        Assert.That(call.Arguments[0], Is.EqualTo(new SymbolExpression("?actor")));
        Assert.That(call.Arguments[1], Is.EqualTo(new IntegerExpression(9007199254740993)));
        Assert.That(call.Arguments[2], Is.EqualTo(new NumberExpression(0.5)));
        Assert.That(SexprSerializer.Serialize(call), Is.EqualTo(text));
    }

    [TestCase("(values 1 2 0.5 x)", 1, "1")]
    [TestCase("(values x 0.5 0.5 x)", 2, "0.5")]
    [TestCase("(values x 2 x x)", 3, "x x")]
    [TestCase("(repeat ?count (move x))", 1, "?count")]
    [TestCase("(repeat 2 x)", 2, "x")]
    [TestCase("(not 1)", 1, "1")]
    public void WrongInputTypeReportsArgumentLocation(string text, int index, string value)
    {
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Deserialize<Condition>(text))!;
        Assert.That(error.Path, Is.EqualTo($"$[{index}]"));
        Assert.That(error.BytePosition, Is.EqualTo(text.IndexOf(value, StringComparison.Ordinal)));
    }

    [Test]
    public void NullOutputReportsArgumentThroughPropertiesAndArrays()
    {
        var call = new Repeat(new IntegerExpression(2), new CallExpression("move", [])) with
        {
            Count = null!
        };
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize(call))!;
        Assert.That(error.Path, Is.EqualTo("$[1]"));
        Assert.That(error.Message, Does.Contain("IntegerExpression"));
        var document = new Document { Conditions = [new And([call])] };
        error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize(document))!;
        Assert.That(error.Path, Is.EqualTo("$.Conditions[0][1][1]"));
        Assert.That(error.BytePosition, Is.Null);
    }

    [Test]
    public void NestedInputTypeErrorPreservesDeepLocation()
    {
        const string text = "(:Conditions ((custom (repeat ?count (move x)))))";
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Deserialize<Document>(text))!;
        Assert.That(error.Path, Is.EqualTo("$.Conditions[0][1][1]"));
        Assert.That(error.BytePosition, Is.EqualTo(text.IndexOf("?count", StringComparison.Ordinal)));
    }

    [Test]
    public void DedicatedCallUsesItsNameAndRuntimeTypeConstraints()
    {
        const string text = "(with-step (step 7))";
        var call = SexprSerializer.Deserialize<WithStep>(text);
        Assert.That(call.Arguments[0], Is.TypeOf<Step>());
        Assert.That(SexprSerializer.Serialize(call), Is.EqualTo(text));
        var error = Assert.Throws<SexprSerializerException>(() =>
            SexprSerializer.Deserialize<WithStep>("(with-step (other 7))"))!;
        Assert.That(error.Path, Is.EqualTo("$[1][0]"));
    }

    [Test]
    public void RecursiveUnionArgumentResolvesKnownAndUnknownCalls()
    {
        const string text = "(not (and (not (ready x)) (repeat 2 (move x))))";
        var call = SexprSerializer.Deserialize<Not>(text);
        Assert.That(call.Arguments[0], Is.TypeOf<And>());
        var and = (And)call.Arguments[0];
        Assert.That(and.Arguments[0], Is.TypeOf<Not>());
        Assert.That(and.Arguments[1], Is.TypeOf<Repeat>());
        Assert.That(SexprSerializer.Serialize(call), Is.EqualTo(text));
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize(
            new Not(new Condition.UnknownCall("not", []))))!;
        Assert.That(error.Path, Is.EqualTo("$[1][0]"));
    }

    [Test]
    public void NumberArgumentAcceptsIntegerSyntaxAfterNormalization()
    {
        var call = new Values(new SymbolExpression("x"), new IntegerExpression(2), new NumberExpression(1), new SymbolExpression("y"));
        var text = SexprSerializer.Serialize(call);
        Assert.That(text, Is.EqualTo("(values x 2 1 y)"));
        var result = SexprSerializer.Deserialize<Values>(text);
        Assert.That(result.Arguments[2], Is.EqualTo(new NumberExpression(1)));
    }

    [TestCase("(open)")]
    [TestCase("(open x 2 0.5 (f))")]
    public void ExpressionParamsAllowArbitraryArguments(string text)
    {
        Assert.That(SexprSerializer.Serialize(SexprSerializer.Deserialize<Open>(text)), Is.EqualTo(text));
    }

    [Test]
    public void DeclaredArgumentUsesCustomFormatterInBothDirections()
    {
        var options = new SexprSerializerOptions { Resolver = new CustomResolver() };
        var call = SexprSerializer.Deserialize<WithStep>("(with-step (step 99))", options);
        Assert.That(((Step)call.Arguments[0]).Arguments, Is.EqualTo(new Expression[] { new IntegerExpression(7) }));
        Assert.That(SexprSerializer.Serialize(call, options), Is.EqualTo("(with-step (step 7))"));
        Condition nested = new And([call]);
        Assert.That(SexprSerializer.Serialize(nested, options), Is.EqualTo("(and (with-step (step 7)))"));
    }

    private sealed class CustomResolver : ISexprFormatterResolver
    {
        public ISexprFormatter<T>? GetFormatter<T>() => typeof(T) == typeof(Step)
            ? (ISexprFormatter<T>)(object)new CustomStepFormatter()
            : DefaultResolver.Instance.GetFormatter<T>();
    }

    private sealed class CustomStepFormatter : ISexprFormatter<Step>
    {
        public Step Deserialize(in SNode node, SexprSerializerOptions options) => new(new IntegerExpression(7));

        public void Serialize(ref SexprEmitter emitter, Step value, SexprSerializerOptions options)
        {
            emitter.BeginList();
            emitter.WriteSymbol("step");
            emitter.WriteNumber(7);
            emitter.EndList();
        }
    }
}
