# DSCParser.Benchmarks

Timing, allocation numbers and CPU profiles for `ConvertTo-DSCObject`, for checking that a refactor
actually moves the needle.

**This project is deliberately not part of `DSCParser.sln`.** It references
`Microsoft.PowerShell.SDK`, which is a large restore and drags in transitive packages that raise
`NU1903` advisories, and none of that belongs in the CI build of the shipping module. Build it by
path when you need it. `Utilities\Build.ps1` skips it for the same reason.

## Measuring a change

Work from the cheapest tool upwards. Most questions are answered before the profiler.

### 1. Phase timing

Attributes a conversion to a phase - module import, discovery, the C# call, the hashtable
projection - without building anything.

```powershell
pwsh -NoProfile -File src\DSCParser.Benchmarks\Tools\Measure-ConvertToDscObject.ps1 `
    -ConfigPath 'D:\testbed\M365TenantConfig.ps1'
```

It reports the first conversion and the steady state separately. They differ a lot: the first call
in a process registers the DSC keywords of every imported module, later calls reuse them.

Run `Utilities\Build.ps1` first, or it will measure the previously staged assemblies.

### 2. Benchmarks

```powershell
$env:DSCPARSER_BENCH_CONFIG = 'D:\testbed\M365TenantConfig.ps1'   # required

dotnet run --project src\DSCParser.Benchmarks -c Release -- --filter '*'
```

Use `--selftest` first whenever a benchmark reports a failed iteration. BenchmarkDotNet only
reports *that* an iteration failed; this runs each operation once outside the harness and prints
resource counts, instance counts and the exception.

```powershell
dotnet run --project src\DSCParser.Benchmarks -c Release -- --selftest
```

Two things about this project are deliberate:

- It references `Microsoft.PowerShell.SDK`, not `System.Management.Automation`. Discovery invokes
  `Get-Module` and `Get-Command`, which need the shipped command modules. With the engine package
  alone every discovery call fails and returns a near-empty resource list, and the benchmark still
  reports a plausible-looking number.
- `RunStrategy.Monitoring` with `invocationCount: 1`. A conversion takes seconds and mutates
  process-wide DSC caches, so the default of many invocations per iteration measures warm-cache
  behaviour rather than a conversion.

### 3. CPU profile

When the totals say a phase got slower but not why:

```powershell
dotnet tool install -g dotnet-trace

dotnet-trace collect --format Speedscope --buffersize 512 `
    -- pwsh -NoProfile -File <script-that-converts-one-config>.ps1
```

Keep the traced script minimal: import the module, discover, convert once. Then either open the
`.speedscope.json` at <https://speedscope.app>, or aggregate it here:

```powershell
pwsh -NoProfile -File src\DSCParser.Benchmarks\Tools\Show-SpeedscopeCallTree.ps1 `
    -Path .\pwsh_*.speedscope.json -RootPattern 'DscParser\.ConvertToDscObject' -MinMs 100
```

Do not use `dotnet-trace report topN`. It sums every thread, so a PowerShell host profile comes out
as ~80% idle worker threads blocked on wait handles, and the pipeline thread doing the real work is
invisible. `Show-SpeedscopeCallTree.ps1` picks the busiest thread and gives inclusive and self time
per frame.

## Traps worth knowing

These cost real time to rediscover.

- **Parsing anything named `Import-DSCResource` resolves modules from disk.** Even a single
  statement parsed on its own, with no configuration around it, costs seconds against a module the
  size of Microsoft365DSC. `DscParser.GetModulesToLoad` re-parses the statement under a placeholder
  command name for exactly this reason. If a change there suddenly costs seconds, this is why.
- **Registered DSC keywords are process-wide and survive a parse.** A benchmark or script that
  measures a second conversion is measuring a warm keyword table. `DscParser.ClearCaches()` resets
  it.
- **Registering keywords changes how the text after a configuration block parses.** An exported
  configuration ends with an invocation line that starts reporting `UnexpectedToken` once keywords
  are registered. `DscParser.ReportParseErrors` only treats errors inside the configuration block as
  fatal because of this.
- **A pinned `-ModuleVersion` that is not installed fails fast.** A parse benchmark that skips
  `RemoveModuleVersionInfo` will look ~7x faster than the real path while producing hundreds of
  errors. Compare error counts, not just times.

## Verifying a change did not alter output

Timing work is worth nothing if the conversion changed. Build the baseline in a worktree, convert
the same corpus with both, and compare hashes:

```powershell
git worktree add $env:TEMP\dscparser-head HEAD
pwsh -NoProfile -File $env:TEMP\dscparser-head\Utilities\Build.ps1 -RepositoryRoot $env:TEMP\dscparser-head
# convert the corpus with each module, then Get-FileHash the rendered output of both
git worktree remove $env:TEMP\dscparser-head --force
```
