using System.Collections;
using DSCParser.CSharp;
using Xunit;

namespace DSCParser.Tests;

/// <summary>
/// Pins the exact rendered layout of ConvertFromDscObject. The Contains-based tests elsewhere do not
/// catch indentation or alignment regressions, which is what a consumer of the generated
/// configuration actually depends on.
/// </summary>
public class DscParserConvertFromDscObjectGoldenTests
{
    private static string Lines(params string[] lines) =>
        string.Join(Environment.NewLine, lines) + Environment.NewLine;

    [Fact]
    public void ThreeLevelNestedCimInstances_ShouldRenderExactly()
    {
        var settings = new Hashtable
        {
            ["CIMInstance"] = "MSFT_Settings",
            ["odataType"] = "#microsoft.graph.x",
            ["autoUpdate"] = "priority"
        };

        var assignment = new Hashtable
        {
            ["CIMInstance"] = "MSFT_Assignment",
            ["groupDisplayName"] = "AADGroup_10",
            ["intent"] = "required",
            ["assignmentSettings"] = settings
        };

        var entry = new Hashtable
        {
            ["ResourceName"] = "IntuneApp",
            ["ResourceInstanceName"] = "MyApp",
            ["DisplayName"] = "App",
            ["Assignments"] = new object[] { assignment }
        };

        string result = DscParser.ConvertFromDscObject([entry]);

        Assert.Equal(
            Lines(
                "IntuneApp \"MyApp\"",
                "{",
                "    Assignments          = @(",
                "        MSFT_Assignment{",
                "            assignmentSettings = MSFT_Settings{",
                "                autoUpdate  = \"priority\"",
                "                odataType   = \"#microsoft.graph.x\"",
                "            }",
                "            groupDisplayName   = \"AADGroup_10\"",
                "            intent             = \"required\"",
                "        }",
                "    )",
                "    DisplayName          = \"App\"",
                "}"),
            result);
    }

    [Fact]
    public void PlainNestedHashtable_ShouldRenderSingleAtSign()
    {
        var entry = new Hashtable
        {
            ["ResourceName"] = "R",
            ["ResourceInstanceName"] = "I",
            ["Config"] = new Hashtable { ["Key1"] = "Value1", ["Key2"] = "Value2" },
            ["Simple"] = "x"
        };

        string result = DscParser.ConvertFromDscObject([entry]);

        Assert.Equal(
            Lines(
                "R \"I\"",
                "{",
                "    Config               = @{",
                "        Key1 = \"Value1\"",
                "        Key2 = \"Value2\"",
                "    }",
                "    Simple               = \"x\"",
                "}"),
            result);

        // @@{ is a PowerShell syntax error; the generated configuration must stay parseable
        Assert.DoesNotContain("@@", result);
    }

    [Fact]
    public void Arrays_ShouldRenderInlineWhenSingleAndMultiLineOtherwise()
    {
        var entry = new Hashtable
        {
            ["ResourceName"] = "R",
            ["ResourceInstanceName"] = "I",
            ["Items"] = new object[] { "a", "b" },
            ["One"] = new object[] { "only" },
            ["Nums"] = new object[] { 1, 2 }
        };

        string result = DscParser.ConvertFromDscObject([entry]);

        Assert.Equal(
            Lines(
                "R \"I\"",
                "{",
                "    Items                = @(",
                "        \"a\"",
                "        \"b\"",
                "    )",
                "    Nums                 = @(",
                "        1",
                "        2",
                "    )",
                "    One                  = @(\"only\")",
                "}"),
            result);
    }

    [Fact]
    public void ListValuedProperty_ShouldRenderAsArrayNotTypeName()
    {
        var entry = new Hashtable
        {
            ["CIMInstance"] = "Test",
            ["Items"] = new List<object> { "a", "b" }
        };

        string result = DscParser.ConvertFromDscObject([entry], 1);

        Assert.DoesNotContain("System.Collections", result);
        Assert.Contains("\"a\"", result);
        Assert.Contains("\"b\"", result);
    }

    [Fact]
    public void DictionaryValuedProperty_ShouldRenderAsNestedObject()
    {
        var entry = new Hashtable
        {
            ["CIMInstance"] = "Test",
            ["Cred"] = new Dictionary<string, object?> { ["CIMInstance"] = "MSFT_Credential", ["UserName"] = "admin" }
        };

        string result = DscParser.ConvertFromDscObject([entry], 1);

        Assert.Contains("= MSFT_Credential{", result);
        Assert.Contains("UserName", result);
        Assert.DoesNotContain("System.Collections", result);
    }

    [Fact]
    public void EmptyHashtable_ShouldRenderWithoutThrowing()
    {
        string result = DscParser.ConvertFromDscObject([new Hashtable()]);

        Assert.Equal(Lines("@{", "}"), result);
    }
}
