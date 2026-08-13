using System.Management.Automation.Language;
using DSCParser.PSDSC;
using Xunit;

namespace DSCParser.Tests;

public class DscKeywordRegistryModuleImportTests
{
    private const string ModuleName = "DscParserKeywordModule";

    private const string ResourceClass = "MSFT_DscParserKeywordResource";

    private const string ResourceKeyword = "DscParserKeywordResource";

    private const string Schema = $$"""
        [ClassVersion("1.0.0.0"), FriendlyName("{{ResourceKeyword}}")]
        class {{ResourceClass}} : OMI_BaseResource
        {
            [Key] String Identity;
            [Write, ValueMap{"Present","Absent"}, Values{"Present","Absent"}] String Ensure;
        };
        """;

    private static readonly Version ModuleVersion = new("1.2.3.4");

    private static TestDscModule NewModule() =>
        TestDscModule.CreateMofResource(ModuleName, ResourceClass, Schema, ModuleVersion);

    private static int CachedKeywordCount() => DscClassCacheReflection.GetCachedKeywords()?.Count() ?? 0;

    [Fact]
    public void ImportModules_WithMofResourceFolder_ShouldRegisterTheResourceAsADynamicKeyword()
    {
        using var module = NewModule();

        try
        {
            DscKeywordRegistry.Reset();
            DscKeywordRegistry.ImportModules([module.ModuleInfo]);

            DynamicKeyword? keyword = DscClassCacheReflection.GetCachedKeywords()?
                .FirstOrDefault(k => k.Keyword == ResourceKeyword);

            Assert.NotNull(keyword);
            Assert.Equal(ResourceClass, keyword!.ResourceName);
            Assert.Equal(ModuleName, keyword.ImplementingModule);
            Assert.Equal(ModuleVersion, keyword.ImplementingModuleVersion);
            Assert.True(keyword.Properties["Identity"].Mandatory);
            Assert.Equal(2, keyword.Properties["Ensure"].ValueMap.Count);
        }
        finally
        {
            DscKeywordRegistry.Reset();
        }
    }

    [Fact]
    public void ImportModules_WithResourceFolderWithoutSchemaFile_ShouldNotRegisterAKeyword()
    {
        using var module = TestDscModule.CreateEmpty(ModuleName).WithResourceFolder("NoSchema");

        try
        {
            DscKeywordRegistry.Reset();
            DscKeywordRegistry.EnsureDefaultKeywordsLoaded();
            int before = CachedKeywordCount();

            DscKeywordRegistry.ImportModules([module.ModuleInfo]);

            Assert.Equal(before, CachedKeywordCount());
        }
        finally
        {
            DscKeywordRegistry.Reset();
        }
    }

    [Fact]
    public void ImportModules_WithoutDscResourcesFolder_ShouldReturnWithoutRegistering()
    {
        using var module = TestDscModule.CreateEmpty(ModuleName);

        try
        {
            DscKeywordRegistry.Reset();
            DscKeywordRegistry.EnsureDefaultKeywordsLoaded();
            int before = CachedKeywordCount();

            DscKeywordRegistry.ImportModules([module.ModuleInfo]);

            Assert.Equal(before, CachedKeywordCount());
        }
        finally
        {
            DscKeywordRegistry.Reset();
        }
    }

    [Fact]
    public void ImportModules_CalledTwice_ShouldRegisterTheKeywordOnlyOnce()
    {
        using var module = NewModule();

        try
        {
            DscKeywordRegistry.Reset();
            DscKeywordRegistry.ImportModules([module.ModuleInfo]);
            int afterFirst = CachedKeywordCount();

            DscKeywordRegistry.ImportModules([module.ModuleInfo]);

            Assert.Equal(afterFirst, CachedKeywordCount());
        }
        finally
        {
            DscKeywordRegistry.Reset();
        }
    }

    [Fact]
    public void ImportModules_ShouldSatisfyASubsequentEnsureRegisteredWithoutVersion()
    {
        using var module = NewModule();

        try
        {
            DscKeywordRegistry.Reset();
            DscKeywordRegistry.ImportModules([module.ModuleInfo]);

            Assert.True(DscKeywordRegistry.EnsureRegistered(ModuleName, null));
            Assert.True(DscKeywordRegistry.EnsureRegistered(ModuleName, ModuleVersion));
        }
        finally
        {
            DscKeywordRegistry.Reset();
        }
    }

    [Fact]
    public void EnsureRegistered_WithADifferentVersion_ShouldNotBeSatisfiedByTheImportedOne()
    {
        using var module = NewModule();

        try
        {
            DscKeywordRegistry.Reset();
            DscKeywordRegistry.ImportModules([module.ModuleInfo]);

            Assert.False(DscKeywordRegistry.EnsureRegistered(ModuleName, new Version("9.9.9.9")));
        }
        finally
        {
            DscKeywordRegistry.Reset();
        }
    }

    [Fact]
    public void EnsureRegistered_WithUnresolvableModule_ShouldWarnThatResolutionFailed()
    {
        var warnings = new List<string>();
        DscResourceService.WarningSink = warnings.Add;

        try
        {
            DscKeywordRegistry.Reset();

            Assert.False(DscKeywordRegistry.EnsureRegistered("__DscParserNoSuchModule__", null));
            Assert.Contains(warnings, w => w.Contains("Failed to resolve module '__DscParserNoSuchModule__'.", StringComparison.Ordinal));
        }
        finally
        {
            DscResourceService.WarningSink = null;
            DscKeywordRegistry.Reset();
        }
    }

    [Fact]
    public void MaterializeKeywordTable_AfterImportingAModule_ShouldExposeTheImportedKeywordToTheParser()
    {
        using var module = NewModule();

        try
        {
            DscKeywordRegistry.Reset();
            DscKeywordRegistry.ImportModules([module.ModuleInfo]);

            DscKeywordRegistry.MaterializeKeywordTable();

            Assert.True(DynamicKeyword.ContainsKeyword(ResourceKeyword));
            Assert.True(DynamicKeyword.ContainsKeyword("Node"));

            DscKeywordRegistry.ClearKeywordTable();

            Assert.False(DynamicKeyword.ContainsKeyword(ResourceKeyword));
            Assert.False(DynamicKeyword.ContainsKeyword("Node"));
            Assert.Contains(DscClassCacheReflection.GetCachedKeywords() ?? [], k => k.Keyword == ResourceKeyword);
        }
        finally
        {
            DscKeywordRegistry.Reset();
        }
    }

    [Fact]
    public void Reset_ShouldDropTheImportedKeywordsTheClassCacheAndTheBookkeeping()
    {
        using var module = NewModule();

        DscKeywordRegistry.Reset();
        DscKeywordRegistry.ImportModules([module.ModuleInfo]);

        Assert.NotEqual(0, CachedKeywordCount());

        DscKeywordRegistry.Reset();

        Assert.Equal(0, CachedKeywordCount());
        Assert.False(DynamicKeyword.ContainsKeyword("Node"));
        Assert.False(DscClassCacheReflection.HasCachedClass("OMI_ConfigurationDocument"));
    }

    [Fact]
    public void EnsureDefaultKeywordsLoaded_ShouldPopulateTheCacheAndBeIdempotent()
    {
        try
        {
            DscKeywordRegistry.Reset();

            DscKeywordRegistry.EnsureDefaultKeywordsLoaded();
            int afterFirst = CachedKeywordCount();

            if (afterFirst == 0)
            {
                Assert.Skip("The PowerShell engine in this environment registered no default DSC keywords.");
            }

            DscKeywordRegistry.EnsureDefaultKeywordsLoaded();

            Assert.Equal(afterFirst, CachedKeywordCount());
        }
        finally
        {
            DscKeywordRegistry.Reset();
        }
    }
}
