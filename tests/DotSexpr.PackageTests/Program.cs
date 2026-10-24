using DotSexpr;
using DotSexpr.Emitter;
using DotSexpr.Serializer;
using DotSexpr.Serializer.Resolvers;

#if NATIVE_AOT_TEST
if (System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
{
    throw new InvalidOperationException("The Native AOT smoke test must run as a native executable.");
}
#endif

const string text = "(:People ((:Name \"Alice\" :Age 20)))";
var document = Load<Document>(text);
if (document.People.Length != 1 || document.People[0].Name != "Alice" || document.People[0].Age != 20)
{
    throw new InvalidOperationException("The packaged generator did not deserialize the model.");
}
if (SexprSerializer.Serialize(document) != text)
{
    throw new InvalidOperationException("The packaged generator did not serialize the model.");
}
var people = Load<Person[]>("((:Name Bob :Age 30))");
if (people.Length != 1 || people[0].Name != "Bob" || people[0].Age != 30)
{
    throw new InvalidOperationException("The packaged generator did not deserialize a model array.");
}
const string conditionText = "(and (not (ready ?actor)) (cost 2 0.5))";
var condition = Load<Condition>(conditionText);
if (condition is not And || condition.Arguments.Count != 2 ||
    condition.Arguments[0] is not Not { Arguments.Count: 1 } negation ||
    negation.Arguments[0] is not Condition.UnknownCall { Name: "ready" } ||
    condition.Arguments[1] is not Condition.UnknownCall { Name: "cost" } cost ||
    cost.Arguments.Count != 2 ||
    cost.Arguments[0] is not IntegerExpression { Value: 2 } ||
    cost.Arguments[1] is not NumberExpression { Value: 0.5 })
{
    throw new InvalidOperationException("The packaged generator did not deserialize typed calls and numeric expressions.");
}
if (SexprSerializer.Serialize(condition) != conditionText)
{
    throw new InvalidOperationException("The packaged generator did not serialize a call union.");
}
var repeat = Load<Repeat>("(repeat 3 (move ?from ?to))");
if (repeat.Arguments[0] is not IntegerExpression { Value: 3 } ||
    repeat.Arguments[1] is not CallExpression { Name: "move" } ||
    SexprSerializer.Serialize(repeat) != "(repeat 3 (move ?from ?to))")
{
    throw new InvalidOperationException("The packaged generator did not apply argument types.");
}
var changed = repeat with { Count = new IntegerExpression(4), Actions = [] };
if (SexprSerializer.Serialize(changed) != "(repeat 4)")
{
    throw new InvalidOperationException("The packaged generator did not use constructor-bound properties.");
}
RoundTrip(42, "42");
RoundTrip(long.MaxValue, "9223372036854775807");
RoundTrip(1.5f, "1.5");
RoundTrip(2.5d, "2.5");
RoundTrip("hello", "\"hello\"");
RoundTrip(new[] { 1, 2 }, "(1 2)");
RoundTrip<Expression>(new CallExpression("move", [new SymbolExpression("home"), new IntegerExpression(3)]),
    "(move home 3)");

DefaultResolver.Register<Guid>(new GuidFormatter());
const string treeText = "(:Value (:Value 7) :Children ((:Value (:Value 8) :Children () :Rows () " +
    ":Id \"00000000-0000-0000-0000-000000000000\")) :Rows ((1 2) (3)) " +
    ":Id \"00000000-0000-0000-0000-000000000000\")";
var tree = Load<Tree>(treeText);
if (tree.Value.Value != 7 || tree.Children[0].Value.Value != 8 || tree.Rows[1][0] != 3 || tree.Id != Guid.Empty)
{
    throw new InvalidOperationException("Recursive models, generic structs, nested arrays or custom formatters failed.");
}
RoundTrip(tree, treeText);
RoundTrip(tree.Value, "(:Value 7)");

var writer = new System.Buffers.ArrayBufferWriter<byte>();
SexprSerializer.Serialize(writer, tree);
if (System.Text.Encoding.UTF8.GetString(writer.WrittenSpan) != treeText ||
    System.Text.Encoding.UTF8.GetString(SexprSerializer.SerializeToUtf8Bytes(tree)) != treeText)
{
    throw new InvalidOperationException("UTF-8 serialization failed.");
}
using (var parsed = SDocument.Parse(treeText))
{
    if (SexprSerializer.Deserialize<Tree>(parsed.RootNode).Children[0].Value.Value != 8)
    {
        throw new InvalidOperationException("Deserialization from a parsed node failed.");
    }
}
foreach (var assembly in new[] { "DotSexpr.SourceGenerator.dll", "Microsoft.CodeAnalysis.dll", "Microsoft.CodeAnalysis.CSharp.dll" })
{
    if (File.Exists(Path.Combine(AppContext.BaseDirectory, assembly)))
    {
        throw new InvalidOperationException($"Build-time dependency '{assembly}' was copied to the application output.");
    }
}
Console.WriteLine("NuGet package serialization and deserialization passed.");

static T Load<T>(string value) => SexprSerializer.Deserialize<T>(value);

static void RoundTrip<T>(T value, string expected)
{
    if (SexprSerializer.Serialize(value) != expected ||
        SexprSerializer.Serialize(Load<T>(expected)) != expected)
    {
        throw new InvalidOperationException($"Round trip failed for '{typeof(T)}'.");
    }
}

[SexprObject]
public partial struct ValueBox<T>
{
    public T Value { get; set; }
}

[SexprObject]
public partial class Tree
{
    public ValueBox<int> Value { get; set; }
    public Tree[] Children { get; set; } = [];
    public int[][] Rows { get; set; } = [];
    public Guid Id { get; set; }
}

public sealed class GuidFormatter : ISexprFormatter<Guid>
{
    public void Serialize(ref SexprEmitter emitter, Guid value, SexprSerializerOptions options) =>
        emitter.WriteString(value.ToString());

    public Guid Deserialize(in SNode node, SexprSerializerOptions options) => Guid.Parse(node.GetString());
}

[SexprObject]
public partial class Document
{
    public Person[] People { get; set; } = [];
}

[SexprObject]
public partial class Person
{
    public string Name { get; set; } = "";
    public int Age { get; set; }
}

[SexprCallUnion(typeof(And), typeof(Not), AllowUnknownCalls = true)]
public abstract partial record Condition : CallExpression;

[SexprCall("and")]
public sealed partial record And(params Condition[] Operands) : Condition;

[SexprCall("not")]
public sealed partial record Not(Condition Operand) : Condition;

[SexprCall("repeat")]
public sealed partial record Repeat(IntegerExpression Count, params CallExpression[] Actions) : CallExpression;
