using DotSexpr.Emitter;
using DotSexpr.Serializer;
using DotSexpr.Serializer.Resolvers;

namespace DotSexpr.Tests;

public partial class CallUnionTests
{
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
        var options = new SexprSerializerOptions { Resolver = new ActionResolver() };
        var action = SexprSerializer.Deserialize<ActionExpression>("(step)", options);
        Assert.That(action.Arguments, Is.EqualTo(new[] { new IntegerExpression(7) }));
        Assert.That(SexprSerializer.Serialize<ActionExpression>(new Step(), options), Is.EqualTo("(step 7)"));
    }

    [Test]
    public void Union_NestedCallsDelegateToCaseFormatterFromCustomResolver()
    {
        var options = new SexprSerializerOptions { Resolver = new ActionResolver() };
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

    [Test]
    public void Union_AttributeDispatchesNestedCallsWithoutOptions()
    {
        const string text = "(and (reachable ?from ?to) (or (visited ?from) (visited ?to)))";
        var condition = SexprSerializer.Deserialize<OpenCondition>(text);
        Assert.That(condition, Is.TypeOf<OpenAnd>());
        Assert.That(condition.Arguments[0], Is.TypeOf<OpenCondition.UnknownCall>());
        var reachable = (CallExpression)condition.Arguments[0];
        Assert.That(reachable.Name, Is.EqualTo("reachable"));
        Assert.That(reachable.Arguments, Is.EqualTo(new[] { new SymbolExpression("?from"), new SymbolExpression("?to") }));
        Assert.That(condition.Arguments[1], Is.TypeOf<OpenOr>());
        var or = (OpenOr)condition.Arguments[1];
        Assert.That(or.Arguments, Is.All.TypeOf<OpenCondition.UnknownCall>());
        Assert.That(or.Arguments.Cast<CallExpression>().Select(c => c.Name), Is.EqualTo(new[] { "visited", "visited" }));
        Assert.That(SexprSerializer.Serialize(condition), Is.EqualTo(text));
    }

    [Test]
    public void Union_AttributeAppliesToArraysAndPropertiesWithoutLeakingToSiblings()
    {
        const string text = "(:Conditions ((and (or)) (or (custom))) :Other (and (or)))";
        var plan = SexprSerializer.Deserialize<OpenConditionDocument>(text);
        Assert.That(plan.Conditions.Select(c => c.GetType()), Is.EqualTo(new[] { typeof(OpenAnd), typeof(OpenOr) }));
        Assert.That(plan.Conditions[0].Arguments[0], Is.TypeOf<OpenOr>());
        Assert.That(plan.Conditions[1].Arguments[0], Is.TypeOf<OpenCondition.UnknownCall>());
        Assert.That(plan.Other, Is.TypeOf<CallExpression>());
        Assert.That(((CallExpression)plan.Other).Arguments[0], Is.TypeOf<CallExpression>());
        Assert.That(SexprSerializer.Serialize(plan), Is.EqualTo(text));
    }

    [Test]
    public void Union_AttributeDoesNotChangeUntypedRootDeserialization()
    {
        var call = SexprSerializer.Deserialize<CallExpression>("(and (or))");
        Assert.That(call, Is.TypeOf<CallExpression>());
        Assert.That(call.Arguments[0], Is.TypeOf<CallExpression>());
        var condition = SexprSerializer.Deserialize<OpenCondition>("(custom (and))");
        Assert.That(condition, Is.TypeOf<OpenCondition.UnknownCall>());
        Assert.That(condition.Arguments[0], Is.TypeOf<OpenAnd>());
    }

    [TestCase("(and (or (3)))", "$[1][1][0]", 10)]
    [TestCase("(and (custom ()))", "$[1][1]", 13)]
    public void Union_InvalidNestedCallsPreserveErrorPaths(string text, string path, int position)
    {
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Deserialize<OpenCondition>(text))!;
        Assert.That(error.Path, Is.EqualTo(path));
        Assert.That(error.BytePosition, Is.EqualTo(position));
    }

    [Test]
    public void Union_AttributePreservesCustomCaseResolverAndOptionsWithoutMutation()
    {
        var resolver = new OpenResolver();
        var options = new SexprSerializerOptions { Resolver = resolver, MaxDepth = 12 };
        var condition = SexprSerializer.Deserialize<OpenCondition>("(and (or))", options);
        Assert.That(condition.Arguments[0], Is.TypeOf<OpenOr>());
        Assert.That(((OpenOr)condition.Arguments[0]).Arguments, Is.EqualTo(new[] { new IntegerExpression(7) }));
        Assert.That(resolver.Formatter.ObservedDepth, Is.EqualTo(12));
        Assert.That(options.Resolver, Is.SameAs(resolver));
        Assert.That(options.MaxDepth, Is.EqualTo(12));
        Assert.That(SexprSerializer.Deserialize<Expression>("(or)", options), Is.TypeOf<CallExpression>());
    }

    [Test]
    public void Union_ArraysAcceptPredicatesAndOperators()
    {
        const string text = "((ready ?actor) (not (injured ?actor)) (and (ready ?actor)))";
        var roots = SexprSerializer.Deserialize<Condition[]>(text);
        Assert.That(roots.Select(c => c.GetType()),
            Is.EqualTo(new[] { typeof(Condition.UnknownCall), typeof(Not), typeof(And) }));
        Assert.That(SexprSerializer.Serialize(roots), Is.EqualTo(text));
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
    public void Union_UnknownIntermediateCallStillDispatchesKnownDescendants()
    {
        const string text = "(and (custom (or (visited ?x))) 2 0.5)";
        var condition = SexprSerializer.Deserialize<OpenCondition>(text);
        Assert.That(condition.Arguments[0], Is.TypeOf<OpenCondition.UnknownCall>());
        var custom = (CallExpression)condition.Arguments[0];
        Assert.That(custom.Name, Is.EqualTo("custom"));
        Assert.That(custom.Arguments[0], Is.TypeOf<OpenOr>());
        Assert.That(condition.Arguments[1], Is.EqualTo(new IntegerExpression(2)));
        Assert.That(condition.Arguments[2], Is.EqualTo(new NumberExpression(0.5)));
        Assert.That(SexprSerializer.Serialize(condition), Is.EqualTo(text));
    }

    [Test]
    public void Union_UnknownRootDispatchesKnownDescendantsAndRetainsNumbers()
    {
        const string text = "(custom (not (ready ?actor)) 3 0.5)";
        var root = SexprSerializer.Deserialize<Condition>(text);
        Assert.That(root, Is.TypeOf<Condition.UnknownCall>());
        Assert.That(root.Arguments[0], Is.TypeOf<Not>());
        Assert.That(root.Arguments[1], Is.EqualTo(new IntegerExpression(3)));
        Assert.That(root.Arguments[2], Is.EqualTo(new NumberExpression(0.5)));
        Assert.That(SexprSerializer.Serialize(root), Is.EqualTo(text));
    }

    [Test]
    public void Union_GeneratedUnknownCallSupportsDirectReadWrite()
    {
        var predicate = new Condition.UnknownCall("ready", [new SymbolExpression("?actor")]);
        Assert.That(SexprSerializer.Serialize(predicate), Is.EqualTo("(ready ?actor)"));
        var result = SexprSerializer.Deserialize<Condition.UnknownCall>("(ready ?actor)");
        Assert.That(result.Name, Is.EqualTo("ready"));
        Assert.That(result.Arguments, Is.EqualTo(predicate.Arguments));
        var array = SexprSerializer.Deserialize<Condition.UnknownCall[]>("((ready ?actor))");
        Assert.That(SexprSerializer.Serialize(array), Is.EqualTo("((ready ?actor))"));
    }

    [Test]
    public void Union_UnknownCallErrorsKeepPropertyAndArgumentLocations()
    {
        var error = Assert.Throws<SexprSerializerException>(() =>
            SexprSerializer.Deserialize<ConditionDocument>("(:precondition (ready (3)))"))!;
        Assert.That(error.Path, Is.EqualTo("$.precondition[1][0]"));
        Assert.That(error.BytePosition, Is.EqualTo(23));
        var value = new ConditionDocument { Precondition = new Condition.UnknownCall("bad name", []) };
        error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize(value))!;
        Assert.That(error.Path, Is.EqualTo("$.precondition[0]"));
        Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize<Condition>(new UnregisteredCondition()));
    }

    [TestCase("(ready ?actor)", typeof(Condition.UnknownCall))]
    [TestCase("(not (injured ?actor))", typeof(Not))]
    [TestCase("(and (ready ?actor) (reachable ?from ?to) (not (dangerous ?to)))", typeof(And))]
    [TestCase("(or (has-key ?actor) (unlocked ?to))", typeof(Or))]
    [TestCase("(not (or (injured ?actor) (tired ?actor)))", typeof(Not))]
    public void Union_LogicalCallsRoundTripAtRootAndInProperties(string expression, Type expectedType)
    {
        var text = $"(:precondition {expression})";
        var task = SexprSerializer.Deserialize<ConditionDocument>(text);
        Assert.That(task.Precondition.GetType(), Is.EqualTo(expectedType));
        AssertConditionTypes(task.Precondition);
        Assert.That(SexprSerializer.Serialize(task), Is.EqualTo(text));
        var root = SexprSerializer.Deserialize<Condition>(expression);
        Assert.That(root.GetType(), Is.EqualTo(expectedType));
        AssertConditionTypes(root);
        Assert.That(SexprSerializer.Serialize(root), Is.EqualTo(expression));
    }

    [Test]
    public void Union_MultilineLogicalCallsRoundTrip()
    {
        const string text = """
            (:precondition (and
                (ready ?actor)
                (reachable ?from ?to)
                (not (dangerous ?to))))
            """;
        var task = SexprSerializer.Deserialize<ConditionDocument>(text);
        Assert.That(task.Precondition, Is.TypeOf<And>());
        AssertConditionTypes(task.Precondition);
        Assert.That(SexprSerializer.Serialize(task),
            Is.EqualTo("(:precondition (and (ready ?actor) (reachable ?from ?to) (not (dangerous ?to))))"));
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
        var task = new ConditionDocument { Precondition = new And([unknown]) };
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
        var task = new ConditionDocument { Precondition = new And([condition]) };
        text = SexprSerializer.Serialize(task);
        Assert.That(SexprSerializer.Serialize(SexprSerializer.Deserialize<ConditionDocument>(text)), Is.EqualTo(text));
    }

    private static void AssertConditionTypes(Expression expression)
    {
        if (expression is not CallExpression call) return;
        var expected = call.Name switch
        {
            "and" => typeof(And),
            "or" => typeof(Or),
            "not" => typeof(Not),
            "zero" => typeof(Zero),
            _ => typeof(Condition.UnknownCall)
        };
        Assert.That(call.GetType(), Is.EqualTo(expected), call.Name);
        foreach (var argument in call.Arguments) AssertConditionTypes(argument);
    }

    // Registered call names only.
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

    // Unknown calls and arbitrary expression arguments are allowed.
    [SexprCallUnion(typeof(OpenAnd), typeof(OpenOr), AllowUnknownCalls = true)]
    public abstract partial record OpenCondition : CallExpression;

    [SexprCall("and")]
    public sealed partial record OpenAnd(params Expression[] Items) : OpenCondition;

    [SexprCall("or")]
    public sealed partial record OpenOr(params Expression[] Items) : OpenCondition;

    [SexprObject]
    public partial class OpenConditionDocument
    {
        public OpenCondition[] Conditions { get; set; } = [];
        public Expression Other { get; set; } = new SymbolExpression("default");
    }

    // Logical operators declare recursive union arguments.
    [SexprCallUnion(typeof(And), typeof(Or), typeof(Not), typeof(Zero), AllowUnknownCalls = true)]
    public abstract partial record Condition : CallExpression;

    [SexprCall("and")]
    public sealed partial record And(params Condition[] Operands) : Condition;

    [SexprCall("or")]
    public sealed partial record Or(params Condition[] Operands) : Condition;

    [SexprCall("not")]
    public sealed partial record Not(Condition Operand) : Condition;

    [SexprObject]
    public partial class ConditionDocument
    {
        [SexprTag("precondition")]
        public Condition Precondition { get; set; } = new And();
    }

    public sealed record UnregisteredCondition() : Condition("ready", []);

    [SexprCall("zero")]
    public sealed partial record Zero() : Condition;

    private sealed class ActionResolver : ISexprFormatterResolver
    {
        public ISexprFormatter<T>? GetFormatter<T>() => typeof(T) == typeof(Step)
            ? (ISexprFormatter<T>)(object)new ActionStepFormatter()
            : DefaultResolver.Instance.GetFormatter<T>();
    }

    private sealed class ActionStepFormatter : ISexprFormatter<Step>
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

    private sealed class OpenResolver : ISexprFormatterResolver
    {
        public OpenOrFormatter Formatter { get; } = new();

        public ISexprFormatter<T>? GetFormatter<T>() => typeof(T) == typeof(OpenOr)
            ? (ISexprFormatter<T>)(object)Formatter : DefaultResolver.Instance.GetFormatter<T>();
    }

    private sealed class OpenOrFormatter : ISexprFormatter<OpenOr>
    {
        public int ObservedDepth { get; private set; }
        public OpenOr Deserialize(in SNode node, SexprSerializerOptions options)
        {
            ObservedDepth = options.MaxDepth;
            return new([new IntegerExpression(7)]);
        }

        public void Serialize(ref SexprEmitter emitter, OpenOr value, SexprSerializerOptions options) =>
            new DotSexpr.Serializer.Formatters.ExpressionFormatter().Serialize(ref emitter, value, options);
    }
}
