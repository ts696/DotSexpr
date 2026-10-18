# DotSexpr

DotSexpr is an S-expression parser and deserializer for .NET Standard 2.1 and .NET 10.
The .NET Standard 2.1 target is intended for use with Unity 6.

The parser design was inspired by [REDox](https://github.com/CAPCOM-TD-OSS/REDox).

## Installation

Install the package using the .NET CLI:

```sh
dotnet add package DotSexpr
```

## Usage

### Parsing

Parse an S-expression and iterate over its child nodes:

```csharp
using System;
using DotSexpr;

using var document = SDocument.Parse("(move home park)");

foreach (var node in document.RootNode)
{
    Console.WriteLine(node.GetSymbol());
}
```

### Deserialization

Deserialize an S-expression into a C# object:

```csharp
using System;
using DotSexpr.Serializer;

var person = SexprSerializer.Deserialize<Person>("(:Name \"Alice\" :Age 20)");

Console.WriteLine($"{person.Name} ({person.Age})");

public class Person
{
    public string Name { get; set; } = string.Empty;
    public int Age { get; set; }
}
```

## Roadmap

- Serialization support
- Attribute-based customization of deserialization
- Source generation to improve deserialization performance

## License

[MIT](LICENSE)
