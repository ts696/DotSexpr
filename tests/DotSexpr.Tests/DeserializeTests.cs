using DotSexpr.Serializer;

namespace DotSexpr.Tests;

public class DeserializeTests
{
    public class TestPerson
    {
        public string Name { get; set; }
        public int Age { get; set; }
    }

    public class TestAccount
    {
        public TestPerson Owner { get; set; }
    }

    public class TestAgent
    {
        public string Name { get; set; }
        public TestTask[] Tasks { get; set; }
    }

    public class TestTask
    {
        public string Name { get; set; }

        public Expression[] Precondition { get; set; }
    }

    public class TestArray<T>
    {
        public T[] Values { get; set; } = [];
    }

    [Test]
    public void Deserialize_Properties_ReturnsObject()
    {
        var person = SexprSerializer.Deserialize<TestPerson>(
            """
            (
            :Name bob
            :Age 20
            )
            """);

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
        var result = SexprSerializer.Deserialize<TestArray<int>>(
            "(:Values (10 -20 0))");

        Assert.That(result.Values, Is.EqualTo(new[] { 10, -20, 0 }));
    }

    [Test]
    public void Deserialize_StringArray_ReturnsValues()
    {
        var result = SexprSerializer.Deserialize<TestArray<string>>(
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

    [TestCase("(:Age bob)")]
    [TestCase("(:Age ())")]
    [TestCase("(:Name 20)")]
    public void Deserialize_TypeMismatch_ThrowsSerializerException(string text)
    {
        Assert.That(() => SexprSerializer.Deserialize<TestPerson>(text),
            Throws.TypeOf<SexprSerializerException>());
    }

    [TestCase("(:Name)")]
    [TestCase("(:Name bob :Age)")]
    [TestCase("(Name bob)")]
    [TestCase("(\":Name\" bob)")]
    public void Deserialize_InvalidProperties_ThrowsSerializerException(string text)
    {
        Assert.That(() => SexprSerializer.Deserialize<TestPerson>(text),
            Throws.TypeOf<SexprSerializerException>());
    }

    [TestCase("bob")]
    [TestCase("20")]
    [TestCase("\"Alice\"")]
    public void Deserialize_NonListRoot_ThrowsSerializerException(string text)
    {
        Assert.That(() => SexprSerializer.Deserialize<TestPerson>(text),
            Throws.TypeOf<SexprSerializerException>());
    }

    [Test]
    public void Deserialize_NonListObjectValue_ThrowsSerializerException()
    {
        Assert.That(() => SexprSerializer.Deserialize<TestAccount>("(:Owner bob)"),
            Throws.TypeOf<SexprSerializerException>());
    }

    [TestCase("(:Tasks 10)")]
    [TestCase("(:Tasks (bob))")]
    public void Deserialize_InvalidObjectArray_ThrowsSerializerException(string text)
    {
        Assert.That(() => SexprSerializer.Deserialize<TestAgent>(text),
            Throws.TypeOf<SexprSerializerException>());
    }
}
