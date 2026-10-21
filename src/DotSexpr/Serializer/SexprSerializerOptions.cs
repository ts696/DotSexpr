using DotSexpr.Serializer.Resolvers;

namespace DotSexpr.Serializer;

public class SexprSerializerOptions()
{
    public static SexprSerializerOptions Default { get; } = new();

    public ISexprFormatterResolver Resolver { get; set; } = DefaultResolver.Instance;

    /// <summary>Maximum list nesting when serializing; must be positive.</summary>
    public int MaxDepth { get; set; } = 64;
}
