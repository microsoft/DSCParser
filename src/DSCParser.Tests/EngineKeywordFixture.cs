using System.Management.Automation;
using System.Management.Automation.Language;
using DSCParser.PSDSC;
using Xunit;

namespace DSCParser.Tests;

/// <summary>
/// The DSC keywords the PowerShell engine ships with. Tests that need a real MOF schema on disk
/// use these rather than fabricating one, because only the engine's own resources are guaranteed
/// to come with a populated class cache.
/// </summary>
internal static class EngineKeywordFixture
{
    public const string SchemaBackedKeyword = "Archive";

    public const string SystemConfigurationKeyword = "File";

    public static DynamicKeyword Require(string keywordName)
    {
        DscKeywordRegistry.EnsureDefaultKeywordsLoaded();

        DynamicKeyword? keyword = DscClassCacheReflection.GetCachedKeywords()?
            .FirstOrDefault(k => k.Keyword.Equals(keywordName, StringComparison.OrdinalIgnoreCase));

        if (keyword is null)
        {
            Assert.Skip($"The PowerShell engine in this environment did not register the '{keywordName}' keyword.");
        }

        return keyword!;
    }

    public static string RequireSchemaFile(DynamicKeyword keyword)
    {
        List<string>? files = DscClassCacheReflection.GetFileDefiningClass(keyword.ResourceName);

        if (files is null || files.Count == 0)
        {
            Assert.Skip($"The PowerShell engine in this environment defines no schema file for '{keyword.ResourceName}'.");
        }

        return files![0];
    }

    public static PSModuleInfo ModuleOwning(DynamicKeyword keyword)
    {
        string schemaFile = RequireSchemaFile(keyword);
        string moduleBase = Directory.GetParent(Path.GetDirectoryName(schemaFile)!)!.Parent!.FullName;

        PSModuleInfo module = PsModuleInfoFactory.Create(
            keyword.ImplementingModule,
            Path.Combine(moduleBase, $"{keyword.ImplementingModule}.psd1"));
        PsModuleInfoFactory.SetVersion(module, keyword.ImplementingModuleVersion);

        return module;
    }
}
