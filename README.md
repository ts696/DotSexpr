# DotSexpr

DotSexpr is an S-expression parser and serializer for .NET and Unity, targeting .NET 10 and .NET Standard 2.1.

The parser design was inspired by [REDox](https://github.com/CAPCOM-TD-OSS/REDox).

## Installation

```sh
dotnet add package DotSexpr
```

The package includes the source generator, which requires Roslyn 4.3 or later.

### Project references

When referencing the source directly, include the generator as an analyzer:

```xml
<ProjectReference Include="path/to/DotSexpr.csproj" />
<ProjectReference Include="path/to/DotSexpr.SourceGenerator.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
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

#### Define and read a model

Annotate a partial type with `[SexprObject]`. The source generator automatically creates
its formatter and resolver without reflection.

```csharp
using DotSexpr.Serializer;

var person = SexprSerializer.Deserialize<Person>(
    "(:Name \"Alice\" :Age 20)");

[SexprObject]
public partial class Person
{
    public string Name { get; set; } = string.Empty;
    public int Age { get; set; }
}
```

Models can be non-abstract classes with a parameterless constructor or mutable structs.
For nested types, the containing types must also be partial.

#### Members and tags

Public writable instance fields and properties with public getters and setters are included.
Use `[SexprTag]` to change a member's key, for example to `:full-name`:

```csharp
[SexprTag("full-name")]
public string Name { get; set; } = string.Empty;
```

- Missing members retain their initializers.
- Unknown keys are ignored, and repeated keys use the last value.
- `init` and `required` members are not supported.

`[SexprTag]` cannot be applied to non-public members or properties without public getters
and setters.

#### Supported types and arrays

Built-in formatters support `string`, `int`, `long`, `float`, and `double`. User-defined
`[SexprObject]` models and their one-dimensional arrays are also supported. For other types,
implement and register a custom formatter.

```csharp
var numbers = SexprSerializer.Deserialize<int[]>("(1 2 3)");

var people = SexprSerializer.Deserialize<Person[]>(
    "((:Name Alice :Age 20) (:Name Bob :Age 30))");
```

Generic models are supported when their concrete types can be collected from another
model's members.

#### Custom formatters

Register an `ISexprFormatter<T>` with `DefaultResolver`:

```csharp
using DotSexpr.Serializer.Resolvers;

DefaultResolver.Register<MyType>(new MyTypeFormatter());
```

To replace an existing formatter, specify a custom resolver through options:

```csharp
var options = new SexprSerializerOptions
{
    Resolver = new CustomResolver()
};

var person = SexprSerializer.Deserialize<Person>(text, options);
```

Implement `ISexprFormatterResolver` and return your formatter from `GetFormatter<T>()`.
Delegate other types to `DefaultResolver.Instance`.

#### Models in another DLL

A model in another DLL may not have its formatter registered when first deserialized,
depending on when that DLL is initialized.

To guarantee registration, expose a startup method from the model DLL:

```csharp
using DotSexpr.Serializer.Resolvers;

public static class ModelSerialization
{
    public static void Initialize()
    {
        DefaultResolver.Register(
            DotSexpr.Generated.DotSexprGeneratedResolver.Instance);
    }
}
```

Call it when the application starts:

```csharp
ModelSerialization.Initialize();

var model = SexprSerializer.Deserialize<ExternalModel>(text);
```

### Serialization

Serialize the same models back to S-expressions:

```csharp
var person = new Person { Name = "Alice", Age = 20 };

string text = SexprSerializer.Serialize(person);
// (:Name "Alice" :Age 20)

byte[] utf8 = SexprSerializer.SerializeToUtf8Bytes(person);

var writer = new System.Buffers.ArrayBufferWriter<byte>();
SexprSerializer.Serialize(writer, person);
```

Output is UTF-8 without a BOM, with spaces between list elements. Strings are quoted and
escaped, numbers use invariant formatting, and arrays are written as lists.

The formatter for the declared type is used; specify the derived type to include its
additional members.

Null values and non-finite floating-point values are not supported.

#### Depth limit

List nesting is limited to 64 levels by default. Set `SexprSerializerOptions.MaxDepth`
to a positive value to change it:

```csharp
var options = new SexprSerializerOptions { MaxDepth = 128 };
string text = SexprSerializer.Serialize(person, options);
```

#### Writing a custom formatter

Custom formatters can use `SexprEmitter.BeginList()`, `EndList()`, `WriteString()`,
`WriteSymbol()`, and `WriteNumber()`.

Use these APIs for nested values to apply the configured resolver:

```csharp
SexprSerializer.Serialize(ref emitter, child, options);
SexprSerializer.Deserialize<Child>(in node, options);
```

When driving an emitter directly, construct it with an `IBufferWriter<byte>` and call
`Complete()` after writing the root value.

## Roadmap

- Additional collection and scalar formatters
- Constructor and init-only member deserialization

## License

[MIT](LICENSE)
