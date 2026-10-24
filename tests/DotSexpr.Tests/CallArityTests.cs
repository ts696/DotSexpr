using DotSexpr.Serializer;

namespace DotSexpr.Tests;

public partial class CallArityTests
{
    [SexprCallUnion(typeof(Not), typeof(And), typeof(Zero), AllowUnknownCalls = true)]
    public abstract partial record Condition : CallExpression;

    [SexprCall("not")]
    public partial record Not(Expression Operand) : Condition;

    [SexprCall("and")]
    public sealed partial record And(params Expression[] Items) : Condition;

    [SexprCall("zero")]
    public sealed partial record Zero() : Condition;

    public sealed record MalformedNot(int Count) : Not(new SymbolExpression("x"))
    {
        protected override IReadOnlyList<Expression> GetCallArguments() =>
            Enumerable.Repeat<Expression>(new SymbolExpression("x"), Count).ToArray();
    }

    [SexprObject]
    public partial class Task
    {
        [SexprTag("precondition")]
        public Condition Precondition { get; set; } = new And();
    }

    [Test]
    public void ConstructorArguments_ValidCallsRoundTrip()
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
    public void ConstructorArguments_InvalidInputFailsBeforeReadingChildren(string text, int actualCount)
    {
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Deserialize<Not>(text))!;
        Assert.That(error.Path, Is.EqualTo("$"));
        Assert.That(error.BytePosition, Is.Zero);
        Assert.That(error.Message, Does.Contain("requires exactly 1 argument(s)").And.Contain($"found {actualCount}"));
    }

    [TestCase(0)]
    [TestCase(2)]
    public void ConstructorArguments_InvalidProjectionFailsForDirectAndNestedCalls(int count)
    {
        Not not = new MalformedNot(count);
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
    public void ConstructorArguments_NestedInputRetainsCallLocation(string text)
    {
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Deserialize<Task>(text))!;
        Assert.That(error.Path, Is.EqualTo("$.precondition[1]"));
        Assert.That(error.BytePosition, Is.EqualTo(text.IndexOf("(not", StringComparison.Ordinal)));
    }

    [Test]
    public void ConstructorArguments_ZeroExcludesTheCallName()
    {
        Assert.That(SexprSerializer.Serialize(new Zero()), Is.EqualTo("(zero)"));
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Deserialize<Zero>("(zero x)"))!;
        Assert.That(error.Message, Does.Contain("requires exactly 0 argument(s)").And.Contain("found 1"));
    }

    [Test]
    public void ConstructorArguments_ExpressionParamsAllowAnyCount()
    {
        Assert.That(SexprSerializer.Serialize(new And()), Is.EqualTo("(and)"));
        var and = SexprSerializer.Deserialize<And>("(and x y z)");
        Assert.That(and.Arguments, Has.Count.EqualTo(3));
        Assert.That(SexprSerializer.Serialize(and), Is.EqualTo("(and x y z)"));
    }

    [Test]
    public void ConstructorArguments_NameValidationComesFirst()
    {
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Deserialize<Not>("(and)"))!;
        Assert.That(error.Path, Is.EqualTo("$[0]"));
        Assert.That(error.Message, Does.Contain("Expected call name 'not'"));
    }

    [TestCase("not", 0)]
    [TestCase("not", 1)]
    [TestCase("and", 0)]
    [TestCase("zero", 0)]
    public void UnknownCall_CannotSerializeRegisteredNameAsUnionRoot(string name, int count)
    {
        Condition condition = new Condition.UnknownCall(name,
            Enumerable.Repeat<Expression>(new SymbolExpression("x"), count).ToArray());
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize(condition))!;
        Assert.That(error.Path, Is.EqualTo("$[0]"));
        Assert.That(error.BytePosition, Is.Null);
        Assert.That(error.Message, Does.Contain($"'{name}'").And.Contain("registered").And.Contain("UnknownCall"));
    }

    [TestCase("not", 0)]
    [TestCase("not", 1)]
    [TestCase("and", 0)]
    [TestCase("zero", 0)]
    public void UnknownCall_CannotSerializeRegisteredNameInsideUnion(string name, int count)
    {
        var unknown = new Condition.UnknownCall(name,
            Enumerable.Repeat<Expression>(new SymbolExpression("x"), count).ToArray());
        var task = new Task { Precondition = new And([unknown]) };
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize(task))!;
        Assert.That(error.Path, Is.EqualTo("$.precondition[1][0]"));
        Assert.That(error.Message, Does.Contain($"'{name}'").And.Contain("UnknownCall"));

        task.Precondition = new Condition.UnknownCall("custom", [unknown]);
        error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize(task))!;
        Assert.That(error.Path, Is.EqualTo("$.precondition[1][0]"));
    }

    [TestCase("ready")]
    [TestCase("Not")]
    public void UnknownCall_UnregisteredNamesStillRoundTripAtRootAndInsideUnion(string name)
    {
        Condition condition = new Condition.UnknownCall(name, []);
        var text = SexprSerializer.Serialize(condition);
        Assert.That(text, Is.EqualTo($"({name})"));
        var read = SexprSerializer.Deserialize<Condition>(text);
        Assert.That(read, Is.TypeOf<Condition.UnknownCall>());
        Assert.That(read.Name, Is.EqualTo(name));
        var task = new Task { Precondition = new And([condition]) };
        text = SexprSerializer.Serialize(task);
        Assert.That(SexprSerializer.Serialize(SexprSerializer.Deserialize<Task>(text)), Is.EqualTo(text));
    }
}
