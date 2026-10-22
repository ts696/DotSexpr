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

#### Expressions and typed calls

`Expression` represents symbols, numbers, and nested calls. Integer literals use
`IntegerExpression` (`long`); literals containing a fraction or exponent use
`NumberExpression` (`double`). Integers outside the `long` range and floating-point
values outside the finite `double` range are rejected with a located error.

```csharp
var expression = SexprSerializer.Deserialize<Expression>("(move ?from ?to 10 0.5)");
var call = (CallExpression)expression;
long distance = ((IntegerExpression)call.Arguments[2]).Value;
double speed = ((NumberExpression)call.Arguments[3]).Value;
```

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
public sealed partial record Move : CallExpression;
```

The generator creates an empty constructor, an arguments constructor, and a formatter
registered through the generated resolver. Typed calls also work in arrays and as
`[SexprObject]` members. Containing types must be partial; explicit constructors and
additional instance fields/properties are not supported on `[SexprCall]` records.

Use `ArgumentCount` to require an exact number of arguments, excluding the call name:

```csharp
[SexprCall("not", ArgumentCount = 1)]
public sealed partial record Not : CallExpression;
```

`(not (ready ?actor))` is valid; `(not)` and `(not a b)` are rejected. The generated
formatter checks the count during serialization and deserialization, including when
used through a union or in a model property, and reports the path to the invalid call.
The default is `-1` (unconstrained), while `0` requires no arguments. Values below `-1`
produce a generator diagnostic. Constructed values are checked when serialized, including
values whose arguments have been changed with `with`.

To allow several calls in the same position, register them on a common abstract record:

```csharp
ActionExpression[] actions = SexprSerializer.Deserialize<ActionExpression[]>(
    "((move ?to 10) (jump 0.5) (step))");
// The elements are Move, Jump, and Step. An unregistered call such as (fly) throws.

[SexprCallUnion(typeof(Move), typeof(Jump), typeof(Step))]
public abstract partial record ActionExpression : CallExpression;

[SexprCall("move")]
public sealed partial record Move : ActionExpression;

[SexprCall("jump")]
public sealed partial record Jump : ActionExpression;

[SexprCall("step")]
public sealed partial record Step : ActionExpression;
```

Union types must be non-generic abstract partial records directly derived from
`CallExpression`; registered calls must directly derive from that union and use unique
names. The union formatter rejects unregistered names on input and unregistered runtime
types on output. The same validation applies to union-typed model members and arrays.

Registered calls are always recognized recursively inside arguments:

```csharp
var expression = SexprSerializer.Deserialize<ConditionExpression>(
    "(and (or))");
// and -> And, nested or -> Or

[SexprCallUnion(typeof(And), typeof(Or))]
public abstract partial record ConditionExpression : CallExpression;

[SexprCall("and")]
public sealed partial record And : ConditionExpression;

[SexprCall("or")]
public sealed partial record Or : ConditionExpression;
```

No serializer option or separate registration is required. This behavior applies when
reading the union type, including union-typed model members and arrays. Custom case and
number formatters still use the supplied resolver. The behavior is scoped to the union
and does not affect unrelated expression properties or mutate options.

Unregistered names are rejected at every level by default, during both serialization
and deserialization. To accept them, set `AllowUnknownCalls = true`. This
generates a sealed `UnknownCall` record derived from the union, so an arbitrary predicate
can be stored in the same property as the registered operators:

```csharp
var task = SexprSerializer.Deserialize<ActionDefinition>(
    "(:precondition (ready ?actor))");
// Also accepts (not (injured ?actor)), (and ...), (or ...), and (not (or ...)).

[SexprCallUnion(typeof(And), typeof(Or), typeof(Not), AllowUnknownCalls = true)]
public abstract partial record ConditionExpression : CallExpression;

[SexprCall("and")]
public sealed partial record And : ConditionExpression;
[SexprCall("or")]
public sealed partial record Or : ConditionExpression;
[SexprCall("not", ArgumentCount = 1)]
public sealed partial record Not : ConditionExpression;

[SexprObject]
public partial class ActionDefinition
{
    [SexprTag("precondition")]
    public ConditionExpression Precondition { get; set; } = new And();
}
```

`ready`, `injured`, and other unregistered names become
`ConditionExpression.UnknownCall`, retaining their names and arguments. This applies
at every level and registered operators inside predicates are
still recognized. These records support serialization and deserialization on their own,
through the union, and in arrays. `AllowUnknownCalls` defaults to `false`; it permits
unknown names, while malformed call lists still produce located errors. The nested name
`UnknownCall` is reserved when this feature is enabled.

Names are case-sensitive and read-only, including on ordinary `CallExpression` values.
Arguments remain expressions and may contain calls with other names. Deserializing as
`Expression` or `CallExpression` produces an ordinary call; request `Move` to validate
its name. Numeric output is normalized, so `1.0` or `1e3` may serialize as `1` or `1000`.

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
