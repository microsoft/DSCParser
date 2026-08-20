using System.Management.Automation;
using System.Reflection;
using DSCParser.PSDSC;
using Xunit;
using DscResourceInfo = Microsoft.PowerShell.DesiredStateConfiguration.DscResourceInfo;
using ImplementedAsType = Microsoft.PowerShell.DesiredStateConfiguration.ImplementedAsType;

namespace DSCParser.Tests;

/// <summary>
/// Composite resources cannot be reached through <see cref="DscResourceService.GetDscResources"/>
/// because the fresh runspace its Get-Command call opens never autoloads configurations, so
/// <see cref="ResourceProcessor.GetCompositeResource"/> is driven directly here.
/// </summary>
public class ResourceProcessorCompositeTests
{
    private const string CompositeName = "DscParserComposite";

    private const string CompositeParameters = """
        param
        (
            [Parameter(Mandatory = $true)]
            [String]
            $Alpha,

            [Parameter()]
            [Int32[]]
            $Beta
        )
        """;

    private static readonly HashSet<string> IgnoredParameters =
        (HashSet<string>)typeof(DscResourceService)
            .GetField("IgnoreResourceParameters", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

    public static TheoryData<string> DeclaredIgnoredParameterNames =>
        [.. ConfigurationInfoFactory.Create(CompositeName, CompositeParameters).Parameters.Keys.Where(IgnoredParameters.Contains)];

    private static ConfigurationInfo NewComposite() => ConfigurationInfoFactory.Create(CompositeName, CompositeParameters);

    [Fact]
    public void GetCompositeResource_WithNonMatchingPattern_ShouldReturnNull()
    {
        Assert.Null(ResourceProcessor.GetCompositeResource(
            ["__DscParserNoSuchComposite__"], NewComposite(), IgnoredParameters, []));
    }

    [Fact]
    public void GetCompositeResource_WithWildcardPattern_ShouldMatchTheConfigurationName()
    {
        DscResourceInfo? resource = ResourceProcessor.GetCompositeResource(
            ["DscParser*"], NewComposite(), IgnoredParameters, []);

        Assert.NotNull(resource);
        Assert.Equal(CompositeName, resource!.Name);
    }

    [Fact]
    public void GetCompositeResource_WithoutPatterns_ShouldMatchEveryConfiguration()
    {
        Assert.NotNull(ResourceProcessor.GetCompositeResource([], NewComposite(), IgnoredParameters, []));
    }

    [Fact]
    public void GetCompositeResource_WithModulelessConfiguration_ShouldReportACompositeWithoutPaths()
    {
        DscResourceInfo resource = ResourceProcessor.GetCompositeResource([], NewComposite(), IgnoredParameters, [])!;

        Assert.Equal(ImplementedAsType.Composite, resource.ImplementedAs);
        Assert.Null(resource.ImplementationDetail);
        Assert.Equal(CompositeName, resource.Name);
        Assert.Equal(CompositeName, resource.ResourceType);
        Assert.Null(resource.Module);
        Assert.Null(resource.Path);
        Assert.Null(resource.ParentPath);
        Assert.Null(resource.CompanyName);
    }

    [Fact]
    public void GetCompositeResource_ShouldExposeDeclaredParametersAsProperties()
    {
        DscResourceInfo resource = ResourceProcessor.GetCompositeResource([], NewComposite(), IgnoredParameters, [])!;

        var alpha = resource.PropertiesAsResourceInfo.Single(p => p.Name == "Alpha");
        var beta = resource.PropertiesAsResourceInfo.Single(p => p.Name == "Beta");

        Assert.True(alpha.IsMandatory);
        Assert.Equal("[String]", alpha.PropertyType);
        Assert.False(beta.IsMandatory);
        Assert.Equal("[Int32[]]", beta.PropertyType);
    }

    [Theory]
    [MemberData(nameof(DeclaredIgnoredParameterNames))]
    public void GetCompositeResource_ShouldOmitEveryIgnoredParameterName(string ignoredParameter)
    {
        DscResourceInfo resource = ResourceProcessor.GetCompositeResource([], NewComposite(), IgnoredParameters, [])!;

        Assert.DoesNotContain(ignoredParameter, resource.PropertiesAsResourceInfo.Select(p => p.Name));
    }

    [Fact]
    public void GetCompositeResource_WithoutIgnoredParameters_ShouldKeepTheCommonParameters()
    {
        DscResourceInfo resource = ResourceProcessor.GetCompositeResource([], NewComposite(), [], [])!;

        var names = resource.PropertiesAsResourceInfo.Select(p => p.Name).ToList();

        Assert.Contains("Verbose", names);
        Assert.Contains("ErrorAction", names);
        Assert.Contains("Alpha", names);
    }

    [Fact]
    public void GetCompositeResource_WithAModuleUnknownToDiscovery_ShouldFallBackToTheConfigurationsOwnModule()
    {
        using var module = TestDscModule.CreateCompositeResource(
            "DscParserCompositeFallback", CompositeName, CompositeParameters);

        ConfigurationInfo configuration = NewComposite();
        ConfigurationInfoFactory.SetModule(configuration, module.ModuleInfo);

        try
        {
            DscResourceInfo resource = ResourceProcessor.GetCompositeResource([], configuration, IgnoredParameters, [])!;

            Assert.Same(module.ModuleInfo, resource.Module);
            Assert.Equal(module.ModuleInfo.Path, resource.Path);
            Assert.Equal(Path.GetDirectoryName(module.ModuleInfo.Path), resource.ParentPath);
        }
        finally
        {
            DscResourceHelpers.ClearModuleCache();
        }
    }

    [Fact]
    public void GetCompositeResource_WithSchemaPsm1UnderDscResources_ShouldResolveTheModuleThroughDiscovery()
    {
        using var module = TestDscModule.CreateCompositeResource(
            "DscParserCompositeOnDisk", CompositeName, CompositeParameters);

        PSModuleInfo declaringModule = PsModuleInfoFactory.Create(
            $"{CompositeName}.Schema", module.SchemaPsm1Path(CompositeName));

        ConfigurationInfo configuration = NewComposite();
        ConfigurationInfoFactory.SetModule(configuration, declaringModule);

        try
        {
            DscResourceInfo resource = ResourceProcessor.GetCompositeResource(
                [], configuration, IgnoredParameters, [module.ModuleInfo])!;

            Assert.Same(module.ModuleInfo, resource.Module);
            Assert.Equal(module.SchemaPsm1Path(CompositeName), resource.Path);
            Assert.Equal(module.ResourceFolder(CompositeName), resource.ParentPath);
        }
        finally
        {
            DscResourceHelpers.ClearModuleCache();
        }
    }

    [Fact]
    public void GetCompositeResource_WithSchemaPsm1_ShouldProduceAPathThatSurvivesTheServiceFilter()
    {
        using var module = TestDscModule.CreateCompositeResource(
            "DscParserCompositeFilter", CompositeName, CompositeParameters);

        ConfigurationInfo configuration = NewComposite();
        ConfigurationInfoFactory.SetModule(
            configuration,
            PsModuleInfoFactory.Create($"{CompositeName}.Schema", module.SchemaPsm1Path(CompositeName)));

        try
        {
            DscResourceInfo resource = ResourceProcessor.GetCompositeResource(
                [], configuration, IgnoredParameters, [module.ModuleInfo])!;

            Assert.False(string.IsNullOrEmpty(resource.Path));
            Assert.Equal($"{resource.Name}.schema.psm1", Path.GetFileName(resource.Path), ignoreCase: true);
        }
        finally
        {
            DscResourceHelpers.ClearModuleCache();
        }
    }

    [Fact]
    public void GetCompositeResource_WithModuleWithoutPath_ShouldLeaveParentPathNull()
    {
        ConfigurationInfo configuration = NewComposite();
        ConfigurationInfoFactory.SetModule(configuration, PsModuleInfoFactory.CreateNameOnly("DscParserPathlessModule"));

        try
        {
            DscResourceInfo resource = ResourceProcessor.GetCompositeResource([], configuration, IgnoredParameters, [])!;

            Assert.True(string.IsNullOrEmpty(resource.Path));
            Assert.Null(resource.ParentPath);
        }
        finally
        {
            DscResourceHelpers.ClearModuleCache();
        }
    }
}
