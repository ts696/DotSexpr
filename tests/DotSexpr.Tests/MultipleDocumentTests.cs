using System.Text;
using DotSexpr.Emitter;
using DotSexpr.Serializer;

namespace DotSexpr.Tests;

[TestFixture(false)]
[TestFixture(true)]
public partial class MultipleDocumentTests(bool utf8)
{
    [Test]
    public void ParseMultiple_PreservesTopLevelNodesAndSourceRanges()
    {
        const string text = "; prefix\r\n(move 日本語 (step))#| separator |#\"; () #|\" 42 () tail";
        using var document = Parse(text);
        var root = document.RootNode;

        Assert.That(root.Token.IsList, Is.True);
        Assert.That(root.Count, Is.EqualTo(5));
        Assert.That(root.GetRawUtf8().ToArray(), Is.EqualTo(Encoding.UTF8.GetBytes(text)));
        Assert.That(document.Source.ToArray(), Is.EqualTo(Encoding.UTF8.GetBytes(text)));
        Assert.That(root.Select(node => Encoding.UTF8.GetString(node.GetRawUtf8().Span)),
            Is.EqualTo(new[] { "(move 日本語 (step))", "\"; () #|\"", "42", "()", "tail" }));
        Assert.That(root[0].Token.Offset, Is.EqualTo(10));
        Assert.That(root[0][1].GetSymbol(), Is.EqualTo("日本語"));
        Assert.That(root[0][2][0].GetSymbol(), Is.EqualTo("step"));
        Assert.That(root[1].GetString(), Is.EqualTo("; () #|"));
        Assert.That(root[2].GetInt32(), Is.EqualTo(42));
        Assert.That(root[3].Count, Is.Zero);
        Assert.That(root[4].GetSymbol(), Is.EqualTo("tail"));
    }

    [TestCase("(a)(b)", 2)]
    [TestCase("a(b)c", 3)]
    [TestCase("\"a\"\"b\"", 2)]
    [TestCase("(a)", 1)]
    [TestCase("a", 1)]
    public void ParseMultiple_RecognizesExpressionBoundaries(string text, int count)
    {
        using var document = Parse(text);
        Assert.That(document.RootNode.Count, Is.EqualTo(count));
    }

    [TestCase("")]
    [TestCase(" \t\r\n")]
    [TestCase("; only a comment")]
    [TestCase("#| outer #| inner |# |# ; end")]
    public void EmptyInput_ReturnsNoDocuments(string text)
    {
        using var document = Parse(text);
        Assert.That(document.RootNode.Count, Is.Zero);
        Assert.That(document.RootNode, Is.Empty);
        Assert.That(Deserialize<int>(text), Is.Empty);
    }

    [Test]
    public void ParseMultiple_MaxDepth_CountsOnlyExplicitLists()
    {
        var options = new SDocumentOptions { MaxDepth = 1 };
        using var document = Parse("(a) (b) 1 ()", options);
        Assert.That(document.RootNode.Count, Is.EqualTo(4));

        var error = Assert.Throws<SexprParserException>(() => Parse("(a) ((b))", options))!;
        Assert.That(error.BytePosition, Is.EqualTo(5));
        Assert.Throws<ArgumentOutOfRangeException>(() => Parse("", new SDocumentOptions { MaxDepth = -1 }));
    }

    [TestCase("ok )", 3)]
    [TestCase("ok (", 4)]
    [TestCase("ok \"unfinished", 3)]
    [TestCase("ok #| unfinished", 3)]
    [TestCase("ok \"\\q\"", 3)]
    [TestCase("#| unfinished", 0)]
    public void InvalidSyntax_ReportsOriginalPosition(string text, int position)
    {
        var parseError = Assert.Throws<SexprParserException>(() => Parse(text))!;
        var deserializeError = Assert.Throws<SexprParserException>(() => Deserialize<string>(text))!;
        Assert.That(parseError.BytePosition, Is.EqualTo(position));
        Assert.That(deserializeError.BytePosition, Is.EqualTo(position));
    }

    [Test]
    public void ParseMultiple_MultilineSyntaxError_PreservesUtf8Position()
    {
        const string text = "日本語\r\n  )";
        var error = Assert.Throws<SexprParserException>(() => Parse(text))!;
        Assert.That(error.BytePosition, Is.EqualTo(13));
        Assert.That(error.LineNumber, Is.EqualTo(2));
        Assert.That(error.BytePositionInLine, Is.EqualTo(2));
    }

    [Test]
    public void DeserializeMultipleDocuments_ReadsModelsInOrder()
    {
        var people = Deserialize<Person>("(:Name 日本語 :Age 20)\r\n; separator\r\n(:Name \"Bob\" :Age 30)");
        Assert.That(people.Select(person => person.Name), Is.EqualTo(new[] { "日本語", "Bob" }));
        Assert.That(people.Select(person => person.Age), Is.EqualTo(new[] { 20, 30 }));
    }

    [Test]
    public void DeserializeMultipleDocuments_ReadsScalarsArraysAndCallUnions()
    {
        Assert.That(Deserialize<int>("1 -2 3"), Is.EqualTo(new[] { 1, -2, 3 }));
        Assert.That(Deserialize<string>("日本語 \"a\\nb\""), Is.EqualTo(new[] { "日本語", "a\nb" }));
        var arrays = Deserialize<int[]>("(1 2) () (3)");
        Assert.That(arrays, Has.Length.EqualTo(3));
        Assert.That(arrays[0], Is.EqualTo(new[] { 1, 2 }));
        Assert.That(arrays[1], Is.Empty);
        Assert.That(arrays[2], Is.EqualTo(new[] { 3 }));
        var calls = Deserialize<CallUnionTests.ActionExpression>("(move 1) (jump 0.5) (step)");
        Assert.That(calls.Select(call => call.GetType()),
            Is.EqualTo(new[] { typeof(CallUnionTests.Move), typeof(CallUnionTests.Jump), typeof(CallUnionTests.Step) }));
    }

    [Test]
    public void DeserializeMultipleDocuments_TypeError_ReportsDocumentIndexAndOriginalLocation()
    {
        const string text = "(:Name 日本語 :Age 20)\r\n(:Age bad)";
        var error = Assert.Throws<SexprSerializerException>(() => Deserialize<Person>(text))!;
        Assert.That(error.Path, Is.EqualTo("$[1].Age"));
        Assert.That(error.BytePosition, Is.EqualTo(33));
        Assert.That(error.LineNumber, Is.EqualTo(2));
        Assert.That(error.BytePositionInLine, Is.EqualTo(6));
    }

    [Test]
    public void DeserializeMultipleDocuments_ArrayError_PreservesNestedIndices()
    {
        var error = Assert.Throws<SexprSerializerException>(() => Deserialize<int[]>("(1) (2 bad)"))!;
        Assert.That(error.Path, Is.EqualTo("$[1][1]"));
        Assert.That(error.BytePosition, Is.EqualTo(7));
    }

    [Test]
    public void DeserializeMultipleDocuments_ForwardsCustomResolverAndOptions()
    {
        var options = new SexprSerializerOptions { Resolver = new CustomResolver(), MaxDepth = 7 };
        Assert.That(Deserialize<int>("1 2", options), Is.EqualTo(new[] { 8, 9 }));
    }

    [Test]
    public void DeserializeMultipleDocuments_MissingFormatter_ReportsDocumentLocation()
    {
        var error = Assert.Throws<SexprSerializerException>(() => Deserialize<DateTime>("; prefix\n0"))!;
        Assert.That(error.Path, Is.EqualTo("$[0]"));
        Assert.That(error.BytePosition, Is.EqualTo(9));
        Assert.That(error.LineNumber, Is.EqualTo(2));
        Assert.That(error.BytePositionInLine, Is.Zero);
    }

    [Test]
    public void SingleDocumentApis_StillRejectMultipleExpressionsAndEmptyInput()
    {
        Assert.Throws<SexprParserException>(() => SDocument.Parse("1 2"));
        Assert.Throws<SexprParserException>(() => SexprSerializer.Deserialize<int>("1 2"));
        Assert.That(SDocument.TryParse("1 2", out _), Is.False);
        Assert.Throws<SexprParserException>(() => SDocument.Parse(""));
    }

    [Test]
    public void StringApis_RejectNullInput()
    {
        Assert.Throws<ArgumentNullException>(() => SDocument.ParseMultiple((string)null!));
        Assert.Throws<ArgumentNullException>(() => SexprSerializer.DeserializeMultipleDocuments<int>((string)null!));
    }

    [Test]
    public void Utf8Apis_RejectInvalidStringEncodingInLaterDocument()
    {
        byte[] bytes = [0x31, 0x20, 0x22, 0xc0, 0xaf, 0x22];
        var error = Assert.Throws<SexprParserException>(() => SDocument.ParseMultiple(bytes))!;
        Assert.That(error.BytePosition, Is.EqualTo(2));
        Assert.Throws<SexprParserException>(() => SexprSerializer.DeserializeMultipleDocuments<string>(bytes));
    }

    private SDocument Parse(string text, SDocumentOptions options = default) =>
        utf8 ? SDocument.ParseMultiple(Encoding.UTF8.GetBytes(text), options) : SDocument.ParseMultiple(text, options);

    private T[] Deserialize<T>(string text, SexprSerializerOptions? options = null) => utf8
        ? SexprSerializer.DeserializeMultipleDocuments<T>(Encoding.UTF8.GetBytes(text), options)
        : SexprSerializer.DeserializeMultipleDocuments<T>(text, options);

    [SexprObject]
    public partial class Person
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
    }

    private sealed class CustomResolver : ISexprFormatterResolver
    {
        public ISexprFormatter<T>? GetFormatter<T>() => typeof(T) == typeof(int)
            ? (ISexprFormatter<T>)(object)new CustomIntFormatter() : null;
    }

    private sealed class CustomIntFormatter : ISexprFormatter<int>
    {
        public int Deserialize(in SNode node, SexprSerializerOptions options) => node.GetInt32() + options.MaxDepth;
        public void Serialize(ref SexprEmitter emitter, int value, SexprSerializerOptions options) => emitter.WriteNumber(value);
    }
}
