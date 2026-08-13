using System.Collections;
using System.Management.Automation.Language;
using DSCParser.CSharp;
using Xunit;

namespace DSCParser.Tests;

public class DscParserConvertFromDscObjectBranchTests
{
    private static string Render(Hashtable entry, int childLevel = 0) =>
        DscParser.ConvertFromDscObject([entry], childLevel);

    private static void AssertReparses(string rendered)
    {
        _ = Parser.ParseInput(rendered, out _, out ParseError[] errors);

        Assert.Empty(errors);
    }

    [Fact]
    public void ConvertFromDscObject_WithNullValuedProperty_ShouldOmitItEntirely()
    {
        string rendered = Render(new Hashtable
        {
            ["ResourceName"] = "R",
            ["ResourceInstanceName"] = "I",
            ["Path"] = "a",
            ["Ensure"] = null,
        });

        Assert.DoesNotContain("Ensure", rendered, StringComparison.Ordinal);
        Assert.Contains("Path", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void ConvertFromDscObject_WithNestedEntryCarryingAResourceName_ShouldKeepTheOpeningLineIndented()
    {
        string rendered = Render(new Hashtable
        {
            ["ResourceName"] = "Outer",
            ["ResourceInstanceName"] = "OuterInstance",
            ["Nested"] = new Hashtable
            {
                ["ResourceName"] = "Inner",
                ["ResourceInstanceName"] = "InnerInstance",
                ["Value"] = "v",
            },
        });

        Assert.Contains("=     Inner \"InnerInstance\"", rendered, StringComparison.Ordinal);
        Assert.Contains("ResourceName         = \"Inner\"", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void ConvertFromDscObject_AtChildLevelAboveZero_ShouldEmitResourceNameAsAProperty()
    {
        string rendered = Render(new Hashtable { ["ResourceName"] = "R", ["Path"] = "a" }, childLevel: 1);

        Assert.Contains("ResourceName", rendered, StringComparison.Ordinal);
        Assert.StartsWith("    ", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void ConvertFromDscObject_AtChildLevelZero_ShouldNotEmitResourceNameAsAProperty()
    {
        string rendered = Render(new Hashtable { ["ResourceName"] = "R", ["ResourceInstanceName"] = "I", ["Path"] = "a" });

        Assert.DoesNotContain("ResourceName  ", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void ConvertFromDscObject_WithEmptyArray_ShouldRenderItInline()
    {
        string rendered = Render(new Hashtable { ["ResourceName"] = "R", ["ResourceInstanceName"] = "I", ["Tags"] = Array.Empty<object>() });

        Assert.Contains("= @()", rendered, StringComparison.Ordinal);
        AssertReparses(rendered);
    }

    [Fact]
    public void ConvertFromDscObject_WithSingleElementSimpleArray_ShouldRenderItInline()
    {
        string rendered = Render(new Hashtable { ["ResourceName"] = "R", ["ResourceInstanceName"] = "I", ["Tags"] = new object[] { "only" } });

        Assert.Contains("= @(\"only\")", rendered, StringComparison.Ordinal);
        AssertReparses(rendered);
    }

    [Fact]
    public void ConvertFromDscObject_WithMultiElementSimpleArray_ShouldRenderOneItemPerLine()
    {
        string rendered = Render(new Hashtable
        {
            ["ResourceName"] = "R",
            ["ResourceInstanceName"] = "I",
            ["Tags"] = new object[] { "first", "second" },
        });

        Assert.DoesNotContain("@(\"first\"", rendered, StringComparison.Ordinal);
        Assert.Contains("= @(", rendered, StringComparison.Ordinal);
        AssertReparses(rendered);
    }

    [Fact]
    public void ConvertFromDscObject_WithArrayMixingDictionariesAndScalars_ShouldRenderEachItemOnItsOwnLine()
    {
        string rendered = Render(new Hashtable
        {
            ["ResourceName"] = "R",
            ["ResourceInstanceName"] = "I",
            ["Items"] = new object[]
            {
                new Hashtable { ["CIMInstance"] = "Setting", ["Name"] = "first" },
                "second",
            },
        });

        Assert.Contains("Setting{", rendered, StringComparison.Ordinal);
        Assert.Contains("\"second\"", rendered, StringComparison.Ordinal);
        AssertReparses(rendered);
    }

    [Fact]
    public void ConvertFromDscObject_WithEmptyArrayInsideANestedInstance_ShouldRenderItInline()
    {
        string rendered = Render(new Hashtable
        {
            ["ResourceName"] = "R",
            ["ResourceInstanceName"] = "I",
            ["Setting"] = new Hashtable
            {
                ["CIMInstance"] = "Setting",
                ["Items"] = Array.Empty<object>(),
            },
        });

        Assert.Contains("= @()", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void ConvertFromDscObject_WithStringStartingWithNewObject_ShouldRenderItAsAnExpression()
    {
        string rendered = Render(new Hashtable
        {
            ["ResourceName"] = "R",
            ["ResourceInstanceName"] = "I",
            ["Credential"] = "New-Object System.Management.Automation.PSCredential('u', $secure)",
        });

        Assert.Contains("= New-Object System.Management.Automation.PSCredential('u', $secure)", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("= \"New-Object", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void ConvertFromDscObject_WithQuotedStringThatIsNotAnExpression_ShouldEscapeTheQuotes()
    {
        string rendered = Render(new Hashtable
        {
            ["ResourceName"] = "R",
            ["ResourceInstanceName"] = "I",
            ["Quoted"] = "\"already quoted\"",
        });

        Assert.Contains("`\"already quoted`\"", rendered, StringComparison.Ordinal);
        AssertReparses(rendered);
    }

    [Fact]
    public void ConvertFromDscObject_WithNonPrimitiveValue_ShouldRenderItWithItsToString()
    {
        string rendered = Render(new Hashtable
        {
            ["ResourceName"] = "R",
            ["ResourceInstanceName"] = "I",
            ["Ratio"] = 3.14,
            ["Version"] = new Version("1.2.3.4"),
        });

        Assert.Contains("= 3.14", rendered, StringComparison.Ordinal);
        Assert.Contains("= 1.2.3.4", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void ConvertFromDscObject_WithValuesNeedingEscape_ShouldProduceTextThatReparses()
    {
        string rendered = Render(new Hashtable
        {
            ["ResourceName"] = "R",
            ["ResourceInstanceName"] = "I",
            ["Quotes"] = "a \"quoted\" value",
            ["Dollar"] = "costs $100",
            ["Backtick"] = "a `backtick`",
            ["Backslash"] = @"C:\temp\path",
        });

        AssertReparses(rendered);
    }

    [Fact]
    public void ConvertFromDscObject_WithThreeLevelsOfNestedInstances_ShouldProduceTextThatReparses()
    {
        string rendered = Render(new Hashtable
        {
            ["ResourceName"] = "R",
            ["ResourceInstanceName"] = "I",
            ["Level1"] = new Hashtable
            {
                ["CIMInstance"] = "One",
                ["Level2"] = new Hashtable
                {
                    ["CIMInstance"] = "Two",
                    ["Level3"] = new Hashtable { ["CIMInstance"] = "Three", ["Value"] = "deep" },
                },
            },
        });

        Assert.Contains("Three{", rendered, StringComparison.Ordinal);
        AssertReparses(rendered);
    }

    [Fact]
    public void ConvertFromDscObject_WithNoResources_ShouldReturnAnEmptyString()
    {
        Assert.Equal(string.Empty, DscParser.ConvertFromDscObject([]));
    }
}
