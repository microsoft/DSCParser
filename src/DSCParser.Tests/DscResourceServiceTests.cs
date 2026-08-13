using DSCParser.PSDSC;
using Xunit;
using DscResourceInfo = Microsoft.PowerShell.DesiredStateConfiguration.DscResourceInfo;

namespace DSCParser.Tests;

/// <summary>
/// Module enumeration and configuration lookup both open a fresh runspace, which the test host
/// cannot provide, so those paths assert the documented degradation contract rather than a happy
/// path.
/// </summary>
public class DscResourceServiceTests
{
    private static List<DscResourceInfo> Discover(string[]? names = null, string? moduleName = null, bool includeComposites = false)
    {
        try
        {
            return DscResourceService.GetDscResources(names, moduleName, includeComposites);
        }
        finally
        {
            DscKeywordRegistry.Reset();
        }
    }

    private static List<DscResourceInfo> RequireEngineResources()
    {
        List<DscResourceInfo> resources = Discover();

        if (resources.Count == 0)
        {
            Assert.Skip("The PowerShell engine in this environment registered no default DSC resources.");
        }

        return resources;
    }

    [Fact]
    public void GetDscResources_WithDefaultKeywords_ShouldReturnTheEngineResources()
    {
        List<DscResourceInfo> resources = RequireEngineResources();

        Assert.Contains(resources, r => r.Name == "Archive");
        Assert.Contains(resources, r => r.Name == "File");
    }

    [Fact]
    public void GetDscResources_ShouldSortByModuleNameThenResourceName()
    {
        List<DscResourceInfo> resources = RequireEngineResources();

        Assert.Equal(
            resources.Select(r => (r.ModuleName ?? string.Empty, r.Name ?? string.Empty)),
            resources.Select(r => (r.ModuleName ?? string.Empty, r.Name ?? string.Empty))
                .OrderBy(r => r.Item1, StringComparer.CurrentCulture)
                .ThenBy(r => r.Item2, StringComparer.CurrentCulture));
    }

    [Fact]
    public void GetDscResources_ShouldNotReturnDuplicateModuleAndNameCombinations()
    {
        List<DscResourceInfo> resources = RequireEngineResources();

        Assert.Distinct(resources.Select(r => (r.ModuleName ?? string.Empty, r.Name ?? string.Empty)));
    }

    [Fact]
    public void GetDscResources_WithNameFilter_ShouldReturnOnlyMatchingResources()
    {
        _ = RequireEngineResources();

        List<DscResourceInfo> resources = Discover(["Arch*"]);

        Assert.NotEmpty(resources);
        Assert.All(resources, r => Assert.StartsWith("Arch", r.Name, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetDscResources_WithHiddenResourceName_ShouldReturnNothing()
    {
        _ = RequireEngineResources();

        Assert.Empty(Discover(["MSFT_Credential"]));
    }

    [Fact]
    public void GetDscResources_WithUnknownName_ShouldReturnNothing()
    {
        Assert.Empty(Discover(["__DscParserNoSuchResource__"]));
    }

    [Fact]
    public void GetDscResources_WithModuleFilter_ShouldWarnThatModuleEnumerationFailed()
    {
        var warnings = new List<string>();
        DscResourceService.WarningSink = warnings.Add;
        try
        {
            _ = Discover(["*"], "__DscParserNoSuchModule__");
        }
        finally
        {
            DscResourceService.WarningSink = null;
        }

        Assert.Contains(warnings, w => w.Contains("Failed to enumerate modules.", StringComparison.Ordinal));
    }

    [Fact]
    public void GetDscResources_WithCompositeResourcesRequested_ShouldWarnThatConfigurationLookupFailed()
    {
        var warnings = new List<string>();
        DscResourceService.WarningSink = warnings.Add;
        try
        {
            _ = Discover(includeComposites: true);
        }
        finally
        {
            DscResourceService.WarningSink = null;
        }

        Assert.Contains(warnings, w => w.Contains("Failed to get commands by command type 'Configuration'.", StringComparison.Ordinal));
    }

    [Fact]
    public void GetDscResources_WithoutCompositeResources_ShouldNotWarnAboutConfigurations()
    {
        var warnings = new List<string>();
        DscResourceService.WarningSink = warnings.Add;
        try
        {
            _ = Discover(includeComposites: false);
        }
        finally
        {
            DscResourceService.WarningSink = null;
        }

        Assert.DoesNotContain(warnings, w => w.Contains("command type 'Configuration'", StringComparison.Ordinal));
    }

    [Fact]
    public void GetDscResources_WithEmptyPsModulePath_ShouldStillReturnTheEngineResources()
    {
        string? original = Environment.GetEnvironmentVariable("PSModulePath");
        try
        {
            Environment.SetEnvironmentVariable("PSModulePath", string.Empty);
            DscResourceHelpers.ClearModuleCache();

            Assert.NotEmpty(RequireEngineResources());
        }
        finally
        {
            Environment.SetEnvironmentVariable("PSModulePath", original);
            DscResourceHelpers.ClearModuleCache();
        }
    }

    [Fact]
    public void GetResourceSyntax_ForADiscoveredResource_ShouldBracketOnlyTheOptionalProperties()
    {
        DscResourceInfo archive = RequireEngineResources().Single(r => r.Name == "Archive");

        string syntax = DscResourceService.GetResourceSyntax(archive);

        Assert.StartsWith("Archive [String] #ResourceName", syntax, StringComparison.Ordinal);
        Assert.Contains("    Path = [string]", syntax, StringComparison.Ordinal);
        Assert.Contains("    Destination = [string]", syntax, StringComparison.Ordinal);
        Assert.Contains("[Force = [bool]]", syntax, StringComparison.Ordinal);
        Assert.Contains("[Ensure = [string]{ Absent | Present }]", syntax, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportWarning_WithSink_ShouldInvoke()
    {
        string? captured = null;
        DscResourceService.WarningSink = message => captured = message;
        try
        {
            DscResourceService.ReportWarning("test warning");

            Assert.Equal("test warning", captured);
        }
        finally
        {
            DscResourceService.WarningSink = null;
        }
    }

    [Fact]
    public void ReportWarning_WithoutSink_ShouldNotThrow()
    {
        DscResourceService.WarningSink = null;

        DscResourceService.ReportWarning("dropped warning");
    }
}
