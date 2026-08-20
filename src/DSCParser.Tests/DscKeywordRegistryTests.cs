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
