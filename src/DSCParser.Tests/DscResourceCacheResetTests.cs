using DSCParser.PSDSC;
using Xunit;

namespace DSCParser.Tests;

/// <summary>
/// Regression coverage for the DscClassCache / Get-DscResourceV2 interaction introduced in 3.1.0.0.
/// When a DSC Configuration is compiled to MOF in the same process, PowerShell's engine clears its
/// internal class cache, including every registered dynamic keyword. DSCParser's process-wide import
/// bookkeeping (DscKeywordRegistry) then no longer matches engine state: discovery believes the
/// resources are already imported, skips re-importing them, reads an empty cache and returns zero
/// resources. ConvertTo-DSCObject then fails with "No DSC resources loaded. Please provide DSC
/// resources to parse the configuration."
///
/// To reproduce the trigger in a unit test, the external engine reset is simulated with
/// <see cref="DscClassCacheReflection.ClearCache"/>, which is exactly what a Configuration compile
/// does to the shared cache (verified: DynamicKeyword.Reset alone does not empty it).
/// </summary>
public class DscResourceCacheResetTests
{
    private static bool EngineCacheAvailable
    {
        get
        {
            if (DscClassCacheReflection.IsDscClassCacheAvailable)
            {
                return true;
            }

            Assert.Skip("The PowerShell engine in this environment does not expose the DscClassCache type.");
            return false;
        }
    }

    private static int KeywordCount()
    {
        var keywords = DscClassCacheReflection.GetCachedKeywords();
        return keywords is null ? -1 : keywords.Count();
    }

    private static void SkipIfNoEngineKeywords()
    {
        if (KeywordCount() == 0)
        {
            Assert.Skip("The PowerShell engine in this environment did not seed any DSC keywords.");
        }
    }

    #region Get-DscResourceV2 end-to-end regression

    [Fact]
    public void GetDscResources_AfterExternalClassCacheClear_ShouldStillReturnResources()
    {
        if (!EngineCacheAvailable)
        {
            return;
        }

        try
        {
            var before = DscResourceService.GetDscResources().Count;
            if (before == 0)
            {
                Assert.Skip("No DSC resources are discoverable in this environment.");
            }

            // Simulate PowerShell clearing its internal class cache, as it does whenever a
            // Configuration block is compiled to MOF in the same process. The registry's own
            // bookkeeping is intentionally NOT reset - that mismatch was the 3.1.0.0 regression:
            // discovery skipped re-importing and returned zero results.
            DscClassCacheReflection.ClearCache();
            Assert.Equal(0, KeywordCount());

            var after = DscResourceService.GetDscResources().Count;

            Assert.Equal(before, after);
        }
        finally
        {
            ResetRegistryState();
        }
    }

    #endregion

    #region HandleExternalCacheReset contract

    [Fact]
    public void HandleExternalCacheReset_WithNothingImported_ShouldNotReset()
    {
        if (!EngineCacheAvailable)
        {
            return;
        }

        try
        {
            Assert.False(DscKeywordRegistry.HandleExternalCacheReset());
        }
        finally
        {
            ResetRegistryState();
        }
    }

    [Fact]
    public void HandleExternalCacheReset_WithConsistentCache_ShouldNotReset()
    {
        if (!EngineCacheAvailable)
        {
            return;
        }

        try
        {
            DscKeywordRegistry.EnsureDefaultKeywordsLoaded();
            SkipIfNoEngineKeywords();

            Assert.False(DscKeywordRegistry.HandleExternalCacheReset());
        }
        finally
        {
            ResetRegistryState();
        }
    }

    [Fact]
    public void HandleExternalCacheReset_AfterEngineWipedKeywords_ShouldReset()
    {
        if (!EngineCacheAvailable)
        {
            return;
        }

        try
        {
            DscKeywordRegistry.EnsureDefaultKeywordsLoaded();
            SkipIfNoEngineKeywords();

            // The engine clears the cache underneath the registry's bookkeeping (MOF compile).
            DscClassCacheReflection.ClearCache();
            Assert.Equal(0, KeywordCount());

            Assert.True(DscKeywordRegistry.HandleExternalCacheReset());

            // The forged bookkeeping was dropped; (re-)importing rebuilds the keywords.
            DscKeywordRegistry.EnsureDefaultKeywordsLoaded();
            SkipIfNoEngineKeywords();

            // State is consistent again, so the heal is now a no-op.
            Assert.False(DscKeywordRegistry.HandleExternalCacheReset());
        }
        finally
        {
            ResetRegistryState();
        }
    }

    [Fact]
    public void HandleExternalCacheReset_SecondCallAfterHeal_ShouldBeNoOp()
    {
        if (!EngineCacheAvailable)
        {
            return;
        }

        try
        {
            DscKeywordRegistry.EnsureDefaultKeywordsLoaded();
            SkipIfNoEngineKeywords();

            DscClassCacheReflection.ClearCache();
            Assert.True(DscKeywordRegistry.HandleExternalCacheReset());

            Assert.False(DscKeywordRegistry.HandleExternalCacheReset());
        }
        finally
        {
            ResetRegistryState();
        }
    }

    #endregion

    private static void ResetRegistryState()
    {
        // Restore the natural process baseline (keyword cache empty, nothing imported) so tests
        // neither leak keywords nor depend on execution order.
        DscKeywordRegistry.Reset();
    }
}