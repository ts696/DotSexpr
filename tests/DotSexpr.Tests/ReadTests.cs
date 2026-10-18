using System.Globalization;
using System.Text;

namespace DotSexpr.Tests;

public class ReadTests
{
    [TestCase("()")]
    [TestCase("(日本語 (\"a\\\"b\") 123)")]
    [TestCase("\"😀\"")]
    public void Read_RawUtf8_PreservesWholeExpression(string text)
    {
        var source = " \n" + text + "\t ";
        var bytes = Encoding.UTF8.GetBytes(source);
        using var document = SDocument.Parse(bytes);
        bytes.AsSpan().Clear();
        Assert.That(Encoding.UTF8.GetString(document.Source.Span), Is.EqualTo(source));
        Assert.That(Encoding.UTF8.GetString(document.RootNode.GetRawUtf8().Span), Is.EqualTo(text));
        Assert.That(document.RootNode.Token.Offset, Is.EqualTo(2));
        Assert.That(document.RootNode.Token.Length, Is.EqualTo(Encoding.UTF8.GetByteCount(text)));
    }

    [Test]
    public void Read_RawUtf8_PreservesNestedNodesAndRejectsDisposedDocument()
    {
        var document = SDocument.Parse("(root (\"日本語\" 123))");
        var nested = document.RootNode[1];
        var token = nested.Token;
        Assert.That(Encoding.UTF8.GetString(nested.GetRawUtf8().Span), Is.EqualTo("(\"日本語\" 123)"));
        Assert.That(Encoding.UTF8.GetString(nested[0].GetRawUtf8().Span), Is.EqualTo("\"日本語\""));
        document.Dispose();
        Assert.That(() => _ = document.Source, Throws.TypeOf<ObjectDisposedException>());
        Assert.That(() => _ = document.GetRawUtf8(token), Throws.TypeOf<ObjectDisposedException>());
        Assert.That(() => _ = nested.GetRawUtf8(), Throws.TypeOf<ObjectDisposedException>());
    }

    [Test]
    public void Dispose_Twice_AllowsSubsequentParsing()
    {
        var document = SDocument.Parse("user");
        document.Dispose();
        document.Dispose();

        using var first = SDocument.Parse("first");
        using var second = SDocument.Parse("second");
        Assert.That(first.RootNode.GetSymbol(), Is.EqualTo("first"));
        Assert.That(second.RootNode.GetSymbol(), Is.EqualTo("second"));
    }

    [TestCase("0", 0)]
    [TestCase("123", 123)]
    [TestCase("-42", -42)]
    [TestCase("2e3", 2000)]
    [TestCase("-2E+3", -2000)]
    [TestCase("-0", 0)]
    [TestCase("1.0", 1)]
    [TestCase("1.5e1", 15)]
    [TestCase("200e-2", 2)]
    [TestCase("2147483647", int.MaxValue)]
    [TestCase("-2147483648", int.MinValue)]
    [TestCase("2147483647e0", int.MaxValue)]
    [TestCase("-2147483648e0", int.MinValue)]
    public void Read_Int32_ReturnsValue(string text, int expected)
    {
        using var document = SDocument.Parse(text);

        Assert.That(document.RootNode.GetInt32(), Is.EqualTo(expected));
    }

    [TestCase("0", 0L)]
    [TestCase("123", 123L)]
    [TestCase("-42", -42L)]
    [TestCase("2e3", 2000L)]
    [TestCase("-2E+3", -2000L)]
    [TestCase("1.0", 1L)]
    [TestCase("1.5e1", 15L)]
    [TestCase("200e-2", 2L)]
    [TestCase("2147483648", 2147483648L)]
    [TestCase("9007199254740993", 9007199254740993L)]
    [TestCase("9007199254740993e0", 9007199254740993L)]
    [TestCase("9223372036854775807", long.MaxValue)]
    [TestCase("-9223372036854775808", long.MinValue)]
    [TestCase("9223372036854775807e0", long.MaxValue)]
    [TestCase("-9223372036854775808e0", long.MinValue)]
    public void Read_Int64_ReturnsValue(string text, long expected)
    {
        using var document = SDocument.Parse(text);

        Assert.That(document.RootNode.GetInt64(), Is.EqualTo(expected));
    }

    [TestCase("1.5")]
    [TestCase("2e-3")]
    [TestCase("2147483648")]
    [TestCase("-2147483649")]
    [TestCase("1e1000")]
    public void Read_Int32_NonIntegralOrOutOfRangeValue_ThrowsOverflow(string text)
    {
        using var document = SDocument.Parse(text);

        Assert.That(() => document.RootNode.GetInt32(), Throws.TypeOf<OverflowException>());
    }

    [TestCase("1.5")]
    [TestCase("2e-3")]
    [TestCase("9223372036854775808")]
    [TestCase("-9223372036854775809")]
    [TestCase("1e1000")]
    public void Read_Int64_NonIntegralOrOutOfRangeValue_ThrowsOverflow(string text)
    {
        using var document = SDocument.Parse(text);

        Assert.That(() => document.RootNode.GetInt64(), Throws.TypeOf<OverflowException>());
    }

    [TestCase("user")]
    [TestCase("\"123\"")]
    [TestCase("()")]
    public void Read_IntegerGetters_NonNumber_ThrowsArgumentOutOfRange(string text)
    {
        using var document = SDocument.Parse(text);

        Assert.That(() => document.RootNode.GetInt32(), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => document.RootNode.GetInt64(), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [TestCase("fr-FR")]
    [TestCase("de-DE")]
    [NonParallelizable]
    public void Read_IntegerGetters_UseInvariantCulture(string cultureName)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            using var document = SDocument.Parse("-1.5e1");

            Assert.That(document.RootNode.GetInt32(), Is.EqualTo(-15));
            Assert.That(document.RootNode.GetInt64(), Is.EqualTo(-15L));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [TestCase("0.0", 0.0f)]
    [TestCase("1.23", 1.23f)]
    [TestCase("-4.2", -4.2f)]
    [TestCase("2.0e3", 2000f)]
    public void Read_Single_ReturnsValue(string text, float expected)
    {
        using var document = SDocument.Parse(text);

        Assert.That(document.RootNode.GetSingle(), Is.EqualTo(expected));
    }

    [TestCase("0.0", 0.0)]
    [TestCase("1.23", 1.23)]
    [TestCase("-4.2", -4.2)]
    [TestCase("2.0e3", 2000.0)]
    public void Read_Double_ReturnsValue(string text, double expected)
    {
        using var document = SDocument.Parse(text);

        Assert.That(document.RootNode.GetDouble(), Is.EqualTo(expected));
    }

    [Test]
    public void Read_EmptyList_ReturnsNoChildren()
    {
        var root = SDocument.Parse("()").RootNode;

        Assert.That(root.Count(), Is.EqualTo(0));
    }

    [Test]
    public void Read_RootChildren_ReturnsSymbols()
    {
        var root = SDocument.Parse("(a b c)").RootNode;

        var result = root.Select(x => x.GetSymbol()).ToArray();
        Assert.That(result, Is.EquivalentTo(["a", "b", "c"]));
    }

    [Test]
    public void Read_SymbolValueEquals_MatchesUtf8Value()
    {
        var root = SDocument.Parse("user").RootNode;
        Assert.That(root.ValueEquals("user"u8), Is.True);
    }

    [TestCase("\"user\"", "user")]
    [TestCase("\"us\\u0065r\"", "user")]
    [TestCase("\"日本語\"", "日本語")]
    [TestCase("\"\"", "")]
    [TestCase("\"a\\\"b\\\\c\"", "a\"b\\c")]
    public void Read_StringValueEquals_MatchesDecodedUtf8Value(string text, string expected)
    {
        using var document = SDocument.Parse(text);

        var bytes = Encoding.UTF8.GetBytes(expected);
        Assert.That(document.RootNode.ValueEquals(bytes), Is.True);
    }

    [TestCase(-1)]
    [TestCase(3)]
    public void Read_ChildIndexOutOfRange_ThrowsIndexOutOfRange(int value)
    {
        var root = SDocument.Parse("(a b c)").RootNode;
        Assert.That(
            () =>
            {
                _ = root[value];
            },
            Throws.InstanceOf<IndexOutOfRangeException>());
    }

    [TestCase("3.4028234663852886e38", float.MaxValue)]
    [TestCase("-3.4028234663852886e38", float.MinValue)]
    [TestCase("1e39", float.PositiveInfinity)]
    [TestCase("-1e39", float.NegativeInfinity)]
    [TestCase("1.401298464324817e-45", float.Epsilon)]
    [TestCase("1e-100", 0f)]
    public void Read_Single_BoundaryValue_ReturnsValue(string text, float expected)
    {
        CheckAllInputs(text, document =>
        {
            Assert.That(document.RootNode.Token.IsNumber, Is.True);
            Assert.That(document.RootNode.GetSingle(), Is.EqualTo(expected));
        });
    }

    [TestCase("1.7976931348623157e308", double.MaxValue)]
    [TestCase("-1.7976931348623157e308", double.MinValue)]
    [TestCase("1e309", double.PositiveInfinity)]
    [TestCase("-1e309", double.NegativeInfinity)]
    [TestCase("4.9406564584124654e-324", double.Epsilon)]
    [TestCase("1e-4000", 0d)]
    public void Read_Double_BoundaryValue_ReturnsValue(string text, double expected)
    {
        CheckAllInputs(text, document =>
        {
            Assert.That(document.RootNode.Token.IsNumber, Is.True);
            Assert.That(document.RootNode.GetDouble(), Is.EqualTo(expected));
        });
    }

    [Test]
    public void Read_FloatingPointGetters_UseRequestedPrecision()
    {
        CheckAllInputs("(values 16777217)", document =>
        {
            var number = document.RootNode[1];
            Assert.That(number.GetSingle(), Is.EqualTo(16777216f));
            Assert.That(number.GetDouble(), Is.EqualTo(16777217d));
        });
    }

    [TestCase("user")]
    [TestCase("\"123\"")]
    [TestCase("()")]
    public void Read_FloatingPointGetters_NonNumber_ThrowsArgumentOutOfRange(string text)
    {
        CheckAllInputs(text, document =>
        {
            Assert.That(() => document.RootNode.GetSingle(), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => document.RootNode.GetDouble(), Throws.TypeOf<ArgumentOutOfRangeException>());
        });
    }

    [TestCase("fr-FR")]
    [NonParallelizable]
    public void Read_FloatingPointGetters_UseInvariantCulture(string cultureName)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            CheckAllInputs("-1.25e2", document =>
            {
                Assert.That(document.RootNode.GetSingle(), Is.EqualTo(-125f));
                Assert.That(document.RootNode.GetDouble(), Is.EqualTo(-125d));
            });
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    private static void CheckAllInputs(string text, Action<SDocument> check)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        using var fromString = SDocument.Parse(text);
        using var fromBytes = SDocument.Parse(bytes);
        check(fromString);
        check(fromBytes);

        var parsedString = SDocument.TryParse(text, out var tryString);
        var parsedBytes = SDocument.TryParse(bytes, out var tryBytes);
        using (tryString)
        using (tryBytes)
        {
            Assert.That(parsedString, Is.True, "TryParse(string)");
            Assert.That(parsedBytes, Is.True, "TryParse(bytes)");
            check(tryString!);
            check(tryBytes!);
        }
    }
}
