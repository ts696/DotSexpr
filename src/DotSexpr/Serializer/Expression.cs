using System;
using System.Collections.Generic;

namespace DotSexpr.Serializer;

public abstract record Expression;

public sealed record SymbolExpression(string Name) : Expression;

public sealed record IntegerExpression(long Value) : Expression;

public sealed record NumberExpression(double Value) : Expression;

public record CallExpression(string Name, IReadOnlyList<Expression> Arguments) : Expression
{
    private readonly string _name = Name;
    private IReadOnlyList<Expression> _arguments = Arguments;

    protected CallExpression() : this("", Array.Empty<Expression>()) { }

    public string Name => GetCallName();

    public IReadOnlyList<Expression> Arguments
    {
        get => GetCallArguments();
        init => SetCallArguments(value);
    }

    protected virtual string GetCallName() => _name;
    protected virtual IReadOnlyList<Expression> GetCallArguments() => _arguments;
    protected virtual void SetCallArguments(IReadOnlyList<Expression> arguments) => _arguments = arguments;

    protected static IReadOnlyList<Expression> CombineCallArguments(Expression[] prefix, IReadOnlyList<Expression>? rest)
    {
        if (rest == null)
            throw SexprSerializerException.ForSerialization("Variadic call arguments cannot be null.");
        if (prefix.Length == 0) return rest;
        var arguments = new Expression[prefix.Length + rest.Count];
        prefix.CopyTo(arguments, 0);
        for (var i = 0; i < rest.Count; i++) arguments[prefix.Length + i] = rest[i];
        return arguments;
    }
}
