using DotSexpr.Emitter;
using DotSexpr.Serializer;
using DotSexpr.Serializer.Resolvers;

namespace DotSexpr.Tests;

public partial class NestedCallTests
{
    [SexprCallUnion(typeof(And), typeof(Or), AllowUnknownCalls = true)]
    public abstract partial record ConditionExpression : CallExpression;

    [SexprCall("and")]
    public sealed partial record And(params Expression[] Items) : ConditionExpression;

    [SexprCall("or")]
    public sealed partial record Or(params Expression[] Items) : ConditionExpression;

    [SexprObject]
    public partial class Plan
    {
        public ConditionExpression[] Conditions { get; set; } = [];
        public Expression Other { get; set; } = new SymbolExpression("default");
    }

    [Test]
    public void Union_AttributeDispatchesNestedCallsWithoutOptions()
    {
        const string text = "(and (reachable ?from ?to) (or (visited ?from) (visited ?to)))";
        var condition = SexprSerializer.Deserialize<ConditionExpression>(text);
        Assert.That(condition, Is.TypeOf<And>());
        Assert.That(condition.Arguments[0], Is.TypeOf<ConditionExpression.UnknownCall>());
        var reachable = (CallExpression)condition.Arguments[0];
        Assert.That(reachable.Name, Is.EqualTo("reachable"));
        Assert.That(reachable.Arguments, Is.EqualTo(new[] { new SymbolExpression("?from"), new SymbolExpression("?to") }));
        Assert.That(condition.Arguments[1], Is.TypeOf<Or>());
        var or = (Or)condition.Arguments[1];
        Assert.That(or.Arguments, Is.All.TypeOf<ConditionExpression.UnknownCall>());
        Assert.That(or.Arguments.Cast<CallExpression>().Select(c => c.Name), Is.EqualTo(new[] { "visited", "visited" }));
        Assert.That(SexprSerializer.Serialize(condition), Is.EqualTo(text));
    }

    [Test]
    public void Union_UnknownIntermediateCallStillDispatchesKnownDescendants()
    {
        const string text = "(and (custom (or (visited ?x))) 2 0.5)";
        var condition = SexprSerializer.Deserialize<ConditionExpression>(text);
        Assert.That(condition.Arguments[0], Is.TypeOf<ConditionExpression.UnknownCall>());
        var custom = (CallExpression)condition.Arguments[0];
        Assert.That(custom.Name, Is.EqualTo("custom"));
        Assert.That(custom.Arguments[0], Is.TypeOf<Or>());
        Assert.That(condition.Arguments[1], Is.EqualTo(new IntegerExpression(2)));
        Assert.That(condition.Arguments[2], Is.EqualTo(new NumberExpression(0.5)));
        Assert.That(SexprSerializer.Serialize(condition), Is.EqualTo(text));
    }

    [Test]
    public void Union_AttributeAppliesToArraysAndPropertiesWithoutLeakingToSiblings()
    {
        const string text = "(:Conditions ((and (or)) (or (custom))) :Other (and (or)))";
        var plan = SexprSerializer.Deserialize<Plan>(text);
        Assert.That(plan.Conditions.Select(c => c.GetType()), Is.EqualTo(new[] { typeof(And), typeof(Or) }));
        Assert.That(plan.Conditions[0].Arguments[0], Is.TypeOf<Or>());
        Assert.That(plan.Conditions[1].Arguments[0], Is.TypeOf<ConditionExpression.UnknownCall>());
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
        var condition = SexprSerializer.Deserialize<ConditionExpression>("(custom (and))");
        Assert.That(condition, Is.TypeOf<ConditionExpression.UnknownCall>());
        Assert.That(condition.Arguments[0], Is.TypeOf<And>());
    }

    [TestCase("(and (or (3)))", "$[1][1][0]", 10)]
    [TestCase("(and (custom ()))", "$[1][1]", 13)]
    public void Union_InvalidNestedCallsPreserveErrorPaths(string text, string path, int position)
    {
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Deserialize<ConditionExpression>(text))!;
        Assert.That(error.Path, Is.EqualTo(path));
        Assert.That(error.BytePosition, Is.EqualTo(position));
    }

    [Test]
    public void Union_AttributePreservesCustomCaseResolverAndOptionsWithoutMutation()
    {
        var resolver = new CustomResolver();
        var options = new SexprSerializerOptions { Resolver = resolver, MaxDepth = 12 };
        var condition = SexprSerializer.Deserialize<ConditionExpression>("(and (or))", options);
        Assert.That(condition.Arguments[0], Is.TypeOf<Or>());
        Assert.That(((Or)condition.Arguments[0]).Arguments, Is.EqualTo(new[] { new IntegerExpression(7) }));
        Assert.That(resolver.Formatter.ObservedDepth, Is.EqualTo(12));
        Assert.That(options.Resolver, Is.SameAs(resolver));
        Assert.That(options.MaxDepth, Is.EqualTo(12));
        Assert.That(SexprSerializer.Deserialize<Expression>("(or)", options), Is.TypeOf<CallExpression>());
    }

    private sealed class CustomResolver : ISexprFormatterResolver
    {
        public CustomOrFormatter Formatter { get; } = new();

        public ISexprFormatter<T>? GetFormatter<T>() => typeof(T) == typeof(Or)
            ? (ISexprFormatter<T>)(object)Formatter : DefaultResolver.Instance.GetFormatter<T>();
    }

    private sealed class CustomOrFormatter : ISexprFormatter<Or>
    {
        public int ObservedDepth { get; private set; }
        public Or Deserialize(in SNode node, SexprSerializerOptions options)
        {
            ObservedDepth = options.MaxDepth;
            return new([new IntegerExpression(7)]);
        }

        public void Serialize(ref SexprEmitter emitter, Or value, SexprSerializerOptions options) =>
            new DotSexpr.Serializer.Formatters.ExpressionFormatter().Serialize(ref emitter, value, options);
    }
}
