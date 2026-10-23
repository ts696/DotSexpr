using DotSexpr.Serializer;

namespace DotSexpr.Tests;

public partial class ConstructorCallTests
{
    [SexprCallUnion(typeof(Not), typeof(And), typeof(Repeat), AllowUnknownCalls = true)]
    public abstract partial record Condition : CallExpression;

    [SexprCall("not")]
    public sealed partial record Not(Condition Operand) : Condition;

    [SexprCall("and")]
    public sealed partial record And(params Condition[] Operands) : Condition;

    [SexprCall("repeat")]
    public sealed partial record Repeat(IntegerExpression Count, params Condition[] Actions) : Condition;

    [SexprCall("cost")]
    public sealed partial record Cost(SymbolExpression Actor, NumberExpression Amount) : CallExpression;

    [SexprCall("zero")]
    public sealed partial record Zero() : CallExpression;

    [SexprCall("value")]
    public sealed partial record Value<T>(T Item) : CallExpression where T : Expression;

    [SexprObject]
    public partial class Document
    {
        public Condition Precondition { get; set; } = new And();
        public Value<IntegerExpression> Value { get; set; } = new(new IntegerExpression(1));
    }

    [Test]
    public void ConstructorReceivesArgumentsAndProvidesName()
    {
        var call = SexprSerializer.Deserialize<Not>("(not (ready ?actor))");
        Assert.That(call.Name, Is.EqualTo("not"));
        Assert.That(call.Operand, Is.TypeOf<Condition.UnknownCall>());
        Assert.That(call.Operand.Name, Is.EqualTo("ready"));
        Assert.That(call.Arguments, Is.EqualTo(new[] { call.Operand }));
        var (name, arguments) = (CallExpression)call;
        Assert.That(name, Is.EqualTo("not"));
        Assert.That(arguments[0], Is.SameAs(call.Operand));
    }

    [TestCase("(and)")]
    [TestCase("(and (ready x))")]
    [TestCase("(and (ready x) (not (injured x)) (repeat 2 (move x) (inspect x)))")]
    [TestCase("(repeat 2)")]
    [TestCase("(repeat 2 (ready x) (not (injured x)))")]
    public void ParamsArgumentsRoundTrip(string text)
    {
        var call = SexprSerializer.Deserialize<Condition>(text);
        Assert.That(SexprSerializer.Serialize(call), Is.EqualTo(text));
        if (call is And and) Assert.That(and.Operands.Length, Is.EqualTo(call.Arguments.Count));
        if (call is Repeat repeat)
        {
            Assert.That(repeat.Count.Value, Is.EqualTo(2));
            Assert.That(repeat.Actions.Length, Is.EqualTo(call.Arguments.Count - 1));
        }
    }

    [TestCase("(not)", "exactly 1")]
    [TestCase("(not (and) (and))", "exactly 1")]
    [TestCase("(repeat)", "at least 1")]
    public void InvalidCountFailsBeforeConstruction(string text, string message)
    {
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Deserialize<Condition>(text))!;
        Assert.That(error.Path, Is.EqualTo("$"));
        Assert.That(error.Message, Does.Contain(message));
    }

    [TestCase("(and 1)", "$[1]")]
    [TestCase("(repeat x)", "$[1]")]
    [TestCase("(not (and (ready) (repeat 2 3)))", "$[1][2][2]")]
    public void InvalidFixedOrParamsTypeReportsLocation(string text, string path)
    {
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Deserialize<Condition>(text))!;
        Assert.That(error.Path, Is.EqualTo(path));
        Assert.That(error.BytePosition, Is.Not.Null);
    }

    [Test]
    public void WithExpressionAndPropertyInitializerAreReflectedInSerialization()
    {
        var original = new Not(new And());
        var changed = original with { Operand = new Condition.UnknownCall("ready", []) };
        Assert.That(SexprSerializer.Serialize(original), Is.EqualTo("(not (and))"));
        Assert.That(SexprSerializer.Serialize(changed), Is.EqualTo("(not (ready))"));
        Assert.That(SexprSerializer.Serialize<Expression>(changed), Is.EqualTo("(not (ready))"));
        var repeat = new Repeat(new IntegerExpression(1)) with
        {
            Count = new IntegerExpression(3), Actions = [changed, new And()]
        };
        Assert.That(SexprSerializer.Serialize(repeat), Is.EqualTo("(repeat 3 (not (ready)) (and))"));
        var and = new And { Operands = [repeat] };
        Assert.That(and.Arguments[0], Is.SameAs(repeat));
    }

    [Test]
    public void ArgumentsCannotBeChangedIndependentlyOfConstructorProperties()
    {
        CallExpression call = new Not(new And());
        var error = Assert.Throws<InvalidOperationException>(() =>
        {
            _ = call with { Arguments = [] };
        })!;
        Assert.That(error.Message, Does.Contain("constructor-bound properties"));
    }

    [Test]
    public void NullConstructorValuesFailWhenSerialized()
    {
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize(new Not(null!)))!;
        Assert.That(error.Path, Is.EqualTo("$[1]"));
        Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize(new And(null!)));
        Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize(new Repeat(new IntegerExpression(1), null!)));
    }

    [Test]
    public void NullParamsElementReportsItsIndex()
    {
        var document = new Document { Precondition = new And(new And(), null!) };
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize(document))!;
        Assert.That(error.Path, Is.EqualTo("$.Precondition[2]"));
    }

    [Test]
    public void FixedConstructorTypesAndEmptyConstructorRoundTrip()
    {
        var cost = SexprSerializer.Deserialize<Cost>("(cost ?actor 1)");
        Assert.That(cost.Actor.Name, Is.EqualTo("?actor"));
        Assert.That(cost.Amount.Value, Is.EqualTo(1));
        Assert.That(SexprSerializer.Serialize(cost), Is.EqualTo("(cost ?actor 1)"));
        Assert.That(SexprSerializer.Serialize(new Zero()), Is.EqualTo("(zero)"));
        Assert.That(SexprSerializer.Deserialize<Zero>("(zero)").Arguments, Is.Empty);
        Assert.Throws<SexprSerializerException>(() => SexprSerializer.Deserialize<Zero>("(zero x)"));
    }

    [Test]
    public void ConstructorCallsWorkInPropertiesAndClosedGenerics()
    {
        const string text = "(:Precondition (not (and (ready x))) :Value (value 7))";
        var document = SexprSerializer.Deserialize<Document>(text);
        Assert.That(document.Value.Item.Value, Is.EqualTo(7));
        Assert.That(SexprSerializer.Serialize(document), Is.EqualTo(text));
        document.Precondition = new Not(new Condition.UnknownCall("not", []));
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize(document))!;
        Assert.That(error.Path, Is.EqualTo("$.Precondition[1][0]"));
    }
}
