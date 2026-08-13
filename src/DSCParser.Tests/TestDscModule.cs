using System.Management.Automation;

namespace DSCParser.Tests;

internal sealed class TestDscModule : IDisposable
{
    private TestDscModule(string root, string moduleName, Version version)
    {
        Root = root;
        ModuleName = moduleName;
        ModuleBase = Path.Combine(root, moduleName);
        ManifestPath = Path.Combine(ModuleBase, $"{moduleName}.psd1");

        ModuleInfo = PsModuleInfoFactory.Create(moduleName, ManifestPath);
        PsModuleInfoFactory.SetVersion(ModuleInfo, version);
    }

    public string Root { get; }

    public string ModuleName { get; }

    public string ModuleBase { get; }

    public string ManifestPath { get; }

    public PSModuleInfo ModuleInfo { get; }

    public string ResourceFolder(string resourceName) => Path.Combine(ModuleBase, "DscResources", resourceName);

    public string SchemaMofPath(string resourceName) => Path.Combine(ResourceFolder(resourceName), $"{resourceName}.schema.mof");

    public string SchemaPsm1Path(string resourceName) => Path.Combine(ResourceFolder(resourceName), $"{resourceName}.schema.psm1");

    public string ImplementingScriptPath(string resourceName) => Path.Combine(ResourceFolder(resourceName), $"{resourceName}.psm1");

    public static TestDscModule CreateMofResource(
        string moduleName,
        string resourceName,
        string schemaContent,
        Version? version = null,
        bool withImplementingScript = true)
    {
        TestDscModule module = CreateEmpty(moduleName, version);

        Directory.CreateDirectory(module.ResourceFolder(resourceName));
        File.WriteAllText(module.SchemaMofPath(resourceName), schemaContent);

        if (withImplementingScript)
        {
            File.WriteAllText(module.ImplementingScriptPath(resourceName), "function Get-TargetResource { }");
        }

        return module;
    }

    public static TestDscModule CreateCompositeResource(
        string moduleName,
        string resourceName,
        string configurationScript,
        Version? version = null)
    {
        TestDscModule module = CreateEmpty(moduleName, version);

        Directory.CreateDirectory(module.ResourceFolder(resourceName));
        File.WriteAllText(module.SchemaPsm1Path(resourceName), configurationScript);
        File.WriteAllText(
            Path.Combine(module.ResourceFolder(resourceName), $"{resourceName}.psd1"),
            $"@{{ RootModule = '{resourceName}.schema.psm1'; ModuleVersion = '{module.ModuleInfo.Version}' }}");

        return module;
    }

    public static TestDscModule CreateEmpty(string moduleName, Version? version = null)
    {
        string root = Path.Combine(Path.GetTempPath(), $"dscparser_module_{Guid.NewGuid():N}");
        TestDscModule module = new(root, moduleName, version ?? new Version("1.0.0.0"));

        Directory.CreateDirectory(module.ModuleBase);
        File.WriteAllText(
            module.ManifestPath,
            $"@{{ ModuleVersion = '{module.ModuleInfo.Version}'; DscResourcesToExport = @() }}");

        return module;
    }

    public TestDscModule WithResourceFolder(string resourceName)
    {
        Directory.CreateDirectory(ResourceFolder(resourceName));
        return this;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, true);
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (IOException)
        {
        }
    }
}
