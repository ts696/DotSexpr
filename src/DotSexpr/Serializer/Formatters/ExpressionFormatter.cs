using DotSexpr.Emitter;

namespace DotSexpr.Serializer.Formatters;

public class ExpressionFormatter : ISexprFormatter<Expression>
{
    public void Serialize(ref SexprEmitter emitter, Expression value, SexprSerializerOptions options)
    {
        switch (value)
        {
            case SymbolExpression symbol:
                emitter.WriteSymbol(symbol.Name);
                break;
            case CallExpression call:
                emitter.BeginList();
                try
                {
                    emitter.WriteSymbol(call.Name);
                }
                catch (SexprSerializerException exception)
                {
                    exception.PrependIndex(0);
                    throw;
                }
                if (call.Arguments == null)
                {
                    throw SexprSerializerException.ForSerialization("Call arguments cannot be null.");
                }
                for (var i = 0; i < call.Arguments.Count; i++)
                {
                    try
                    {
                        SexprSerializer.Serialize(ref emitter, call.Arguments[i], options);
                    }
                    catch (SexprSerializerException exception)
                    {
                        exception.PrependIndex(i + 1);
                        throw;
                    }
                }
                emitter.EndList();
                break;
            default:
                throw SexprSerializerException.ForSerialization($"Unsupported expression type '{value.GetType()}'.");
        }
    }

    public Expression Deserialize(in SNode node, SexprSerializerOptions options)
    {
        if (node.Token.IsSymbol)
        {
            return new SymbolExpression(node.GetSymbol());
        }
        if (!node.Token.IsList)
        {
            throw new SexprSerializerException("Expected a symbol or a non-empty list for an expression.", node);
        }
        if (node.Count == 0)
        {
            throw new SexprSerializerException("A call expression must contain a function name.", node);
        }

        var arguments = new Expression[node.Count - 1];
        var name = "";
        var index = 0;
        try
        {
            foreach (var child in node)
            {
                if (index == 0)
                {
                    if (!child.Token.IsSymbol)
                    {
                        throw new SexprSerializerException("A call expression's function name must be a symbol.", child);
                    }
                    name = child.GetSymbol();
                }
                else
                {
                    arguments[index - 1] = SexprSerializer.Deserialize<Expression>(in child, options);
                }
                index++;
            }
        }
        catch (SexprSerializerException exception)
        {
            exception.PrependIndex(index);
            throw;
        }
        return new CallExpression(name, arguments);
    }
}
