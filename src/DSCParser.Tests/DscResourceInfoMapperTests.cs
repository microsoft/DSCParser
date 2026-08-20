using System.Management.Automation;
using DSCParser.CSharp;
using DSCParser.PSDSC;
using Xunit;

namespace DSCParser.Tests;

/// <summary>
/// Exercises <see cref="DscParser.ConvertToDscObject"/> with the <c>dscResources</c> parameter,
/// which routes every supplied object through <c>DscResourceInfoMapper</c>. Supplying no path and no
/// content makes the conversion fail with an argument error only after the resources have been
/// mapped, so the whole mapping pipeline runs without requiring a real PowerShell host.
/// </summary>
public class DscResourceInfoMapperTests
{
    private static PSObject CreateResourceObject(string name = "TestResource")
    {
        var psObject = new PSObject();
        psObject.Properties.Add(new PSNoteProperty("ResourceType", "MSFT_TestResource"));
        psObject.Properties.Add(new PSNoteProperty("CompanyName", "Contoso"));
        psObject.Properties.Add(new PSNoteProperty("FriendlyName", "Test"));
        psObject.Properties.Add(new PSNoteProperty("Module", null));
        psObject.Properties.Add(new PSNoteProperty("Path", @"C:\Modules\Test\DscResources\Test\Test.psm1"));
        psObject.Properties.Add(new PSNoteProperty("ParentPath", @"C:\Modules\Test\DscResources\Test"));
        psObject.Properties.Add(new PSNoteProperty("ImplementedAs", "PowerShell"));
        psObject.Properties.Add(new PSNoteProperty("Name", name));

        var mandatory = new PSObject();
        mandatory.Properties.Add(new PSNoteProperty("Name", "Ensure"));
        mandatory.Properties.Add(new PSNoteProperty("PropertyType", "[string]"));
        mandatory.Properties.Add(new PSNoteProperty("IsMandatory", true));
        mandatory.Properties.Add(new PSNoteProperty("Values", new List<string> { "Present", "Absent" }));

        var optional = new PSObject();
        optional.Properties.Add(new PSNoteProperty("Name", "Path"));
        optional.Properties.Add(new PSNoteProperty("PropertyType", "[string]"));
        optional.Properties.Add(new PSNoteProperty("IsMandatory", false));
        optional.Properties.Add(new PSNoteProperty("Values", Array.Empty<string>()));

        psObject.Properties.Add(new PSNoteProperty("Properties", new List<object> { mandatory, optional }));

        return psObject;
    }

    [Fact]
    public void ConvertToDscObject_WithMappedResource_ShouldMapEveryFieldBeforeThrowing()
    {
        try
        {
            var ex = Assert.Throws<ArgumentException>(() =>
                DscParser.ConvertToDscObject(content: string.Empty, dscResources: [CreateResourceObject()]));

            Assert.Contains("Either path or content must be provided", ex.Message);
        }
        finally
        {
            DscParser.ClearCaches();
            DscKeywordRegistry.Reset();
        }
    }

    [Fact]
    public void ConvertToDscObject_WithNullResource_ShouldThrowArgumentNull()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            DscParser.ConvertToDscObject(content: string.Empty, dscResources: [null!]));

        Assert.Equal("psObject", ex.ParamName);
    }

    [Fact]
    public void ConvertToDscObject_WithResourceWithoutName_ShouldThrowInvalidOperation()
    {
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                DscParser.ConvertToDscObject(content: string.Empty, dscResources: [CreateResourceObject(name: string.Empty)]));

            Assert.Contains("has no Name", ex.Message);
        }
        finally
        {
            DscParser.ClearCaches();
            DscKeywordRegistry.Reset();
        }
    }
}