# DotSexpr

DotSexpr is an S-expression parser and serializer for .NET and Unity, targeting .NET 10 and .NET Standard 2.1.

The parser design was inspired by [REDox](https://github.com/CAPCOM-TD-OSS/REDox).

## Installation

```sh
dotnet add package DotSexpr
```

The package includes the source generator, which requires Roslyn 4.3 or later.

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

#### Multiple documents

Use `DeserializeMultipleDocuments<T>` to read zero or more top-level expressions as
separate documents, without wrapping them in an outer list:

```csharp
var people = SexprSerializer.DeserializeMultipleDocuments<Person>(
    "(:Name Alice :Age 20)\n(:Name Bob :Age 30)");
// Returns a Person[] in source order.
```

Both `string` and UTF-8 `ReadOnlySpan<byte>` inputs are supported, along with custom
serializer options. Whitespace and comments may separate documents; no special document
separator is needed. Empty input or input containing only whitespace and comments returns
an empty array. Each expression is deserialized as `T`, so lists remain individual documents
(use `T = int[]`, for example, to read several lists of integers).

Invalid syntax or values throw immediately without returning partial results. Error
positions refer to the original input, and serializer error paths start with the zero-based
document index, for example `$[1].Age`.

To inspect syntax without deserializing, use `SDocument.ParseMultiple` with either input
type and optional `SDocumentOptions`. Its `RootNode` is a synthetic list whose children are
the top-level expressions. Its raw text covers the entire original input; the synthetic
list adds no parentheses and does not count toward `MaxDepth`.

The existing `SDocument.Parse`, `TryParse`, and `SexprSerializer.Deserialize<T>` APIs
continue to require exactly one top-level expression.

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

Only public writable instance fields and properties with public getters and setters are
serialized and deserialized.
Use `[SexprTag]` to change a member's key, for example to `:full-name`:

```csharp
[SexprTag("full-name")]
public string Name { get; set; } = string.Empty;
```

- Missing members retain their initializers.
- Unknown keys are ignored, and repeated keys use the last value.
- `init` and `required` members are not supported.

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

#### Expressions

`Expression` represents symbols, numbers, and nested calls. Integer literals use
`IntegerExpression` (`long`); literals containing a fraction or exponent use
`NumberExpression` (`double`).

```csharp
var expression = SexprSerializer.Deserialize<Expression>("(move ?from ?to 10 0.5)");
var call = (CallExpression)expression;
long distance = ((IntegerExpression)call.Arguments[2]).Value;
double speed = ((NumberExpression)call.Arguments[3]).Value;
```

#### Typed calls

To constrain a call's name, define a partial record derived directly from
`CallExpression` and annotate it with `[SexprCall]`:

```csharp
var move = SexprSerializer.Deserialize<Move>("(move ?from ?to 10)");
// Deserialize<Move>("(jump ?from ?to)") throws at $[0].

var created = new Move(new Expression[]
{
    new SymbolExpression("?from"),
    new SymbolExpression("?to"),
    new IntegerExpression(10),
    new NumberExpression(0.5)
});
string text = SexprSerializer.Serialize(created); // (move ?from ?to 10 0.5)

[SexprCall("move")]
public sealed partial record Move(params Expression[] Items) : CallExpression;
```

The generator creates a formatter registered through the generated resolver. Typed
calls also work in arrays and as `[SexprObject]` members. Containing types must be partial.

Define arguments through the positional record's constructor. The call name appears
only in `[SexprCall]`:

```csharp
[SexprCall("not")]
public sealed partial record Not(CallExpression Operand) : CallExpression;

[SexprCall("and")]
public sealed partial record And(params CallExpression[] Operands) : CallExpression;

[SexprCall("repeat")]
public sealed partial record Repeat(IntegerExpression Count, params CallExpression[] Actions) : CallExpression;

[SexprCall("step")]
public sealed partial record Step() : CallExpression;
```

Each ordinary parameter is one required argument. A trailing `params T[]` accepts
zero or more arguments of type `T`, so `(and)`, `(and (ready x) (visited y))`, and
`(repeat 3 (move x) (inspect x))` are valid. An empty positional constructor such as
`Step()` requires no arguments. Use `params Expression[]` to accept arbitrary expressions.
Parameters must derive from `Expression`, or be `Expression`
itself; optional parameters, additional constructors and instance fields/properties
are not supported.

The formatter calls the positional constructor when reading. `Name` is generated from
the attribute, and `Arguments` reflects the positional properties. Update those
properties directly or through `with`; assigning `Arguments` on a positional call
throws `InvalidOperationException`.

Each argument uses its declared type's formatter in both directions, including
dedicated calls, unions, and custom formatters. Invalid values report the argument's
path; constructed values and values changed with `with` are checked when serialized.
All `[SexprCall]` records must declare a positional parameter list, including `()`
for calls without arguments.

Names are case-sensitive and read-only, including on ordinary `CallExpression` values.
Arguments remain expressions and may contain calls with other names. Deserializing as
`Expression` or `CallExpression` produces an ordinary call; request `Move` to validate
its name.

#### Call unions

To allow several calls in the same position, register them on a common abstract record:

```csharp
ActionExpression[] actions = SexprSerializer.Deserialize<ActionExpression[]>(
    "((move ?to 10) (jump 0.5) (step))");
// The elements are Move, Jump, and Step. An unregistered call such as (fly) throws.

[SexprCallUnion(typeof(Move), typeof(Jump), typeof(Step))]
public abstract partial record ActionExpression : CallExpression;

[SexprCall("move")]
public sealed partial record Move(params Expression[] Items) : ActionExpression;

[SexprCall("jump")]
public sealed partial record Jump(params Expression[] Items) : ActionExpression;

[SexprCall("step")]
public sealed partial record Step() : ActionExpression;
```

Union types must be non-generic abstract partial records directly derived from
`CallExpression`; registered calls must directly derive from that union and use unique
names. The union formatter rejects unregistered names on input and unregistered runtime
types on output. The same validation applies to union-typed model members and arrays.

Registered calls are recognized recursively inside arguments. Set `AllowUnknownCalls = true`
to accept unregistered names at any depth. The generated
`UnknownCall` type inherits from the union and preserves the call name and arguments:

```csharp
var task = SexprSerializer.Deserialize<ActionDefinition>(
    "(:precondition (and (ready ?actor) (not (injured ?actor))))");
// and -> And, nested not -> Not, ready and injured -> ConditionExpression.UnknownCall

[SexprCallUnion(typeof(And), typeof(Or), typeof(Not), AllowUnknownCalls = true)]
public abstract partial record ConditionExpression : CallExpression;

[SexprCall("and")]
public sealed partial record And(params ConditionExpression[] Operands) : ConditionExpression;
[SexprCall("or")]
public sealed partial record Or(params ConditionExpression[] Operands) : ConditionExpression;
[SexprCall("not")]
public sealed partial record Not(ConditionExpression Operand) : ConditionExpression;

[SexprObject]
public partial class ActionDefinition
{
    [SexprTag("precondition")]
    public ConditionExpression Precondition { get; set; } = new And();
}
```

The same model also accepts a standalone predicate: `(:precondition (ready ?actor))`.

When serialized through the union, registered names must use their dedicated call types.
The generated nested name `UnknownCall` is reserved.

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

During serialization, list nesting is limited to 64 levels by default. Set
`SexprSerializerOptions.MaxDepth` to a positive value to change this limit.

### Models in another DLL

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

### Custom formatters

Implement `ISexprFormatter<T>` for both serialization and deserialization. For example,
this formatter represents a `Guid` as a quoted string:

```csharp
using System;
using DotSexpr;
using DotSexpr.Emitter;
using DotSexpr.Serializer;
using DotSexpr.Serializer.Resolvers;

public sealed class GuidFormatter : ISexprFormatter<Guid>
{
    public void Serialize(ref SexprEmitter emitter, Guid value, SexprSerializerOptions options)
    {
        emitter.WriteString(value.ToString());
    }

    public Guid Deserialize(in SNode node, SexprSerializerOptions options)
    {
        return Guid.Parse(node.GetString());
    }
}
```

Register it with `DefaultResolver` to add support for the type:

```csharp
DefaultResolver.Register<Guid>(new GuidFormatter());

string text = SexprSerializer.Serialize(Guid.Empty);
Guid value = SexprSerializer.Deserialize<Guid>(text);
```

To choose a formatter for individual operations, including overriding an existing formatter,
specify a custom resolver through options:

```csharp
var options = new SexprSerializerOptions
{
    Resolver = new CustomResolver()
};

string text = SexprSerializer.Serialize(Guid.Empty, options);
Guid value = SexprSerializer.Deserialize<Guid>(text, options);

public sealed class CustomResolver : ISexprFormatterResolver
{
    private readonly GuidFormatter formatter = new GuidFormatter();

    public ISexprFormatter<T>? GetFormatter<T>()
    {
        if (typeof(T) == typeof(Guid))
        {
            return (ISexprFormatter<T>)(object)formatter;
        }
        return DefaultResolver.Instance.GetFormatter<T>();
    }
}
```

## License

[MIT](LICENSE)
