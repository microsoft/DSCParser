using DSCParser.PSDSC;
using Xunit;

namespace DSCParser.Tests;

public class DscKeywordRegistryTests
{
    [Fact]
    public void EnsureRegistered_WithNullOrEmptyName_ShouldReturnFalse()
    {
        Assert.False(DscKeywordRegistry.EnsureRegistered(null!, null));
        Assert.False(DscKeywordRegistry.EnsureRegistered(string.Empty, null));
    }

    [Fact]
    public void EnsureRegistered_WithNoInstalledModule_ShouldReturnFalse()
    {
        try
        {
            DscKeywordRegistry.Reset();

            Assert.False(DscKeywordRegistry.EnsureRegistered("__DscParserNoSuchModule__", null));
        }
        finally
        {
            DscKeywordRegistry.Reset();
        }
    }

    [Fact]
    public void ImportModules_WithNull_ShouldBeNoOp()
    {
        try
        {
            DscKeywordRegistry.Reset();

            DscKeywordRegistry.ImportModules(null!);

            Assert.Empty(DscClassCacheReflection.GetCachedKeywords() ?? []);
        }
        finally
        {
            DscKeywordRegistry.Reset();
        }
    }

    [Fact]
    public void GetKeywordSnapshot_ShouldReuseTheListUntilTheClassCacheChanges()
    {
        if (!DscClassCacheReflection.IsDscClassCacheAvailable)
        {
            Assert.Skip("The PowerShell engine in this environment does not expose the DscClassCache type.");
        }

        using var module = TestDscModule.CreateMofResource(
            "DscParserSnapshotModule",
            "MSFT_DscParserSnapshotResource",
            """
            [ClassVersion("1.0.0.0"), FriendlyName("DscParserSnapshotResource")]
            class MSFT_DscParserSnapshotResource : OMI_BaseResource
            {
                [Key] String Identity;
            };
            """,
            new Version("1.0.0.0"));

        try
        {
            DscKeywordRegistry.Reset();
            DscKeywordRegistry.EnsureDefaultKeywordsLoaded();

            var first = DscKeywordRegistry.GetKeywordSnapshot();
            Assert.Same(first, DscKeywordRegistry.GetKeywordSnapshot());

            DscKeywordRegistry.MaterializeKeywordTable();
            DscKeywordRegistry.ClearKeywordTable();
            DscKeywordRegistry.MaterializeKeywordTable();
            DscKeywordRegistry.ClearKeywordTable();
            Assert.Same(first, DscKeywordRegistry.GetKeywordSnapshot());

            DscKeywordRegistry.ImportModules([module.ModuleInfo]);

            var afterImport = DscKeywordRegistry.GetKeywordSnapshot();
            Assert.NotSame(first, afterImport);
            Assert.Contains(afterImport, k => k.Keyword == "DscParserSnapshotResource");

            DscKeywordRegistry.ImportModules([module.ModuleInfo]);
            Assert.Same(afterImport, DscKeywordRegistry.GetKeywordSnapshot());

            DscKeywordRegistry.Reset();
            Assert.Empty(DscKeywordRegistry.GetKeywordSnapshot());
        }
        finally
        {
            DscKeywordRegistry.Reset();
        }
    }

    [Fact]
    public void HandleExternalCacheReset_OnACleanThread_ShouldNotReportAReset()
    {
        try
        {
            DscKeywordRegistry.Reset();

            Assert.False(DscKeywordRegistry.HandleExternalCacheReset());
        }
        finally
        {
            DscKeywordRegistry.Reset();
        }
    }
}
