using System.Globalization;
using DotSexpr.Emitter;
using DotSexpr.Serializer;
using DotSexpr.Serializer.Resolvers;

namespace DotSexpr.Tests;

public partial class ExpressionTests
{
    [SexprCall("move")]
    public sealed partial record Move(params Expression[] Items) : CallExpression;

    [SexprObject]
    public partial class Plan
    {
        public Move[] Steps { get; set; } = [];
    }

    [TestCase("0", 0L)]
    [TestCase("-42", -42L)]
    [TestCase("9007199254740993", 9007199254740993L)]
    [TestCase("9223372036854775807", long.MaxValue)]
    [TestCase("-9223372036854775808", long.MinValue)]
    public void Integer_PreservesExactValue(string text, long expected)
    {
        var expression = SexprSerializer.Deserialize<Expression>(text);
        Assert.That(expression, Is.EqualTo(new IntegerExpression(expected)));
        Assert.That(SexprSerializer.Serialize(expression), Is.EqualTo(text));
        Assert.That(SexprSerializer.Deserialize<IntegerExpression>(text).Value, Is.EqualTo(expected));
        Assert.That(SexprSerializer.Serialize(new IntegerExpression(expected)), Is.EqualTo(text));
    }

    [TestCase("1.25", 1.25)]
    [TestCase("-0.5", -0.5)]
    [TestCase("1e3", 1000)]
    [TestCase("1.25E-3", 0.00125)]
    public void Number_ReadsFractionsAndExponents(string text, double expected)
    {
        Assert.That(SexprSerializer.Deserialize<Expression>(text), Is.EqualTo(new NumberExpression(expected)));
        var serialized = SexprSerializer.Serialize(new NumberExpression(expected));
        Assert.That(SexprSerializer.Deserialize<NumberExpression>(serialized).Value, Is.EqualTo(expected));
    }

    [Test]
    public void Number_UsesInvariantCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var expression = SexprSerializer.Deserialize<Expression>("(move ?x -2 1.25 (scale 0.5))");
            Assert.That(SexprSerializer.Serialize(expression), Is.EqualTo("(move ?x -2 1.25 (scale 0.5))"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [TestCase("9223372036854775808")]
    [TestCase("-9223372036854775809")]
    [TestCase("1e309")]
    public void Number_OutOfRange_ReportsNestedPath(string number)
    {
        var error = Assert.Throws<SexprSerializerException>(() =>
            SexprSerializer.Deserialize<Move>($"(move (scale {number}))"))!;
        Assert.That(error.Path, Is.EqualTo("$[1][1]"));
        Assert.That(error.BytePosition, Is.EqualTo(13));
        Assert.That(error.InnerException, Is.TypeOf<OverflowException>());
    }

    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(double.NegativeInfinity)]
    public void Number_NonFinite_ReportsArgumentPath(double number)
    {
        var error = Assert.Throws<SexprSerializerException>(() =>
            SexprSerializer.Serialize(new Move([new NumberExpression(number)])))!;
        Assert.That(error.Path, Is.EqualTo("$[1]"));
    }

    [Test]
    public void TypedCall_GeneratesConstructorsAndPreservesArguments()
    {
        const string text = "(move ?from ?to 10 0.5 (speed 2))";
        var move = SexprSerializer.Deserialize<Move>(text);
        Assert.That(move.Name, Is.EqualTo("move"));
        Assert.That(move.Arguments, Has.Count.EqualTo(5));
        Assert.That(move.Arguments[2], Is.EqualTo(new IntegerExpression(10)));
        Assert.That(move.Arguments[3], Is.EqualTo(new NumberExpression(0.5)));
        Assert.That(SexprSerializer.Serialize(move), Is.EqualTo(text));
        Assert.That(SexprSerializer.Serialize<Expression>(move), Is.EqualTo(text));
        Assert.That(SexprSerializer.Serialize(new Move()), Is.EqualTo("(move)"));
        Assert.That(SexprSerializer.Serialize(move with { Items = [new IntegerExpression(3)] }),
            Is.EqualTo("(move 3)"));
    }

    [TestCase("(jump)", "$[0]", 1)]
    [TestCase("(Move)", "$[0]", 1)]
    [TestCase("(123)", "$[0]", 1)]
    [TestCase("(\"move\")", "$[0]", 1)]
    [TestCase("()", "$", 0)]
    [TestCase("move", "$", 0)]
    [TestCase("42", "$", 0)]
    public void TypedCall_RejectsMismatchedNameOrShape(string text, string path, int position)
    {
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Deserialize<Move>(text))!;
        Assert.That(error.Path, Is.EqualTo(path));
        Assert.That(error.BytePosition, Is.EqualTo(position));
    }

    [Test]
    public void TypedCall_ArraysAndModelProperties_UseGeneratedFormatter()
    {
        const string text = "(:Steps ((move 1) (move 2)))";
        var plan = SexprSerializer.Deserialize<Plan>(text);
        Assert.That(plan.Steps.Select(m => m.Name), Is.EqualTo(new[] { "move", "move" }));
        Assert.That(SexprSerializer.Serialize(plan), Is.EqualTo(text));
        Assert.That(SexprSerializer.Deserialize<Move[]>("((move))"), Has.Length.EqualTo(1));
        var error = Assert.Throws<SexprSerializerException>(() =>
            SexprSerializer.Deserialize<Plan>("(:Steps ((move) (jump)))"))!;
        Assert.That(error.Path, Is.EqualTo("$.Steps[1][0]"));
        Assert.That(error.BytePosition, Is.EqualTo(17));
        Assert.That(error.Message, Does.Contain("'move'").And.Contain("'jump'"));
    }

    [Test]
    public void UntypedCall_KeepsArbitraryNames()
    {
        var call = SexprSerializer.Deserialize<CallExpression>("(jump 2)");
        Assert.That(call.Name, Is.EqualTo("jump"));
        Assert.That(SexprSerializer.Serialize(call), Is.EqualTo("(jump 2)"));
    }

    [Test]
    public void NumbersAndTypedCalls_RespectCustomPrimitiveFormatters()
    {
        var options = new SexprSerializerOptions { Resolver = new NumericResolver() };
        var move = SexprSerializer.Deserialize<Move>("(move 1 0.5)", options);
        Assert.That(move.Arguments, Is.EqualTo(new Expression[]
        {
            new IntegerExpression(11), new NumberExpression(10.5)
        }));
        Assert.That(SexprSerializer.Serialize(move, options), Is.EqualTo("(move 21 20.5)"));
    }

    private sealed class NumericResolver : ISexprFormatterResolver
    {
        public ISexprFormatter<T>? GetFormatter<T>()
        {
            if (typeof(T) == typeof(long)) return (ISexprFormatter<T>)(object)new LongFormatter();
            if (typeof(T) == typeof(double)) return (ISexprFormatter<T>)(object)new DoubleFormatter();
            return DefaultResolver.Instance.GetFormatter<T>();
        }
    }

    private sealed class LongFormatter : ISexprFormatter<long>
    {
        public long Deserialize(in SNode node, SexprSerializerOptions options) => node.GetInt64() + 10;
        public void Serialize(ref SexprEmitter emitter, long value, SexprSerializerOptions options) => emitter.WriteNumber(value + 10);
    }

    private sealed class DoubleFormatter : ISexprFormatter<double>
    {
        public double Deserialize(in SNode node, SexprSerializerOptions options) => node.GetDouble() + 10;
        public void Serialize(ref SexprEmitter emitter, double value, SexprSerializerOptions options) => emitter.WriteNumber(value + 10);
    }
}
