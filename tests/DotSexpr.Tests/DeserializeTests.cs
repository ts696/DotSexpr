using DotSexpr.Serializer;

namespace DotSexpr.Tests;

public partial class DeserializeTests
{
    [SexprObject]
    public partial class TestPerson
    {
        public string Name { get; set; }
        public int Age { get; set; }
    }

    [SexprObject]
    public partial class TestAccount
    {
        public TestPerson Owner { get; set; }
    }

    [SexprObject]
    public partial class TestAgent
    {
        public string Name { get; set; }
        public TestTask[] Tasks { get; set; }
    }

    [SexprObject]
    public partial class TestTask
    {
        public string Name { get; set; }

        public Expression[] Precondition { get; set; }
    }

    [SexprObject]
    public partial class TestIntArray
    {
        public int[] Values { get; set; } = [];
    }

    [SexprObject]
    public partial class TestStringArray
    {
        public string[] Values { get; set; } = [];
    }

    [Test]
    public void Deserialize_Properties_ReturnsObject()
    {
        var person = SexprSerializer.Deserialize<TestPerson>(
            "(:Name bob :Age 20)");

        Assert.That(person, Is.Not.Null);
        Assert.That(person.Name, Is.EqualTo("bob"));
        Assert.That(person.Age, Is.EqualTo(20));
    }

    [Test]
    public void Deserialize_QuotedString_ReturnsDecodedValue()
    {
        var person = SexprSerializer.Deserialize<TestPerson>(
            "(:Name \"Alice Smith\" :Age 20)");

        Assert.That(person.Name, Is.EqualTo("Alice Smith"));
        Assert.That(person.Age, Is.EqualTo(20));
    }

    [Test]
    public void Deserialize_NestedObject_ReturnsObject()
    {
        var account = SexprSerializer.Deserialize<TestAccount>(
            "(:Owner (:Name bob :Age 20))");

        Assert.That(account.Owner, Is.Not.Null);
        Assert.That(account.Owner.Name, Is.EqualTo("bob"));
        Assert.That(account.Owner.Age, Is.EqualTo(20));
    }

    [Test]
    public void Deserialize_ObjectArray_PreservesElementOrder()
    {
        var agent = SexprSerializer.Deserialize<TestAgent>(
            "(:Name scout :Tasks ((:Name move) (:Name inspect)))");

        Assert.That(agent.Name, Is.EqualTo("scout"));
        Assert.That(agent.Tasks, Is.Not.Null);
        Assert.That(agent.Tasks.Select(task => task.Name), Is.EqualTo(new[] { "move", "inspect" }));
    }

    [Test]
    public void Deserialize_Int32Array_ReturnsValues()
    {
        var result = SexprSerializer.Deserialize<TestIntArray>(
            "(:Values (10 -20 0))");

        Assert.That(result.Values, Is.EqualTo(new[] { 10, -20, 0 }));
    }

    [Test]
    public void Deserialize_StringArray_ReturnsValues()
    {
        var result = SexprSerializer.Deserialize<TestStringArray>(
            "(:Values (alice \"Bob Smith\"))");

        Assert.That(result.Values, Is.EqualTo(new[] { "alice", "Bob Smith" }));
    }

    [Test]
    public void Deserialize_SymbolExpression_ReturnsSymbol()
    {
        var task = SexprSerializer.Deserialize<TestTask>(
            "(:Precondition (?from))");

        Assert.That(task.Precondition, Has.Length.EqualTo(1));
        Assert.That(task.Precondition[0], Is.EqualTo(new SymbolExpression("?from")));
    }

    [Test]
    public void Deserialize_CallExpression_ReturnsCallWithArguments()
    {
        var task = SexprSerializer.Deserialize<TestTask>(
            "(:Precondition ((reachable ?from ?to)))");

        Assert.That(task.Precondition, Has.Length.EqualTo(1));
        Assert.That(task.Precondition[0], Is.TypeOf<CallExpression>());
        var call = (CallExpression)task.Precondition[0];
        Assert.That(call.Name, Is.EqualTo("reachable"));
        Assert.That(call.Arguments, Is.EqualTo(new Expression[]
        {
            new SymbolExpression("?from"),
            new SymbolExpression("?to")
        }));
    }

    [Test]
    public void Deserialize_NestedExpression_PreservesHierarchy()
    {
        var task = SexprSerializer.Deserialize<TestTask>(
            """
            (:Precondition (
                (and
                    (reachable ?from ?to)
                    (not (Place_Visited ?to)))))
            """);

        Assert.That(task.Precondition, Has.Length.EqualTo(1));
        var conjunction = (CallExpression)task.Precondition[0];
        Assert.That(conjunction.Name, Is.EqualTo("and"));
        Assert.That(conjunction.Arguments, Has.Count.EqualTo(2));

        var reachable = (CallExpression)conjunction.Arguments[0];
        Assert.That(reachable.Name, Is.EqualTo("reachable"));
        Assert.That(reachable.Arguments, Is.EqualTo(new Expression[]
        {
            new SymbolExpression("?from"),
            new SymbolExpression("?to")
        }));

        var negation = (CallExpression)conjunction.Arguments[1];
        Assert.That(negation.Name, Is.EqualTo("not"));
        Assert.That(negation.Arguments, Has.Count.EqualTo(1));

        var visited = (CallExpression)negation.Arguments[0];
        Assert.That(visited.Name, Is.EqualTo("Place_Visited"));
        Assert.That(visited.Arguments, Is.EqualTo(new[] { new SymbolExpression("?to") }));
    }

    [Test]
    public void Deserialize_ExpressionArray_PreservesOrder()
    {
        var task = SexprSerializer.Deserialize<TestTask>(
            """
            (:Precondition (
                (reachable ?from ?to)
                (Place_Visited ?to)))
            """);

        Assert.That(task.Precondition, Has.Length.EqualTo(2));
        Assert.That(task.Precondition, Is.All.TypeOf<CallExpression>());
        Assert.That(task.Precondition.Cast<CallExpression>().Select(call => call.Name),
            Is.EqualTo(new[] { "reachable", "Place_Visited" }));
    }

    [Test]
    public void Deserialize_UnknownProperty_IsIgnored()
    {
        var person = SexprSerializer.Deserialize<TestPerson>(
            "(:Name bob :Extra (:Value 10) :Age 20)");

        Assert.That(person.Name, Is.EqualTo("bob"));
        Assert.That(person.Age, Is.EqualTo(20));
    }

    [TestCase("(:Age bob)", "$.Age", 6)]
    [TestCase("(:Age ())", "$.Age", 6)]
    [TestCase("(:Name 20)", "$.Name", 7)]
    public void Deserialize_TypeMismatch_ReportsValueLocation(string text, string path, int offset)
    {
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Deserialize<TestPerson>(text))!;

        AssertError(error, path, offset, 1, offset);
    }

    [TestCase("(:Name)", "$.Name", 1)]
    [TestCase("(:Name bob :Age)", "$.Age", 11)]
    [TestCase("(Name bob)", "$", 1)]
    [TestCase("(\":Name\" bob)", "$", 1)]
    public void Deserialize_InvalidProperties_ReportsKeyLocation(string text, string path, int offset)
    {
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Deserialize<TestPerson>(text))!;

        AssertError(error, path, offset, 1, offset);
    }

    [TestCase("bob")]
    [TestCase("20")]
    [TestCase("\"Alice\"")]
    public void Deserialize_NonListRoot_ThrowsSerializerException(string text)
    {
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Deserialize<TestPerson>(text))!;

        AssertError(error, "$", 0, 1, 0);
    }

    [Test]
    public void Deserialize_NonListObjectValue_ReportsValueLocation()
    {
        var error = Assert.Throws<SexprSerializerException>(
            () => SexprSerializer.Deserialize<TestAccount>("(:Owner bob)"))!;

        AssertError(error, "$.Owner", 8, 1, 8);
    }

    [TestCase("(:Tasks 10)", "$.Tasks", 8)]
    [TestCase("(:Tasks (bob))", "$.Tasks[0]", 9)]
    [TestCase("(:Tasks ((:Name move) (:Name 20)))", "$.Tasks[1].Name", 29)]
    public void Deserialize_InvalidObjectArray_ReportsElementLocation(string text, string path, int offset)
    {
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Deserialize<TestAgent>(text))!;

        AssertError(error, path, offset, 1, offset);
    }

    [Test]
    public void Deserialize_NestedObject_ReportsDeepestValueAndFormatsMessage()
    {
        const string text = "(\r\n  :Owner (\r\n    :Name 日本語 :Age bob\r\n  )\r\n)";
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Deserialize<TestAccount>(text))!;

        AssertError(error, "$.Owner.Age", 40, 3, 25);
        Assert.That(error.Message, Is.EqualTo(
            "Cannot deserialize property ':Age' as 'Int32'. Expected a number. " +
            "(Path: $.Owner.Age, Line 3, byte position 25.)"));
    }

    // Call-list indices include the function name at index 0.
    [TestCase("(:Precondition ((20 ?x)))", "$.Precondition[0][0]", 17)]
    [TestCase("(:Precondition ((and ?x (not 20))))", "$.Precondition[0][2][1]", 29)]
    public void Deserialize_InvalidExpression_ReportsSourcePath(string text, string path, int offset)
    {
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Deserialize<TestTask>(text))!;

        AssertError(error, path, offset, 1, offset);
    }

    [Test]
    public void Deserialize_ArrayOverflow_PreservesConversionException()
    {
        var error = Assert.Throws<SexprSerializerException>(
            () => SexprSerializer.Deserialize<TestIntArray>("(:Values (0 2147483648))"))!;

        AssertError(error, "$.Values[1]", 12, 1, 12);
        Assert.That(error.InnerException, Is.TypeOf<OverflowException>());
    }

    [Test]
    public void Deserialize_MissingUnusualKeyValue_QuotesAndEscapesPath()
    {
        var error = Assert.Throws<SexprSerializerException>(
            () => SexprSerializer.Deserialize<TestPerson>("(:a.b'\\c)"))!;

        AssertError(error, "$['a.b\\'\\\\c']", 1, 1, 1);
    }

    private static void AssertError(SexprSerializerException error, string path, int offset, int line, int byteInLine)
    {
        Assert.That(error.Path, Is.EqualTo(path));
        Assert.That(error.BytePosition, Is.EqualTo(offset));
        Assert.That(error.LineNumber, Is.EqualTo(line));
        Assert.That(error.BytePositionInLine, Is.EqualTo(byteInLine));
    }

    [SexprObject]
    public partial struct TestPersonStruct
    {
        public int Age { get; set; }
    }

    [Test]
    public void Deserialize_Struct_ReturnsPopulatedValue()
    {
        var person = SexprSerializer.Deserialize<TestPersonStruct>(
            "(:Age 20)");

        Assert.That(person.Age, Is.EqualTo(20));
    }
}
