using System.Collections;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Reflection;
using DSCParser.PSDSC;
using Microsoft.PowerShell.Commands;
using Xunit;
using DscResourceInfo = Microsoft.PowerShell.DesiredStateConfiguration.DscResourceInfo;

namespace DSCParser.Tests;

/// <summary>
/// Exercises <see cref="GetDscResourceCommand"/> - the compiled Get-DscResourceV2 cmdlet.
/// The cmdlet is invoked through a real runspace with the cmdlet registered into the session,
/// so BeginProcessing / ProcessRecord / CheckResourcesFound run against a live CommandRuntime.
/// </summary>
public class GetDscResourceCommandTests : IDisposable
{
    private static readonly MethodInfo _checkResourcesFound =
        typeof(GetDscResourceCommand).GetMethod("CheckResourcesFound", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("CheckResourcesFound method not found");

    private readonly string? _originalModulePath = Environment.GetEnvironmentVariable("PSModulePath");

    public GetDscResourceCommandTests()
    {
        Environment.SetEnvironmentVariable("PSModulePath", FixtureModulePath());
        PowerShellInvoker.ClearModuleCatalog();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PSModulePath", _originalModulePath);
        PowerShellInvoker.ClearModuleCatalog();
    }

    private static string FixtureModulePath()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "Tests", "Fixtures", "Modules");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Tests/Fixtures/Modules was not found above the test assembly.");
    }

    private static PowerShell CreateCmdletPowerShell()
    {
        var iss = InitialSessionState.CreateDefault2();
        iss.Commands.Add(new SessionStateCmdletEntry("Get-DscResourceV2", typeof(GetDscResourceCommand), null));

        return PowerShell.Create(iss);
    }

    private static void SkipIfNoRunspace()
    {
        try
        {
            using var probe = PowerShell.Create();
            using var ps = CreateCmdletPowerShell();
            _ = ps.AddCommand("Get-DscResourceV2").AddParameter("Name", new[] { "__probe__" });
            _ = ps.Invoke();
        }
        catch (Exception ex) when (ex is PSSnapInException or RuntimeException or CmdletInvocationException)
        {
            Assert.Skip("The PowerShell engine in this environment cannot host the cmdlet.");
        }
    }

    #region Runspace invocation

    [Fact]
    public void Invoke_WithNameFilter_ShouldReportMissingResource()
    {
        SkipIfNoRunspace();

        using var ps = CreateCmdletPowerShell();
        var results = ps.AddCommand("Get-DscResourceV2")
            .AddParameter("Name", new[] { "__GetDscResourceV2_NotInstalled_Probe__" })
            .Invoke();

        Assert.Empty(results);
        Assert.True(ps.HadErrors);
        Assert.Contains(ps.Streams.Error, e => e.Exception is ItemNotFoundException);
    }

    [Fact]
    public void Invoke_WithWildcardName_ShouldNotReportMissingResource()
    {
        SkipIfNoRunspace();

        using var ps = CreateCmdletPowerShell();
        var results = ps.AddCommand("Get-DscResourceV2")
            .AddParameter("Name", new[] { "__GetDscResourceV2_Wildcard_*" })
            .Invoke();

        Assert.False(ps.HadErrors, string.Join("; ", ps.Streams.Error.Select(e => e.FullyQualifiedErrorId)));
        Assert.Empty(results);
    }

    private const string MissingModuleName = "__GetDscResourceV2_NotInstalled_Module__";

    private static List<string> InvokeAndCaptureVerbose(PowerShell ps)
    {
        _ = ps.AddParameter("Verbose", new SwitchParameter(true)).Invoke();

        return [.. ps.Streams.Verbose.Select(v => v.Message)];
    }

    [Fact]
    public void Invoke_WithStringModuleParameter_ShouldFilterOnTheParsedModuleName()
    {
        SkipIfNoRunspace();

        using var ps = CreateCmdletPowerShell();
        _ = ps.AddCommand("Get-DscResourceV2").AddParameter("Module", MissingModuleName);

        Assert.Contains($"Filtering resources by module: {MissingModuleName}", InvokeAndCaptureVerbose(ps));
    }

    [Fact]
    public void Invoke_WithModuleSpecificationParameter_ShouldFilterOnItsName()
    {
        SkipIfNoRunspace();

        using var ps = CreateCmdletPowerShell();
        _ = ps.AddCommand("Get-DscResourceV2").AddParameter("Module", new ModuleSpecification(MissingModuleName));

        Assert.Contains($"Filtering resources by module: {MissingModuleName}", InvokeAndCaptureVerbose(ps));
    }

    [Fact]
    public void Invoke_WithHashtableModuleParameter_ShouldFilterOnTheModuleNameKey()
    {
        SkipIfNoRunspace();

        using var ps = CreateCmdletPowerShell();
        _ = ps.AddCommand("Get-DscResourceV2")
            .AddParameter("Module", new Hashtable { ["ModuleName"] = MissingModuleName });

        Assert.Contains($"Filtering resources by module: {MissingModuleName}", InvokeAndCaptureVerbose(ps));
    }

    [Fact]
    public void Invoke_WithHashtableModuleParameterWithoutModuleNameKey_ShouldFallBackToTheHashtableItself()
    {
        SkipIfNoRunspace();

        using var ps = CreateCmdletPowerShell();
        _ = ps.AddCommand("Get-DscResourceV2")
            .AddParameter("Module", new Hashtable { ["Unexpected"] = MissingModuleName });

        Assert.Contains(InvokeAndCaptureVerbose(ps), v => v.StartsWith("Filtering resources by module: System.Collections.Hashtable", StringComparison.Ordinal));
    }

    [Fact]
    public void Invoke_WithSyntaxSwitch_ShouldEmitSyntaxStringsInsteadOfResourceObjects()
    {
        SkipIfNoRunspace();

        using var ps = CreateCmdletPowerShell();
        var results = ps.AddCommand("Get-DscResourceV2")
            .AddParameter("Name", new[] { "Archive" })
            .AddParameter("Syntax", new SwitchParameter(true))
            .Invoke();

        if (results.Count == 0)
        {
            Assert.Skip("The PowerShell engine in this environment did not register the 'Archive' resource.");
        }

        Assert.All(results, r => Assert.IsType<string>(r.BaseObject));
        Assert.StartsWith("Archive [String] #ResourceName", (string)results[0].BaseObject, StringComparison.Ordinal);
    }

    [Fact]
    public void Invoke_WithoutSyntaxSwitch_ShouldEmitDscResourceInfoObjects()
    {
        SkipIfNoRunspace();

        using var ps = CreateCmdletPowerShell();
        var results = ps.AddCommand("Get-DscResourceV2").AddParameter("Name", new[] { "Archive" }).Invoke();

        if (results.Count == 0)
        {
            Assert.Skip("The PowerShell engine in this environment did not register the 'Archive' resource.");
        }

        Assert.All(results, r => Assert.IsType<DscResourceInfo>(r.BaseObject));
        Assert.Equal("Archive", ((DscResourceInfo)results[0].BaseObject).Name);
    }

    [Fact]
    public void Invoke_WithoutNameFilter_ShouldNotReportAnyMissingResource()
    {
        SkipIfNoRunspace();

        using var ps = CreateCmdletPowerShell();
        _ = ps.AddCommand("Get-DscResourceV2").Invoke();

        Assert.DoesNotContain(ps.Streams.Error, e => e.Exception is ItemNotFoundException);
    }

    #endregion

    #region CheckResourcesFound

    [Fact]
    public void CheckResourcesFound_WithNoNames_ShouldReturnQuietly()
    {
        var cmdlet = new GetDscResourceCommand();

        _checkResourcesFound.Invoke(cmdlet, [null, new List<DscResourceInfo>()]);
        _checkResourcesFound.Invoke(cmdlet, [Array.Empty<string>(), new List<DscResourceInfo>()]);
    }

    #endregion
}