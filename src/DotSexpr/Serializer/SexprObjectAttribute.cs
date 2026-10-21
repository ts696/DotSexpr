using System;

namespace DotSexpr.Serializer;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public class SexprObjectAttribute : Attribute
{
}
