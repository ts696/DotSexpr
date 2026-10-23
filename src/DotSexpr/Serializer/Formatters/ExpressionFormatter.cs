using System;
using System.Buffers.Text;
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
            case IntegerExpression integer:
                SexprSerializer.Serialize(ref emitter, integer.Value, options);
                break;
            case NumberExpression number:
                SexprSerializer.Serialize(ref emitter, number.Value, options);
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
        if (node.Token.IsNumber)
        {
            var bytes = node.GetRawUtf8().Span;
            if (bytes.IndexOf((byte)'.') < 0 && bytes.IndexOf((byte)'e') < 0 && bytes.IndexOf((byte)'E') < 0)
            {
                return new IntegerExpression(SexprSerializer.Deserialize<long>(in node, options));
            }
            // GetDouble alone cannot distinguish conversion failure from zero.
            if (!Utf8Parser.TryParse(bytes, out double value, out var consumed) || consumed != bytes.Length ||
                double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new SexprSerializerException("The expression's number is outside the finite Double range.", node,
                    new OverflowException());
            }
            return new NumberExpression(SexprSerializer.Deserialize<double>(in node, options));
        }
        return DeserializeCall(in node, options);
    }

    internal static string ReadCallName(in SNode node)
    {
        if (!node.Token.IsList)
        {
            throw new SexprSerializerException("Expected a non-empty call list.", node);
        }
        if (node.Count == 0)
        {
            throw new SexprSerializerException("A call expression must contain a function name.", node);
        }
        var first = node[0];
        if (!first.Token.IsSymbol)
        {
            var error = new SexprSerializerException("A call expression's function name must be a symbol.", first);
            error.PrependIndex(0);
            throw error;
        }
        return first.GetSymbol();
    }

    internal static CallExpression DeserializeCall(in SNode node, SexprSerializerOptions options)
    {
        var name = ReadCallName(in node);
        var actualCount = node.Count - 1;
        var arguments = new Expression[actualCount];
        var index = 0;
        try
        {
            foreach (var child in node)
            {
                if (index > 0)
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
