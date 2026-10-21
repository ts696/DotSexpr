using DotSexpr.Serializer;

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
foreach (var assembly in new[] { "DotSexpr.SourceGenerator.dll", "Microsoft.CodeAnalysis.dll", "Microsoft.CodeAnalysis.CSharp.dll" })
{
    if (File.Exists(Path.Combine(AppContext.BaseDirectory, assembly)))
    {
        throw new InvalidOperationException($"Build-time dependency '{assembly}' was copied to the application output.");
    }
}
Console.WriteLine("NuGet package serialization and deserialization passed.");

static T Load<T>(string value) => SexprSerializer.Deserialize<T>(value);

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
