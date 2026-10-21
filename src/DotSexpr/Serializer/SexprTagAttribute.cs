using System;

namespace DotSexpr.Serializer;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class SexprTagAttribute : Attribute
{
    public string Tag { get; }

    public SexprTagAttribute(string tag)
    {
        Tag = tag;
    }
}
