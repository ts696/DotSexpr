using System;

namespace DotSexpr.Serializer;

/// <summary>Generates constructors and a formatter for a call with a fixed symbol name.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SexprCallAttribute(string name) : Attribute
{
    public string Name { get; } = name;

    /// <summary>Exact argument count, excluding the call name. -1 (the default) allows any count.</summary>
    public int ArgumentCount { get; set; } = -1;
}
