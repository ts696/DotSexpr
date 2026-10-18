using System.Collections.Generic;

namespace DotSexpr.Serializer;

public abstract record Expression;

public sealed record SymbolExpression(string Name) : Expression;

public sealed record CallExpression(string Name, IReadOnlyList<Expression> Arguments) : Expression;
