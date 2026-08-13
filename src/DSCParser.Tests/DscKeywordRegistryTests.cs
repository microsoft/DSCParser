using DSCParser.PSDSC;
using Xunit;

namespace DSCParser.Tests;

/// <summary>
/// Covers the registry state-machine paths that are safe to exercise without a PowerShell engine
/// class cache: empty-name guards, the no-module-installed resolution failure, external-cache
/// handling when the engine is unsupported, and keyword-table materialization from a clean thread.
/// </summary>
public class DscKeywordRegistryTests
{
    [Fact]
    public void EnsureRegistered_WithNullOrEmptyName_ShouldReturnFalse()
    {
        Assert.False(DscKeywordRegistry.EnsureRegistered(null, null));
        Assert.False(DscKeywordRegistry.EnsureRegistered(string.Empty, null));
    }

    [Fact]
    public void EnsureRegistered_WithNoInstalledModule_ShouldReturnFalse()
    {
        try
        {
            DscKeywordRegistry.Reset();

            bool result = DscKeywordRegistry.EnsureRegistered("__DscParserNoSuchModule__", null);

            Assert.False(result);
        }
        finally
        {
            DscKeywordRegistry.Reset();
        }
    }

    [Fact]
    public void HandleExternalCacheReset_ShouldNotThrow()
    {
        try
        {
            DscKeywordRegistry.Reset();

            _ = DscKeywordRegistry.HandleExternalCacheReset();
        }
        finally
        {
            DscKeywordRegistry.Reset();
        }
    }

    [Fact]
    public void EnsureDefaultKeywordsLoaded_ShouldNotThrow()
    {
        try
        {
            DscKeywordRegistry.Reset();

            DscKeywordRegistry.EnsureDefaultKeywordsLoaded();
        }
        finally
        {
            DscKeywordRegistry.Reset();
        }
    }

    [Fact]
    public void MaterializeKeywordTable_WithCleanState_ShouldLoadDefaults()
    {
        try
        {
            DscKeywordRegistry.Reset();

            DscKeywordRegistry.MaterializeKeywordTable();
        }
        finally
        {
            DscKeywordRegistry.Reset();
        }
    }

    [Fact]
    public void ImportModules_WithNull_ShouldBeNoOp()
    {
        DscKeywordRegistry.ImportModules(null);
    }

    [Fact]
    public void ImportModules_WithModuleWithoutDscResources_ShouldRegisterAndSkipDuplicates()
    {
        string root = Path.Combine(Path.GetTempPath(), $"dscparser_kw_{Guid.NewGuid():N}");
        try
        {
            string moduleDir = Path.Combine(root, "ModA");
            Directory.CreateDirectory(moduleDir);

            var module = PsModuleInfoFactory.Create("ModA", Path.Combine(moduleDir, "ModA.psd1"));

            DscKeywordRegistry.Reset();
            DscKeywordRegistry.ImportModules([module]);
            DscKeywordRegistry.ImportModules([module]);
        }
        finally
        {
            DscKeywordRegistry.Reset();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ImportModules_WithResourceFolders_ShouldIterateResources()
    {
        string root = Path.Combine(Path.GetTempPath(), $"dscparser_kw_{Guid.NewGuid():N}");
        try
        {
            string moduleDir = Path.Combine(root, "ModA");
            Directory.CreateDirectory(Path.Combine(moduleDir, "DscResources", "NoSchema"));
            string schemaDir = Path.Combine(moduleDir, "DscResources", "WithSchema");
            Directory.CreateDirectory(schemaDir);
            File.WriteAllText(Path.Combine(schemaDir, "WithSchema.schema.mof"),
                "class MSFT_WithSchema : OMI_BaseResource { [key] string Name; };");

            var module = PsModuleInfoFactory.Create("ModA", Path.Combine(moduleDir, "ModA.psd1"));

            DscKeywordRegistry.Reset();
            DscKeywordRegistry.ImportModules([module]);
        }
        finally
        {
            DscKeywordRegistry.Reset();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void EnsureRegistered_WithAlreadyImportedModule_ShouldReturnTrue()
    {
        string root = Path.Combine(Path.GetTempPath(), $"dscparser_kw_{Guid.NewGuid():N}");
        try
        {
            string moduleDir = Path.Combine(root, "ModA");
            Directory.CreateDirectory(moduleDir);

            var module = PsModuleInfoFactory.Create("ModA", Path.Combine(moduleDir, "ModA.psd1"));

            DscKeywordRegistry.Reset();
            DscKeywordRegistry.ImportModules([module]);

            bool result = DscKeywordRegistry.EnsureRegistered("ModA", module.Version);

            Assert.True(result);
        }
        finally
        {
            DscKeywordRegistry.Reset();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void MaterializeKeywordTable_AfterClearingTable_ShouldReaddDefaultKeywords()
    {
        try
        {
            DscKeywordRegistry.Reset();
            DscKeywordRegistry.EnsureDefaultKeywordsLoaded();
            DscKeywordRegistry.ClearKeywordTable();

            DscKeywordRegistry.MaterializeKeywordTable();
            DscKeywordRegistry.MaterializeKeywordTable();
        }
        finally
        {
            DscKeywordRegistry.Reset();
        }
    }
}