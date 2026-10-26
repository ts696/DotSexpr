param([string]$BaselineRef = '6763030')
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/../..").Path
$work = "$PSScriptRoot/obj/variants"
$variants = @('baseline', 'direct', 'in', 'ref-readonly')
New-Item -ItemType Directory -Force -Path $work | Out-Null
foreach ($variant in $variants) {
    $dir = "$work/$variant"
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    Get-ChildItem "$repo/src/DotSexpr" -Recurse -Filter '*.cs' |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } | ForEach-Object {
        $relative = [IO.Path]::GetRelativePath("$repo/src/DotSexpr", $_.FullName)
        $target = Join-Path $dir $relative
        New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $target
    }
    # Pin the pre-optimization document so baseline stays meaningful after merging.
    $baselineDocument = git -C $repo show "${BaselineRef}:src/DotSexpr/SDocument.cs"
    if ($LASTEXITCODE) { throw "Cannot read baseline document: $BaselineRef" }
    $document = $baselineDocument -join "`n"
    $token = Get-Content "$dir/SToken.cs" -Raw
    $node = Get-Content "$dir/SNode.cs" -Raw
    if ($variant -ne 'baseline') {
        $document = $document.Replace('_tokens[id] = _tokens[id] with { Length = length };', '_tokens[id].Length = length;')
        $document = $document.Replace('_tokens[id] = _tokens[id] with { Count = count };', '_tokens[id].Count = count;')
        $document = $document.Replace('_tokens[id] = _tokens[id] with { EndIndex = endIndex };', '_tokens[id].EndIndex = endIndex;')
        # Preserve the existing disposed check and Count semantics to isolate copy costs.
        $document = $document -replace 'var token = GetToken\(id\);\s+SetCount\(id, token.Count \+ 1\);',
            "ThrowIfDisposed();`n        ref var token = ref _tokens[id];`n        token.Count++;"
    }
    if ($variant -in @('in', 'ref-readonly')) {
        $token = $token.Replace('public bool ', 'public readonly bool ').Replace('public int Offset', 'public readonly int Offset').Replace('get =>', 'readonly get =>')
        $document = $document.Replace('(SToken token', '(in SToken token')
    }
    if ($variant -eq 'ref-readonly') {
        $document = $document.Replace('internal SToken GetToken(int id)', 'internal ref readonly SToken GetToken(int id)').Replace('return _tokens[id];', 'return ref _tokens[id];')
        $document = $document.Replace('var token = GetToken(id);', 'ref readonly var token = ref GetToken(id);').Replace('var current = GetToken(currentId);', 'ref readonly var current = ref GetToken(currentId);')
        $node = $node.Replace('public SToken Token => Document.GetToken(Id);', 'public ref readonly SToken Token => ref Document.GetToken(Id);').Replace('var token = _node.Document.GetToken(_current);', 'ref readonly var token = ref _node.Document.GetToken(_current);')
    }
    Set-Content "$dir/SDocument.cs" $document
    Set-Content "$dir/SToken.cs" $token
    Set-Content "$dir/SNode.cs" $node
    '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><AssemblyName>DotSexpr</AssemblyName><Nullable>enable</Nullable><LangVersion>12</LangVersion></PropertyGroup></Project>' | Set-Content "$dir/Library.csproj"
    dotnet build "$dir/Library.csproj" -c Release -m:1 -p:UseSharedCompilation=false -v quiet --ignore-failed-sources
    if ($LASTEXITCODE) { throw "Library build failed: $variant" }
    dotnet build "$PSScriptRoot/TokenBenchmarks.csproj" -c Release -m:1 -p:UseSharedCompilation=false "-p:VariantDll=$dir/bin/Release/net10.0/DotSexpr.dll" "-p:OutputPath=$dir/runner/" -v quiet --ignore-failed-sources
    if ($LASTEXITCODE) { throw "Runner build failed: $variant" }
}
$envNames = @('DOTNET_TieredCompilation', 'DOTNET_ReadyToRun', 'DOTNET_JitDisasm', 'DOTNET_JitStdOutFile')
$previousEnv = @{}
foreach ($name in $envNames) { $previousEnv[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
$env:DOTNET_TieredCompilation = '0'
$env:DOTNET_ReadyToRun = '0'
$env:DOTNET_JitDisasm = $null
$env:DOTNET_JitStdOutFile = $null
try {
    'variant,round,case,sample,ns_per_op,bytes_per_op' | Set-Content "$PSScriptRoot/results.csv"
    # Rotate execution order so warm-up and machine drift do not always favor one variant.
    for ($round = 0; $round -lt 4; $round++) {
        for ($i = 0; $i -lt $variants.Length; $i++) {
            $variant = $variants[($i + $round) % $variants.Length]
            Write-Host "Measuring $variant, round $round"
            dotnet "$work/$variant/runner/TokenBenchmarks.dll" $variant $round | Add-Content "$PSScriptRoot/results.csv"
            if ($LASTEXITCODE) { throw "Benchmark failed: $variant" }
        }
    }
    foreach ($variant in $variants) {
        $env:DOTNET_JitDisasm = 'DotSexpr.SDocument:IncrementCount DotSexpr.SNode+Enumerator:MoveNext DotSexpr.SDocument:GetInt32'
        $env:DOTNET_JitStdOutFile = "$work/$variant/asm.txt"
        if (Test-Path $env:DOTNET_JitStdOutFile) { Remove-Item -LiteralPath $env:DOTNET_JitStdOutFile }
        dotnet "$work/$variant/runner/TokenBenchmarks.dll" $variant asm | Out-Null
    }
} finally {
    foreach ($name in $envNames) { [Environment]::SetEnvironmentVariable($name, $previousEnv[$name], 'Process') }
}
