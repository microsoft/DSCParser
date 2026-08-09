using System.Management.Automation;
using System.Management.Automation.Language;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using DSCParser.CSharp;
using DSCParser.PSDSC;

namespace DSCParser.Benchmarks;

/// <summary>
/// Measures the cost of converting a real exported configuration. The configuration path and the
/// module under test come from environment variables so the benchmark is not tied to one machine:
/// DSCPARSER_BENCH_CONFIG (required) and DSCPARSER_BENCH_MODULE (default Microsoft365DSC).
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Monitoring, warmupCount: 1, iterationCount: 5, invocationCount: 1)]
public class ConvertToDscObjectBenchmarks
{
    private string _content = string.Empty;
    private string _contentWithoutModuleVersion = string.Empty;
    private List<object> _resources = [];
    private DscParseOptions _options = new();

    [GlobalSetup]
    public void Setup()
    {
        string configPath = Environment.GetEnvironmentVariable("DSCPARSER_BENCH_CONFIG")
            ?? throw new InvalidOperationException("Set DSCPARSER_BENCH_CONFIG to an exported DSC configuration file.");

        _content = File.ReadAllText(configPath);
        // ConvertToDscObject strips -ModuleVersion before parsing. Without the same treatment the
        // bare parse benchmark measures a fast failure to resolve the pinned version instead.
        _contentWithoutModuleVersion = System.Text.RegularExpressions.Regex.Replace(
            _content,
            @"(import-dscresource\b[^\n]*?)\s+-moduleversion\s+(?:""[^""]*""|'[^']*'|\S+)([^\n]*)",
            "$1$2",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        _options = new DscParseOptions { IncludeComments = false, IncludeCIMInstanceInfo = true };
        _resources = [.. DscResourceService.GetDscResources().Cast<object>()];
    }

    /// <summary>Full conversion, resources supplied from the cache the module keeps.</summary>
    [Benchmark(Baseline = true, Description = "ConvertToDscObject (full)")]
    public int ConvertToDscObject()
    {
        DscParser.ClearCaches();
        return DscParser.ConvertToDscObject(null, _content, _options, _resources).Count;
    }

    /// <summary>
    /// The PowerShell parse alone. Isolates how much of the conversion is PowerShell re-resolving
    /// Import-DscResource rather than work this repository controls.
    /// </summary>
    [Benchmark(Description = "Parser.ParseInput only")]
    public int ParseInputOnly()
    {
        _ = Parser.ParseInput(_contentWithoutModuleVersion, out Token[] _, out ParseError[] errors);
        return errors.Length;
    }

    /// <summary>Resource discovery, which a host normally does once per process.</summary>
    [Benchmark(Description = "Get-DscResourceV2 (discovery)")]
    public int Discovery() => DscResourceService.GetDscResources().Count;
}

public static class Program
{
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--selftest")
        {
            SelfTest();
            return;
        }

        BenchmarkDotNet.Running.BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }

    /// <summary>
    /// Runs each benchmarked operation once outside the harness, reporting counts and failures.
    /// The harness only reports that an iteration failed, not why.
    /// </summary>
    private static void SelfTest()
    {
        DscResourceService.WarningSink = m => Console.WriteLine($"  [discovery warning] {m}");
        DscParser.WarningSink = m => Console.WriteLine($"  [parser warning] {m}");

        Console.WriteLine($"PSModulePath: {Environment.GetEnvironmentVariable("PSModulePath")}");

        var bench = new ConvertToDscObjectBenchmarks();
        try
        {
            bench.Setup();
            Console.WriteLine($"Discovery returned {bench.Discovery()} resources");
            Console.WriteLine($"ParseInput errors: {bench.ParseInputOnly()}");
            Console.WriteLine($"ConvertToDscObject instances: {bench.ConvertToDscObject()}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }
}
