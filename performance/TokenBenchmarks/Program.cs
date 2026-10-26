using System.Diagnostics;
using System.Globalization;
using System.Text;
using DotSexpr;
using DotSexpr.Serializer;

var variant = args[0];
var round = args[1];
var flat = "(" + string.Join(" ", Enumerable.Range(0, 256)) + ")";
var nested = "(root " + string.Join(" ", Enumerable.Repeat("(move alpha 123 (jump beta 4.5))", 32)) + ")";
var bytes = Encoding.UTF8.GetBytes(nested);
using var numbers = SDocument.Parse(flat);
using var symbols = SDocument.Parse("(" + string.Join(" ", Enumerable.Repeat("alpha", 256)) + ")");
using var strings = SDocument.Parse("(" + string.Join(" ", Enumerable.Repeat("\"alpha\"", 256)) + ")");
var cases = new (string Name, Func<long> Run)[]
{
    ("ParseUtf8Nested", () => { using var doc = SDocument.Parse(bytes); return doc.RootNode.Count; }),
    ("ParseStringFlat", () => { using var doc = SDocument.Parse(flat); return doc.RootNode.Count; }),
    ("EnumerateInt32", () => { long sum = 0; foreach (var node in numbers.RootNode) sum += node.GetInt32(); return sum; }),
    ("SymbolEquals", () => { long sum = 0; foreach (var node in symbols.RootNode) if (node.ValueEquals("alpha"u8)) sum++; return sum; }),
    ("ReadStrings", () => { long sum = 0; foreach (var node in strings.RootNode) sum += node.GetString().Length; return sum; }),
    ("DeserializeExpression", () => ((CallExpression)SexprSerializer.Deserialize<Expression>(nested)).Arguments.Count),
};
long sink = 0;
var expected = new long[] { 33, 256, 32640, 256, 1280, 32 };
for (var i = 0; i < cases.Length; i++)
    if (cases[i].Run() != expected[i]) throw new InvalidOperationException($"Unexpected result: {cases[i].Name}");
if (args.Length > 2 && args[2] == "--validate")
{
    Console.WriteLine($"{variant}: all {cases.Length} correctness checks passed");
    return;
}
foreach (var item in cases)
{
    var count = 1;
    while (true)
    {
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < count; i++) sink ^= item.Run();
        if (watch.Elapsed.TotalMilliseconds >= 40) break;
        count *= 2;
    }
    for (var sample = 0; sample < 7; sample++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        for (var i = 0; i < count; i++) sink ^= item.Run();
        var elapsed = Stopwatch.GetElapsedTime(start).TotalNanoseconds / count;
        allocated = (GC.GetAllocatedBytesForCurrentThread() - allocated) / count;
        Console.WriteLine(string.Join(",", variant, round, item.Name, sample,
            elapsed.ToString("F3", CultureInfo.InvariantCulture), allocated));
    }
}
GC.KeepAlive(sink);
