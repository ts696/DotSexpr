using System.Text;

namespace DotSexpr.Tests;

public class NumberSyntaxTests
{
    [TestCase("0")]
    [TestCase("-0")]
    [TestCase("1")]
    [TestCase("123")]
    [TestCase("-123")]
    [TestCase("0.5")]
    [TestCase("-0.5")]
    [TestCase("1.0")]
    [TestCase("1.2500")]
    [TestCase("0e3")]
    [TestCase("-0E3")]
    [TestCase("0e+03")]
    [TestCase("0e-03")]
    [TestCase("1e3")]
    [TestCase("1E3")]
    [TestCase("1e+3")]
    [TestCase("1e-3")]
    [TestCase("1e003")]
    [TestCase("1e+03")]
    [TestCase("1e-003")]
    [TestCase("1.25e3")]
    [TestCase("-1.25E-3")]
    [TestCase("0.0E+00")]
    [TestCase("-0.0e-00")]
    public void Parse_JsonNumberSyntax_ReturnsNumber(string text)
    {
        AssertClassification(text, true);
    }

    [TestCase("+123")]
    [TestCase("+0")]
    [TestCase(".5")]
    [TestCase("-.5")]
    [TestCase("01")]
    [TestCase("-01")]
    [TestCase("00")]
    [TestCase("-00")]
    [TestCase("00.5")]
    [TestCase("-00.5")]
    [TestCase("01e3")]
    [TestCase("-01e3")]
    [TestCase("1.")]
    [TestCase("-1.")]
    [TestCase("0.")]
    [TestCase("-0.")]
    [TestCase("1.e3")]
    [TestCase("-.")]
    [TestCase("-")]
    [TestCase(".")]
    [TestCase("+")]
    [TestCase("--1")]
    [TestCase("-+1")]
    [TestCase("1e")]
    [TestCase("1E")]
    [TestCase("1e+")]
    [TestCase("1e-")]
    [TestCase("1e+-2")]
    [TestCase("1e--2")]
    [TestCase("1e++2")]
    [TestCase("1e2-3")]
    [TestCase("1e2+3")]
    [TestCase("1e2.3")]
    [TestCase("1.2.3")]
    [TestCase("1ee3")]
    [TestCase("1e2E3")]
    [TestCase("1+2")]
    [TestCase("1-2")]
    [TestCase("1.0f3")]
    [TestCase("1.0d3")]
    [TestCase("NaN")]
    [TestCase("nan")]
    [TestCase("+Infinity")]
    [TestCase("-Infinity")]
    [TestCase("Infinity")]
    [TestCase("123abc")]
    [TestCase("0abc")]
    [TestCase("abc123")]
    [TestCase("0xFF")]
    [TestCase("1_000")]
    [TestCase("1,000")]
    [TestCase("1/2")]
    [TestCase("1/0")]
    [TestCase("2/2")]
    [TestCase("-3/4")]
    public void Parse_NonJsonNumberSyntax_ReturnsSymbol(string text)
    {
        AssertClassification(text, false);
    }

    [TestCase(4096, false)]
    [TestCase(4096, true)]
    public void Parse_LargeInteger_ClassifiesWithoutConvertingValue(int digits, bool negative)
    {
        AssertClassification((negative ? "-" : "") + new string('9', digits), true);
    }

    [TestCase("1e309")]
    [TestCase("1e-4000")]
    public void Parse_NumberOutsideDoubleRange_ReturnsNumber(string text)
    {
        AssertClassification(text, true);
    }

    [Test]
    public void Parse_LargeExponent_ClassifiesWithoutConvertingExponent()
    {
        AssertClassification("1e" + new string('9', 128), true);
        AssertClassification("1e-" + new string('9', 128), true);
    }

    private static void AssertClassification(string text, bool isNumber)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        using var fromString = SDocument.Parse(text);
        using var fromBytes = SDocument.Parse(bytes);
        var parsedString = SDocument.TryParse(text, out var tryString);
        var parsedBytes = SDocument.TryParse(bytes, out var tryBytes);
        using (tryString)
        using (tryBytes)
        {
            Assert.That(parsedString, Is.True, "TryParse(string)");
            Assert.That(parsedBytes, Is.True, "TryParse(bytes)");
            CheckNode(fromString.RootNode);
            CheckNode(fromBytes.RootNode);
            CheckNode(tryString!.RootNode);
            CheckNode(tryBytes!.RootNode);
        }

        void CheckNode(SNode node)
        {
            Assert.That(node.Token.IsNumber, Is.EqualTo(isNumber));
            Assert.That(node.Token.IsSymbol, Is.EqualTo(!isNumber));
            Assert.That(node.Token.Length, Is.EqualTo(bytes.Length));
            Assert.That(node.ValueEquals(bytes), Is.True);
            if (!isNumber)
            {
                Assert.That(node.GetSymbol(), Is.EqualTo(text));
            }
        }
    }
}
