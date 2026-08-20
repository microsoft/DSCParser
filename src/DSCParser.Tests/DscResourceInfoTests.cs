using Xunit;
using DscResourceInfo = Microsoft.PowerShell.DesiredStateConfiguration.DscResourceInfo;
using DscResourcePropertyInfo = Microsoft.PowerShell.DesiredStateConfiguration.DscResourcePropertyInfo;
using ImplementedAsType = Microsoft.PowerShell.DesiredStateConfiguration.ImplementedAsType;

namespace DSCParser.Tests;

public class DscResourceInfoTests
{
    #region Constructor / Default State

    [Fact]
    public void Constructor_ShouldInitializePropertiesAsList()
    {
        var info = new DscResourceInfo();

        Assert.NotNull(info.Properties);
        Assert.Empty(info.Properties);
    }

    [Fact]
    public void Constructor_ShouldHaveNullModuleName()
    {
        var info = new DscResourceInfo();

        Assert.Null(info.ModuleName);
    }

    [Fact]
    public void Constructor_ShouldHaveNullVersion()
    {
        var info = new DscResourceInfo();

        Assert.Null(info.Version);
    }

    #endregion

    #region Property Getters/Setters

    [Fact]
    public void Name_ShouldBeSettableAndGettable()
    {
        var info = new DscResourceInfo { Name = "TestResource" };

        Assert.Equal("TestResource", info.Name);
    }

    [Fact]
    public void ResourceType_ShouldBeSettableAndGettable()
    {
        var info = new DscResourceInfo { ResourceType = "MSFT_TestResource" };

        Assert.Equal("MSFT_TestResource", info.ResourceType);
    }

    [Fact]
    public void FriendlyName_ShouldBeSettableAndGettable()
    {
        var info = new DscResourceInfo { FriendlyName = "TestFriendly" };

        Assert.Equal("TestFriendly", info.FriendlyName);
    }

    [Fact]
    public void Path_ShouldBeSettableAndGettable()
    {
        var info = new DscResourceInfo { Path = @"C:\DSC\test.psm1" };

        Assert.Equal(@"C:\DSC\test.psm1", info.Path);
    }

    [Fact]
    public void ParentPath_ShouldBeSettableAndGettable()
    {
        var info = new DscResourceInfo { ParentPath = @"C:\DSC" };

        Assert.Equal(@"C:\DSC", info.ParentPath);
    }

    [Fact]
    public void ImplementedAs_ShouldBeSettableAndGettable()
    {
        var info = new DscResourceInfo { ImplementedAs = ImplementedAsType.PowerShell };

        Assert.Equal(ImplementedAsType.PowerShell, info.ImplementedAs);
    }

    [Fact]
    public void CompanyName_ShouldBeSettableAndGettable()
    {
        var info = new DscResourceInfo { CompanyName = "Microsoft" };

        Assert.Equal("Microsoft", info.CompanyName);
    }

    [Fact]
    public void ImplementationDetail_ShouldBeSettableAndGettable()
    {
        var info = new DscResourceInfo { ImplementationDetail = "ScriptBased" };

        Assert.Equal("ScriptBased", info.ImplementationDetail);
    }

    #endregion

    #region ImplementedAsType Enum

    [Fact]
    public void ImplementedAsType_None_ShouldBeZero()
    {
        Assert.Equal(0, (int)ImplementedAsType.None);
    }

    [Fact]
    public void ImplementedAsType_PowerShell_ShouldBeOne()
    {
        Assert.Equal(1, (int)ImplementedAsType.PowerShell);
    }

    [Fact]
    public void ImplementedAsType_Binary_ShouldBeTwo()
    {
        Assert.Equal(2, (int)ImplementedAsType.Binary);
    }

    [Fact]
    public void ImplementedAsType_Composite_ShouldBeThree()
    {
        Assert.Equal(3, (int)ImplementedAsType.Composite);
    }

    #endregion

    #region UpdateProperties

    [Fact]
    public void UpdateProperties_WithDscResourcePropertyInfoList_ShouldSetProperties()
    {
        var info = new DscResourceInfo();
        var props = new List<DscResourcePropertyInfo>
        {
            new() { Name = "Prop1", PropertyType = "[String]", IsMandatory = true },
            new() { Name = "Prop2", PropertyType = "[Int32]", IsMandatory = false }
        };

        info.UpdateProperties(props);

        Assert.Equal(2, info.Properties.Count);
    }

    [Fact]
    public void UpdateProperties_WithObjectList_ShouldSetProperties()
    {
        var info = new DscResourceInfo();
        var props = new List<object>
        {
            new DscResourcePropertyInfo { Name = "Prop1", PropertyType = "[String]" }
        };

        info.UpdateProperties(props);

        Assert.Single(info.Properties);
    }

    [Fact]
    public void PropertiesAsResourceInfo_ShouldReturnConvertedList()
    {
        var info = new DscResourceInfo();
        var props = new List<DscResourcePropertyInfo>
        {
            new() { Name = "Ensure", PropertyType = "[String]", IsMandatory = true },
            new() { Name = "Path", PropertyType = "[String]", IsMandatory = false }
        };
        info.UpdateProperties(props);

        var result = info.PropertiesAsResourceInfo;

        Assert.Equal(2, result.Count);
        Assert.Equal("Ensure", result[0].Name);
        Assert.Equal("Path", result[1].Name);
    }

    #endregion

    #region Properties / PropertiesAsResourceInfo share one backing store

    [Fact]
    public void Properties_Add_ShouldBeVisibleFromPropertiesAsResourceInfo()
    {
        var info = new DscResourceInfo();
        var prop = new DscResourcePropertyInfo { Name = "Ensure", PropertyType = "[String]" };

        info.Properties.Add(prop);

        Assert.Single(info.PropertiesAsResourceInfo);
        Assert.Same(prop, info.PropertiesAsResourceInfo[0]);
    }

    [Fact]
    public void PropertiesAsResourceInfo_Add_ShouldBeVisibleFromProperties()
    {
        var info = new DscResourceInfo();
        var prop = new DscResourcePropertyInfo { Name = "Path", PropertyType = "[String]" };

        info.PropertiesAsResourceInfo.Add(prop);

        Assert.Single(info.Properties);
        Assert.Same(prop, info.Properties[0]);
    }

    [Fact]
    public void Properties_Add_ShouldSurviveSubsequentAddProperty()
    {
        var info = new DscResourceInfo();
        info.Properties.Add(new DscResourcePropertyInfo { Name = "First" });

        info.AddProperty(new DscResourcePropertyInfo { Name = "Second" });

        Assert.Equal(2, info.Properties.Count);
        Assert.Equal(["First", "Second"], info.PropertiesAsResourceInfo.Select(p => p.Name));
    }

    [Fact]
    public void UpdateProperties_ShouldBeVisibleThroughAPreviouslyReadPropertiesReference()
    {
        var info = new DscResourceInfo();
        info.AddProperty(new DscResourcePropertyInfo { Name = "Stale" });

        var captured = info.Properties;
        var captiredAsResourceInfo = info.PropertiesAsResourceInfo;

        info.UpdateProperties([new DscResourcePropertyInfo { Name = "Fresh" }]);

        Assert.Single(captured);
        Assert.Equal("Fresh", ((DscResourcePropertyInfo)captured[0]).Name);
        Assert.Single(captiredAsResourceInfo);
        Assert.Equal("Fresh", captiredAsResourceInfo[0].Name);
    }

    [Fact]
    public void UpdateProperties_WithSelf_ShouldBeANoOp()
    {
        var info = new DscResourceInfo();
        info.AddProperty(new DscResourcePropertyInfo { Name = "Keep" });

        info.UpdateProperties(info.Properties);

        Assert.Single(info.Properties);
        Assert.Equal("Keep", info.PropertiesAsResourceInfo[0].Name);
    }

    [Fact]
    public void UpdateProperties_WithNonPropertyObject_ShouldThrowAndLeaveExistingPropertiesIntact()
    {
        var info = new DscResourceInfo();
        info.AddProperty(new DscResourcePropertyInfo { Name = "Existing" });

        var bad = new List<object>
        {
            new DscResourcePropertyInfo { Name = "Good" },
            "not a property"
        };

        Assert.Throws<InvalidCastException>(() => info.UpdateProperties(bad));

        Assert.Single(info.Properties);
        Assert.Equal("Existing", info.PropertiesAsResourceInfo[0].Name);
    }

    #endregion

    #region PropertiesAsResourceInfo full IList surface

    [Fact]
    public void PropertiesAsResourceInfo_IndexerSet_ShouldReplaceElementInSharedStore()
    {
        var info = new DscResourceInfo();
        info.AddProperty(new DscResourcePropertyInfo { Name = "First" });
        var replacement = new DscResourcePropertyInfo { Name = "Second" };

        info.PropertiesAsResourceInfo[0] = replacement;

        Assert.Same(replacement, info.Properties[0]);
        Assert.Equal("Second", info.PropertiesAsResourceInfo[0].Name);
    }

    [Fact]
    public void PropertiesAsResourceInfo_InsertAndRemoveAt_ShouldMutateSharedStore()
    {
        var info = new DscResourceInfo();
        info.AddProperty(new DscResourcePropertyInfo { Name = "A" });
        info.AddProperty(new DscResourcePropertyInfo { Name = "B" });
        var inserted = new DscResourcePropertyInfo { Name = "Mid" };

        info.PropertiesAsResourceInfo.Insert(1, inserted);
        Assert.Equal(["A", "Mid", "B"], info.PropertiesAsResourceInfo.Select(p => p.Name));

        info.PropertiesAsResourceInfo.RemoveAt(1);
        Assert.Equal(["A", "B"], info.PropertiesAsResourceInfo.Select(p => p.Name));
    }

    [Fact]
    public void PropertiesAsResourceInfo_Remove_ShouldReturnWhetherItemWasPresent()
    {
        var info = new DscResourceInfo();
        var present = new DscResourcePropertyInfo { Name = "A" };
        info.AddProperty(present);

        Assert.True(info.PropertiesAsResourceInfo.Remove(present));
        Assert.False(info.PropertiesAsResourceInfo.Remove(new DscResourcePropertyInfo()));
        Assert.Empty(info.Properties);
    }

    [Fact]
    public void PropertiesAsResourceInfo_Clear_ShouldEmptySharedStore()
    {
        var info = new DscResourceInfo();
        info.AddProperty(new DscResourcePropertyInfo { Name = "A" });
        info.AddProperty(new DscResourcePropertyInfo { Name = "B" });

        info.PropertiesAsResourceInfo.Clear();

        Assert.Empty(info.Properties);
    }

    [Fact]
    public void PropertiesAsResourceInfo_ContainsAndIndexOf_ShouldSearchSharedStore()
    {
        var info = new DscResourceInfo();
        var target = new DscResourcePropertyInfo { Name = "B" };
        info.AddProperty(new DscResourcePropertyInfo { Name = "A" });
        info.AddProperty(target);
        info.AddProperty(new DscResourcePropertyInfo { Name = "C" });

        Assert.True(info.PropertiesAsResourceInfo.Contains(target));
        Assert.Equal(1, info.PropertiesAsResourceInfo.IndexOf(target));
        Assert.False(info.PropertiesAsResourceInfo.Contains(new DscResourcePropertyInfo()));
    }

    [Fact]
    public void PropertiesAsResourceInfo_IsReadOnly_ShouldBeFalse()
    {
        Assert.False(new DscResourceInfo().PropertiesAsResourceInfo.IsReadOnly);
    }

    [Fact]
    public void PropertiesAsResourceInfo_CopyTo_ShouldCopyIntoArrayAtIndex()
    {
        var info = new DscResourceInfo();
        info.AddProperty(new DscResourcePropertyInfo { Name = "A" });
        info.AddProperty(new DscResourcePropertyInfo { Name = "B" });

        var array = new DscResourcePropertyInfo[4];
        info.PropertiesAsResourceInfo.CopyTo(array, 1);

        Assert.Null(array[0]);
        Assert.Equal("A", array[1].Name);
        Assert.Equal("B", array[2].Name);
        Assert.Null(array[3]);
    }

    #endregion
}
