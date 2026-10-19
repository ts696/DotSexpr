using System;
using System.Reflection;

namespace DotSexpr.Serializer;

public static class SexprSerializer
{
    private enum ReadState
    {
        Key,
        Value
    }

    public static T Deserialize<T>(string text)
    {
        var targetType = typeof(T);
        object target = Activator.CreateInstance(targetType)!;

        using var document = SDocument.Parse(text);
        var root = document.RootNode;
        if (!root.Token.IsList)
        {
            throw new SexprSerializerException("The root value must be a list.", root);
        }

        DeserializeObject(root, targetType, target);
        return (T)target;
    }

    private static void DeserializeObject(SNode node, Type targetType, object target)
    {
        PropertyInfo? currentProperty = null;
        string? currentKey = null;
        var keyNode = node;
        var state = ReadState.Key;

        foreach (var current in node)
        {
            switch (state)
            {
                case ReadState.Key:
                    {
                        if (!current.Token.IsSymbol)
                        {
                            throw new SexprSerializerException("Property keys must be symbols beginning with ':'.", current);
                        }

                        var key = current.GetSymbol();
                        if (key.Length < 2 || key[0] != ':')
                        {
                            throw new SexprSerializerException($"Invalid property key '{key}'. Expected ':' followed by a property name.", current);
                        }

                        currentKey = key;
                        keyNode = current;
                        currentProperty = targetType.GetProperty(key.Substring(1));
                        state = ReadState.Value;
                        break;
                    }

                case ReadState.Value:
                    state = ReadState.Key;
                    if (currentProperty == null)
                    {
                        continue;
                    }

                    try
                    {
                        var propertyType = currentProperty.PropertyType;
                        if (IsScalarType(propertyType))
                        {
                            var value = DeserializeScalar(current, propertyType, currentKey);
                            currentProperty.SetValue(target, value);
                        }
                        else if (current.Token.IsList)
                        {
                            if (propertyType.IsArray)
                            {
                                var arrayTarget = Array.CreateInstance(propertyType.GetElementType()!, current.Count);
                                DeserializeArray(current, propertyType, arrayTarget);
                                currentProperty.SetValue(target, arrayTarget);
                            }
                            else if (propertyType == typeof(Expression))
                            {
                                var expression = DeserializeExpression(current);
                                currentProperty.SetValue(target, expression);
                            }
                            else
                            {
                                var nestedTarget = Activator.CreateInstance(propertyType)!;
                                DeserializeObject(current, propertyType, nestedTarget);
                                currentProperty.SetValue(target, nestedTarget);
                            }
                        }
                        else
                        {
                            throw new SexprSerializerException($"Cannot deserialize property '{currentKey}' as '{propertyType.Name}'. Expected a list.", current);
                        }
                    }
                    catch (SexprSerializerException exception)
                    {
                        exception.PrependProperty(currentKey!.Substring(1));
                        throw;
                    }
                    break;
            }
        }

        if (state == ReadState.Value)
        {
            var exception = new SexprSerializerException($"Expected a value after property key '{currentKey}'.", keyNode);
            exception.PrependProperty(currentKey!.Substring(1));
            throw exception;
        }
    }

    private static bool IsScalarType(Type type)
    {
        return type == typeof(string) || type == typeof(int) || type == typeof(long)
            || type == typeof(float) || type == typeof(double);
    }

    private static object DeserializeScalar(SNode node, Type targetType, string? propertyKey = null, int arrayIndex = 0)
    {
        var token = node.Token;
        var isString = targetType == typeof(string);
        if ((isString && !token.IsSymbol && !token.IsString) || (!isString && !token.IsNumber))
        {
            var expected = isString ? "a symbol or string" : "a number";
            throw new SexprSerializerException($"Cannot deserialize {GetTargetName(propertyKey, arrayIndex)} as '{targetType.Name}'. Expected {expected}.", node);
        }

        if (isString)
        {
            return token.IsSymbol ? node.GetSymbol() : node.GetString();
        }
        try
        {
            if (targetType == typeof(int))
            {
                return node.GetInt32();
            }
            if (targetType == typeof(long))
            {
                return node.GetInt64();
            }
            if (targetType == typeof(float))
            {
                return node.GetSingle();
            }
            if (targetType == typeof(double))
            {
                return node.GetDouble();
            }
        }
        catch (Exception exception) when (exception is FormatException or OverflowException)
        {
            throw new SexprSerializerException($"Cannot deserialize {GetTargetName(propertyKey, arrayIndex)} as '{targetType.Name}'.", node, exception);
        }

        throw new NotSupportedException($"Type '{targetType.Name}' is not supported as a scalar value.");
    }

    private static string GetTargetName(string? propertyKey, int arrayIndex)
    {
        return propertyKey == null ? $"array element at index {arrayIndex}" : $"property '{propertyKey}'";
    }

    private static void DeserializeArray(SNode node, Type targetType, Array target)
    {
        var elementType = targetType.GetElementType()!;
        var index = 0;

        try
        {
            foreach (var current in node)
            {
                if (IsScalarType(elementType))
                {
                    var value = DeserializeScalar(current, elementType, arrayIndex: index);
                    target.SetValue(value, index);
                }
                else if (elementType == typeof(Expression))
                {
                    var expression = DeserializeExpression(current);
                    target.SetValue(expression, index);
                }
                else
                {
                    if (!current.Token.IsList)
                    {
                        throw new SexprSerializerException($"Cannot deserialize array element at index {index} as '{elementType.Name}'. Expected a list.", current);
                    }

                    var element = Activator.CreateInstance(elementType)!;
                    DeserializeObject(current, elementType, element);
                    target.SetValue(element, index);
                }
                index++;
            }
        }
        catch (SexprSerializerException exception)
        {
            exception.PrependIndex(index);
            throw;
        }
    }

    private static Expression DeserializeExpression(SNode node)
    {
        if (node.Token.IsSymbol)
        {
            return new SymbolExpression(node.GetSymbol());
        }
        if (node.Token.IsList)
        {
            if (node.Count <= 0)
            {
                throw new SexprSerializerException("A call expression must contain a function name.", node);
            }

            var index = 0;
            var arguments = new Expression[node.Count - 1];
            CallExpression call = null!;

            try
            {
                foreach (var current in node)
                {
                    if (index == 0)
                    {
                        if (!current.Token.IsSymbol)
                        {
                            throw new SexprSerializerException("A call expression's function name must be a symbol.", current);
                        }

                        call = new CallExpression(current.GetSymbol(), arguments);
                    }
                    else
                    {
                        arguments[index - 1] = DeserializeExpression(current);
                    }

                    index++;
                }
            }
            catch (SexprSerializerException exception)
            {
                exception.PrependIndex(index);
                throw;
            }

            return call;
        }

        throw new SexprSerializerException("Expected a symbol or a non-empty list for an expression.", node);
    }
}
