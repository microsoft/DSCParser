# DSCParser tests

The suite has two halves that the same harness runs and reports on:

| Half | Location | Framework | Coverage format |
| :--- | :--- | :--- | :--- |
| C# | `src/DSCParser.Tests` | xUnit v3 on Microsoft.Testing.Platform | Cobertura (`TestResults/coverage.cobertura.xml`) |
| PowerShell | `Tests/Unit/DSCParser` | Pester 5 | JaCoCo (`CodeCov.xml`) |

## Running

```powershell
Import-Module ./Tests/TestHarness.psm1 -Force

$results = Invoke-TestHarness
Write-TestHarnessSummary -Result $results
```

`Invoke-TestHarness` builds the solution, stages the PowerShell module into `DSCParser/`, runs both
suites with coverage, prints a combined summary and returns the results. It never throws on a test
failure — the caller decides, which is what lets CI turn the same object into a step summary before
failing the build.

Useful switches:

```powershell
Invoke-TestHarness -SkipBuild                     # reuse the current build output
Invoke-TestHarness -SkipDotNetTests               # PowerShell only
Invoke-TestHarness -SkipPesterTests               # C# only
Invoke-TestHarness -IgnoreCodeCoverage            # faster, no coverage collection
Invoke-TestHarness -TestResultsFile results.xml   # also write NUnit XML for the Pester run
Invoke-TestHarness -DscTestsPath ./Tests/Unit/DSCParser/RoundTrip.Tests.ps1
```

Coverage is reported, never enforced. Only failing tests break the build.

## Fixtures

`Tests/Fixtures/Modules` holds purpose-built DSC modules that the harness prepends to
`PSModulePath`, so the PowerShell tests exercise real discovery rather than injected objects.

| Module | Shape | Why it exists |
| :--- | :--- | :--- |
| `DscParserTest.MofResources` | MOF schemas under `DscResources/<class>/` | `ScriptBased` discovery, ValueMaps, embedded CIM instances |
| `DscParserTest.ClassResources` | `[DscResource()]` classes in a `1.2.0` subfolder | `ClassBased` discovery, versioned layout, and the helper class that must *not* be reported as a resource |
| `DscParserTest.MultiVersion` | The same resource in `1.0.0` and `2.0.0` | Side-by-side version resolution |
| `DscParserTest.Composite` | A `.schema.psm1` configuration | Pins that composites are not autoloaded, so `Get-DscResourceV2` cannot return them |

Two rules the PowerShell engine imposes on these fixtures, both learned the hard way:

- A MOF resource folder and schema file must be named after the **class**, not the friendly name.
  `MSFT_Foo.schema.mof` declaring `class MSFT_Foo` with `FriendlyName("Foo")`.
- A manifest must not declare `FunctionsToExport`, `CmdletsToExport`, `VariablesToExport` **and**
  `AliasesToExport` as empty. With all four empty, PowerShell skips module analysis and
  `Get-Module -ListAvailable` reports no `ExportedDscResources`, which silently disables class-based
  discovery.

`Tests/Fixtures/Configurations` holds configuration files on disk for the `-Path` parameter set.

## Conventions

- Pester files are `*.Tests.ps1` under `Tests/Unit`, discovered recursively.
- No comments in tests. `Describe` / `Context` / `It` and the xUnit method names carry the meaning.
- Both `DscParser` and the engine DSC caches are process-wide, so every Pester file restores
  `PSModulePath` and calls `[DSCParser.CSharp.DscParser]::ClearCaches()` in `AfterAll`, and the C#
  suite keeps parallelization disabled (`AssemblyInfo.cs`).

## Known limits

- `Initialize-DscParserAssembly`'s failure paths are covered by `ModuleImport.Tests.ps1` through a
  child `pwsh` process, which Pester cannot instrument — those lines show as uncovered.
- The test project references `System.Management.Automation` rather than `Microsoft.PowerShell.SDK`,
  so `PowerShell.Create()` cannot open a full runspace. `DscResourceService.GetModuleList` /
  `GetConfigurations` and `DscKeywordRegistry.ResolveModules` therefore assert their documented
  degradation contracts instead of happy paths.
