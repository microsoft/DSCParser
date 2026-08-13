using System.Management.Automation;
using DSCParser.PSDSC;
using Xunit;

namespace DSCParser.Tests;

/// <summary>
/// Exercises the file-system driven helpers of <see cref="DscResourceHelpers"/> that are not
/// reachable through the already-covered discovery paths. These build real temp module folders so
/// the module enumeration logic runs against the file system.
/// </summary>
public class DscResourceHelperFileTests
{
    private static string NewTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"dscparser_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    #region GetImplementingModulePath

    [Fact]
    public void GetImplementingModulePath_WithPsd1Sibling_ShouldReturnPsd1()
    {
        string dir = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "Test.schema.mof"), string.Empty);
            File.WriteAllText(Path.Combine(dir, "Test.psd1"), string.Empty);

            string result = DscResourceHelpers.GetImplementingModulePath(Path.Combine(dir, "Test.schema.mof"))!;

            Assert.Equal(Path.Combine(dir, "Test.psd1"), result);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void GetImplementingModulePath_WithPsm1Sibling_ShouldReturnPsm1()
    {
        string dir = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "Test.schema.mof"), string.Empty);
            File.WriteAllText(Path.Combine(dir, "Test.psm1"), string.Empty);

            string result = DscResourceHelpers.GetImplementingModulePath(Path.Combine(dir, "Test.schema.mof"))!;

            Assert.Equal(Path.Combine(dir, "Test.psm1"), result);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void GetImplementingModulePath_WithNoImplementingModule_ShouldReturnNull()
    {
        string dir = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "Test.schema.mof"), string.Empty);

            Assert.Null(DscResourceHelpers.GetImplementingModulePath(Path.Combine(dir, "Test.schema.mof")));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void GetImplementingModulePath_WithNonSchemaSuffix_ShouldReturnNull()
    {
        string dir = NewTempDir();
        try
        {
            Assert.Null(DscResourceHelpers.GetImplementingModulePath(Path.Combine(dir, "Test.mof")));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    #endregion

    #region GetModule

    [Fact]
    public void GetModule_WithNoModules_ShouldReturnNull()
    {
        Assert.Null(DscResourceHelpers.GetModule([], "C:\\Test\\schema.mof"));
        Assert.Null(DscResourceHelpers.GetModule([], null));
    }

    [Fact]
    public void GetModule_WithMatchingTempModule_ShouldResolveAndCache()
    {
        string root = NewTempDir();
        try
        {
            string moduleDir = Path.Combine(root, "ModA");
            string resourceDir = Path.Combine(moduleDir, "DscResources", "TestResource");
            Directory.CreateDirectory(resourceDir);
            string schemaMof = Path.Combine(resourceDir, "TestResource.schema.mof");
            File.WriteAllText(schemaMof, string.Empty);
            File.WriteAllText(Path.Combine(resourceDir, "TestResource.psm1"), string.Empty);

            var module = PsModuleInfoFactory.Create("ModA", Path.Combine(moduleDir, "ModA.psd1"));

            DscResourceHelpers.ClearModuleCache();
            var first = DscResourceHelpers.GetModule([module], schemaMof);
            var cached = DscResourceHelpers.GetModule([module], schemaMof);

            Assert.Same(module, first);
            Assert.Same(module, cached);
        }
        finally
        {
            DscResourceHelpers.ClearModuleCache();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void GetModule_WithNoImplementingFile_ShouldReturnNull()
    {
        string root = NewTempDir();
        try
        {
            string moduleDir = Path.Combine(root, "ModA");
            string resourceDir = Path.Combine(moduleDir, "DscResources", "TestResource");
            Directory.CreateDirectory(resourceDir);
            string schemaMof = Path.Combine(resourceDir, "TestResource.schema.mof");
            File.WriteAllText(schemaMof, string.Empty);

            var module = PsModuleInfoFactory.Create("ModA", Path.Combine(moduleDir, "ModA.psd1"));

            DscResourceHelpers.ClearModuleCache();
            PSModuleInfo? result = DscResourceHelpers.GetModule([module], schemaMof);

            Assert.Null(result);
        }
        finally
        {
            DscResourceHelpers.ClearModuleCache();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void GetModule_WithLogResource_ShouldReturnModuleWithoutImplementingFile()
    {
        string root = NewTempDir();
        try
        {
            string moduleDir = Path.Combine(root, "ModA");
            string resourceDir = Path.Combine(moduleDir, "DscResources", "Log");
            Directory.CreateDirectory(resourceDir);
            string schemaMof = Path.Combine(resourceDir, "MSFT_LogResource.schema.mof");
            File.WriteAllText(schemaMof, string.Empty);

            var module = PsModuleInfoFactory.Create("ModA", Path.Combine(moduleDir, "ModA.psd1"));

            DscResourceHelpers.ClearModuleCache();
            PSModuleInfo? result = DscResourceHelpers.GetModule([module], schemaMof);

            Assert.Same(module, result);
        }
        finally
        {
            DscResourceHelpers.ClearModuleCache();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void GetModule_WithSchemaPsm1File_ShouldResolveModule()
    {
        string root = NewTempDir();
        try
        {
            string moduleDir = Path.Combine(root, "ModA");
            string resourceDir = Path.Combine(moduleDir, "DscResources", "TestResource");
            Directory.CreateDirectory(resourceDir);
            string schemaPsm1 = Path.Combine(resourceDir, "TestResource.schema.psm1");
            File.WriteAllText(schemaPsm1, string.Empty);
            File.WriteAllText(Path.Combine(resourceDir, "TestResource.psd1"), string.Empty);

            var module = PsModuleInfoFactory.Create("ModA", Path.Combine(moduleDir, "ModA.psd1"));

            DscResourceHelpers.ClearModuleCache();
            PSModuleInfo? result = DscResourceHelpers.GetModule([module], schemaPsm1);

            Assert.Same(module, result);
        }
        finally
        {
            DscResourceHelpers.ClearModuleCache();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void GetModule_WithFileNotUnderDscResources_ShouldReturnNull()
    {
        string root = NewTempDir();
        try
        {
            string plainDir = Path.Combine(root, "Plain");
            Directory.CreateDirectory(plainDir);
            string schemaMof = Path.Combine(plainDir, "TestResource.schema.mof");
            File.WriteAllText(schemaMof, string.Empty);

            var module = PsModuleInfoFactory.Create("ModA", Path.Combine(root, "ModA", "ModA.psd1"));

            DscResourceHelpers.ClearModuleCache();
            PSModuleInfo? result = DscResourceHelpers.GetModule([module], schemaMof);

            Assert.Null(result);
        }
        finally
        {
            DscResourceHelpers.ClearModuleCache();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void GetModule_WithNonSchemaExtension_ShouldReturnNull()
    {
        string root = NewTempDir();
        try
        {
            string moduleDir = Path.Combine(root, "ModA");
            string resourceDir = Path.Combine(moduleDir, "DscResources", "TestResource");
            Directory.CreateDirectory(resourceDir);
            string plainFile = Path.Combine(resourceDir, "TestResource.mof");
            File.WriteAllText(plainFile, string.Empty);

            var module = PsModuleInfoFactory.Create("ModA", Path.Combine(moduleDir, "ModA.psd1"));

            DscResourceHelpers.ClearModuleCache();
            PSModuleInfo? result = DscResourceHelpers.GetModule([module], plainFile);

            Assert.Null(result);
        }
        finally
        {
            DscResourceHelpers.ClearModuleCache();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void GetModule_WithModuleWithoutModuleBase_ShouldReturnNull()
    {
        string root = NewTempDir();
        try
        {
            string moduleDir = Path.Combine(root, "ModA");
            string resourceDir = Path.Combine(moduleDir, "DscResources", "TestResource");
            Directory.CreateDirectory(resourceDir);
            string schemaMof = Path.Combine(resourceDir, "TestResource.schema.mof");
            File.WriteAllText(schemaMof, string.Empty);
            File.WriteAllText(Path.Combine(resourceDir, "TestResource.psm1"), string.Empty);

            var module = PsModuleInfoFactory.CreateNameOnly("ModA");

            DscResourceHelpers.ClearModuleCache();
            PSModuleInfo? result = DscResourceHelpers.GetModule([module], schemaMof);

            Assert.Null(result);
        }
        finally
        {
            DscResourceHelpers.ClearModuleCache();
            Directory.Delete(root, true);
        }
    }

    #endregion

    #region GetDscResourceModules

    [Fact]
    public void GetDscResourceModules_WithEmptyPsModulePath_ShouldReturnEmpty()
    {
        string? original = Environment.GetEnvironmentVariable("PSModulePath");
        try
        {
            Environment.SetEnvironmentVariable("PSModulePath", string.Empty);

            Assert.Empty(DscResourceHelpers.GetDscResourceModules());
        }
        finally
        {
            Environment.SetEnvironmentVariable("PSModulePath", original);
        }
    }

    [Fact]
    public void GetDscResourceModules_WithNonexistentFolder_ShouldReturnEmpty()
    {
        string? original = Environment.GetEnvironmentVariable("PSModulePath");
        try
        {
            Environment.SetEnvironmentVariable("PSModulePath", Path.Combine(Path.GetTempPath(), "__dscparser_no_such_dir__"));

            Assert.Empty(DscResourceHelpers.GetDscResourceModules());
        }
        finally
        {
            Environment.SetEnvironmentVariable("PSModulePath", original);
        }
    }

    [Fact]
    public void GetDscResourceModules_ShouldFindDirectNestedAndManifestModules()
    {
        string? original = Environment.GetEnvironmentVariable("PSModulePath");
        string root = NewTempDir();
        try
        {
            string modA = Path.Combine(root, "ModADirect");
            Directory.CreateDirectory(Path.Combine(modA, "DscResources"));

            string modB = Path.Combine(root, "ModBNested");
            Directory.CreateDirectory(Path.Combine(modB, "Sub", "DscResources"));

            string modC = Path.Combine(root, "ModCManifest");
            Directory.CreateDirectory(modC);
            File.WriteAllText(Path.Combine(modC, "ModCManifest.psd1"), "DscResourcesToExport = @('X')");

            string modD = Path.Combine(root, "ModDNestedManifest");
            Directory.CreateDirectory(Path.Combine(modD, "Sub"));
            File.WriteAllText(Path.Combine(modD, "Sub", "ModDNestedManifest.psd1"), "DscResourcesToExport = @('Y')");

            string modE = Path.Combine(root, "ModEPlain");
            Directory.CreateDirectory(modE);

            Environment.SetEnvironmentVariable("PSModulePath", root + Path.PathSeparator + Path.Combine(root, "__missing__"));

            var result = DscResourceHelpers.GetDscResourceModules();

            Assert.Contains("ModADirect", result);
            Assert.Contains("ModBNested", result);
            Assert.Contains("ModCManifest", result);
            Assert.Contains("ModDNestedManifest", result);
            Assert.DoesNotContain("ModEPlain", result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PSModulePath", original);
            Directory.Delete(root, true);
        }
    }

    #endregion
}
