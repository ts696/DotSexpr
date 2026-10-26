# Token passing experiment

Run `./performance/TokenBenchmarks/run.ps1` from PowerShell with the .NET 10 SDK.
No benchmark packages are required. Production sources are copied into ignored
`obj/variants/` folders and transformed there. SDocument is read from the
pre-optimization commit `6763030` (override with `-BaselineRef`) so the baseline
remains unchanged after the optimization is merged. Other library sources use
the working tree; this experiment assumes they match the original comparison.
Each variant has a separately compiled runner, including for API signature changes.

Variants are cumulative:

- `baseline`: unchanged source.
- `direct`: replace token `with` assignments with array element property assignments;
  increment Count through a ref local. Preserve the existing disposed check and
  property semantics.
- `in`: direct updates plus readonly token getters and `in SToken` parameters on
  SDocument's reading methods. GetToken and SNode.Token still return values.
- `ref-readonly`: additionally return readonly references from GetToken and
  SNode.Token, using ref locals in child traversal. This changes the public API
  and exposes references into pooled storage; it is experimental, not a proposed
  public API change.

## Results, 2026-10-26

Windows x64, Intel Core Ultra 7 265, SDK 10.0.302, runtime 10.0.10, Release.
Tiered compilation and ReadyToRun are disabled to compare fully optimized JIT
code without promotion timing. This does not measure default dynamic PGO behavior
or Unity/Mono/IL2CPP.

Each case calibrates its batch to at least 40 ms before measurement, then records
7 batches per process. Four rounds rotate variant order, for 28 samples per cell.
The table shows the median nanoseconds per operation (lower is better).
All variants perform six deterministic result checks before measurement.
GC runs before each measured batch, outside the timed interval; allocations and
any collections during the batch remain part of the measured workload.
This is a small custom benchmark, not a BenchmarkDotNet statistical analysis;
small differences should not be treated as established improvements.

| Operation | Baseline | Direct | + readonly/in | + ref readonly |
|---|---:|---:|---:|---:|
| Parse UTF-8 nested | 3252.8 | 1614.7 | 1644.5 | 1657.1 |
| Parse string flat | 3120.0 | 2011.8 | 2013.8 | 2056.5 |
| Enumerate + GetInt32 | 4697.3 | 4640.7 | 4664.6 | 4649.2 |
| Symbol ValueEquals | 1135.6 | 1141.7 | 1133.9 | 1003.8 |
| Read strings | 3612.2 | 3722.3 | 3762.9 | 3414.3 |
| Deserialize Expression | 9261.4 | 7965.7 | 7763.8 | 7619.4 |

Inputs: 256 integer elements for flat parsing and numeric traversal; 32 nested
`(move alpha 123 (jump beta 4.5))` calls inside `(root ...)` for nested parsing and
expression deserialization; 256 `alpha` symbols or quoted strings for reading.
Reading cases reuse an existing document; parsing and deserialization include
document construction and disposal. Allocation counts are identical across all
variants (respectively 1136, 992, 0, 0, 8192, and 13512 bytes per operation).

Direct updates reduce these parsing times by 50.4% and 35.5%, and expression
deserialization by 14.0%. Adding in parameters does not provide a general benefit
here; string reads take 4.2% longer than baseline. Ref readonly improves symbol
comparison by 11.6% against baseline but gives back some parsing improvement.

## Assembly evidence

The script captures IncrementCount, Enumerator.MoveNext, and GetInt32 in
`obj/variants/<variant>/asm.txt`. IncrementCount goes from 163 bytes to 120 bytes
with direct updates. The baseline contains four `vmovdqu` instructions transferring
the token through stack storage; the direct version has none. MoveNext remains
239 bytes and GetInt32 remains 409 bytes between baseline and direct, consistent
with the unchanged numeric traversal results.

Raw measurements from the final rerun, including explicit result assertions,
are in `results.csv`. The production library now uses
the direct field/property updates. A blanket change to ref/in is not supported
by this experiment.
