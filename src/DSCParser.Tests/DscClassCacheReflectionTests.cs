using System.Management.Automation.Language;
using DSCParser.PSDSC;
using Xunit;

namespace DSCParser.Tests;

public class DscClassCacheReflectionTests
{
    [Fact]
    public void IsDscClassCacheAvailable_OnTheTestHost_ShouldBeTrue()
    {
        Assert.True(DscClassCacheReflection.IsDscClassCacheAvailable);
    }

    [Fact]
    public void LoadDefaultCimKeywords_ShouldRegisterTheEngineFileAndArchiveKeywords()
    {
        try
        {
            DscClassCacheReflection.ClearCache();
            DscClassCacheReflection.LoadDefaultCimKeywords();

            var keywords = DscClassCacheReflection.GetCachedKeywords()?.ToList() ?? [];
            if (keywords.Count == 0)
            {
                Assert.Skip("The PowerShell engine in this environment registered no default DSC keywords.");
            }

            Assert.Contains(keywords, k => k.Keyword == "File" && k.ResourceName == "MSFT_FileDirectoryConfiguration");
            Assert.Contains(keywords, k => k.Keyword == "Archive" && k.ResourceName == "MSFT_ArchiveResource");
        }
        finally
        {
            DscKeywordRegistry.Reset();
        }
    }

    [Fact]
    public void LoadDefaultCimKeywords_ShouldRegisterTheClassCacheSentinel()
    {
        try
        {
            DscClassCacheReflection.ClearCache();
            DscClassCacheReflection.LoadDefaultCimKeywords();

            if (DscClassCacheReflection.GetCachedKeywords()?.Any() != true)
            {
                Assert.Skip("The PowerShell engine in this environment registered no default DSC keywords.");
            }

            Assert.True(DscClassCacheReflection.HasCachedClass("OMI_ConfigurationDocument"));
        }
        finally
        {
            DscKeywordRegistry.Reset();
        }
    }

    [Fact]
    public void GetFileDefiningClass_WithLoadedClass_ShouldReturnAnExistingSchemaFile()
    {
        try
        {
            DynamicKeyword archive = EngineKeywordFixture.Require(EngineKeywordFixture.SchemaBackedKeyword);
            string schemaFile = EngineKeywordFixture.RequireSchemaFile(archive);

            Assert.True(File.Exists(schemaFile));
            Assert.EndsWith(".schema.mof", schemaFile, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DscKeywordRegistry.Reset();
        }
    }

    [Fact]
    public void GetFileDefiningClass_WithUnknownClass_ShouldReturnNoFile()
    {
        Assert.Empty(DscClassCacheReflection.GetFileDefiningClass("__DscParserNoSuchClass__") ?? []);
    }

    [Fact]
    public void GetCachedClassByFileName_WithLoadedSchemaFile_ShouldReturnTheDeserializedCimClass()
    {
        try
        {
            DynamicKeyword archive = EngineKeywordFixture.Require(EngineKeywordFixture.SchemaBackedKeyword);
            string schemaFile = EngineKeywordFixture.RequireSchemaFile(archive);

            var classes = DscClassCacheReflection.GetCachedClassByFileName(schemaFile);

            Assert.Contains(classes, c =>
                c.CimSystemProperties.ClassName == "MSFT_ArchiveResource" &&
                c.CimSuperClassName == "OMI_BaseResource");
        }
        finally
        {
            DscKeywordRegistry.Reset();
        }
    }

    [Fact]
    public void GetCachedClassByFileName_WithUnknownFile_ShouldReturnEmptyList()
    {
        var result = DscClassCacheReflection.GetCachedClassByFileName("__DscParserNoSuchSchemaFile__");

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void HasCachedClass_WithLoadedClass_ShouldBeTrue()
    {
        try
        {
            _ = EngineKeywordFixture.Require(EngineKeywordFixture.SchemaBackedKeyword);

            Assert.True(DscClassCacheReflection.HasCachedClass("MSFT_ArchiveResource"));
        }
        finally
        {
            DscKeywordRegistry.Reset();
        }
    }

    [Fact]
    public void HasCachedClass_WithUnknownClass_ShouldBeFalse()
    {
        Assert.False(DscClassCacheReflection.HasCachedClass("__DscParserNoSuchClass__"));
    }

    [Fact]
    public void ClearCache_ShouldEmptyBothTheKeywordsAndTheCachedClasses()
    {
        try
        {
            _ = EngineKeywordFixture.Require(EngineKeywordFixture.SchemaBackedKeyword);

            DscClassCacheReflection.ClearCache();

            Assert.Empty(DscClassCacheReflection.GetCachedKeywords() ?? []);
            Assert.False(DscClassCacheReflection.HasCachedClass("OMI_ConfigurationDocument"));
        }
        finally
        {
            DscKeywordRegistry.Reset();
        }
    }

    [Fact]
    public void ResetDynamicKeywords_ShouldEmptyTheKeywordTableAndKeepTheClassCache()
    {
        try
        {
            _ = EngineKeywordFixture.Require(EngineKeywordFixture.SchemaBackedKeyword);
            DscKeywordRegistry.MaterializeKeywordTable();

            Assert.True(DynamicKeyword.ContainsKeyword("Node"));

            DscClassCacheReflection.ResetDynamicKeywords();

            Assert.False(DynamicKeyword.ContainsKeyword("Node"));
            Assert.NotEmpty(DscClassCacheReflection.GetCachedKeywords() ?? []);
        }
        finally
        {
            DscKeywordRegistry.Reset();
        }
    }

    [Fact]
    public void ImportCimKeywordsFromModule_WithRealSchemaFile_ShouldRegisterTheFriendlyKeyword()
    {
        using var module = TestDscModule.CreateMofResource(
            "DscParserCacheModule",
            "MSFT_DscParserCacheResource",
            """
            [ClassVersion("1.0.0.0"), FriendlyName("DscParserCacheResource")]
            class MSFT_DscParserCacheResource : OMI_BaseResource
            {
                [Key] String Identity;
                [Required] String Title;
                [Write, ValueMap{"Present","Absent"}, Values{"Present","Absent"}] String Ensure;
            };
            """,
            new Version("1.2.3.4"));

        try
        {
            DscKeywordRegistry.EnsureDefaultKeywordsLoaded();
            DscClassCacheReflection.ImportCimKeywordsFromModule(module.ModuleInfo, "MSFT_DscParserCacheResource");

            var keyword = DscClassCacheReflection.GetCachedKeywords()?
                .FirstOrDefault(k => k.Keyword == "DscParserCacheResource");

            Assert.NotNull(keyword);
            Assert.Equal("MSFT_DscParserCacheResource", keyword!.ResourceName);
            Assert.Equal("DscParserCacheModule", keyword.ImplementingModule);
            Assert.Equal(new Version("1.2.3.4"), keyword.ImplementingModuleVersion);
            Assert.True(keyword.Properties["Identity"].Mandatory);
            Assert.Equal(
                module.SchemaMofPath("MSFT_DscParserCacheResource"),
                DscClassCacheReflection.GetFileDefiningClass("MSFT_DscParserCacheResource")![0]);
        }
        finally
        {
            DscKeywordRegistry.Reset();
        }
    }

    [Fact]
    public void ImportClassResourcesFromModule_WithModuleDeclaringNoResources_ShouldLeaveTheCacheUnchanged()
    {
        using var module = TestDscModule.CreateEmpty("DscParserEmptyClassModule");

        try
        {
            DscKeywordRegistry.EnsureDefaultKeywordsLoaded();
            int before = DscClassCacheReflection.GetCachedKeywords()?.Count() ?? 0;

            DscClassCacheReflection.ImportClassResourcesFromModule(module.ModuleInfo, []);

            Assert.Equal(before, DscClassCacheReflection.GetCachedKeywords()?.Count() ?? 0);
        }
        finally
        {
            DscKeywordRegistry.Reset();
        }
    }
}
