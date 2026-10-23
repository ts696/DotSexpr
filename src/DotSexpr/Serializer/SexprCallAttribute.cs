using System;

namespace DotSexpr.Serializer;

/// <summary>Generates constructors and a formatter for a call with a fixed symbol name.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SexprCallAttribute(string name) : Attribute
{
    public string Name { get; } = name;

    /// <summary>Expression types in argument order, excluding the call name. Null allows any arguments; an empty array requires no arguments.</summary>
    public Type[]? ArgumentTypes { get; set; }
}
