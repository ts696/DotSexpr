using DotSexpr.Emitter;
using DotSexpr.Serializer;
using DotSexpr.Serializer.Resolvers;

namespace DotSexpr.Tests;

public partial class TypedCallTests
{
    [Test]
    public void ConstructorReceivesArgumentsAndProvidesName()
    {
        var call = SexprSerializer.Deserialize<Not>("(not (ready ?actor))");
        Assert.That(call.Name, Is.EqualTo("not"));
        Assert.That(call.Operand, Is.TypeOf<Condition.UnknownCall>());
        Assert.That(call.Operand.Name, Is.EqualTo("ready"));
        Assert.That(call.Arguments, Is.EqualTo(new[] { call.Operand }));
        var (name, arguments) = (CallExpression)call;
        Assert.That(name, Is.EqualTo("not"));
        Assert.That(arguments[0], Is.SameAs(call.Operand));
    }

    [Test]
    public void ConstructorPreservesArgumentsAndSupportsWith()
    {
        const string text = "(move ?from ?to 10 0.5 (speed 2))";
        var move = SexprSerializer.Deserialize<Move>(text);
        Assert.That(move.Name, Is.EqualTo("move"));
        Assert.That(move.Arguments, Has.Count.EqualTo(5));
        Assert.That(move.Arguments[2], Is.EqualTo(new IntegerExpression(10)));
        Assert.That(move.Arguments[3], Is.EqualTo(new NumberExpression(0.5)));
        Assert.That(SexprSerializer.Serialize(move), Is.EqualTo(text));
        Assert.That(SexprSerializer.Serialize<Expression>(move), Is.EqualTo(text));
        Assert.That(SexprSerializer.Serialize(new Move()), Is.EqualTo("(move)"));
        Assert.That(SexprSerializer.Serialize(move with { Items = [new IntegerExpression(3)] }),
            Is.EqualTo("(move 3)"));
    }

    [Test]
    public void DeclaredTypesProduceTypedArgumentsAndRoundTrip()
    {
        const string text = "(values ?actor 9007199254740993 0.5 (not (ready ?actor)))";
        var call = SexprSerializer.Deserialize<Values>(text);
        Assert.That(call.Arguments[0], Is.EqualTo(new SymbolExpression("?actor")));
        Assert.That(call.Arguments[1], Is.EqualTo(new IntegerExpression(9007199254740993)));
        Assert.That(call.Arguments[2], Is.EqualTo(new NumberExpression(0.5)));
        Assert.That(SexprSerializer.Serialize(call), Is.EqualTo(text));
    }

    [Test]
    public void FixedConstructorTypesAndEmptyConstructorRoundTrip()
    {
        var cost = SexprSerializer.Deserialize<Cost>("(cost ?actor 1)");
        Assert.That(cost.Actor.Name, Is.EqualTo("?actor"));
        Assert.That(cost.Amount.Value, Is.EqualTo(1));
        Assert.That(SexprSerializer.Serialize(cost), Is.EqualTo("(cost ?actor 1)"));
        Assert.That(SexprSerializer.Serialize(new Zero()), Is.EqualTo("(zero)"));
        Assert.That(SexprSerializer.Deserialize<Zero>("(zero)").Arguments, Is.Empty);
        Assert.Throws<SexprSerializerException>(() => SexprSerializer.Deserialize<Zero>("(zero x)"));
    }

    [Test]
    public void ExpressionArgumentAcceptsSymbolsAndCalls()
    {
        const string text = "(:precondition (and (not (ready ?actor)) (zero) (custom 1 2)))";
        var task = SexprSerializer.Deserialize<ExpressionDocument>(text);
        Assert.That(SexprSerializer.Serialize(task), Is.EqualTo(text));
        var not = SexprSerializer.Deserialize<ExpressionNot>("(not ?actor)");
        Assert.That(not.Arguments, Is.EqualTo(new[] { new SymbolExpression("?actor") }));
        Assert.That(SexprSerializer.Serialize(not), Is.EqualTo("(not ?actor)"));
    }

    [TestCase("(and)")]
    [TestCase("(and (ready x))")]
    [TestCase("(and (ready x) (not (injured x)) (repeat 2 (move x) (inspect x)))")]
    [TestCase("(repeat 2)")]
    [TestCase("(repeat 2 (ready x) (not (injured x)))")]
    public void ParamsArgumentsRoundTrip(string text)
    {
        var call = SexprSerializer.Deserialize<Condition>(text);
        Assert.That(SexprSerializer.Serialize(call), Is.EqualTo(text));
        if (call is And and) Assert.That(and.Operands.Length, Is.EqualTo(call.Arguments.Count));
        if (call is Repeat repeat)
        {
            Assert.That(repeat.Count.Value, Is.EqualTo(2));
            Assert.That(repeat.Actions.Length, Is.EqualTo(call.Arguments.Count - 1));
        }
    }

    [TestCase("(move)")]
    [TestCase("(move x 2 0.5 (f))")]
    public void ExpressionParamsAllowArbitraryArguments(string text)
    {
        Assert.That(SexprSerializer.Serialize(SexprSerializer.Deserialize<Move>(text)), Is.EqualTo(text));
    }

    [Test]
    public void ExpressionParamsAcceptAnyCount()
    {
        Assert.That(SexprSerializer.Serialize(new ExpressionAnd()), Is.EqualTo("(and)"));
        var and = SexprSerializer.Deserialize<ExpressionAnd>("(and x y z)");
        Assert.That(and.Arguments, Has.Count.EqualTo(3));
        Assert.That(SexprSerializer.Serialize(and), Is.EqualTo("(and x y z)"));
    }

    [Test]
    public void WithExpressionAndPropertyInitializerAreReflectedInSerialization()
    {
        var original = new Not(new And());
        var changed = original with { Operand = new Condition.UnknownCall("ready", []) };
        Assert.That(SexprSerializer.Serialize(original), Is.EqualTo("(not (and))"));
        Assert.That(SexprSerializer.Serialize(changed), Is.EqualTo("(not (ready))"));
        Assert.That(SexprSerializer.Serialize<Expression>(changed), Is.EqualTo("(not (ready))"));
        var repeat = new Repeat(new IntegerExpression(1)) with
        {
            Count = new IntegerExpression(3), Actions = [changed, new And()]
        };
        Assert.That(SexprSerializer.Serialize(repeat), Is.EqualTo("(repeat 3 (not (ready)) (and))"));
        var and = new And { Operands = [repeat] };
        Assert.That(and.Arguments[0], Is.SameAs(repeat));
    }

    [Test]
    public void ArgumentsCannotBeChangedIndependentlyOfConstructorProperties()
    {
        CallExpression call = new Not(new And());
        var error = Assert.Throws<InvalidOperationException>(() =>
        {
            _ = call with { Arguments = [] };
        })!;
        Assert.That(error.Message, Does.Contain("constructor-bound properties"));
    }

    [Test]
    public void ArraysAndPropertiesUseGeneratedFormatter()
    {
        const string text = "(:Steps ((move 1) (move 2)))";
        var plan = SexprSerializer.Deserialize<MovePlan>(text);
        Assert.That(plan.Steps.Select(m => m.Name), Is.EqualTo(new[] { "move", "move" }));
        Assert.That(SexprSerializer.Serialize(plan), Is.EqualTo(text));
        Assert.That(SexprSerializer.Deserialize<Move[]>("((move))"), Has.Length.EqualTo(1));
        var error = Assert.Throws<SexprSerializerException>(() =>
            SexprSerializer.Deserialize<MovePlan>("(:Steps ((move) (jump)))"))!;
        Assert.That(error.Path, Is.EqualTo("$.Steps[1][0]"));
        Assert.That(error.BytePosition, Is.EqualTo(17));
        Assert.That(error.Message, Does.Contain("'move'").And.Contain("'jump'"));
    }

    [Test]
    public void ConstructorCallsWorkInPropertiesAndClosedGenerics()
    {
        const string text = "(:Precondition (not (and (ready x))) :Value (value 7))";
        var document = SexprSerializer.Deserialize<Document>(text);
        Assert.That(document.Value.Item.Value, Is.EqualTo(7));
        Assert.That(SexprSerializer.Serialize(document), Is.EqualTo(text));
        document.Precondition = new Not(new Condition.UnknownCall("not", []));
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize(document))!;
        Assert.That(error.Path, Is.EqualTo("$.Precondition[1][0]"));
    }

    [Test]
    public void DedicatedCallArgumentUsesDeclaredTypeAndName()
    {
        const string text = "(with-step (step 7))";
        var call = SexprSerializer.Deserialize<WithStep>(text);
        Assert.That(call.Arguments[0], Is.TypeOf<Step>());
        Assert.That(SexprSerializer.Serialize(call), Is.EqualTo(text));
        var error = Assert.Throws<SexprSerializerException>(() =>
            SexprSerializer.Deserialize<WithStep>("(with-step (other 7))"))!;
        Assert.That(error.Path, Is.EqualTo("$[1][0]"));
    }

    [Test]
    public void RecursiveUnionArgumentResolvesKnownAndUnknownCalls()
    {
        const string text = "(not (and (not (ready x)) (repeat 2 (move x))))";
        var call = SexprSerializer.Deserialize<Not>(text);
        Assert.That(call.Arguments[0], Is.TypeOf<And>());
        var and = (And)call.Arguments[0];
        Assert.That(and.Arguments[0], Is.TypeOf<Not>());
        Assert.That(and.Arguments[1], Is.TypeOf<Repeat>());
        Assert.That(SexprSerializer.Serialize(call), Is.EqualTo(text));
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize(
            new Not(new Condition.UnknownCall("not", []))))!;
        Assert.That(error.Path, Is.EqualTo("$[1][0]"));
    }

    [Test]
    public void NumberArgumentAcceptsIntegerSyntaxAfterNormalization()
    {
        var call = new Values(new SymbolExpression("x"), new IntegerExpression(2), new NumberExpression(1), new SymbolExpression("y"));
        var text = SexprSerializer.Serialize(call);
        Assert.That(text, Is.EqualTo("(values x 2 1 y)"));
        var result = SexprSerializer.Deserialize<Values>(text);
        Assert.That(result.Arguments[2], Is.EqualTo(new NumberExpression(1)));
    }

    [Test]
    public void DeclaredArgumentUsesCustomFormatterInBothDirections()
    {
        var options = new SexprSerializerOptions { Resolver = new StepResolver() };
        var call = SexprSerializer.Deserialize<WithStep>("(with-step (step 99))", options);
        Assert.That(((Step)call.Arguments[0]).Arguments, Is.EqualTo(new Expression[] { new IntegerExpression(7) }));
        Assert.That(SexprSerializer.Serialize(call, options), Is.EqualTo("(with-step (step 7))"));
        Condition nested = new And([call]);
        Assert.That(SexprSerializer.Serialize(nested, options), Is.EqualTo("(and (with-step (step 7)))"));
    }

    [Test]
    public void NumericArgumentsUseCustomPrimitiveFormatters()
    {
        var options = new SexprSerializerOptions { Resolver = new NumericResolver() };
        var move = SexprSerializer.Deserialize<Move>("(move 1 0.5)", options);
        Assert.That(move.Arguments, Is.EqualTo(new Expression[]
        {
            new IntegerExpression(11), new NumberExpression(10.5)
        }));
        Assert.That(SexprSerializer.Serialize(move, options), Is.EqualTo("(move 21 20.5)"));
    }

    [TestCase("(jump)", "$[0]", 1)]
    [TestCase("(Move)", "$[0]", 1)]
    [TestCase("(123)", "$[0]", 1)]
    [TestCase("(\"move\")", "$[0]", 1)]
    [TestCase("()", "$", 0)]
    [TestCase("move", "$", 0)]
    [TestCase("42", "$", 0)]
    public void InvalidNameOrShapeReportsLocation(string text, string path, int position)
    {
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Deserialize<Move>(text))!;
        Assert.That(error.Path, Is.EqualTo(path));
        Assert.That(error.BytePosition, Is.EqualTo(position));
    }

    [Test]
    public void NameIsValidatedBeforeArity()
    {
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Deserialize<ExpressionNot>("(and)"))!;
        Assert.That(error.Path, Is.EqualTo("$[0]"));
        Assert.That(error.Message, Does.Contain("Expected call name 'not'"));
    }

    [TestCase("(not)", "exactly 1")]
    [TestCase("(not (and) (and))", "exactly 1")]
    [TestCase("(repeat)", "at least 1")]
    public void InvalidCountFailsBeforeConstruction(string text, string message)
    {
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Deserialize<Condition>(text))!;
        Assert.That(error.Path, Is.EqualTo("$"));
        Assert.That(error.Message, Does.Contain(message));
    }

    [TestCase("(not)", 0)]
    [TestCase("(not x y)", 2)]
    [TestCase("(not \"invalid\" \"invalid\")", 2)]
    public void InvalidArityFailsBeforeReadingArguments(string text, int actualCount)
    {
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Deserialize<ExpressionNot>(text))!;
        Assert.That(error.Path, Is.EqualTo("$"));
        Assert.That(error.BytePosition, Is.Zero);
        Assert.That(error.Message, Does.Contain("requires exactly 1 argument(s)").And.Contain($"found {actualCount}"));
    }

    [Test]
    public void EmptyConstructorExcludesCallNameFromArity()
    {
        Assert.That(SexprSerializer.Serialize(new ExpressionZero()), Is.EqualTo("(zero)"));
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Deserialize<ExpressionZero>("(zero x)"))!;
        Assert.That(error.Message, Does.Contain("requires exactly 0 argument(s)").And.Contain("found 1"));
    }

    [TestCase("(and 1)", "$[1]")]
    [TestCase("(repeat x)", "$[1]")]
    [TestCase("(not (and (ready) (repeat 2 3)))", "$[1][2][2]")]
    public void InvalidFixedOrParamsTypeReportsLocation(string text, string path)
    {
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Deserialize<Condition>(text))!;
        Assert.That(error.Path, Is.EqualTo(path));
        Assert.That(error.BytePosition, Is.Not.Null);
    }

    [TestCase("(values 1 2 0.5 x)", 1, "1")]
    [TestCase("(values x 0.5 0.5 x)", 2, "0.5")]
    [TestCase("(values x 2 x x)", 3, "x x")]
    [TestCase("(repeat ?count (move x))", 1, "?count")]
    [TestCase("(repeat 2 x)", 2, "x")]
    [TestCase("(not 1)", 1, "1")]
    public void WrongInputTypeReportsArgumentLocation(string text, int index, string value)
    {
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Deserialize<Condition>(text))!;
        Assert.That(error.Path, Is.EqualTo($"$[{index}]"));
        Assert.That(error.BytePosition, Is.EqualTo(text.IndexOf(value, StringComparison.Ordinal)));
    }

    [TestCase("9223372036854775808")]
    [TestCase("-9223372036854775809")]
    [TestCase("1e309")]
    public void NumericArgumentOutOfRangeReportsNestedPath(string number)
    {
        var error = Assert.Throws<SexprSerializerException>(() =>
            SexprSerializer.Deserialize<Move>($"(move (scale {number}))"))!;
        Assert.That(error.Path, Is.EqualTo("$[1][1]"));
        Assert.That(error.BytePosition, Is.EqualTo(13));
        Assert.That(error.InnerException, Is.TypeOf<OverflowException>());
    }

    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(double.NegativeInfinity)]
    public void NonFiniteNumericArgumentReportsPath(double number)
    {
        var error = Assert.Throws<SexprSerializerException>(() =>
            SexprSerializer.Serialize(new Move([new NumberExpression(number)])))!;
        Assert.That(error.Path, Is.EqualTo("$[1]"));
    }

    [Test]
    public void NullConstructorValuesFailWhenSerialized()
    {
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize(new Not(null!)))!;
        Assert.That(error.Path, Is.EqualTo("$[1]"));
        Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize(new And(null!)));
        Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize(new Repeat(new IntegerExpression(1), null!)));
    }

    [Test]
    public void NullParamsElementReportsItsIndex()
    {
        var document = new Document { Precondition = new And(new And(), null!) };
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize(document))!;
        Assert.That(error.Path, Is.EqualTo("$.Precondition[2]"));
    }

    [Test]
    public void NullOutputReportsArgumentThroughPropertiesAndArrays()
    {
        var call = new Repeat(new IntegerExpression(2), new Condition.UnknownCall("move", [])) with
        {
            Count = null!
        };
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize(call))!;
        Assert.That(error.Path, Is.EqualTo("$[1]"));
        Assert.That(error.Message, Does.Contain("IntegerExpression"));
        var document = new ConditionArrayDocument { Conditions = [new And([call])] };
        error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize(document))!;
        Assert.That(error.Path, Is.EqualTo("$.Conditions[0][1][1]"));
        Assert.That(error.BytePosition, Is.Null);
    }

    [TestCase(0)]
    [TestCase(2)]
    public void InvalidArgumentProjectionIsRejected(int count)
    {
        ExpressionNot not = new MalformedNot(count);
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize(not))!;
        Assert.That(error.Path, Is.EqualTo("$"));
        Assert.That(error.Message, Does.Contain("requires exactly 1 argument(s)").And.Contain($"found {count}"));
        var task = new ExpressionDocument { Precondition = new ExpressionAnd([not]) };
        error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize(task))!;
        Assert.That(error.Path, Is.EqualTo("$.precondition[1]"));
        Assert.That(error.BytePosition, Is.Null);
    }

    [TestCase("(:precondition (and (not)))")]
    [TestCase("(:precondition (custom (not x y)))")]
    public void NestedArityErrorsRetainCallLocation(string text)
    {
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Deserialize<ExpressionDocument>(text))!;
        Assert.That(error.Path, Is.EqualTo("$.precondition[1]"));
        Assert.That(error.BytePosition, Is.EqualTo(text.IndexOf("(not", StringComparison.Ordinal)));
    }

    [Test]
    public void NestedInputTypeErrorPreservesDeepLocation()
    {
        const string text = "(:Conditions ((custom (repeat ?count (move x)))))";
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Deserialize<ConditionArrayDocument>(text))!;
        Assert.That(error.Path, Is.EqualTo("$.Conditions[0][1][1]"));
        Assert.That(error.BytePosition, Is.EqualTo(text.IndexOf("?count", StringComparison.Ordinal)));
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void CustomFormatterErrorRetainsArgumentIndex(bool variadic, bool located)
    {
        var resolver = new ErrorResolver(located);
        var options = new SexprSerializerOptions { Resolver = resolver };
        if (variadic) Check(new Variadic(new SymbolExpression("x")));
        else Check(new Positional(new SymbolExpression("x")));

        void Check<T>(T value)
        {
            var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize(value, options))!;
            Assert.That(error.Path, Is.EqualTo("$[1]"));
            if (located)
            {
                Assert.That(error, Is.SameAs(resolver.LastError));
                Assert.That(error.BytePosition, Is.Zero);
            }
            else
            {
                Assert.That(error.InnerException, Is.SameAs(resolver.LastError));
                Assert.That(error.BytePosition, Is.Null);
            }
            error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize(new[] { value }, options))!;
            Assert.That(error.Path, Is.EqualTo("$[0][1]"));
        }
    }

    [Test]
    public void UnlocatedErrorRetainsModelPropertyAndArgumentIndex()
    {
        var options = new SexprSerializerOptions { Resolver = new ErrorResolver(false) };
        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize(new ErrorDocument(), options))!;
        Assert.That(error.Path, Is.EqualTo("$.Value[1]"));
        Assert.That(error.Message, Does.Contain("bad"));
    }

    [SexprCall("move")]
    public sealed partial record Move(params Expression[] Items) : CallExpression;

    [SexprObject]
    public partial class MovePlan
    {
        public Move[] Steps { get; set; } = [];
    }

    [SexprCallUnion(typeof(Not), typeof(And), typeof(Repeat), typeof(Values), typeof(WithStep), AllowUnknownCalls = true)]
    public abstract partial record Condition : CallExpression;

    [SexprCall("not")]
    public sealed partial record Not(Condition Operand) : Condition;

    [SexprCall("and")]
    public sealed partial record And(params Condition[] Operands) : Condition;

    [SexprCall("repeat")]
    public sealed partial record Repeat(IntegerExpression Count, params Condition[] Actions) : Condition;

    [SexprCall("cost")]
    public sealed partial record Cost(SymbolExpression Actor, NumberExpression Amount) : CallExpression;

    [SexprCall("zero")]
    public sealed partial record Zero() : CallExpression;

    [SexprCall("value")]
    public sealed partial record Value<T>(T Item) : CallExpression where T : Expression;

    [SexprObject]
    public partial class Document
    {
        public Condition Precondition { get; set; } = new And();
        public Value<IntegerExpression> Value { get; set; } = new(new IntegerExpression(1));
    }

    [SexprCall("values")]
    public sealed partial record Values(SymbolExpression Symbol, IntegerExpression Integer, NumberExpression Number, Expression Other) : Condition;

    [SexprCall("with-step")]
    public sealed partial record WithStep(Step Step) : Condition;

    [SexprCall("step")]
    public sealed partial record Step(IntegerExpression Count) : CallExpression;

    [SexprObject]
    public partial class ConditionArrayDocument
    {
        public Condition[] Conditions { get; set; } = [];
    }

    [SexprCall("positional")]
    public sealed partial record Positional(SymbolExpression Item) : CallExpression;

    [SexprCall("variadic")]
    public sealed partial record Variadic(params SymbolExpression[] Items) : CallExpression;

    [SexprObject]
    public partial class ErrorDocument
    {
        public Positional Value { get; set; } = new(new SymbolExpression("x"));
    }

    // Expression-valued operands also accept symbols and numbers.
    [SexprCallUnion(typeof(ExpressionNot), typeof(ExpressionAnd), typeof(ExpressionZero), AllowUnknownCalls = true)]
    public abstract partial record ExpressionCondition : CallExpression;

    [SexprCall("not")]
    public partial record ExpressionNot(Expression Operand) : ExpressionCondition;

    [SexprCall("and")]
    public sealed partial record ExpressionAnd(params Expression[] Items) : ExpressionCondition;

    [SexprCall("zero")]
    public sealed partial record ExpressionZero() : ExpressionCondition;

    public sealed record MalformedNot(int Count) : ExpressionNot(new SymbolExpression("x"))
    {
        protected override IReadOnlyList<Expression> GetCallArguments() =>
            Enumerable.Repeat<Expression>(new SymbolExpression("x"), Count).ToArray();
    }

    [SexprObject]
    public partial class ExpressionDocument
    {
        [SexprTag("precondition")]
        public ExpressionCondition Precondition { get; set; } = new ExpressionAnd();
    }

    private sealed class StepResolver : ISexprFormatterResolver
    {
        public ISexprFormatter<T>? GetFormatter<T>() => typeof(T) == typeof(Step)
            ? (ISexprFormatter<T>)(object)new StepFormatter()
            : DefaultResolver.Instance.GetFormatter<T>();
    }

    private sealed class StepFormatter : ISexprFormatter<Step>
    {
        public Step Deserialize(in SNode node, SexprSerializerOptions options) => new(new IntegerExpression(7));

        public void Serialize(ref SexprEmitter emitter, Step value, SexprSerializerOptions options)
        {
            emitter.BeginList();
            emitter.WriteSymbol("step");
            emitter.WriteNumber(7);
            emitter.EndList();
        }
    }

    private sealed class ErrorResolver(bool located) : ISexprFormatterResolver, ISexprFormatter<SymbolExpression>
    {
        public SexprSerializerException? LastError { get; private set; }

        public ISexprFormatter<T>? GetFormatter<T>() => typeof(T) == typeof(SymbolExpression)
            ? (ISexprFormatter<T>)(object)this : DefaultResolver.Instance.GetFormatter<T>();

        public SymbolExpression Deserialize(in SNode node, SexprSerializerOptions options) => new(node.GetSymbol());

        public void Serialize(ref SexprEmitter emitter, SymbolExpression value, SexprSerializerOptions options)
        {
            using var document = SDocument.Parse("x");
            LastError = located ? new SexprSerializerException("bad", document.RootNode) : new SexprSerializerException("bad");
            throw LastError;
        }
    }

    private sealed class NumericResolver : ISexprFormatterResolver
    {
        public ISexprFormatter<T>? GetFormatter<T>()
        {
            if (typeof(T) == typeof(long)) return (ISexprFormatter<T>)(object)new LongFormatter();
            if (typeof(T) == typeof(double)) return (ISexprFormatter<T>)(object)new DoubleFormatter();
            return DefaultResolver.Instance.GetFormatter<T>();
        }
    }

    private sealed class LongFormatter : ISexprFormatter<long>
    {
        public long Deserialize(in SNode node, SexprSerializerOptions options) => node.GetInt64() + 10;
        public void Serialize(ref SexprEmitter emitter, long value, SexprSerializerOptions options) => emitter.WriteNumber(value + 10);
    }

    private sealed class DoubleFormatter : ISexprFormatter<double>
    {
        public double Deserialize(in SNode node, SexprSerializerOptions options) => node.GetDouble() + 10;
        public void Serialize(ref SexprEmitter emitter, double value, SexprSerializerOptions options) => emitter.WriteNumber(value + 10);
    }
}
