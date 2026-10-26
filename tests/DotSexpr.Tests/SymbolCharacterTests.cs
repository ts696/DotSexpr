using System.Text;
using DotSexpr.Serializer;

namespace DotSexpr.Tests;

public class SymbolCharacterTests
{
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(64)]
    public void Parse_ControlCharactersInSymbols_AreRejected(int prefixLength)
    {
        foreach (var character in Enumerable.Range(0, 32).Select(i => (char)i))
        {
            if (character is '\n' or '\r' or '\t')
            {
                continue;
            }
            AssertInvalid(new string('a', prefixLength) + character + "b", prefixLength);
        }
    }

    [TestCase("a\"b", 1)]
    [TestCase("abc\"def\"", 3)]
    [TestCase("日本語\"x\"", 9)]
    public void Parse_QuotesInsideSymbols_AreRejected(string text, int invalidBytePosition)
    {
        AssertInvalid(text, invalidBytePosition);
    }

    private static void AssertInvalid(string text, int invalidBytePosition)
    {
        foreach (var nested in new[] { false, true })
        {
            var source = nested ? "(ok\n" + text + ")" : text;
            var expectedPosition = invalidBytePosition + (nested ? 4 : 0);
            var bytes = Encoding.UTF8.GetBytes(source);
            Assert.That(Assert.Throws<SexprParserException>(() => SDocument.Parse(source))!.BytePosition,
                Is.EqualTo(expectedPosition));
            Assert.That(Assert.Throws<SexprParserException>(() => SDocument.Parse(bytes))!.BytePosition,
                Is.EqualTo(expectedPosition));
            Assert.That(SDocument.TryParse(source, out var fromText), Is.False);
            Assert.That(fromText, Is.Null);
            Assert.That(SDocument.TryParse(bytes, out var fromBytes), Is.False);
            Assert.That(fromBytes, Is.Null);
        }
        var multiple = "ok\n" + text;
        Assert.That(Assert.Throws<SexprParserException>(() => SDocument.ParseMultiple(multiple))!.BytePosition,
            Is.EqualTo(3 + invalidBytePosition));
        Assert.That(Assert.Throws<SexprParserException>(() => SDocument.ParseMultiple(Encoding.UTF8.GetBytes(multiple)))!.BytePosition,
            Is.EqualTo(3 + invalidBytePosition));
    }

    [TestCase(" ")]
    [TestCase("\t")]
    [TestCase("\n")]
    [TestCase("\r")]
    public void Parse_WhitespaceBetweenSymbols_RemainsValid(string whitespace)
    {
        var text = "(alpha" + whitespace + "日本語😀)";
        using var document = SDocument.Parse(Encoding.UTF8.GetBytes(text));
        var expression = SexprSerializer.Deserialize<Expression>(document.RootNode);
        Assert.That(SexprSerializer.Serialize(expression), Is.EqualTo("(alpha 日本語😀)"));
    }

    [TestCase("\"a\\n\\\"b\"")]
    [TestCase("(a; ignored \"\u0001\n b)")]
    [TestCase("(a #| ignored \"\u0001 |# b)")]
    public void Parse_QuotesAndControlsInStringsOrComments_RemainValid(string text)
    {
        using var document = SDocument.Parse(text);
        using var fromBytes = SDocument.Parse(Encoding.UTF8.GetBytes(text));
        Assert.That(document.RootNode.Token.Length, Is.EqualTo(fromBytes.RootNode.Token.Length));
    }
}
