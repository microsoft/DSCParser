using System.Management.Automation.Language;
using System.Reflection;
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
/// <see cref="DscClassCacheReflection.ClearCache"/> and/or
/// <see cref="DscClassCacheReflection.ResetDynamicKeywords"/>. A Configuration compile wipes both
/// engine caches, but each can also be emptied independently, so staleness detection has to probe
/// the class cache (read by Get-DscResourceV2) and the DynamicKeyword table (read by the parser)
/// separately.
/// </summary>
public class DscResourceCacheResetTests
{
    public DscResourceCacheResetTests()
    {
        // Explicit clean baseline: other tests in the assembly may have touched the shared
        // engine caches or the registry bookkeeping on this thread.
        DscKeywordRegistry.Reset();
    }

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

    [Fact]
    public void HandleExternalCacheReset_WhenCachedKeywordCountShrinks_ShouldReset()
    {
        if (!EngineCacheAvailable)
        {
            return;
        }

        try
        {
            DscKeywordRegistry.EnsureDefaultKeywordsLoaded();
            SkipIfNoEngineKeywords();

            // Windows PowerShell reinitializes the class cache to the default set during every
            // configuration parse: the sentinel class survives but all module entries vanish.
            // Simulate that by claiming more classes were imported than the cache now holds.
            var expectedCountField = typeof(DscKeywordRegistry).GetField(
                "t_expectedCachedClassCount",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(expectedCountField);
            int currentClassCount = DscClassCacheReflection.GetCachedClassCount();
            expectedCountField!.SetValue(null, (currentClassCount >= 0 ? currentClassCount : KeywordCount()) + 1);

            Assert.True(DscKeywordRegistry.HandleExternalCacheReset());
        }
        finally
        {
            ResetRegistryState();
        }
    }

    #endregion

    #region Keyword table lifecycle

    [Fact]
    public void MaterializeKeywordTable_ShouldPopulateNodeAndCachedKeywords_AndClearShouldEmptyIt()
    {
        if (!EngineCacheAvailable)
        {
            return;
        }

        try
        {
            DscKeywordRegistry.EnsureDefaultKeywordsLoaded();
            SkipIfNoEngineKeywords();

            // Simulate the between-operations state: class cache warm, keyword table empty.
            DscClassCacheReflection.ResetDynamicKeywords();
            Assert.False(DynamicKeyword.ContainsKeyword("Node"));

            var cachedBefore = KeywordCount();
            DscKeywordRegistry.MaterializeKeywordTable();

            Assert.True(DynamicKeyword.ContainsKeyword("Node"));
            // Materializing must never reinitialize the class cache (LoadDefaultCimKeywords
            // would): that silently drops every imported module from it.
            Assert.Equal(cachedBefore, KeywordCount());

            DscKeywordRegistry.ClearKeywordTable();

            Assert.False(DynamicKeyword.ContainsKeyword("Node"));
            // The class cache must survive the table clear.
            SkipIfNoEngineKeywords();
        }
        finally
        {
            ResetRegistryState();
        }
    }

    [Fact]
    public void GetDscResources_ShouldLeaveNoKeywordTableResidue()
    {
        if (!EngineCacheAvailable)
        {
            return;
        }

        try
        {
            var resources = DscResourceService.GetDscResources().Count;
            if (resources == 0)
            {
                Assert.Skip("No DSC resources are discoverable in this environment.");
            }

            // Leftover DynamicKeyword table entries make the engine skip its internal reset when
            // it compiles a Configuration, and a configuration invoked in the same script that
            // defines it (the layout every Microsoft365DSC export uses) then fails to parse.
            Assert.False(DynamicKeyword.ContainsKeyword("Node"));
            SkipIfNoEngineKeywords();
        }
        finally
        {
            ResetRegistryState();
        }
    }

    #endregion

    #region EnsureRegistered staleness

    [Fact]
    public void EnsureRegistered_AfterEngineWipe_ShouldNotTrustStaleBookkeeping()
    {
        if (!EngineCacheAvailable)
        {
            return;
        }

        try
        {
            DscKeywordRegistry.EnsureDefaultKeywordsLoaded();
            SkipIfNoEngineKeywords();

            // Forge the bookkeeping: pretend FakeModule was imported on this thread.
            var field = typeof(DscKeywordRegistry).GetField(
                "t_importedModules",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(field);
            var imported = Assert.IsType<HashSet<string>>(field!.GetValue(null));
            Assert.True(imported.Add("FakeModule"));

            // The engine wipes both caches (MOF compile).
            DscClassCacheReflection.ClearCache();
            DscClassCacheReflection.ResetDynamicKeywords();

            // Pre-fix this returned a stale true without re-importing anything; now the heal drops
            // the forged bookkeeping and the unresolvable module is reported as not installed.
            Assert.False(DscKeywordRegistry.EnsureRegistered("FakeModule", null));

            // The heal restored the default class cache as a side effect.
            SkipIfNoEngineKeywords();
        }
        finally
        {
            ResetRegistryState();
        }
    }

    #endregion

    #region Thread-static scope

    [Fact]
    public void EnsureDefaultKeywordsLoaded_OnFreshThread_ShouldPopulateThatThreadsCache()
    {
        if (!EngineCacheAvailable)
        {
            return;
        }

        try
        {
            DscKeywordRegistry.EnsureDefaultKeywordsLoaded();
            SkipIfNoEngineKeywords();

            // The engine caches are thread-static: a fresh thread starts empty and must import for
            // itself instead of trusting bookkeeping populated by another thread.
            var classCachePopulatedOnNewThread = false;
            var thread = new Thread(() =>
            {
                DscKeywordRegistry.EnsureDefaultKeywordsLoaded();
                classCachePopulatedOnNewThread = KeywordCount() > 0;
                DscKeywordRegistry.Reset();
            });
            thread.Start();
            thread.Join();

            Assert.True(classCachePopulatedOnNewThread);
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
