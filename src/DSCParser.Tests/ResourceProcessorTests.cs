using DSCParser.PSDSC;
using System.Management.Automation.Language;
using Xunit;
using DscResourceInfo = Microsoft.PowerShell.DesiredStateConfiguration.DscResourceInfo;
using ImplementedAsType = Microsoft.PowerShell.DesiredStateConfiguration.ImplementedAsType;

namespace DSCParser.Tests;

/// <summary>
/// Exercises <c>ResourceProcessor.GetResourceFromKeyword</c> against hand-built
/// <see cref="DynamicKeyword"/> instances. These tests deliberately pass an empty module list so the
/// class-based fallback path runs without needing a PowerShell engine cache.
/// </summary>
public class ResourceProcessorTests
{
    private static DynamicKeyword CreateKeyword(string keywordName = "TestResource", string resourceName = "TestResource")
    {
        var keyword = new DynamicKeyword
        {
            Keyword = keywordName,
            ResourceName = resourceName,
            ImplementingModule = "TestModule",
            ImplementingModuleVersion = new Version("1.0.0.0")
        };

        keyword.Properties["Ensure"] = new DynamicKeywordProperty
        {
            Name = "Ensure",
            TypeConstraint = "[string]",
            Mandatory = true
        };

        var state = new DynamicKeywordProperty
        {
            Name = "State",
            TypeConstraint = "[string]",
            Mandatory = false
        };
        state.ValueMap["Started"] = "Started";
        keyword.Properties["State"] = state;

        keyword.Properties["ResourceId"] = new DynamicKeywordProperty
        {
            Name = "ResourceId",
            TypeConstraint = "[string]",
            Mandatory = false
        };

        return keyword;
    }

    [Fact]
    public void GetResourceFromKeyword_WithNonMatchingPatterns_ShouldReturnNull()
    {
        DynamicKeyword keyword = CreateKeyword();

        DscResourceInfo? resource = ResourceProcessor.GetResourceFromKeyword(keyword, ["Other"], []);

        Assert.Null(resource);
    }

    [Fact]
    public void GetResourceFromKeyword_WithNoModule_ShouldReturnClassBasedBinaryResource()
    {
        DynamicKeyword keyword = CreateKeyword();

        DscResourceInfo? resource = ResourceProcessor.GetResourceFromKeyword(keyword, ["*"], []);

        Assert.NotNull(resource);
        Assert.Equal("TestResource", resource!.ResourceType);
        Assert.Equal("TestResource", resource.Name);
        Assert.Null(resource.FriendlyName);
        Assert.Equal(ImplementedAsType.Binary, resource.ImplementedAs);
        Assert.Null(resource.ImplementationDetail);
        Assert.Null(resource.Path);
        Assert.Null(resource.ParentPath);
        Assert.Null(resource.Module);

        // ResourceId is ignored; mandatory properties sort first.
        Assert.Equal(["Ensure", "State"], resource.PropertiesAsResourceInfo.Select(p => p.Name));
        Assert.True(resource.PropertiesAsResourceInfo[0].IsMandatory);
        Assert.Equal("[[string]]", resource.PropertiesAsResourceInfo[0].PropertyType);
        Assert.Equal(["Started"], resource.PropertiesAsResourceInfo[1].Values);
    }

    [Fact]
    public void GetResourceFromKeyword_WithDifferentResourceName_ShouldSetFriendlyName()
    {
        DynamicKeyword keyword = CreateKeyword("TestResource", "MSFT_TestResource");

        DscResourceInfo? resource = ResourceProcessor.GetResourceFromKeyword(keyword, ["TestResource"], []);

        Assert.NotNull(resource);
        Assert.Equal("MSFT_TestResource", resource!.ResourceType);
        // FriendlyName mirrors the keyword name when it differs from the resource type.
        Assert.Equal("TestResource", resource.FriendlyName);
    }

    [Fact]
    public void GetResourceFromKeyword_WithPatternMatchingKeyword_ShouldMatch()
    {
        DynamicKeyword keyword = CreateKeyword("TestResource", "MSFT_TestResource");

        DscResourceInfo? resource = ResourceProcessor.GetResourceFromKeyword(keyword, ["*Resource"], []);

        Assert.NotNull(resource);
    }

    [Fact]
    public void GetResourceFromKeyword_WithMatchingModule_ShouldAttachModuleAndPath()
    {
        DynamicKeyword keyword = CreateKeyword();
        keyword.ImplementingModule = "ModA";
        keyword.ImplementingModuleVersion = new Version("1.2.3.4");

        var module = PsModuleInfoFactory.Create("ModA", @"C:\ModA\ModA.psd1");
        PsModuleInfoFactory.SetVersion(module, new Version("1.2.3.4"));

        DscResourceInfo? resource = ResourceProcessor.GetResourceFromKeyword(keyword, ["TestResource"], [module]);

        Assert.NotNull(resource);
        Assert.Same(module, resource!.Module);
        Assert.Equal(@"C:\ModA\ModA.psd1", resource.Path);
        Assert.Equal(@"C:\ModA", resource.ParentPath);
        Assert.Equal(ImplementedAsType.PowerShell, resource.ImplementedAs);
        Assert.Equal("ClassBased", resource.ImplementationDetail);
        Assert.Null(resource.CompanyName);
        Assert.Equal(["Ensure", "State"], resource.PropertiesAsResourceInfo.Select(p => p.Name));
    }

    [Fact]
    public void GetResourceFromKeyword_WithMatchingModuleAndNoMatch_ShouldNotThrow()
    {
        DynamicKeyword keyword = CreateKeyword();
        keyword.ImplementingModule = "ModA";
        keyword.ImplementingModuleVersion = new Version("1.2.3.4");

        var module = PsModuleInfoFactory.Create("ModA", @"C:\ModA\ModA.psd1");
        PsModuleInfoFactory.SetVersion(module, new Version("1.2.3.4"));

        DscResourceInfo? resource = ResourceProcessor.GetResourceFromKeyword(keyword, ["Other"], [module]);

        Assert.Null(resource);
    }

    [Fact]
    public void GetResourceFromKeyword_WithNonMatchingModuleName_ShouldReturnClassBasedBinary()
    {
        DynamicKeyword keyword = CreateKeyword();
        var module = PsModuleInfoFactory.Create("OtherModule", @"C:\OtherModule\OtherModule.psd1");

        DscResourceInfo? resource = ResourceProcessor.GetResourceFromKeyword(keyword, ["TestResource"], [module]);

        Assert.NotNull(resource);
        Assert.Null(resource!.Module);
        Assert.Equal(ImplementedAsType.Binary, resource.ImplementedAs);
        Assert.Null(resource.ImplementationDetail);
    }

    [Fact]
    public void GetResourceFromKeyword_WithMatchingNameButDifferentVersion_ShouldReturnClassBasedBinary()
    {
        DynamicKeyword keyword = CreateKeyword();
        var module = PsModuleInfoFactory.Create("TestModule", @"C:\TestModule\TestModule.psd1");
        PsModuleInfoFactory.SetVersion(module, new Version("9.9.9.9"));

        DscResourceInfo? resource = ResourceProcessor.GetResourceFromKeyword(keyword, ["TestResource"], [module]);

        Assert.NotNull(resource);
        Assert.Null(resource!.Module);
        Assert.Equal(ImplementedAsType.Binary, resource.ImplementedAs);
    }

    [Fact]
    public void GetResourceFromKeyword_WithModuleWithoutPath_ShouldHandleNullPath()
    {
        DynamicKeyword keyword = CreateKeyword();
        keyword.ImplementingModule = "ModA";
        keyword.ImplementingModuleVersion = new Version("0.0");

        var module = PsModuleInfoFactory.CreateNameOnly("ModA");

        DscResourceInfo? resource = ResourceProcessor.GetResourceFromKeyword(keyword, ["TestResource"], [module]);

        Assert.NotNull(resource);
        Assert.Same(module, resource!.Module);
        Assert.Equal(string.Empty, resource.Path);
        Assert.Null(resource.ParentPath);
        Assert.Equal(ImplementedAsType.Binary, resource.ImplementedAs);
        Assert.Null(resource.ImplementationDetail);
    }
}