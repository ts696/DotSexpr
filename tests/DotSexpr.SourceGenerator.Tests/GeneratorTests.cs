using DotSexpr.Serializer;
using DotSexpr.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Runtime.Loader;

namespace DotSexpr.SourceGenerator.Tests;

public class GeneratorTests
{
    private static readonly MetadataReference[] References =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
        .Select(path => MetadataReference.CreateFromFile(path))
        .Append(MetadataReference.CreateFromFile(typeof(SexprObjectAttribute).Assembly.Location))
        .ToArray();

    [TestCase(false)]
    [TestCase(true)]
    public void Generate_CallUnion_CompilesWithCSharp9(bool allowUnknownCalls)
    {
        var result = Generate($$"""
            [SexprCallUnion(typeof(Move), typeof(Jump), typeof(Step), AllowUnknownCalls = {{(allowUnknownCalls ? "true" : "false")}})]
            public abstract partial record ActionExpression : CallExpression;
            [SexprCall("move")] public sealed partial record Move : ActionExpression;
            [SexprCall("jump")] public sealed partial record Jump : ActionExpression;
            [SexprCall("step")] public sealed partial record Step : ActionExpression;
            [SexprObject] public partial class Plan
            {
                public ActionExpression[] Actions { get; set; } = System.Array.Empty<ActionExpression>();
            }
            """, language: LanguageVersion.CSharp9, warningsAsErrors: true);
        Assert.That(result.GeneratorDiagnostics, Is.Empty);
        Assert.That(result.CompilerErrors, Is.Empty);
        Assert.That(result.GeneratedCount, Is.EqualTo(6));
    }

    [Test]
    public void Generate_UnionFallbackNameCollision_ReportsDiagnostic()
    {
        var result = Generate("""
            [SexprCallUnion(typeof(Move), AllowUnknownCalls = true)]
            public abstract partial record ActionExpression : CallExpression
            {
                public class UnknownCall { }
            }
            [SexprCall("move")] public partial record Move : ActionExpression;
            """);
        Assert.That(result.GeneratorDiagnostics.Select(d => d.Id), Does.Contain("DSX008"));
    }

    [TestCase("[SexprCallUnion] public abstract partial record ActionExpression : CallExpression;")]
    [TestCase("[SexprCallUnion(typeof(int))] public abstract partial record ActionExpression : CallExpression;")]
    [TestCase("[SexprCallUnion] public partial record ActionExpression : CallExpression;")]
    [TestCase("[SexprCallUnion] public abstract partial record ActionExpression;")]
    [TestCase("[SexprCallUnion] public abstract partial record ActionExpression<T> : CallExpression;")]
    [TestCase("[SexprCallUnion(null)] public abstract partial record ActionExpression : CallExpression;")]
    public void Generate_InvalidCallUnion_ReportsDiagnostic(string source)
    {
        var result = Generate(source);
        Assert.That(result.GeneratorDiagnostics.Select(d => d.Id), Is.EqualTo(new[] { "DSX008" }));
        Assert.That(result.GeneratedCount, Is.Zero);
    }

    [TestCase("typeof(Move), typeof(Move)")]
    [TestCase("typeof(Move), typeof(Other)")]
    [TestCase("typeof(Unrelated)")]
    public void Generate_InvalidUnionRegistrations_ReportsDiagnostic(string registrations)
    {
        var result = Generate($$"""
            [SexprCallUnion({{registrations}})]
            public abstract partial record ActionExpression : CallExpression;
            [SexprCall("move")] public partial record Move : ActionExpression;
            [SexprCall("move")] public partial record Other : ActionExpression;
            [SexprCall("other")] public partial record Unrelated : CallExpression;
            """);
        Assert.That(result.GeneratorDiagnostics.Select(d => d.Id), Does.Contain("DSX008"));
    }

    [TestCase("[SexprCall(\"move\")] public record Move : CallExpression;", "DSX001")]
    [TestCase("[SexprCall(\"move\")] public partial record Move;", "DSX007")]
    [TestCase("[SexprCall(\"move\")] public abstract partial record Move : CallExpression;", "DSX002")]
    [TestCase("[SexprCall(\"move\")] public partial record Move(int X) : CallExpression(\"move\", System.Array.Empty<Expression>());", "DSX007")]
    [TestCase("[SexprCall(\"move\")] public partial record Move : CallExpression { public Move() : base(\"jump\", System.Array.Empty<Expression>()) { } }", "DSX007")]
    [TestCase("[SexprCall(\"move\")] public partial record Move : CallExpression { public int X { get; set; } }", "DSX007")]
    [TestCase("[SexprCall(\"move\"), SexprObject] public partial record Move : CallExpression;", "DSX007")]
    public void Generate_InvalidCallDeclaration_ReportsDiagnostic(string source, string id)
    {
        var result = Generate(source);
        Assert.That(result.GeneratorDiagnostics.Select(d => d.Id), Is.EqualTo(new[] { id }));
        Assert.That(result.GeneratedCount, Is.Zero);
    }

    [TestCase("")]
    [TestCase("10")]
    [TestCase("-1.25e+3")]
    [TestCase("1e9999")]
    [TestCase("bad name")]
    [TestCase("a;b")]
    [TestCase("a(b")]
    [TestCase("#|comment|#")]
    [TestCase("\"move\"")]
    public void Generate_InvalidCallName_ReportsDiagnostic(string name)
    {
        var literal = SymbolDisplay.FormatLiteral(name, true);
        var result = Generate($"[SexprCall({literal})] public partial record Move : CallExpression;");
        Assert.That(result.GeneratorDiagnostics.Select(d => d.Id), Is.EqualTo(new[] { "DSX007" }));
        Assert.That(result.GeneratedCount, Is.Zero);
    }

    [Test]
    public void Generate_InvalidUtf16CallName_ReportsDiagnostic()
    {
        var result = Generate("""
            [SexprCall("\uD800")] public partial record Move : CallExpression;
            """);
        Assert.That(result.GeneratorDiagnostics.Select(d => d.Id), Is.EqualTo(new[] { "DSX007" }));
        Assert.That(result.GeneratedCount, Is.Zero);
    }

    [TestCase("[SexprCall(\"move\")] public sealed partial record Move : CallExpression;")]
    [TestCase("[SexprCall(\"移動🚀\")] public partial record Move : CallExpression;")]
    [TestCase("[SexprCall(\"+1\")] public partial record Move : CallExpression;")]
    [TestCase("[SexprCall(\"move\")] public partial record Move<T> : CallExpression where T : class, new();")]
    [TestCase("public partial class Outer<T> where T : class { [SexprCall(\"move\")] public partial record Move : CallExpression; }")]
    [TestCase("namespace A.@event { [SexprCall(\"move\")] public partial record @class : CallExpression; }")]
    public void Generate_Call_CompilesWithCSharp9(string source)
    {
        var result = Generate(source, language: LanguageVersion.CSharp9, warningsAsErrors: true);
        Assert.That(result.GeneratorDiagnostics, Is.Empty);
        Assert.That(result.CompilerErrors, Is.Empty);
        Assert.That(result.GeneratedCount, Is.EqualTo(2));
    }

    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(int.MaxValue)]
    public void Generate_ArgumentCount_CompilesWithCSharp9(int count)
    {
        var source = $"[SexprCall(\"not\", ArgumentCount = {count})] public sealed partial record Not : CallExpression;";
        var result = Generate(source, language: LanguageVersion.CSharp9, warningsAsErrors: true);
        Assert.That(result.GeneratorDiagnostics, Is.Empty);
        Assert.That(result.CompilerErrors, Is.Empty);
        Assert.That(result.GeneratedCount, Is.EqualTo(2));
    }

    [TestCase(-2)]
    [TestCase(int.MinValue)]
    public void Generate_InvalidArgumentCount_ReportsDiagnostic(int count)
    {
        var result = Generate($"[SexprCall(\"not\", ArgumentCount = {count})] public partial record Not : CallExpression;");
        Assert.That(result.GeneratorDiagnostics.Select(d => d.Id), Is.EqualTo(new[] { "DSX007" }));
        Assert.That(result.GeneratorDiagnostics[0].GetMessage(), Does.Contain("ArgumentCount"));
        Assert.That(result.GeneratedCount, Is.Zero);
    }

    [Test]
    public void Generate_CallName_CannotBeOverriddenWithInitializerOrWithExpression()
    {
        var result = Generate("""
            [SexprCall("move")] public partial record Move : CallExpression;
            public class Usage
            {
                public void Test()
                {
                    var move = new Move { Name = "jump" };
                    var jump = move with { Name = "jump" };
                }
            }
            """);
        Assert.That(result.GeneratorDiagnostics, Is.Empty);
        Assert.That(result.CompilerErrors.Select(e => e.Id), Is.EqualTo(new[] { "CS0200", "CS0200" }));
    }

    [Test]
    public void Generate_Call_RegistersClosedGenericModelMembers()
    {
        var result = Generate("""
            [SexprCall("move")] public partial record Move<T> : CallExpression where T : class;
            [SexprObject] public partial class Plan
            {
                public Move<string>[] Steps { get; set; } = System.Array.Empty<Move<string>>();
            }
            """, language: LanguageVersion.CSharp9, warningsAsErrors: true);
        Assert.That(result.GeneratorDiagnostics, Is.Empty);
        Assert.That(result.CompilerErrors, Is.Empty);
        Assert.That(result.GeneratedCount, Is.EqualTo(3));
    }

    [TestCase("[SexprObject] public class Item { }", "DSX001")]
    [TestCase("public class Outer { [SexprObject] public partial class Item { } }", "DSX001")]
    [TestCase("[SexprObject] public abstract partial class Item { }", "DSX002")]
    [TestCase("[SexprObject] public static partial class Item { }", "DSX002")]
    [TestCase("[SexprObject] public ref partial struct Item { }", "DSX002")]
    [TestCase("[SexprObject] public partial class Item { public Item(int x) { } }", "DSX003")]
    [TestCase("[SexprObject] public partial class Item { public int Value { get; init; } }", "DSX004")]
    [TestCase("[SexprObject] public partial class Item { public required int Value { get; set; } }", "DSX004")]
    [TestCase("[SexprObject] public partial class Item { public required int Value; }", "DSX004")]
    [TestCase("[SexprObject] public partial class Item { [SexprTag(\"x\")] private int[] Values; }", "DSX004")]
    [TestCase("[SexprObject] public partial class Item { [SexprTag(\"x\")] internal int Value; }", "DSX004")]
    [TestCase("[SexprObject] public partial class Item { [SexprTag(\"x\")] protected int Value; }", "DSX004")]
    [TestCase("[SexprObject] public partial class Item { [SexprTag(\"x\")] private int Value { get; set; } }", "DSX004")]
    [TestCase("[SexprObject] public partial class Item { [SexprTag(\"x\")] public int Value { get; private set; } }", "DSX004")]
    [TestCase("[SexprObject] public partial class Item { [SexprTag(\"x\")] public int Value { private get; set; } }", "DSX004")]
    [TestCase("[SexprObject] public partial class Item { [SexprTag(\"\")] public int Value; }", "DSX005")]
    [TestCase("[SexprObject] public partial class Item { [SexprTag(\":x\")] public int Value; }", "DSX005")]
    [TestCase("[SexprObject] public partial class Item { [SexprTag(\"a b\")] public int Value; }", "DSX005")]
    [TestCase("[SexprObject] public partial class Item { [SexprTag(\"x\")] public int A; [SexprTag(\"x\")] public int B; }", "DSX005")]
    public void Generate_UnsupportedDeclarations_ReportsActionableDiagnostic(string source, string id)
    {
        var result = Generate(source);
        Assert.That(result.GeneratorDiagnostics.Select(d => d.Id), Does.Contain(id));
        Assert.That(result.GeneratorDiagnostics.All(d => d.Location.IsInSource), Is.True);
        Assert.That(result.GeneratedCount, Is.Zero);
    }

    [TestCase("[SexprObject] public partial class Item<T> where T : class, new() { public T Value { get; set; } }")]
    [TestCase("[SexprObject] public partial class Item { private Item() { } [SexprTag(\"x\")] public int Value { get; set; } }")]
    [TestCase("[SexprObject] public partial class Item { [SexprTag(\"a.b'\\\\c\")] public int Value { get; set; } }")]
    [TestCase("public class Base { public int Value { private get; set; } } [SexprObject] public partial class Item : Base { }")]
    [TestCase("[SexprObject] public partial class Item { public int this[int i] { get => 0; set { } } }")]
    [TestCase("public partial interface Outer<T> { [SexprObject] public partial class Item { public int Value; } }")]
    [TestCase("namespace @event { [SexprObject] public partial class Item { public int Value; } }")]
    [TestCase("namespace A.@class.B { [SexprObject] public partial class Item { public int Value; } }")]
    public void Generate_SupportedDeclarations_Compile(string source)
    {
        var result = Generate(source);
        Assert.That(result.GeneratorDiagnostics, Is.Empty);
        Assert.That(result.CompilerErrors, Is.Empty);
        Assert.That(result.GeneratedCount, Is.EqualTo(2));
    }

    [Test]
    public void Generate_MultiplePartials_EmitsOnlyOneFormatter()
    {
        var result = Generate("[SexprObject] public partial class Item { public int A; } public partial class Item { public int B; }");
        Assert.That(result.GeneratorDiagnostics, Is.Empty);
        Assert.That(result.CompilerErrors, Is.Empty);
        Assert.That(result.GeneratedCount, Is.EqualTo(2));
    }

    [Test]
    public void Generate_SimilarNestedNames_DoNotCollide()
    {
        var result = Generate("""
            public partial class A_B { [SexprObject] public partial class C { } }
            public partial class A { [SexprObject] public partial class B_C { } }
            """);
        Assert.That(result.GeneratorDiagnostics, Is.Empty);
        Assert.That(result.CompilerErrors, Is.Empty);
        Assert.That(result.GeneratedCount, Is.EqualTo(3));
    }

    [Test]
    public void Generate_DerivedStaticMemberHidingProperty_Compiles()
    {
        var result = Generate("""
            public class Base { public int Value { get; set; } }
            [SexprObject] public partial class Item : Base { public static new int Value { get; set; } }
            """);
        Assert.That(result.GeneratorDiagnostics, Is.Empty);
        Assert.That(result.CompilerErrors, Is.Empty);
    }

    [Test]
    public void Generate_UnannotatedType_DoesNotEmitFormatter()
    {
        var result = Generate("public partial class Item { public int Value; }");
        Assert.That(result.GeneratorDiagnostics, Is.Empty);
        Assert.That(result.GeneratedCount, Is.Zero);
    }

    [Test]
    public void Generate_ResolverInEscapedNamespace_DiscoversClosedGenericMembers()
    {
        var result = Generate("""
            namespace A.@event
            {
                [SexprObject] public partial class Item<T> { public T[] Values { get; set; } = System.Array.Empty<T>(); }
                [SexprObject] public partial class Document
                {
                    public Item<int>[][] Items { get; set; } = System.Array.Empty<Item<int>[]>();
                }
            }
            """);
        Assert.That(result.GeneratorDiagnostics, Is.Empty);
        Assert.That(result.CompilerErrors, Is.Empty);
        Assert.That(result.GeneratedCount, Is.EqualTo(3));
    }

    [Test]
    public void Generate_NullableMembersAndConstraints_CompileWithWarningsAsErrors()
    {
        var result = Generate("""
            [SexprObject] public partial class Box<T> where T : class?
            {
                public T? Value { get; set; }
            }
            [SexprObject] public partial class Document
            {
                public string? Name { get; set; }
                public string?[] Items { get; set; } = System.Array.Empty<string?>();
                public Box<string?> Box { get; set; } = new Box<string?>();
                public Box<string?>? OptionalBox { get; set; }
                public Child? Child { get; set; }
            }
            [SexprObject] public partial class Child { public string? Name { get; set; } }
            """, language: LanguageVersion.CSharp9, warningsAsErrors: true);
        Assert.That(result.GeneratorDiagnostics, Is.Empty);
        Assert.That(result.CompilerErrors, Is.Empty);
    }

    [Test]
    public void Generate_Record_CompilesWithCSharp9()
    {
        var result = Generate("[SexprObject] public partial record Item { public int Value { get; set; } }",
            language: LanguageVersion.CSharp9, warningsAsErrors: true);
        Assert.That(result.GeneratorDiagnostics, Is.Empty);
        Assert.That(result.CompilerErrors, Is.Empty);
    }

    [Test]
    public void Generate_InaccessibleModel_ReportsDiagnostic()
    {
        var result = Generate("public partial class Outer { [SexprObject] private partial class Item { } }");
        Assert.That(result.GeneratorDiagnostics.Select(d => d.Id), Does.Contain("DSX006"));
    }

    [Test]
    public void Generate_ResolverForReferencedModel_UsesOnlyPublicMembers()
    {
        var model = Generate("""
            [SexprObject] public partial class External<T>
            {
                public T[] Values { get; set; } = System.Array.Empty<T>();
                public NonPublicDependency[] PrivateSetter { get; private set; } = System.Array.Empty<NonPublicDependency>();
                public NonPublicDependency[] PrivateGetter { private get; set; } = System.Array.Empty<NonPublicDependency>();
                private NonPublicDependency[] _values = System.Array.Empty<NonPublicDependency>();
                public int PrivateCount => _values.Length;
            }
            public class NonPublicDependency { }
            [SexprObject] public partial class Foreign { public int Value { get; set; } }
            """);
        using var stream = new MemoryStream();
        var emitted = model.Compilation.WithAssemblyName("ExternalModels" + Guid.NewGuid().ToString("N")).Emit(stream);
        Assert.That(emitted.Success, Is.True, string.Join("\n", emitted.Diagnostics));
        var reference = MetadataReference.CreateFromImage(stream.ToArray());
        var result = Generate("""
            [SexprObject] public partial class Document
            {
                public External<long> Model { get; set; } = new External<long>();
                public Foreign Other { get; set; } = new Foreign();
            }
            public static class Probe
            {
                public static string Run()
                {
                    if (DotSexpr.Generated.DotSexprGeneratedResolver.Instance.GetFormatter<NonPublicDependency[]>() != null)
                    {
                        throw new System.InvalidOperationException("Non-public dependencies must not be included.");
                    }
                    var options = new SexprSerializerOptions { Resolver = DotSexpr.Generated.DotSexprGeneratedResolver.Instance };
                    if (options.Resolver.GetFormatter<Foreign>() == null)
                    {
                        throw new System.InvalidOperationException("Referenced models must be available.");
                    }
                    var model = SexprSerializer.Deserialize<Document>(
                        "(:Model (:Values (1 2) :PrivateSetter (3) :PrivateGetter (4) :_values (5)))", options);
                    return SexprSerializer.Serialize(model, options);
                }
            }
            """, reference, warningsAsErrors: true);
        Assert.That(result.GeneratorDiagnostics, Is.Empty);
        Assert.That(result.CompilerErrors, Is.Empty);
        Assert.That(result.GeneratedCount, Is.EqualTo(2));
        stream.Position = 0;
        AssemblyLoadContext.Default.LoadFromStream(stream);
        using var application = new MemoryStream();
        var compiled = result.Compilation.WithAssemblyName("Consumer" + Guid.NewGuid().ToString("N")).Emit(application);
        Assert.That(compiled.Success, Is.True, string.Join("\n", compiled.Diagnostics));
        application.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(application);
        var text = assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null);
        Assert.That(text, Is.EqualTo("(:Model (:Values (1 2)) :Other (:Value 0))"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Generate_ExternalUnionWithInternalCase_UsesPublicFormatter(bool allowUnknownCalls)
    {
        var model = Generate($$"""
            [SexprCallUnion(typeof(InternalMove), AllowUnknownCalls = {{(allowUnknownCalls ? "true" : "false")}})]
            public abstract partial record ExternalAction : CallExpression;
            [SexprCall("move", ArgumentCount = 1)] internal sealed partial record InternalMove : ExternalAction;
            """, language: LanguageVersion.CSharp9, warningsAsErrors: true);
        Assert.That(model.GeneratorDiagnostics, Is.Empty);
        Assert.That(model.CompilerErrors, Is.Empty);
        using var modelStream = new MemoryStream();
        var emitted = model.Compilation.WithAssemblyName("InternalCaseModels" + Guid.NewGuid().ToString("N")).Emit(modelStream);
        Assert.That(emitted.Success, Is.True, string.Join("\n", emitted.Diagnostics));
        var reference = MetadataReference.CreateFromImage(modelStream.ToArray());
        var consumer = Generate("""
            [SexprObject] public partial class Document
            {
                public ExternalAction Action { get; set; } = null!;
                public ExternalAction[] Actions { get; set; } = System.Array.Empty<ExternalAction>();
            }
            public static class Probe
            {
                public static string Run(string text)
                {
                    // Deliberately use only the consumer resolver, not DefaultResolver.
                    var options = new SexprSerializerOptions { Resolver = DotSexpr.Generated.DotSexprGeneratedResolver.Instance };
                    var document = SexprSerializer.Deserialize<Document>(text, options);
                    if (document.Action.GetType().Name != "InternalMove")
                        throw new System.InvalidOperationException("The external case formatter was not used.");
                    return SexprSerializer.Serialize(document, options);
                }
            }
            """, reference, language: LanguageVersion.CSharp9, warningsAsErrors: true);
        Assert.That(consumer.GeneratorDiagnostics, Is.Empty);
        Assert.That(consumer.CompilerErrors, Is.Empty);
        modelStream.Position = 0;
        AssemblyLoadContext.Default.LoadFromStream(modelStream);
        using var consumerStream = new MemoryStream();
        emitted = consumer.Compilation.WithAssemblyName("InternalCaseConsumer" + Guid.NewGuid().ToString("N")).Emit(consumerStream);
        Assert.That(emitted.Success, Is.True, string.Join("\n", emitted.Diagnostics));
        consumerStream.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(consumerStream);
        var actions = allowUnknownCalls ? "((move 1) (custom (move 2)))" : "((move 1))";
        var input = $"(:Action (move (move 3)) :Actions {actions})";
        var output = assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, new object[] { input });
        Assert.That(output, Is.EqualTo(input));
        var error = Assert.Throws<System.Reflection.TargetInvocationException>(() =>
            assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, new object[] { "(:Action (move) :Actions ())" }))!;
        Assert.That(error.InnerException, Is.TypeOf<SexprSerializerException>());
        Assert.That(((SexprSerializerException)error.InnerException!).Path, Is.EqualTo("$.Action"));
        Assert.That(error.InnerException!.Message, Does.Contain("requires exactly 1 argument(s)"));
    }

    [Test]
    public void Generate_ModuleInitializers_RegisterMultipleAssemblies()
    {
        var result = Generate("""
            [SexprObject] public partial class Item
            {
                public int[] Values { get; set; } = System.Array.Empty<int>();
            }
            public static class Probe
            {
                public static string Run(string text)
                {
                    return SexprSerializer.Serialize(SexprSerializer.Deserialize<Item>(text));
                }
            }
            """, language: LanguageVersion.CSharp9, warningsAsErrors: true);
        Assert.That(result.GeneratorDiagnostics, Is.Empty);
        Assert.That(result.CompilerErrors, Is.Empty);
        var first = LoadAssembly();
        var second = LoadAssembly();
        var resolver = SexprSerializerOptions.Default.Resolver;
        var getValue = typeof(ISexprFormatterResolver).GetMethod(nameof(ISexprFormatterResolver.GetFormatter))!;
        var firstType = first.GetType("Item")!;
        var secondType = second.GetType("Item")!;
        var firstRun = first.GetType("Probe")!.GetMethod("Run")!;
        var secondRun = second.GetType("Probe")!.GetMethod("Run")!;
        const string firstText = "(:Values (1 2))";
        const string secondText = "(:Values (3 4))";
        Assert.That(firstRun.Invoke(null, new object[] { firstText }), Is.EqualTo(firstText));
        Assert.That(secondRun.Invoke(null, new object[] { secondText }), Is.EqualTo(secondText));
        Assert.That(firstRun.Invoke(null, new object[] { firstText }), Is.EqualTo(firstText));
        var formatter = getValue.MakeGenericMethod(firstType).Invoke(resolver, null);
        Assert.That(formatter, Is.Not.Null);
        Assert.That(getValue.MakeGenericMethod(firstType).Invoke(resolver, null), Is.SameAs(formatter));
        Assert.That(getValue.MakeGenericMethod(secondType).Invoke(resolver, null), Is.Not.Null);

        System.Reflection.Assembly LoadAssembly()
        {
            using var stream = new MemoryStream();
            var emitted = result.Compilation.WithAssemblyName("AutomaticResolver" + Guid.NewGuid().ToString("N")).Emit(stream);
            Assert.That(emitted.Success, Is.True, string.Join("\n", emitted.Diagnostics));
            stream.Position = 0;
            return AssemblyLoadContext.Default.LoadFromStream(stream);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Generate_ExternalModel_CallerDeserializesBeforeExecutingModelCode(bool registrationPending)
    {
        var model = Generate("""
            [SexprObject] public partial class ExternalModel
            {
                public int[] Values { get; set; } = System.Array.Empty<int>();
            }
            public static class ModelSerialization
            {
                public static void Initialize()
                {
                    DotSexpr.Serializer.Resolvers.DefaultResolver.Register(
                        DotSexpr.Generated.DotSexprGeneratedResolver.Instance);
                }
            }
            """, language: LanguageVersion.CSharp9, warningsAsErrors: true);
        Assert.That(model.GeneratorDiagnostics, Is.Empty);
        Assert.That(model.CompilerErrors, Is.Empty);
        var compilation = model.Compilation;
        if (registrationPending)
        {
            // Keep registration pending regardless of the runtime's eager initialization policy.
            var tree = compilation.SyntaxTrees.Single(t => t.FilePath.EndsWith("DotSexprGeneratedResolver.g.cs"));
            var root = tree.GetRoot();
            var register = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.ValueText == "Register");
            compilation = compilation.ReplaceSyntaxTree(tree,
                tree.WithRootAndOptions(root.ReplaceNode(register, register.WithAttributeLists(default)), tree.Options));
        }
        using var modelStream = new MemoryStream();
        var emitted = compilation.WithAssemblyName("CallerFirstModels" + Guid.NewGuid().ToString("N")).Emit(modelStream);
        Assert.That(emitted.Success, Is.True, string.Join("\n", emitted.Diagnostics));
        var caller = Generate("""
            public static class Caller
            {
                public static string DeserializeFirst()
                {
                    return SexprSerializer.Serialize(SexprSerializer.Deserialize<ExternalModel>("(:Values (1 2))"));
                }
                public static void InitializeModels()
                {
                    ModelSerialization.Initialize();
                }
            }
            """, MetadataReference.CreateFromImage(modelStream.ToArray()),
            language: LanguageVersion.CSharp9, warningsAsErrors: true);
        Assert.That(caller.GeneratorDiagnostics, Is.Empty);
        Assert.That(caller.CompilerErrors, Is.Empty);
        Assert.That(caller.GeneratedCount, Is.Zero);
        modelStream.Position = 0;
        AssemblyLoadContext.Default.LoadFromStream(modelStream);
        using var callerStream = new MemoryStream();
        emitted = caller.Compilation.WithAssemblyName("CallerFirst" + Guid.NewGuid().ToString("N")).Emit(callerStream);
        Assert.That(emitted.Success, Is.True, string.Join("\n", emitted.Diagnostics));
        callerStream.Position = 0;
        var callerType = AssemblyLoadContext.Default.LoadFromStream(callerStream).GetType("Caller")!;
        var deserialize = callerType.GetMethod("DeserializeFirst")!;
        object? firstResult = null;
        System.Reflection.TargetInvocationException? firstError = null;
        try
        {
            firstResult = deserialize.Invoke(null, null);
        }
        catch (System.Reflection.TargetInvocationException error)
        {
            firstError = error;
        }
        if (firstError == null)
        {
            Assert.That(registrationPending, Is.False);
            Assert.That(firstResult, Is.EqualTo("(:Values (1 2))"));
        }
        else
        {
            Assert.That(firstError.InnerException, Is.TypeOf<SexprSerializerException>());
            Assert.That(firstError.InnerException!.Message, Does.Contain("No formatter is available"));
        }
        TestContext.WriteLine($"Registration pending: {registrationPending}; first Deserialize succeeded: {firstError == null}");
        callerType.GetMethod("InitializeModels")!.Invoke(null, null);
        Assert.That(deserialize.Invoke(null, null), Is.EqualTo("(:Values (1 2))"));
    }

    private static (Diagnostic[] GeneratorDiagnostics, Diagnostic[] CompilerErrors, int GeneratedCount, Compilation Compilation)
        Generate(string body, MetadataReference? additionalReference = null,
            LanguageVersion language = LanguageVersion.Preview, bool warningsAsErrors = false)
    {
        var parseOptions = new CSharpParseOptions(language);
        var syntax = CSharpSyntaxTree.ParseText("using DotSexpr.Serializer;\n" + body, parseOptions);
        var references = additionalReference == null ? References : References.Append(additionalReference).ToArray();
        var compilation = CSharpCompilation.Create("GeneratorInput", new[] { syntax }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable,
                generalDiagnosticOption: warningsAsErrors ? ReportDiagnostic.Error : ReportDiagnostic.Default));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new SexprGenerator().AsSourceGenerator() }, parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        return (diagnostics.ToArray(), output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray(),
            driver.GetRunResult().GeneratedTrees.Length, output);
    }
}
