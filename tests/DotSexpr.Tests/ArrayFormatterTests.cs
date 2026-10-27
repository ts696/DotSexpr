using DotSexpr.Serializer;
using DotSexpr.Serializer.Formatters;
using DotSexpr.Serializer.Resolvers;

namespace DotSexpr.Tests;

public class ArrayFormatterTests
{
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(3)]
    public void Arrays_ResolveElementsOncePerOperation_AndRoundTrip(int length)
    {
        var resolver = new CountingResolver();
        var options = new SexprSerializerOptions { Resolver = resolver };
        var values = Enumerable.Range(1, length).ToArray();

        var text = SexprSerializer.Serialize(values, options);
        Assert.That(resolver.IntLookups, Is.EqualTo(length == 0 ? 0 : 1));
        Assert.That(SexprSerializer.Deserialize<int[]>(text, options), Is.EqualTo(values));
        Assert.That(resolver.IntLookups, Is.EqualTo(length == 0 ? 0 : 2));
    }

    [Test]
    public void EmptyArrays_DoNotRequireAnElementFormatter()
    {
        var resolver = new CountingResolver { MissingInt = true };
        var options = new SexprSerializerOptions { Resolver = resolver };

        Assert.That(SexprSerializer.Serialize(Array.Empty<int>(), options), Is.EqualTo("()"));
        Assert.That(SexprSerializer.Deserialize<int[]>("()", options), Is.Empty);
        Assert.That(resolver.IntLookups, Is.Zero);
    }

    [Test]
    public void MissingElementFormatter_ReportsFirstElementPathAndInputPosition()
    {
        var options = new SexprSerializerOptions { Resolver = new CountingResolver { MissingInt = true } };

        var writeError = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize(new[] { 1 }, options))!;
        Assert.That(writeError.Path, Is.EqualTo("$[0]"));
        var readError = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Deserialize<int[]>("(1)", options))!;
        Assert.That(readError.Path, Is.EqualTo("$[0]"));
        Assert.That(readError.BytePosition, Is.EqualTo(1));
    }

    [TestCase(0)]
    [TestCase(1)]
    public void NullElement_PreservesPath_AndDoesNotResolveBeforeFirstNull(int index)
    {
        var resolver = new CountingResolver();
        var options = new SexprSerializerOptions { Resolver = resolver };
        var values = index == 0 ? new string[] { null! } : new[] { "ok", null! };

        var error = Assert.Throws<SexprSerializerException>(() => SexprSerializer.Serialize(values, options))!;

        Assert.That(error.Path, Is.EqualTo($"$[{index}]"));
        Assert.That(error.Message, Does.Contain("Null values"));
        Assert.That(resolver.StringLookups, Is.EqualTo(index));
    }

    [Test]
    public void NestedArrays_ResolveElementFormattersSeparatelyForEachArray()
    {
        var resolver = new CountingResolver();
        var options = new SexprSerializerOptions { Resolver = resolver };
        var values = new[] { new[] { 1, 2 }, new[] { 3, 4 } };

        var text = SexprSerializer.Serialize(values, options);
        Assert.That(text, Is.EqualTo("((1 2) (3 4))"));
        Assert.That(resolver.IntLookups, Is.EqualTo(2));
        Assert.That(SexprSerializer.Deserialize<int[][]>(text, options), Is.EqualTo(values));
        Assert.That(resolver.IntLookups, Is.EqualTo(4));
    }

    private sealed class CountingResolver : ISexprFormatterResolver
    {
        public int IntLookups { get; private set; }
        public int StringLookups { get; private set; }
        public bool MissingInt { get; init; }

        public ISexprFormatter<T>? GetFormatter<T>()
        {
            if (typeof(T) == typeof(int))
            {
                IntLookups++;
                if (MissingInt) return null;
            }
            if (typeof(T) == typeof(string)) StringLookups++;
            if (typeof(T) == typeof(int[][]))
                return (ISexprFormatter<T>)(object)new ArrayFormatter<int[]>();
            return BuiltinResolver.Instance.GetFormatter<T>();
        }
    }
}
