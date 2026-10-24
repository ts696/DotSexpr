using System.Globalization;
using DotSexpr.Serializer;

namespace DotSexpr.Tests;

public partial class ExpressionTests
{
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

    [Test]
    public void UntypedCall_KeepsArbitraryNames()
    {
        var call = SexprSerializer.Deserialize<CallExpression>("(jump 2)");
        Assert.That(call.Name, Is.EqualTo("jump"));
        Assert.That(SexprSerializer.Serialize(call), Is.EqualTo("(jump 2)"));
    }
}
