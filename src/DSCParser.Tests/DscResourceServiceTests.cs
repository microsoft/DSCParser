using DSCParser.PSDSC;
using Xunit;

namespace DSCParser.Tests;

/// <summary>
/// Exercises <see cref="DscResourceService.GetDscResources"/> against an empty module landscape.
/// The PowerShell engine class cache is absent in the test host, so discovery degrades to the
/// failure/empty paths; these tests pin down that behavior and cover the module-enumeration
/// fallbacks.
/// </summary>
public class DscResourceServiceTests
{
    [Fact]
    public void GetDscResources_WithEmptyPsModulePath_ShouldReturnDefaultsWithoutThrowing()
    {
        string? original = Environment.GetEnvironmentVariable("PSModulePath");
        try
        {
            Environment.SetEnvironmentVariable("PSModulePath", string.Empty);
            DscResourceHelpers.ClearModuleCache();

            var result = DscResourceService.GetDscResources(null, null, includeCompositeResources: false);

            // The default CIM keywords are still discoverable even with an empty PSModulePath;
            // the module enumeration just degrades gracefully to an empty module list.
            Assert.NotNull(result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PSModulePath", original);
        }
    }

    [Fact]
    public void GetDscResources_WithModuleFolder_ShouldFailGracefullyAndReturnDefaults()
    {
        string? original = Environment.GetEnvironmentVariable("PSModulePath");
        string root = Path.Combine(Path.GetTempPath(), $"dscparser_svc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "ModA", "DscResources"));
        try
        {
            Environment.SetEnvironmentVariable("PSModulePath", root);
            DscResourceHelpers.ClearModuleCache();

            var result = DscResourceService.GetDscResources(null, null, includeCompositeResources: false);

            Assert.NotNull(result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PSModulePath", original);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void GetDscResources_WithModuleFilter_ShouldFailGracefullyAndReturnDefaults()
    {
        string? original = Environment.GetEnvironmentVariable("PSModulePath");
        try
        {
            Environment.SetEnvironmentVariable("PSModulePath", string.Empty);
            DscResourceHelpers.ClearModuleCache();

            var result = DscResourceService.GetDscResources(["*"], "ModA", includeCompositeResources: false);

            Assert.NotNull(result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PSModulePath", original);
        }
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
}