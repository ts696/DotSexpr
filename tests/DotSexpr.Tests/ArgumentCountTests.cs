using DotSexpr.Serializer;

namespace DotSexpr.Tests;

public partial class ArgumentCountTests
{
    [SexprCallUnion(typeof(Not), typeof(And), typeof(Zero), AllowUnknownCalls = true)]
    public abstract partial record Condition : CallExpression;

    [SexprCall("not", ArgumentCount = 1)]
    public sealed partial record Not : Condition;

    [SexprCall("and")]
    public sealed partial record And : Condition;

    [SexprCall("zero", ArgumentCount = 0)]
    public sealed partial record Zero : Condition;

    [SexprObject]
    public partial class Task
    {
        [SexprTag("precondition")]
        public Condition Precondition { get; set; } = new And();
    }

    [Test]
    public void ArgumentCount_ValidCallsRoundTrip()
    {
        const string text = "(:precondition (and (not (ready ?actor)) (zero) (custom 1 2)))";
        var task = SexprSerializer.Deserialize<Task>(text);
        Assert.That(SexprSerializer.Serialize(task), Is.EqualTo(text));
        var not = SexprSerializer.Deserialize<Not>("(not ?actor)");
        Assert.That(not.Arguments, Is.EqualTo(new[] { new SymbolExpression("?actor") }));
        Assert.That(SexprSerializer.Serialize(not), Is.EqualTo("(not ?actor)"));
    }

    [TestCase("(not)", 0)]
    [TestCase("(not x y)", 2)]
    [TestCase("(not \"invalid\" \"invalid\")", 2)]
    public void ArgumentCount_InvalidInputFailsBeforeReadingChildren(string text, int actualCount)
    {
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Deserialize<Not>(text))!;
        Assert.That(error.Path, Is.EqualTo("$"));
        Assert.That(error.BytePosition, Is.Zero);
        Assert.That(error.Message, Does.Contain("requires exactly 1 argument(s)").And.Contain($"found {actualCount}"));
    }

    [TestCase(0)]
    [TestCase(2)]
    public void ArgumentCount_InvalidOutputFailsForDirectAndNestedCalls(int count)
    {
        var not = new Not([new SymbolExpression("x")]) with
        {
            Arguments = Enumerable.Repeat<Expression>(new SymbolExpression("x"), count).ToArray()
        };
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize(not))!;
        Assert.That(error.Path, Is.EqualTo("$"));
        Assert.That(error.Message, Does.Contain("requires exactly 1 argument(s)").And.Contain($"found {count}"));
        var task = new Task { Precondition = new And([not]) };
        error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize(task))!;
        Assert.That(error.Path, Is.EqualTo("$.precondition[1]"));
        Assert.That(error.BytePosition, Is.Null);
    }

    [TestCase("(:precondition (and (not)))")]
    [TestCase("(:precondition (custom (not x y)))")]
    public void ArgumentCount_NestedInputRetainsCallLocation(string text)
    {
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Deserialize<Task>(text))!;
        Assert.That(error.Path, Is.EqualTo("$.precondition[1]"));
        Assert.That(error.BytePosition, Is.EqualTo(text.IndexOf("(not", StringComparison.Ordinal)));
    }

    [Test]
    public void ArgumentCount_ZeroExcludesTheCallName()
    {
        Assert.That(SexprSerializer.Serialize(new Zero()), Is.EqualTo("(zero)"));
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Deserialize<Zero>("(zero x)"))!;
        Assert.That(error.Message, Does.Contain("requires exactly 0 argument(s)").And.Contain("found 1"));
        Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize(new Zero([new SymbolExpression("x")])));
    }

    [Test]
    public void ArgumentCount_OmittedConstraintRemainsUnrestricted()
    {
        Assert.That(SexprSerializer.Serialize(new And()), Is.EqualTo("(and)"));
        var and = SexprSerializer.Deserialize<And>("(and x y z)");
        Assert.That(and.Arguments, Has.Count.EqualTo(3));
        Assert.That(SexprSerializer.Serialize(and), Is.EqualTo("(and x y z)"));
    }

    [Test]
    public void ArgumentCount_NameValidationComesFirst()
    {
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Deserialize<Not>("(and)"))!;
        Assert.That(error.Path, Is.EqualTo("$[0]"));
        Assert.That(error.Message, Does.Contain("Expected call name 'not'"));
    }
}
