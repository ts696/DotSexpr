using System;

namespace DotSexpr.Serializer;

/// <summary>Registers the allowed derived call types for an abstract call record.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SexprCallUnionAttribute(params Type[] callTypes) : Attribute
{
    public Type[] CallTypes { get; } = callTypes;

    /// <summary>Accepts unregistered names using the union's generated UnknownCall record.</summary>
    public bool AllowUnknownCalls { get; set; }
}
