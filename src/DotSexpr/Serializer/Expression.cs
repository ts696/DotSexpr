using System.Collections.Generic;

namespace DotSexpr.Serializer;

public abstract record Expression;

public sealed record SymbolExpression(string Name) : Expression;

public sealed record IntegerExpression(long Value) : Expression;

public sealed record NumberExpression(double Value) : Expression;

public record CallExpression(string Name, IReadOnlyList<Expression> Arguments) : Expression
{
    public string Name { get; } = Name;
}
