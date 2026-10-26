using System.Text;
using DotSexpr.Serializer;

namespace DotSexpr.Tests;

public class SymbolUtf8Tests
{
    [TestCase(new byte[] { 0xff })]
    [TestCase(new byte[] { 0x80 })]
    [TestCase(new byte[] { 0xc0, 0xaf })]
    [TestCase(new byte[] { 0xed, 0xa0, 0x80 })]
    [TestCase(new byte[] { 0xf4, 0x90, 0x80, 0x80 })]
    [TestCase(new byte[] { 0xe3, 0x81 })]
    [TestCase(new byte[] { (byte)'a', 0xff, (byte)'b' })]
    public void Parse_InvalidSymbolUtf8_RejectsInsteadOfReplacingBytes(byte[] invalid)
    {
        foreach (var prefix in new[] { "", "(ok\n" })
        {
            var source = Encoding.UTF8.GetBytes(prefix).Concat(invalid)
                .Concat(prefix.Length == 0 ? [] : new byte[] { (byte)')' }).ToArray();
            var error = Assert.Throws<SexprParserException>(() => SDocument.Parse(source))!;
            Assert.That(error.BytePosition, Is.EqualTo(prefix.Length));
            Assert.That(error.LineNumber, Is.EqualTo(prefix.Length == 0 ? 1 : 2));
            Assert.That(SDocument.TryParse(source, out var document), Is.False);
            Assert.That(document, Is.Null);
        }

        var multiple = "ok\n"u8.ToArray().Concat(invalid).ToArray();
        var multipleError = Assert.Throws<SexprParserException>(() => SDocument.ParseMultiple(multiple))!;
        Assert.That(multipleError.BytePosition, Is.EqualTo(3));
    }

    [TestCase("日本語😀")]
    [TestCase("a\u007fb")]
    [TestCase("?名前")]
    [TestCase("a#|text|#b")]
    public void Parse_ValidSymbolUtf8_PreservesBytesAndRoundTrips(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        using var document = SDocument.Parse(bytes);
        Assert.That(document.RootNode.GetSymbol(), Is.EqualTo(value));
        Assert.That(document.RootNode.GetRawUtf8().Span.SequenceEqual(bytes), Is.True);
        Assert.That(SexprSerializer.Serialize(new SymbolExpression(document.RootNode.GetSymbol())), Is.EqualTo(value));
        Assert.That(SDocument.TryParse(bytes, out var parsed), Is.True);
        using (parsed)
        {
            Assert.That(parsed!.RootNode.GetSymbol(), Is.EqualTo(value));
        }
    }
}
