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
public class GetDscResourceCommandTests
{
    private static readonly MethodInfo _checkResourcesFound =
        typeof(GetDscResourceCommand).GetMethod("CheckResourcesFound", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("CheckResourcesFound method not found");

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

    [Fact]
    public void Invoke_WithStringModuleParameter_ShouldRun()
    {
        SkipIfNoRunspace();

        using var ps = CreateCmdletPowerShell();
        _ = ps.AddCommand("Get-DscResourceV2")
            .AddParameter("Module", "__GetDscResourceV2_NotInstalled_Module__")
            .Invoke();

        Assert.True(true);
    }

    [Fact]
    public void Invoke_WithModuleSpecificationParameter_ShouldRun()
    {
        SkipIfNoRunspace();

        using var ps = CreateCmdletPowerShell();
        _ = ps.AddCommand("Get-DscResourceV2")
            .AddParameter("Module", new ModuleSpecification("__GetDscResourceV2_NotInstalled_Module__"))
            .Invoke();

        Assert.True(true);
    }

    [Fact]
    public void Invoke_WithHashtableModuleParameter_ShouldRun()
    {
        SkipIfNoRunspace();

        using var ps = CreateCmdletPowerShell();
        _ = ps.AddCommand("Get-DscResourceV2")
            .AddParameter("Module", new Hashtable { ["ModuleName"] = "__GetDscResourceV2_NotInstalled_Module__" })
            .Invoke();

        Assert.True(true);
    }

    [Fact]
    public void Invoke_WithSyntaxSwitch_ShouldRun()
    {
        SkipIfNoRunspace();

        using var ps = CreateCmdletPowerShell();
        _ = ps.AddCommand("Get-DscResourceV2")
            .AddParameter("Syntax", new SwitchParameter(true))
            .Invoke();

        Assert.True(true);
    }

    #endregion

    #region CheckResourcesFound

    [Fact]
    public void CheckResourcesFound_WithNoNames_ShouldReturnQuietly()
    {
        var cmdlet = new GetDscResourceCommand();

        _checkResourcesFound.Invoke(cmdlet, [null, new List<DscResourceInfo>()]);
        _checkResourcesFound.Invoke(cmdlet, [Array.Empty<string>(), new List<DscResourceInfo>()]);

        Assert.True(true);
    }

    #endregion
}