using System.Management.Automation;
using System.Management.Automation.Language;
using DSCParser.PSDSC;
using Xunit;
using DscResourceInfo = Microsoft.PowerShell.DesiredStateConfiguration.DscResourceInfo;
using ImplementedAsType = Microsoft.PowerShell.DesiredStateConfiguration.ImplementedAsType;

namespace DSCParser.Tests;

/// <summary>
/// The schema-file branch of <see cref="ResourceProcessor.GetResourceFromKeyword"/> only runs when
/// the engine class cache defines a MOF schema for the keyword, so these tests need real engine
/// keywords. <see cref="ResourceProcessorTests"/> covers the class-based fallback.
/// </summary>
public class ResourceProcessorSchemaTests
{
    private static void Cleanup()
    {
        DscResourceHelpers.ClearModuleCache();
        DscKeywordRegistry.Reset();
    }

    [Fact]
    public void GetResourceFromKeyword_WithMofSchemaAndMatchingModule_ShouldReportAScriptBasedPowerShellResource()
    {
        try
        {
            DynamicKeyword archive = EngineKeywordFixture.Require(EngineKeywordFixture.SchemaBackedKeyword);
            string schemaFile = EngineKeywordFixture.RequireSchemaFile(archive);
            PSModuleInfo module = EngineKeywordFixture.ModuleOwning(archive);

            DscResourceInfo? resource = ResourceProcessor.GetResourceFromKeyword(archive, [], [module]);

            Assert.NotNull(resource);
            Assert.Equal("MSFT_ArchiveResource", resource!.ResourceType);
            Assert.Equal("Archive", resource.Name);
            Assert.Equal("Archive", resource.FriendlyName);
            Assert.Equal(ImplementedAsType.PowerShell, resource.ImplementedAs);
            Assert.Equal("ScriptBased", resource.ImplementationDetail);
            Assert.Same(module, resource.Module);
            Assert.EndsWith("MSFT_ArchiveResource.psm1", resource.Path, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(resource.Path));
            Assert.Equal(Path.GetDirectoryName(schemaFile), resource.ParentPath);
        }
        finally
        {
            Cleanup();
        }
    }

    [Fact]
    public void GetResourceFromKeyword_WithMofSchema_ShouldOrderMandatoryPropertiesFirstThenAlphabetically()
    {
        try
        {
            DynamicKeyword archive = EngineKeywordFixture.Require(EngineKeywordFixture.SchemaBackedKeyword);

            DscResourceInfo resource = ResourceProcessor.GetResourceFromKeyword(
                archive, [], [EngineKeywordFixture.ModuleOwning(archive)])!;

            var names = resource.PropertiesAsResourceInfo.Select(p => p.Name).ToList();
            var mandatory = resource.PropertiesAsResourceInfo.Where(p => p.IsMandatory).Select(p => p.Name).ToList();

            Assert.Equal(["Destination", "Path"], names.Take(2));
            Assert.Equal(["Destination", "Path"], mandatory);
            Assert.Equal(names.Skip(2), names.Skip(2).OrderBy(n => n, StringComparer.Ordinal));
        }
        finally
        {
            Cleanup();
        }
    }

    [Fact]
    public void GetResourceFromKeyword_WithMofSchema_ShouldMapValueMapsInSortedOrder()
    {
        try
        {
            DynamicKeyword archive = EngineKeywordFixture.Require(EngineKeywordFixture.SchemaBackedKeyword);

            DscResourceInfo resource = ResourceProcessor.GetResourceFromKeyword(
                archive, [], [EngineKeywordFixture.ModuleOwning(archive)])!;

            var ensure = resource.PropertiesAsResourceInfo.Single(p => p.Name == "Ensure");
            var checksum = resource.PropertiesAsResourceInfo.Single(p => p.Name == "Checksum");

            Assert.Equal(["Absent", "Present"], ensure.Values);
            Assert.Equal(archive.Properties["Checksum"].ValueMap.Keys.OrderBy(k => k), checksum.Values);
            Assert.Contains("SHA-256", checksum.Values);
        }
        finally
        {
            Cleanup();
        }
    }

    [Fact]
    public void GetResourceFromKeyword_WithMofSchema_ShouldConvertCimTypeConstraintsToPowerShellTypes()
    {
        try
        {
            DynamicKeyword archive = EngineKeywordFixture.Require(EngineKeywordFixture.SchemaBackedKeyword);

            DscResourceInfo resource = ResourceProcessor.GetResourceFromKeyword(
                archive, [], [EngineKeywordFixture.ModuleOwning(archive)])!;

            var types = resource.PropertiesAsResourceInfo.ToDictionary(p => p.Name!, p => p.PropertyType ?? string.Empty);

            Assert.Equal("[string]", types["Ensure"]);
            Assert.Equal("[bool]", types["Force"]);
            Assert.Equal("[string[]]", types["DependsOn"]);
            Assert.Equal("[PSCredential]", types["PsDscRunAsCredential"]);
        }
        finally
        {
            Cleanup();
        }
    }

    [Fact]
    public void GetResourceFromKeyword_WithSchemaUnderTheSystemConfigurationFolder_ShouldSkipClassValidationAndReportBinary()
    {
        try
        {
            DynamicKeyword file = EngineKeywordFixture.Require(EngineKeywordFixture.SystemConfigurationKeyword);
            string schemaFile = EngineKeywordFixture.RequireSchemaFile(file);

            DscResourceInfo? resource = ResourceProcessor.GetResourceFromKeyword(file, [], []);

            Assert.NotNull(resource);
            Assert.Null(resource!.Module);
            Assert.Null(resource.Path);
            Assert.Equal(Path.GetDirectoryName(schemaFile), resource.ParentPath);
            Assert.Equal(ImplementedAsType.Binary, resource.ImplementedAs);
            Assert.Null(resource.ImplementationDetail);
            Assert.Contains(resource.PropertiesAsResourceInfo, p => p.Name == "DestinationPath" && p.IsMandatory);
        }
        finally
        {
            Cleanup();
        }
    }

    [Fact]
    public void GetResourceFromKeyword_WithNonMatchingModuleVersion_ShouldFallBackToTheFirstSchemaFileAndReresolveTheModule()
    {
        try
        {
            DynamicKeyword archive = EngineKeywordFixture.Require(EngineKeywordFixture.SchemaBackedKeyword);
            PSModuleInfo module = EngineKeywordFixture.ModuleOwning(archive);
            PsModuleInfoFactory.SetVersion(module, new Version("9.9.9.9"));

            DscResourceInfo? resource = ResourceProcessor.GetResourceFromKeyword(archive, [], [module]);

            Assert.NotNull(resource);
            Assert.Same(module, resource!.Module);
        }
        finally
        {
            Cleanup();
        }
    }

    [Fact]
    public void GetResourceFromKeyword_WithNonMatchingPattern_ShouldReturnNull()
    {
        try
        {
            DynamicKeyword archive = EngineKeywordFixture.Require(EngineKeywordFixture.SchemaBackedKeyword);

            Assert.Null(ResourceProcessor.GetResourceFromKeyword(
                archive, ["__DscParserNoSuchResource__"], [EngineKeywordFixture.ModuleOwning(archive)]));
        }
        finally
        {
            Cleanup();
        }
    }

    [Fact]
    public void GetResourceFromKeyword_WithTempMofModule_ShouldResolvePathParentPathAndFriendlyName()
    {
        using var module = TestDscModule.CreateMofResource(
            "DscParserSchemaModule",
            "MSFT_DscParserSchemaResource",
            """
            [ClassVersion("1.0.0.0"), FriendlyName("DscParserSchemaResource")]
            class MSFT_DscParserSchemaResource : OMI_BaseResource
            {
                [Key] String Identity;
                [Required] String Title;
                [Write] Boolean Enabled;
                [Write] String Tags[];
            };
            """);

        try
        {
            DscKeywordRegistry.EnsureDefaultKeywordsLoaded();
            DscKeywordRegistry.ImportModules([module.ModuleInfo]);

            DynamicKeyword? keyword = DscClassCacheReflection.GetCachedKeywords()?
                .FirstOrDefault(k => k.Keyword == "DscParserSchemaResource");
            Assert.NotNull(keyword);

            DscResourceInfo? resource = ResourceProcessor.GetResourceFromKeyword(keyword!, [], [module.ModuleInfo]);

            Assert.NotNull(resource);
            Assert.Equal("DscParserSchemaResource", resource!.FriendlyName);
            Assert.Equal(module.ImplementingScriptPath("MSFT_DscParserSchemaResource"), resource.Path);
            Assert.Equal(module.ResourceFolder("MSFT_DscParserSchemaResource"), resource.ParentPath);
            Assert.Equal("ScriptBased", resource.ImplementationDetail);

            var mandatory = resource.PropertiesAsResourceInfo.Where(p => p.IsMandatory).Select(p => p.Name).OrderBy(n => n);
            Assert.Equal(["Identity", "Title"], mandatory);
        }
        finally
        {
            Cleanup();
        }
    }

    [Fact]
    public void GetResourceFromKeyword_WithTempMofModuleWithoutImplementingScript_ShouldReportBinary()
    {
        using var module = TestDscModule.CreateMofResource(
            "DscParserOrphanModule",
            "MSFT_DscParserOrphanResource",
            """
            [ClassVersion("1.0.0.0"), FriendlyName("DscParserOrphanResource")]
            class MSFT_DscParserOrphanResource : OMI_BaseResource
            {
                [Key] String Identity;
            };
            """,
            withImplementingScript: false);

        try
        {
            DscKeywordRegistry.EnsureDefaultKeywordsLoaded();
            DscKeywordRegistry.ImportModules([module.ModuleInfo]);

            DynamicKeyword? keyword = DscClassCacheReflection.GetCachedKeywords()?
                .FirstOrDefault(k => k.Keyword == "DscParserOrphanResource");
            Assert.NotNull(keyword);

            DscResourceInfo? resource = ResourceProcessor.GetResourceFromKeyword(keyword!, [], [module.ModuleInfo]);

            Assert.NotNull(resource);
            Assert.Null(resource!.Module);
            Assert.Null(resource.Path);
            Assert.Equal(ImplementedAsType.Binary, resource.ImplementedAs);
            Assert.Null(resource.ImplementationDetail);
        }
        finally
        {
            Cleanup();
        }
    }

    [Fact]
    public void GetResourceFromKeyword_WithSchemaClassThatIsNotADscResource_ShouldReturnNull()
    {
        using var module = TestDscModule.CreateMofResource(
            "DscParserEmbeddedModule",
            "MSFT_DscParserEmbeddedHost",
            """
            [ClassVersion("1.0.0.0")]
            class MSFT_DscParserEmbeddedType
            {
                [Write] String Value;
            };

            [ClassVersion("1.0.0.0"), FriendlyName("DscParserEmbeddedHost")]
            class MSFT_DscParserEmbeddedHost : OMI_BaseResource
            {
                [Key] String Identity;
                [Write, EmbeddedInstance("MSFT_DscParserEmbeddedType")] String Settings[];
            };
            """);

        try
        {
            DscKeywordRegistry.EnsureDefaultKeywordsLoaded();
            DscKeywordRegistry.ImportModules([module.ModuleInfo]);

            DynamicKeyword? embedded = DscClassCacheReflection.GetCachedKeywords()?
                .FirstOrDefault(k => k.ResourceName == "MSFT_DscParserEmbeddedType");

            if (embedded is null)
            {
                Assert.Skip("The PowerShell engine in this environment did not register the embedded CIM type as a keyword.");
            }

            Assert.Null(ResourceProcessor.GetResourceFromKeyword(embedded!, [], [module.ModuleInfo]));
        }
        finally
        {
            Cleanup();
        }
    }
}
