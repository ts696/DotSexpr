using System.Text;

namespace DotSexpr.Tests;

public class ParseTests
{
    [TestCase("\n")]
    [TestCase("\r\n")]
    [TestCase("\r")]
    public void Parse_LineComments_DoNotCreateNodes(string newline)
    {
        var text = "; 日本語😀 ) \"" + newline + "(user; ignored" + newline + "100); EOF";
        foreach (var utf8 in new[] { false, true })
        {
            using var document = utf8 ? SDocument.Parse(Encoding.UTF8.GetBytes(text)) : SDocument.Parse(text);
            Assert.That(document.RootNode.Count, Is.EqualTo(2));
            Assert.That(document.RootNode[0].GetSymbol(), Is.EqualTo("user"));
            Assert.That(document.RootNode[1].GetInt32(), Is.EqualTo(100));
            Assert.That(document.RootNode.Token.Offset, Is.EqualTo(Encoding.UTF8.GetByteCount("; 日本語😀 ) \"" + newline)));
        }
    }

    [Test]
    public void Parse_BlockComments_NestAndPreserveStringsAndSource()
    {
        const string text = "#| leading |#(user #| ( \" ; #| nested |# ) |# \"; #| text |#\")#| EOF |#";
        using var document = SDocument.Parse(text, new SDocumentOptions { MaxDepth = 1 });
        Assert.That(document.RootNode.Count, Is.EqualTo(2));
        Assert.That(document.RootNode[1].GetString(), Is.EqualTo("; #| text |#"));
        Assert.That(document.RootNode.Select(x => x.Token).Count(), Is.EqualTo(2));
        Assert.That(Encoding.UTF8.GetString(document.RootNode.GetRawUtf8().Span), Is.EqualTo("(user #| ( \" ; #| nested |# ) |# \"; #| text |#\")"));
        using var symbol = SDocument.Parse("a#|text|#b");
        Assert.That(symbol.RootNode.GetSymbol(), Is.EqualTo("a#|text|#b"));
    }

    [TestCase("; only a comment")]
    [TestCase("#| only a comment |#")]
    [TestCase("#| unfinished")]
    [TestCase("#| outer #| inner |#")]
    public void Parse_CommentWithoutExpressionOrTerminator_IsRejected(string text)
    {
        Assert.That(() => SDocument.Parse(text), Throws.TypeOf<SexprParserException>());
        Assert.That(SDocument.TryParse(text, out var fromText), Is.False);
        Assert.That(fromText, Is.Null);
        var bytes = Encoding.UTF8.GetBytes(text);
        Assert.That(SDocument.TryParse(bytes, out var fromBytes), Is.False);
        Assert.That(fromBytes, Is.Null);
    }

    [Test]
    public void Parse_BlockComments_DeepNestingAndErrorPosition()
    {
        var text = string.Concat(Enumerable.Repeat("#|", 1024)) + string.Concat(Enumerable.Repeat("|#", 1024)) + "user";
        using var document = SDocument.Parse(text);
        Assert.That(document.RootNode.GetSymbol(), Is.EqualTo("user"));
        var error = Assert.Throws<SexprParserException>(() => SDocument.Parse("; prefix\r\n#| unfinished"))!;
        Assert.That(error.BytePosition, Is.EqualTo(10));
        Assert.That(error.LineNumber, Is.EqualTo(2));
    }

    [TestCase("\"a\\nb\"", "a\nb")]
    [TestCase("\"\\\"\\\\\\/\\b\\f\\n\\r\\t\"", "\"\\/\b\f\n\r\t")]
    [TestCase("\"\\u0000\\u007f\\u0080\\u07ff\\u0800\\u3042\\uFFfF\"", "\0\u007f\u0080\u07ff\u0800あ\uffff")]
    [TestCase("\"\\uD83D\\uDE00\"", "😀")]
    [TestCase("\"日本語😀\"", "日本語😀")]
    [TestCase("\"\\u005Cn\"", "\\n")]
    [TestCase("\"a\\\\nb\"", "a\\nb")]
    public void Parse_StringEscapes_DecodeJsonValue(string text, string expected)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        using var fromText = SDocument.Parse(text);
        using var fromBytes = SDocument.Parse(bytes);
        foreach (var document in new[] { fromText, fromBytes })
        {
            Assert.That(Encoding.UTF8.GetString(document.RootNode.GetRawUtf8().Span), Is.EqualTo(text));
            Assert.That(document.RootNode.GetString(), Is.EqualTo(expected));
            Assert.That(document.RootNode.ValueEquals(Encoding.UTF8.GetBytes(expected)), Is.True);
            Assert.That(document.RootNode.ValueEquals(Encoding.UTF8.GetBytes(expected + "!")), Is.False);
            Assert.That(document.RootNode.ValueEquals(Encoding.UTF8.GetBytes(expected).AsSpan(1)), Is.False);
        }

        Assert.That(SDocument.TryParse(text, out var tryText), Is.True);
        using (tryText)
        {
            Assert.That(tryText!.RootNode.GetString(), Is.EqualTo(expected));
        }
        Assert.That(SDocument.TryParse(bytes, out var tryBytes), Is.True);
        using (tryBytes)
        {
            Assert.That(tryBytes!.RootNode.GetString(), Is.EqualTo(expected));
        }
    }

    [TestCase("\"\\q\"")]
    [TestCase("\"\\日本語\"")]
    [TestCase("\"a\nb\"")]
    [TestCase("\"a\tb\"")]
    [TestCase("\"\0\"")]
    [TestCase("\"a\\\nb\"")]
    [TestCase("\"\\u12\"")]
    [TestCase("\"\\u12x4\"")]
    [TestCase("\"\\uD800\"")]
    [TestCase("\"\\uDC00\"")]
    [TestCase("\"\\uD800\\u0041\"")]
    [TestCase("\"\\uD800\\uD800\"")]
    public void Parse_StringInvalidJsonEscapeOrControl_IsRejected(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        Assert.That(Assert.Throws<SexprParserException>(() => SDocument.Parse(text))!.BytePosition, Is.EqualTo(0));
        Assert.That(() => SDocument.Parse(bytes), Throws.TypeOf<SexprParserException>());
        Assert.That(SDocument.TryParse(text, out var fromText), Is.False);
        Assert.That(fromText, Is.Null);
        Assert.That(SDocument.TryParse(bytes, out var fromBytes), Is.False);
        Assert.That(fromBytes, Is.Null);
    }

    [Test]
    public void Parse_StringControlAfterLongPrefix_IsRejected()
    {
        var text = "\"" + new string('a', 64) + "\u001f\"";
        Assert.That(() => SDocument.Parse(text), Throws.TypeOf<SexprParserException>());
        Assert.That(() => SDocument.Parse(Encoding.UTF8.GetBytes(text)), Throws.TypeOf<SexprParserException>());
    }

    [Test]
    public void Parse_LongJsonEscapedString_DecodesPooledBuffer()
    {
        var text = "\"" + string.Concat(Enumerable.Repeat("\\u3042\\uD83D\\uDE00\\n", 100)) + "\"";
        var expected = string.Concat(Enumerable.Repeat("あ😀\n", 100));
        using var document = SDocument.Parse(text);
        Assert.That(document.RootNode.GetString(), Is.EqualTo(expected));
        Assert.That(document.RootNode.ValueEquals(Encoding.UTF8.GetBytes(expected)), Is.True);
        Assert.That(document.RootNode.ValueEquals(Encoding.UTF8.GetBytes("い" + expected[1..])), Is.False);
    }

    [Test]
    public void Parse_StringInput_PreservesEncodingFallback()
    {
        var text = "\"" + (char)0xd800 + "\"";
        using var document = SDocument.Parse(text);
        Assert.That(document.RootNode.GetString(), Is.EqualTo("\ufffd"));
        Assert.That(SDocument.TryParse(text, out var parsed), Is.True);
        using (parsed)
        {
            Assert.That(parsed!.RootNode.GetString(), Is.EqualTo("\ufffd"));
        }
    }

    [TestCase(new byte[] { 0x22, 0xc0, 0xaf, 0x22 })]
    [TestCase(new byte[] { 0x22, 0xed, 0xa0, 0x80, 0x22 })]
    [TestCase(new byte[] { 0x22, 0xf4, 0x90, 0x80, 0x80, 0x22 })]
    [TestCase(new byte[] { 0x22, 0xe3, 0x81, 0x22 })]
    [TestCase(new byte[] { 0x22, 0x5c, 0x80, 0x22 })]
    public void Parse_StringInvalidUtf8_IsRejectedBeforeReading(byte[] bytes)
    {
        Assert.That(() => SDocument.Parse(bytes), Throws.TypeOf<SexprParserException>());
        Assert.That(SDocument.TryParse(bytes, out var document), Is.False);
        Assert.That(document, Is.Null);
    }

    [TestCase("atom")]
    [TestCase("(a (b) (c) \"(((\")")]
    public void Parse_MaxDepth_OnlyCountsNestedLists(string text)
    {
        var options = new SDocumentOptions { MaxDepth = 2 };
        var bytes = Encoding.UTF8.GetBytes(text);
        using var fromText = SDocument.Parse(text, options);
        using var fromBytes = SDocument.Parse(bytes, options);
        Assert.That(SDocument.TryParse(text, options, out var tryText), Is.True);
        using (tryText)
        {
            Assert.That(tryText!.RootNode.Token.Length, Is.GreaterThan(0));
        }
        Assert.That(SDocument.TryParse(bytes, options, out var tryBytes), Is.True);
        using (tryBytes)
        {
            Assert.That(tryBytes!.RootNode.Token.Length, Is.GreaterThan(0));
        }
    }

    [Test]
    public void Parse_MaxDepth_RejectsExcessAndInvalidOptions()
    {
        var options = new SDocumentOptions { MaxDepth = 2 };
        const string text = "(((a)))";
        var bytes = Encoding.UTF8.GetBytes(text);
        Assert.That(Assert.Throws<SexprParserException>(() => SDocument.Parse(text, options))!.BytePosition, Is.EqualTo(2));
        Assert.That(() => SDocument.Parse(bytes, options), Throws.TypeOf<SexprParserException>());
        Assert.That(SDocument.TryParse(text, options, out var fromText), Is.False);
        Assert.That(fromText, Is.Null);
        Assert.That(SDocument.TryParse(bytes, options, out var fromBytes), Is.False);
        Assert.That(fromBytes, Is.Null);

        var invalid = new SDocumentOptions { MaxDepth = -1 };
        Assert.That(() => SDocument.Parse(text, invalid), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => SDocument.TryParse(text, invalid, out _), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => SDocument.Parse(bytes, invalid), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => SDocument.TryParse(bytes, invalid, out _), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Parse_EmptyList_ReturnsListWithNoElements()
    {
        var root = SDocument.Parse("()").RootNode;

        Assert.That(root.Token.IsList, Is.True);
        Assert.That(root.Count, Is.EqualTo(0));
    }

    [Test]
    public void Parse_Symbol_ReturnsSymbol()
    {
        var root = SDocument.Parse("user").RootNode;

        Assert.That(root.Token.IsSymbol, Is.True);
        Assert.That(root.GetSymbol(), Is.EqualTo("user"));
    }

    [TestCase("\"\"", "")]
    [TestCase("\"Alice\"", "Alice")]
    [TestCase("\"アリス\"", "アリス")]
    [TestCase("\"a\\\"b\\\\c\"", "a\"b\\c")]
    public void Parse_String_ReturnsDecodedString(string text, string expected)
    {
        using var document = SDocument.Parse(text);
        var root = document.RootNode;

        Assert.That(root.Token.IsString, Is.True);
        Assert.That(root.GetString(), Is.EqualTo(expected));
    }

    [Test]
    public void Parse_List_PreservesElementOrderAndKinds()
    {
        using var document = SDocument.Parse("(user \"Alice\" 100)");
        var root = document.RootNode;

        Assert.That(root.Token.IsList, Is.True);
        Assert.That(root.Count, Is.EqualTo(3));
        Assert.That(root[0].Token.IsSymbol, Is.True);
        Assert.That(root[0].GetSymbol(), Is.EqualTo("user"));
        Assert.That(root[1].Token.IsString, Is.True);
        Assert.That(root[1].GetString(), Is.EqualTo("Alice"));
        Assert.That(root[2].Token.IsNumber, Is.True);
        Assert.That(root[2].GetInt64(), Is.EqualTo(100L));
    }

    [Test]
    public void Parse_NestedLists_PreservesHierarchy()
    {
        using var document = SDocument.Parse(
            "(user (name \"Alice\") (score 100) (items \"Notebook\" \"Blue Pen\"))");
        var root = document.RootNode;

        Assert.That(root.Token.IsList, Is.True);
        Assert.That(root.Count, Is.EqualTo(4));
        Assert.That(root[0].Token.IsSymbol, Is.True);
        Assert.That(root[0].GetSymbol(), Is.EqualTo("user"));

        var name = root[1];
        Assert.That(name.Token.IsList, Is.True);
        Assert.That(name.Count, Is.EqualTo(2));
        Assert.That(name[0].Token.IsSymbol, Is.True);
        Assert.That(name[0].GetSymbol(), Is.EqualTo("name"));
        Assert.That(name[1].Token.IsString, Is.True);
        Assert.That(name[1].GetString(), Is.EqualTo("Alice"));

        var score = root[2];
        Assert.That(score.Token.IsList, Is.True);
        Assert.That(score.Count, Is.EqualTo(2));
        Assert.That(score[0].Token.IsSymbol, Is.True);
        Assert.That(score[0].GetSymbol(), Is.EqualTo("score"));
        Assert.That(score[1].Token.IsNumber, Is.True);
        Assert.That(score[1].GetInt64(), Is.EqualTo(100L));

        var items = root[3];
        Assert.That(items.Token.IsList, Is.True);
        Assert.That(items.Count, Is.EqualTo(3));
        Assert.That(items[0].Token.IsSymbol, Is.True);
        Assert.That(items[0].GetSymbol(), Is.EqualTo("items"));
        Assert.That(items[1].Token.IsString, Is.True);
        Assert.That(items[1].GetString(), Is.EqualTo("Notebook"));
        Assert.That(items[2].Token.IsString, Is.True);
        Assert.That(items[2].GetString(), Is.EqualTo("Blue Pen"));
    }

    [Test]
    public void Parse_Whitespace_IgnoresSpacesTabsAndLineBreaks()
    {
        using var document = SDocument.Parse(" \t\r\n( user\t\n100 )\r\n ");
        var root = document.RootNode;

        Assert.That(root.Token.IsList, Is.True);
        Assert.That(root.Count, Is.EqualTo(2));
        Assert.That(root[0].Token.IsSymbol, Is.True);
        Assert.That(root[0].GetSymbol(), Is.EqualTo("user"));
        Assert.That(root[1].Token.IsNumber, Is.True);
        Assert.That(root[1].GetInt64(), Is.EqualTo(100L));
    }

    [Test]
    public void Parse_MixedNumberSyntax_PreservesNestedTokenBoundaries()
    {
        using var document = SDocument.Parse("(0e3 (0.5 -0.25E+03) - 1/0 1e2.3 123abc)");
        var root = document.RootNode;
        Assert.That(root.Count, Is.EqualTo(6));
        Assert.That(root[0].Token.IsNumber, Is.True);
        Assert.That(root[0].ValueEquals("0e3"u8), Is.True);
        Assert.That(root[1].Count, Is.EqualTo(2));
        Assert.That(root[1][0].Token.IsNumber, Is.True);
        Assert.That(root[1][0].ValueEquals("0.5"u8), Is.True);
        Assert.That(root[1][1].Token.IsNumber, Is.True);
        Assert.That(root[1][1].ValueEquals("-0.25E+03"u8), Is.True);
        Assert.That(root[2].GetSymbol(), Is.EqualTo("-"));
        Assert.That(root[3].GetSymbol(), Is.EqualTo("1/0"));
        Assert.That(root[4].GetSymbol(), Is.EqualTo("1e2.3"));
        Assert.That(root[5].GetSymbol(), Is.EqualTo("123abc"));
    }

    [TestCase(")", 0, 1, 0)]
    [TestCase("user)", 4, 1, 4)]
    [TestCase("\n)", 1, 2, 0)]
    [TestCase("\r\n)", 2, 2, 0)]
    [TestCase("\r)", 1, 2, 0)]
    [TestCase("\t)", 1, 1, 1)]
    [TestCase("(日本語))", 11, 1, 11)]
    [TestCase("(\r\n 日本語\r\n))", 16, 3, 1)]
    [TestCase("\"Alice", 0, 1, 0)]
    [TestCase("(user \n\t\"Alice", 8, 2, 1)]
    [TestCase("\"abc\\", 0, 1, 0)]
    [TestCase("\"a\nb", 0, 1, 0)]
    [TestCase("(user", 5, 1, 5)]
    [TestCase("(\r\n", 3, 2, 0)]
    [TestCase("(\r", 2, 2, 0)]
    [TestCase("(\n日", 5, 2, 3)]
    [TestCase("", 0, 1, 0)]
    [TestCase("\r\n\t ", 4, 2, 2)]
    [TestCase("user 100", 5, 1, 5)]
    [TestCase("() ()", 3, 1, 3)]
    [TestCase("() user", 3, 1, 3)]
    [TestCase("user ()", 5, 1, 5)]
    [TestCase("() \"Alice\"", 3, 1, 3)]
    [TestCase("日本語\nnext", 10, 2, 0)]
    [TestCase("\"😀\"\r\nnext", 8, 2, 0)]
    public void Parse_InvalidSyntax_ReportsUtf8Position(string text, int bytePosition, int lineNumber, int bytePositionInLine)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var fromString = Assert.Throws<SexprParserException>(() => SDocument.Parse(text))!;
        var fromBytes = Assert.Throws<SexprParserException>(() => SDocument.Parse(bytes))!;

        foreach (var exception in new[] { fromString, fromBytes })
        {
            Assert.That(exception.BytePosition, Is.EqualTo(bytePosition));
            Assert.That(exception.LineNumber, Is.EqualTo(lineNumber));
            Assert.That(exception.BytePositionInLine, Is.EqualTo(bytePositionInLine));
        }

        Assert.That(SDocument.TryParse(text, out var stringDocument), Is.False);
        Assert.That(stringDocument, Is.Null);
        Assert.That(SDocument.TryParse(bytes, out var bytesDocument), Is.False);
        Assert.That(bytesDocument, Is.Null);
    }

    [Test]
    public void Parse_DeeplyNestedLists_PreservesHierarchy()
    {
        var count = 1000000;

        var sb = new StringBuilder();
        sb.Append('(');
        for (var i = 0; i < count / 2; i++)
        {
            sb.Append('(');
            sb.Append('a');
        }
        sb.Append('b');
        for (var i = 0; i < count / 2; i++)
        {
            sb.Append('c');
            sb.Append(')');
        }
        sb.Append(')');

        var text = sb.ToString();

        using var document = SDocument.Parse(text);
        var current = document.RootNode[0];
        for (var i = 0; i < count / 2 - 1; i++)
        {
            Assert.That(current.Token.IsList, Is.True);
            current = current[1];
        }
    }

    [Test]
    public void Parse_LongEscapedString_ReturnsDecodedLength()
    {
        var count = 1000000;
        var sb = new StringBuilder();
        sb.Append('\"');
        for (var i = 0; i < count; i++)
        {
            sb.Append("a\\\"b\\\\c");
        }
        sb.Append('\"');

        var text = sb.ToString();

        using var document = SDocument.Parse(text);
        var root = document.RootNode;
        Assert.That(root.Token.IsString, Is.True);
        Assert.That(root.Token.IsStringEscaped, Is.True);
        Assert.That(root.GetString().Length, Is.EqualTo(text.Length - (count + 1) * 2));
    }
}
