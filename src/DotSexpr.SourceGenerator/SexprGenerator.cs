using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DotSexpr.SourceGenerator;

[Generator(LanguageNames.CSharp)]
public sealed class SexprGenerator : IIncrementalGenerator
{
    private const string ObjectAttribute = "DotSexpr.Serializer.SexprObjectAttribute";
    private const string CallAttribute = "DotSexpr.Serializer.SexprCallAttribute";
    private const string CallUnionAttribute = "DotSexpr.Serializer.SexprCallUnionAttribute";
    private const string TagAttribute = "DotSexpr.Serializer.SexprTagAttribute";
    private static readonly SymbolDisplayFormat NullableTypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private static readonly DiagnosticDescriptor PartialRequired = Descriptor("DSX001",
        "Partial type required", "Type '{0}' and its containing types must be partial");
    private static readonly DiagnosticDescriptor InvalidType = Descriptor("DSX002",
        "Unsupported object type", "Type '{0}' must be a non-abstract, non-static class or struct and cannot be ref-like or file-local");
    private static readonly DiagnosticDescriptor ConstructorRequired = Descriptor("DSX003",
        "Parameterless constructor required", "Type '{0}' must have a parameterless constructor");
    private static readonly DiagnosticDescriptor InvalidMember = Descriptor("DSX004",
        "Unsupported member", "Member '{0}' cannot be deserialized: {1}");
    private static readonly DiagnosticDescriptor InvalidTag = Descriptor("DSX005",
        "Invalid or duplicate property tag", "Tag '{0}' on member '{1}' must be a unique, non-empty property name without ':' or delimiters");
    private static readonly DiagnosticDescriptor InvalidResolverType = Descriptor("DSX006",
        "Unsupported resolver dependency", "Cannot generate a resolver entry for '{0}': {1}");
    private static readonly DiagnosticDescriptor InvalidCall = Descriptor("DSX007",
        "Unsupported call declaration", "Cannot generate call '{0}': {1}");
    private static readonly DiagnosticDescriptor InvalidCallUnion = Descriptor("DSX008",
        "Unsupported call union", "Cannot generate call union '{0}': {1}");

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var types = context.SyntaxProvider.ForAttributeWithMetadataName(ObjectAttribute,
            static (node, _) => node is TypeDeclarationSyntax,
            static (attributeContext, _) => attributeContext);
        var calls = context.SyntaxProvider.ForAttributeWithMetadataName(CallAttribute,
            static (node, _) => node is TypeDeclarationSyntax,
            static (attributeContext, _) => attributeContext);
        var unions = context.SyntaxProvider.ForAttributeWithMetadataName(CallUnionAttribute,
            static (node, _) => node is TypeDeclarationSyntax,
            static (attributeContext, _) => attributeContext);
        var declarations = types.Collect().Combine(calls.Collect()).Combine(unions.Collect())
            .Select(static (input, _) => input.Left.Left.Concat(input.Left.Right).Concat(input.Right).ToArray());
        context.RegisterSourceOutput(declarations.Combine(context.CompilationProvider), static (output, input) =>
        {
            var models = new Dictionary<INamedTypeSymbol, List<(ISymbol Symbol, ITypeSymbol Type, string Tag)>>(SymbolEqualityComparer.Default);
            var processed = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            foreach (var attributeContext in input.Left)
            {
                var type = (INamedTypeSymbol)attributeContext.TargetSymbol;
                if (!processed.Add(type))
                {
                    continue;
                }
                var members = Generate(output, type);
                if (members != null)
                {
                    models.Add(type, members);
                }
            }
            GenerateResolver(output, models, input.Right);
        });
    }

    private static List<(ISymbol Symbol, ITypeSymbol Type, string Tag)>? Generate(SourceProductionContext output, INamedTypeSymbol type)
    {
        var unionAttribute = type.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == CallUnionAttribute);
        var allowUnknownCalls = unionAttribute?.NamedArguments.Any(a => a.Key == "AllowUnknownCalls" && a.Value.Value is true) == true;
        var scopes = new Stack<INamedTypeSymbol>();
        for (var scope = type; scope != null; scope = scope.ContainingType)
        {
            scopes.Push(scope);
            if (scope.DeclaringSyntaxReferences.Any(r => r.GetSyntax() is TypeDeclarationSyntax d &&
                !d.Modifiers.Any(SyntaxKind.PartialKeyword)))
            {
                Report(output, PartialRequired, scope, scope.Name);
                return null;
            }
        }
        if ((type.IsAbstract && unionAttribute == null) || type.IsStatic || type.IsRefLikeType ||
            scopes.Any(s => s.DeclaringSyntaxReferences.Any(r =>
                r.GetSyntax() is TypeDeclarationSyntax d && d.Modifiers.Any(m => m.ValueText == "file"))))
        {
            Report(output, InvalidType, type, type.Name);
            return null;
        }
        var callAttribute = type.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == CallAttribute);
        var callName = callAttribute?.ConstructorArguments.FirstOrDefault().Value as string;
        var unionCases = new List<(INamedTypeSymbol Type, string Name)>();
        if (unionAttribute != null)
        {
            if (!type.IsRecord || type.IsValueType || !type.IsAbstract || HasGenericScope(type) ||
                type.BaseType?.ToDisplayString() != "DotSexpr.Serializer.CallExpression" ||
                callAttribute != null || type.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == ObjectAttribute) ||
                HasCallMembersOrConstructors(type))
            {
                Report(output, InvalidCallUnion, type, type.Name,
                    "use an abstract, non-generic partial record directly derived from CallExpression, without other serialization attributes, constructors or instance fields/properties");
                return null;
            }
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var caseType in GetUnionTypes(type))
            {
                var caseName = caseType?.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == CallAttribute)
                    ?.ConstructorArguments.FirstOrDefault().Value as string;
                if (caseType == null || caseType.IsAbstract || caseType.IsUnboundGenericType ||
                    !SymbolEqualityComparer.Default.Equals(caseType.BaseType, type) || !IsCallName(caseName) ||
                    !names.Add(caseName!))
                {
                    Report(output, InvalidCallUnion, type, type.Name,
                        "each registered type must directly derive from the union and have a distinct, valid SexprCall name");
                    return null;
                }
                unionCases.Add((caseType, caseName!));
            }
            if (unionCases.Count == 0)
            {
                Report(output, InvalidCallUnion, type, type.Name, "register at least one SexprCall type");
                return null;
            }
            if (allowUnknownCalls && (type.Name == "UnknownCall" || type.GetMembers("UnknownCall").Length != 0))
            {
                Report(output, InvalidCallUnion, type, type.Name, "the generated name UnknownCall is reserved when AllowUnknownCalls is true");
                return null;
            }
        }
        else if (callAttribute != null)
        {
            if (!type.IsRecord || type.IsValueType ||
                (type.BaseType?.ToDisplayString() != "DotSexpr.Serializer.CallExpression" &&
                    type.BaseType?.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == CallUnionAttribute) != true) ||
                type.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == ObjectAttribute) ||
                HasCallMembersOrConstructors(type))
            {
                Report(output, InvalidCall, type, type.Name,
                    "use a partial record derived from CallExpression or a SexprCallUnion, without SexprObject, constructors or instance fields/properties");
                return null;
            }
            if (!IsCallName(callName))
            {
                Report(output, InvalidCall, type, type.Name, "the name must be a valid, non-numeric symbol");
                return null;
            }
        }
        else if (!type.IsValueType && !type.InstanceConstructors.Any(c => c.Parameters.Length == 0))
        {
            Report(output, ConstructorRequired, type, type.Name);
            return null;
        }

        var members = callAttribute != null || unionAttribute != null
            ? new List<(ISymbol Symbol, ITypeSymbol Type, string Tag)>() : GetObjectMembers(output, type);
        if (members == null)
        {
            return null;
        }

        var code = new StringBuilder("// <auto-generated />\n#nullable enable\n");
        if (!type.ContainingNamespace.IsGlobalNamespace)
        {
            code.Append("namespace ").Append(type.ContainingNamespace.ToDisplayString()).AppendLine(" {");
        }
        foreach (var scope in scopes)
        {
            code.Append("partial ").Append(Keyword(scope)).Append(' ').Append(Name(scope));
            foreach (var parameter in scope.TypeParameters)
            {
                var constraints = Constraints(parameter);
                if (constraints.Count > 0)
                {
                    code.Append(" where @").Append(parameter.Name).Append(" : ").Append(string.Join(", ", constraints));
                }
            }
            code.AppendLine(" {");
        }

        var fullName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var formatterName = "@" + type.Name + "GeneratedFormatter";
        if (unionAttribute != null)
        {
            code.Append("protected @").Append(type.Name)
                .AppendLine("(string name, global::System.Collections.Generic.IReadOnlyList<global::DotSexpr.Serializer.Expression> arguments) : base(name, arguments) { }");
            if (allowUnknownCalls)
            {
                code.Append("public sealed record UnknownCall : ").Append(fullName).AppendLine(" {")
                    .AppendLine("public UnknownCall(string name, global::System.Collections.Generic.IReadOnlyList<global::DotSexpr.Serializer.Expression> arguments) : base(name, arguments) { }")
                    .Append("public sealed class UnknownCallGeneratedFormatter : global::DotSexpr.Serializer.ISexprFormatter<")
                    .Append(fullName).AppendLine(".UnknownCall> {")
                    .Append("public ").Append(fullName).AppendLine(".UnknownCall Deserialize(in global::DotSexpr.SNode node, global::DotSexpr.Serializer.SexprSerializerOptions options) {")
                    .AppendLine("var call = global::DotSexpr.Serializer.SexprSerializer.Deserialize<global::DotSexpr.Serializer.CallExpression>(in node, options);")
                    .Append("return new ").Append(fullName).AppendLine(".UnknownCall(call.Name, call.Arguments);\n}")
                    .Append("public void Serialize(ref global::DotSexpr.Emitter.SexprEmitter emitter, ").Append(fullName)
                    .AppendLine(".UnknownCall value, global::DotSexpr.Serializer.SexprSerializerOptions options) {")
                    .AppendLine("new global::DotSexpr.Serializer.Formatters.ExpressionFormatter().Serialize(ref emitter, value, options);\n}\n}\n}");
            }
            code.Append("public sealed class ").Append(formatterName)
                .Append(" : global::DotSexpr.Serializer.Formatters.CallExpressionUnionFormatter<").Append(fullName).AppendLine("> {")
                .Append("public ").Append(formatterName).AppendLine("() {");
            foreach (var unionCase in unionCases)
            {
                code.Append("Register<").Append(unionCase.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                    .Append(">(").Append(SymbolDisplay.FormatLiteral(unionCase.Name, true)).AppendLine(");");
            }
            if (allowUnknownCalls)
            {
                code.Append("RegisterFallback<").Append(fullName).Append(".UnknownCall>((name, arguments) => new ")
                    .Append(fullName).AppendLine(".UnknownCall(name, arguments));");
            }
            code.AppendLine("}\n}");
        }
        else if (callAttribute != null)
        {
            code.Append("public @").Append(type.Name).AppendLine("() : this(global::System.Array.Empty<global::DotSexpr.Serializer.Expression>()) { }")
                .Append("public @").Append(type.Name)
                .Append("(global::System.Collections.Generic.IReadOnlyList<global::DotSexpr.Serializer.Expression> arguments) : base(")
                .Append(SymbolDisplay.FormatLiteral(callName!, true)).AppendLine(", arguments) { }")
                .Append("public sealed class ").Append(formatterName)
                .Append(" : global::DotSexpr.Serializer.Formatters.CallExpressionFormatter<").Append(fullName).AppendLine("> {")
                .Append("public ").Append(formatterName).Append("() : base(")
                .Append(SymbolDisplay.FormatLiteral(callName!, true)).Append(", arguments => new ")
                .Append(fullName).AppendLine("(arguments)) { }\n}");
        }
        else
        {
            code.Append("public sealed class ").Append(formatterName).Append(" : global::DotSexpr.Serializer.ISexprFormatter<")
                .Append(fullName).AppendLine("> {");
            for (var i = 0; i < members.Count; i++)
            {
                code.Append("private static readonly byte[] Key").Append(i)
                    .Append(" = new byte[] { ").Append(string.Join(", ", Encoding.UTF8.GetBytes(":" + members[i].Tag)))
                    .AppendLine(" };");
            }
            code.Append("public void Serialize(ref global::DotSexpr.Emitter.SexprEmitter emitter, ").Append(fullName)
                .AppendLine(" value, global::DotSexpr.Serializer.SexprSerializerOptions options) {")
                .AppendLine("emitter.BeginList();");
            for (var i = 0; i < members.Count; i++)
            {
                var member = members[i];
                code.Append("emitter.WriteSymbol(Key").Append(i).AppendLine(");")
                    .Append("global::DotSexpr.Serializer.SexprSerializer.SerializeProperty<")
                    .Append(member.Type.ToDisplayString(NullableTypeFormat))
                    .Append(">(ref emitter, value.@").Append(member.Symbol.Name).Append(", options, ")
                    .Append(SymbolDisplay.FormatLiteral(member.Tag, true)).AppendLine(");");
            }
            code.AppendLine("emitter.EndList();\n}");
            code.Append("public ").Append(fullName)
                .AppendLine(" Deserialize(in global::DotSexpr.SNode node, global::DotSexpr.Serializer.SexprSerializerOptions options) {")
                .AppendLine("var reader = new global::DotSexpr.Serializer.SexprObjectReader(in node);")
                .Append("var result = new ").Append(fullName).AppendLine("();")
                .AppendLine("while (reader.MoveNext(out var key, out var value)) {");
            for (var i = 0; i < members.Count; i++)
            {
                var member = members[i];
                code.Append(i == 0 ? "if" : "else if").Append(" (key.ValueEquals(Key").Append(i).AppendLine(")) {")
                    .Append("result.@").Append(member.Symbol.Name).Append(" = global::DotSexpr.Serializer.SexprSerializer.DeserializeProperty<")
                    .Append(member.Type.ToDisplayString(NullableTypeFormat)).Append(">(in value, options, ")
                    .Append(SymbolDisplay.FormatLiteral(member.Tag, true)).AppendLine(");\n}");
            }
            code.AppendLine("}\nreturn result;\n}\n}");
        }
        foreach (var _ in scopes) code.AppendLine("}");
        if (!type.ContainingNamespace.IsGlobalNamespace)
        {
            code.AppendLine("}");
        }
        var hint = string.Join("+", scopes.Select(s => s.MetadataName));
        for (var scope = type.ContainingNamespace; !scope.IsGlobalNamespace; scope = scope.ContainingNamespace)
        {
            hint = scope.MetadataName + "." + hint;
        }
        var source = CSharpSyntaxTree.ParseText(code.ToString()).GetRoot().NormalizeWhitespace().ToFullString();
        output.AddSource(hint + ".Sexpr.g.cs", SourceText.From(source + "\n", Encoding.UTF8));
        return members;
    }

    private static void GenerateResolver(SourceProductionContext output,
        Dictionary<INamedTypeSymbol, List<(ISymbol Symbol, ITypeSymbol Type, string Tag)>> models,
        Compilation compilation)
    {
        if (models.Count == 0)
        {
            return;
        }
        var concreteModels = models.Keys.Where(t => !HasGenericScope(t))
            .OrderBy(t => t.ToDisplayString(), System.StringComparer.Ordinal).ToArray();
        var pending = new Queue<ITypeSymbol>(concreteModels);
        var seen = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        var formatters = new List<(string Type, string Constructor)>();
        while (pending.Count > 0)
        {
            output.CancellationToken.ThrowIfCancellationRequested();
            var type = pending.Dequeue();
            if (!seen.Add(type))
            {
                continue;
            }
            if (seen.Count > 512)
            {
                Report(output, InvalidResolverType, concreteModels[0], type.Name,
                    "more than 512 distinct types are reachable; check for expanding generic recursion");
                return;
            }
            if (type is INamedTypeSymbol { IsUnboundGenericType: true } || type.TypeKind == TypeKind.TypeParameter)
            {
                Report(output, InvalidResolverType, concreteModels[0], type.ToDisplayString(), "a closed type is required");
                return;
            }
            if (!compilation.IsSymbolAccessibleWithin(type, compilation.Assembly))
            {
                Report(output, InvalidResolverType, concreteModels[0], type.ToDisplayString(),
                    "the type must be accessible to the automatically generated resolver");
                return;
            }
            if (type is IArrayTypeSymbol { IsSZArray: true } array)
            {
                formatters.Add((type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), "global::DotSexpr.Serializer.Formatters.ArrayFormatter<" +
                    array.ElementType.ToDisplayString(NullableTypeFormat) + ">"));
                pending.Enqueue(array.ElementType);
            }
            else if (type is INamedTypeSymbol named &&
                named.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() is ObjectAttribute or CallAttribute or CallUnionAttribute))
            {
                var members = models.TryGetValue(named, out var cached) ? cached :
                    named.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() is CallAttribute or CallUnionAttribute)
                        ? new List<(ISymbol Symbol, ITypeSymbol Type, string Tag)>() : GetObjectMembers(output, named);
                if (members == null)
                {
                    return;
                }
                formatters.Add((type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), named.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
                    .ToDisplayString(NullableTypeFormat) + ".@" + named.Name + "GeneratedFormatter"));
                pending.Enqueue(compilation.CreateArrayTypeSymbol(named.WithNullableAnnotation(NullableAnnotation.NotAnnotated)));
                foreach (var member in members)
                {
                    pending.Enqueue(member.Type);
                }
                foreach (var unionType in GetUnionTypes(named))
                {
                    if (unionType != null) pending.Enqueue(unionType);
                }
                if (named.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == CallUnionAttribute &&
                    a.NamedArguments.Any(p => p.Key == "AllowUnknownCalls" && p.Value.Value is true)))
                {
                    // UnknownCall is generated in this pass, so it has no symbol in the input compilation.
                    var fallbackType = named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ".UnknownCall";
                    formatters.Add((fallbackType, fallbackType + ".UnknownCallGeneratedFormatter"));
                    formatters.Add((fallbackType + "[]", "global::DotSexpr.Serializer.Formatters.ArrayFormatter<" + fallbackType + ">"));
                }
            }
        }

        var code = new StringBuilder("// <auto-generated />\n#nullable enable\nnamespace DotSexpr.Generated {\n")
            .AppendLine("internal sealed class DotSexprGeneratedResolver : global::DotSexpr.Serializer.ISexprFormatterResolver {")
            .AppendLine("public static DotSexprGeneratedResolver Instance { get; } = new DotSexprGeneratedResolver();");
        if (concreteModels.Length > 0)
        {
            code.AppendLine("[global::System.Runtime.CompilerServices.ModuleInitializer]")
                .AppendLine("internal static void Register() {")
                .AppendLine("global::DotSexpr.Serializer.Resolvers.DefaultResolver.Register(Instance);\n}");
        }
        code.AppendLine("public global::DotSexpr.Serializer.ISexprFormatter<T>? GetFormatter<T>() => Cache<T>.Formatter;")
            .AppendLine("private static class Cache<T> {")
            .AppendLine("internal static readonly global::DotSexpr.Serializer.ISexprFormatter<T>? Formatter = Create();")
            .AppendLine("private static global::DotSexpr.Serializer.ISexprFormatter<T>? Create() {");
        foreach (var formatter in formatters)
        {
            code.Append("if (typeof(T) == typeof(")
                .Append(formatter.Type).AppendLine(")) {")
                .Append("return (global::DotSexpr.Serializer.ISexprFormatter<T>)(object)new ")
                .Append(formatter.Constructor).AppendLine("();\n}");
        }
        code.AppendLine("return global::DotSexpr.Serializer.Resolvers.BuiltinResolver.Instance.GetFormatter<T>();\n}\n}\n}\n}");
        var initializerAttribute = compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.ModuleInitializerAttribute");
        if (concreteModels.Length > 0 && (initializerAttribute == null ||
            !compilation.IsSymbolAccessibleWithin(initializerAttribute, compilation.Assembly)))
        {
            code.AppendLine("namespace System.Runtime.CompilerServices {")
                .AppendLine("[global::System.AttributeUsage(global::System.AttributeTargets.Method, Inherited = false)]")
                .AppendLine("internal sealed class ModuleInitializerAttribute : global::System.Attribute { }\n}");
        }
        var source = CSharpSyntaxTree.ParseText(code.ToString()).GetRoot().NormalizeWhitespace().ToFullString();
        output.AddSource("DotSexprGeneratedResolver.g.cs", SourceText.From(source + "\n", Encoding.UTF8));
    }

    private static bool HasGenericScope(INamedTypeSymbol type)
    {
        for (var scope = type; scope != null; scope = scope.ContainingType)
        {
            if (scope.Arity != 0)
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasCallMembersOrConstructors(INamedTypeSymbol type) =>
        type.DeclaringSyntaxReferences.Any(r => r.GetSyntax() is RecordDeclarationSyntax { ParameterList: not null }) ||
        type.InstanceConstructors.Any(c => !c.IsImplicitlyDeclared) ||
        type.GetMembers().Any(m => !m.IsImplicitlyDeclared && !m.IsStatic && m is IPropertySymbol or IFieldSymbol);

    private static IEnumerable<INamedTypeSymbol?> GetUnionTypes(INamedTypeSymbol type)
    {
        var attribute = type.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == CallUnionAttribute);
        if (attribute?.ConstructorArguments.FirstOrDefault() is { Kind: TypedConstantKind.Array, IsNull: false } types)
        {
            foreach (var item in types.Values) yield return item.Value as INamedTypeSymbol;
        }
    }

    private static bool IsCallName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name!.StartsWith("#|", StringComparison.Ordinal) ||
            name.Any(c => c <= ' ' || c is '(' or ')' or ';' or '"'))
        {
            return false;
        }
        // Keep generator validation identical to the parser's JSON number grammar.
        try
        {
            new UTF8Encoding(false, true).GetByteCount(name);
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
        return !System.Text.RegularExpressions.Regex.IsMatch(name,
            @"\A-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?\z");
    }

    private static List<(ISymbol Symbol, ITypeSymbol Type, string Tag)>? GetObjectMembers(
        SourceProductionContext output, INamedTypeSymbol type)
    {
        var members = new List<(ISymbol Symbol, ITypeSymbol Type, string Tag)>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var tags = new HashSet<string>(StringComparer.Ordinal);
        var invalid = false;
        for (var current = type; current != null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers())
            {
                // A derived declaration hides the base member, even when it is read-only.
                if (!names.Add(member.Name) || member.IsStatic || member is not (IPropertySymbol or IFieldSymbol))
                {
                    continue;
                }
                var tagAttribute = GetTagAttribute(member);
                if (member.DeclaredAccessibility != Accessibility.Public ||
                    member is IPropertySymbol accessors &&
                    (accessors.GetMethod?.DeclaredAccessibility != Accessibility.Public ||
                        accessors.SetMethod?.DeclaredAccessibility != Accessibility.Public))
                {
                    if (tagAttribute != null)
                    {
                        Report(output, InvalidMember, member, member.Name,
                            "only public fields and properties with public getters and setters are supported");
                        invalid = true;
                    }
                    continue;
                }
                if (member.DeclaringSyntaxReferences.Any(r =>
                    r.GetSyntax().FirstAncestorOrSelf<MemberDeclarationSyntax>() is { } declaration &&
                    declaration.Modifiers.Any(m => m.ValueText == "required")) ||
                    member.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "System.Runtime.CompilerServices.RequiredMemberAttribute"))
                {
                    Report(output, InvalidMember, member, member.Name, "required members are not supported");
                    invalid = true;
                    continue;
                }
                ITypeSymbol memberType;
                if (member is IPropertySymbol property)
                {
                    if (property.IsIndexer || property.ExplicitInterfaceImplementations.Length != 0)
                    {
                        continue;
                    }
                    if (property.SetMethod!.IsInitOnly)
                    {
                        Report(output, InvalidMember, member, member.Name, "init setters are not supported");
                        invalid = true;
                        continue;
                    }
                    memberType = property.Type;
                }
                else
                {
                    var field = (IFieldSymbol)member;
                    if (field.IsReadOnly || field.IsConst || field.IsImplicitlyDeclared)
                    {
                        continue;
                    }
                    memberType = field.Type;
                }
                if (memberType.IsRefLikeType || memberType.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer)
                {
                    Report(output, InvalidMember, member, member.Name, "the member type must allow a formatter call");
                    invalid = true;
                    continue;
                }
                var tag = tagAttribute == null ? member.Name : tagAttribute.ConstructorArguments.FirstOrDefault().Value as string;
                if (string.IsNullOrEmpty(tag) || tag!.Any(c => char.IsWhiteSpace(c) || c is ':' or '(' or ')' or '"' or ';') || !tags.Add(tag!))
                {
                    Report(output, InvalidTag, member, tag ?? "", member.Name);
                    invalid = true;
                    continue;
                }
                members.Add((member, memberType, tag!));
            }
        }
        if (invalid)
        {
            return null;
        }

        return members;
    }

    private static AttributeData? GetTagAttribute(ISymbol member)
    {
        for (ISymbol? current = member; current != null; current = (current as IPropertySymbol)?.OverriddenProperty)
        {
            var attribute = current.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == TagAttribute);
            if (attribute != null)
            {
                return attribute;
            }
        }
        return null;
    }

    private static string Name(INamedTypeSymbol type) => "@" + type.Name +
        (type.TypeParameters.Length == 0 ? "" : "<" + string.Join(", ", type.TypeParameters.Select(p =>
            (p.Variance == VarianceKind.Out ? "out " : p.Variance == VarianceKind.In ? "in " : "") + "@" + p.Name)) + ">");

    private static string Keyword(INamedTypeSymbol type) => type.IsRecord
        ? (type.IsValueType ? "record struct" : "record")
        : type.TypeKind == TypeKind.Interface ? "interface" : type.IsValueType ? "struct" : "class";

    private static List<string> Constraints(ITypeParameterSymbol parameter)
    {
        var constraints = new List<string>();
        if (parameter.HasUnmanagedTypeConstraint)
        {
            constraints.Add("unmanaged");
        }
        else if (parameter.HasValueTypeConstraint)
        {
            constraints.Add("struct");
        }
        else if (parameter.HasReferenceTypeConstraint)
        {
            constraints.Add(parameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated ? "class?" : "class");
        }
        else if (parameter.HasNotNullConstraint)
        {
            constraints.Add("notnull");
        }
        constraints.AddRange(parameter.ConstraintTypes.Select(t => t.ToDisplayString(NullableTypeFormat)));
        if (parameter.HasConstructorConstraint)
        {
            constraints.Add("new()");
        }
        return constraints;
    }

    private static DiagnosticDescriptor Descriptor(string id, string title, string message) =>
        new(id, title, message, "DotSexpr", DiagnosticSeverity.Error, true);

    private static void Report(SourceProductionContext output, DiagnosticDescriptor descriptor, ISymbol symbol, params object[] args) =>
        output.ReportDiagnostic(Diagnostic.Create(descriptor, symbol.Locations.FirstOrDefault(), args));
}
