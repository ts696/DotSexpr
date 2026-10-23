using System;

namespace DotSexpr.Serializer;

/// <summary>Generates a formatter for a call with a fixed symbol name. Positional records define arguments through their constructor parameters.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SexprCallAttribute(string name) : Attribute
{
    public string Name { get; } = name;

    /// <summary>Argument types for records without positional parameters. Null allows any arguments; an empty array requires no arguments.</summary>
    public Type[]? ArgumentTypes { get; set; }
}
