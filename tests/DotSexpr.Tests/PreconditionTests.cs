using DotSexpr.Serializer;

namespace DotSexpr.Tests;

public partial class PreconditionTests
{
    [SexprCallUnion(typeof(And), typeof(Or), typeof(Not), AllowUnknownCalls = true)]
    public abstract partial record ConditionExpression : CallExpression;

    [SexprCall("and")]
    public sealed partial record And : ConditionExpression;

    [SexprCall("or")]
    public sealed partial record Or : ConditionExpression;

    [SexprCall("not", ArgumentCount = 1)]
    public sealed partial record Not : ConditionExpression;

    [SexprObject]
    public partial class Task
    {
        [SexprTag("precondition")]
        public ConditionExpression Precondition { get; set; } = new And();
    }

    public sealed record Unregistered() : ConditionExpression("ready", []);

    [TestCase("(ready ?actor)", typeof(ConditionExpression.UnknownCall))]
    [TestCase("(not (injured ?actor))", typeof(Not))]
    [TestCase("(and (ready ?actor) (reachable ?from ?to) (not (dangerous ?to)))", typeof(And))]
    [TestCase("(or (has-key ?actor) (unlocked ?to))", typeof(Or))]
    [TestCase("(not (or (injured ?actor) (tired ?actor)))", typeof(Not))]
    public void Precondition_AllRequestedShapes_ReadWriteAndPreserveTypes(string expression, Type expectedType)
    {
        var text = $"(:precondition {expression})";
        var task = SexprSerializer.Deserialize<Task>(text);
        Assert.That(task.Precondition.GetType(), Is.EqualTo(expectedType));
        AssertCallTypes(task.Precondition);
        Assert.That(SexprSerializer.Serialize(task), Is.EqualTo(text));
        var root = SexprSerializer.Deserialize<ConditionExpression>(expression);
        Assert.That(root.GetType(), Is.EqualTo(expectedType));
        AssertCallTypes(root);
        Assert.That(SexprSerializer.Serialize(root), Is.EqualTo(expression));
    }

    [Test]
    public void Precondition_MultilineCompoundCondition_ReadsWithoutOptions()
    {
        const string text = """
            (:precondition (and
                (ready ?actor)
                (reachable ?from ?to)
                (not (dangerous ?to))))
            """;
        var task = SexprSerializer.Deserialize<Task>(text);
        Assert.That(task.Precondition, Is.TypeOf<And>());
        AssertCallTypes(task.Precondition);
        Assert.That(SexprSerializer.Serialize(task),
            Is.EqualTo("(:precondition (and (ready ?actor) (reachable ?from ?to) (not (dangerous ?to))))"));
    }

    [Test]
    public void Precondition_UnknownRootDispatchesKnownDescendantsAndRetainsNumbers()
    {
        const string text = "(custom (not (ready ?actor)) 3 0.5)";
        var root = SexprSerializer.Deserialize<ConditionExpression>(text);
        Assert.That(root, Is.TypeOf<ConditionExpression.UnknownCall>());
        Assert.That(root.Arguments[0], Is.TypeOf<Not>());
        Assert.That(root.Arguments[1], Is.EqualTo(new IntegerExpression(3)));
        Assert.That(root.Arguments[2], Is.EqualTo(new NumberExpression(0.5)));
        Assert.That(SexprSerializer.Serialize(root), Is.EqualTo(text));
    }

    [Test]
    public void Precondition_UnionArraysAcceptPredicatesAndOperators()
    {
        const string text = "((ready ?actor) (not (injured ?actor)) (and (ready ?actor)))";
        var roots = SexprSerializer.Deserialize<ConditionExpression[]>(text);
        Assert.That(roots.Select(c => c.GetType()),
            Is.EqualTo(new[] { typeof(ConditionExpression.UnknownCall), typeof(Not), typeof(And) }));
        Assert.That(SexprSerializer.Serialize(roots), Is.EqualTo(text));
    }

    [Test]
    public void Precondition_GeneratedUnknownCallSupportsDirectReadWrite()
    {
        var predicate = new ConditionExpression.UnknownCall("ready", [new SymbolExpression("?actor")]);
        Assert.That(SexprSerializer.Serialize(predicate), Is.EqualTo("(ready ?actor)"));
        var result = SexprSerializer.Deserialize<ConditionExpression.UnknownCall>("(ready ?actor)");
        Assert.That(result.Name, Is.EqualTo("ready"));
        Assert.That(result.Arguments, Is.EqualTo(predicate.Arguments));
        var array = SexprSerializer.Deserialize<ConditionExpression.UnknownCall[]>("((ready ?actor))");
        Assert.That(SexprSerializer.Serialize(array), Is.EqualTo("((ready ?actor))"));
    }

    [Test]
    public void Precondition_UnknownCallErrorsKeepPropertyAndArgumentLocations()
    {
        var error = Assert.Throws<SexprSerializerException>(() =>
            SexprSerializer.Deserialize<Task>("(:precondition (ready (3)))"))!;
        Assert.That(error.Path, Is.EqualTo("$.precondition[1][0]"));
        Assert.That(error.BytePosition, Is.EqualTo(23));
        var value = new Task { Precondition = new ConditionExpression.UnknownCall("bad name", []) };
        error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize(value))!;
        Assert.That(error.Path, Is.EqualTo("$.precondition[0]"));
        Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize<ConditionExpression>(new Unregistered()));
    }

    private static void AssertCallTypes(Expression expression)
    {
        if (expression is not CallExpression call) return;
        var expected = call.Name switch
        {
            "and" => typeof(And),
            "or" => typeof(Or),
            "not" => typeof(Not),
            _ => typeof(ConditionExpression.UnknownCall)
        };
        Assert.That(call.GetType(), Is.EqualTo(expected), call.Name);
        foreach (var argument in call.Arguments) AssertCallTypes(argument);
    }
}
