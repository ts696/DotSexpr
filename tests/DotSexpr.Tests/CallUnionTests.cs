using DotSexpr.Emitter;
using DotSexpr.Serializer;
using DotSexpr.Serializer.Resolvers;

namespace DotSexpr.Tests;

public partial class CallUnionTests
{
    [SexprCallUnion(typeof(Move), typeof(Jump), typeof(Step))]
    public abstract partial record ActionExpression : CallExpression;

    [SexprCall("move")]
    public sealed partial record Move(params Expression[] Items) : ActionExpression;

    [SexprCall("jump")]
    public sealed partial record Jump(params Expression[] Items) : ActionExpression;

    [SexprCall("step")]
    public sealed partial record Step(params Expression[] Items) : ActionExpression;

    // Being a derived type (or sharing a registered name) does not register a call.
    public sealed record Unregistered() : ActionExpression("move", []);

    public sealed record ForeignCall() : CallExpression("move", []);

    [SexprObject]
    public partial class Plan
    {
        public ActionExpression[] Actions { get; set; } = [];
    }

    [TestCase("(move 1)", typeof(Move))]
    [TestCase("(jump 0.5)", typeof(Jump))]
    [TestCase("(step)", typeof(Step))]
    public void Union_DispatchesRegisteredNameAndRoundTrips(string text, Type expectedType)
    {
        var action = SexprSerializer.Deserialize<ActionExpression>(text);
        Assert.That(action.GetType(), Is.EqualTo(expectedType));
        Assert.That(SexprSerializer.Serialize(action), Is.EqualTo(text));
    }

    [Test]
    public void Union_ArraysAndProperties_AcceptAllRegisteredCalls()
    {
        const string actions = "((move ?to 3) (jump 0.5) (step))";
        var result = SexprSerializer.Deserialize<ActionExpression[]>(actions);
        Assert.That(result.Select(a => a.GetType()), Is.EqualTo(new[] { typeof(Move), typeof(Jump), typeof(Step) }));
        Assert.That(SexprSerializer.Serialize(result), Is.EqualTo(actions));

        const string text = "(:Actions " + actions + ")";
        var plan = SexprSerializer.Deserialize<Plan>(text);
        Assert.That(plan.Actions.Select(a => a.GetType()), Is.EqualTo(new[] { typeof(Move), typeof(Jump), typeof(Step) }));
        Assert.That(SexprSerializer.Serialize(plan), Is.EqualTo(text));
    }

    [Test]
    public void Union_UnknownName_ReportsPropertyArrayAndNamePath()
    {
        var error = Assert.Throws<SexprSerializerException>(() =>
            SexprSerializer.Deserialize<Plan>("(:Actions ((move) (fly)))"))!;
        Assert.That(error.Path, Is.EqualTo("$.Actions[1][0]"));
        Assert.That(error.BytePosition, Is.EqualTo(19));
        Assert.That(error.Message, Does.Contain("'fly'").And.Contain("not registered"));
    }

    [TestCase("(Move)", "$[0]")]
    [TestCase("(fly)", "$[0]")]
    [TestCase("(1)", "$[0]")]
    [TestCase("(\"move\")", "$[0]")]
    [TestCase("()", "$")]
    [TestCase("move", "$")]
    [TestCase("1", "$")]
    public void Union_RejectsUnknownNameAndInvalidShape(string text, string path)
    {
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Deserialize<ActionExpression>(text))!;
        Assert.That(error.Path, Is.EqualTo(path));
    }

    [Test]
    public void Union_UnregisteredRuntimeType_ReportsNestedSerializationPath()
    {
        var error = Assert.Throws<SexprSerializerException>(() =>
            SexprSerializer.Serialize(new Plan { Actions = [new Unregistered()] }))!;
        Assert.That(error.Path, Is.EqualTo("$.Actions[0]"));
        Assert.That(error.Message, Does.Contain("not registered"));
    }

    [Test]
    public void Union_ResolvesNestedRegisteredNamesWithoutFlags()
    {
        const string text = "(move (jump (step)))";
        var action = SexprSerializer.Deserialize<ActionExpression>(text);
        Assert.That(action.Arguments[0], Is.TypeOf<Jump>());
        Assert.That(((Jump)action.Arguments[0]).Arguments[0], Is.TypeOf<Step>());
        Assert.That(SexprSerializer.Serialize(action), Is.EqualTo(text));
    }

    [TestCase("(move (fly 3))", "$[1][0]", 7)]
    [TestCase("(move (jump (fly)))", "$[1][1][0]", 13)]
    public void Union_RejectsUnknownNamesAtEveryLevelByDefault(string text, string path, int position)
    {
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Deserialize<ActionExpression>(text))!;
        Assert.That(error.Path, Is.EqualTo(path));
        Assert.That(error.BytePosition, Is.EqualTo(position));
    }

    [Test]
    public void Union_RejectsUnknownNestedNamesWhenSerializing()
    {
        ActionExpression action = new Move([new Jump([new CallExpression("fly", [])])]);
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize(action))!;
        Assert.That(error.Path, Is.EqualTo("$[1][1][0]"));
    }

    [Test]
    public void Union_DoesNotConstrainUntypedCalls()
    {
        Assert.That(SexprSerializer.Deserialize<CallExpression>("(fly)").Name, Is.EqualTo("fly"));
        Assert.That(SexprSerializer.Deserialize<Expression>("(jump)"), Is.TypeOf<CallExpression>());
        Assert.Throws<SexprSerializerException>(() => SexprSerializer.Deserialize<Move>("(jump)"));
    }

    [Test]
    public void Union_DelegatesToCaseFormatterFromCustomResolver()
    {
        var options = new SexprSerializerOptions { Resolver = new CustomResolver() };
        var action = SexprSerializer.Deserialize<ActionExpression>("(step)", options);
        Assert.That(action.Arguments, Is.EqualTo(new[] { new IntegerExpression(7) }));
        Assert.That(SexprSerializer.Serialize<ActionExpression>(new Step(), options), Is.EqualTo("(step 7)"));
    }

    [Test]
    public void Union_NestedCallsDelegateToCaseFormatterFromCustomResolver()
    {
        var options = new SexprSerializerOptions { Resolver = new CustomResolver() };
        ActionExpression action = new Move([new Jump([new Step()])]);
        Assert.That(SexprSerializer.Serialize(action, options), Is.EqualTo("(move (jump (step 7)))"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Union_NestedUnregisteredTypeCannotUseARegisteredName(bool foreign)
    {
        Expression call = foreign ? new ForeignCall() : new Unregistered();
        var plan = new Plan { Actions = [new Move([new Jump([call])])] };
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize(plan))!;
        Assert.That(error.Path, Is.EqualTo("$.Actions[0][1][1]"));
        Assert.That(error.Message, Does.Contain("not registered"));
    }

    private sealed class CustomResolver : ISexprFormatterResolver
    {
        public ISexprFormatter<T>? GetFormatter<T>() => typeof(T) == typeof(Step)
            ? (ISexprFormatter<T>)(object)new CustomStepFormatter()
            : DefaultResolver.Instance.GetFormatter<T>();
    }

    private sealed class CustomStepFormatter : ISexprFormatter<Step>
    {
        public Step Deserialize(in SNode node, SexprSerializerOptions options) => new([new IntegerExpression(7)]);
        public void Serialize(ref SexprEmitter emitter, Step value, SexprSerializerOptions options)
        {
            emitter.BeginList();
            emitter.WriteSymbol("step");
            emitter.WriteNumber(7);
            emitter.EndList();
        }
    }
}
