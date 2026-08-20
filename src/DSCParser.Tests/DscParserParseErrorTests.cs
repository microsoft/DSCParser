using System.Management.Automation.Language;
using System.Reflection;
using DSCParser.CSharp;
using Xunit;

namespace DSCParser.Tests;

public class DscParserParseErrorTests
{
    private static readonly MethodInfo _reportParseErrors =
        typeof(DscParser).GetMethod("ReportParseErrors", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ReportParseErrors method not found");

    private static List<string> CaptureWarnings(ParseError[] errors, ConfigurationDefinitionAst? configAst, string errorPrefix = "")
    {
        var warnings = new List<string>();
        DscParser.WarningSink = warnings.Add;
        try
        {
            _ = _reportParseErrors.Invoke(null, [errors, configAst, errorPrefix]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
        finally
        {
            DscParser.WarningSink = null;
        }

        return warnings;
    }

    [Fact]
    public void ReportParseErrors_WithErrorOutsideTheConfigurationBlock_ShouldNotWarn()
    {
        const string content = "Configuration Test { }\r\n!!! unexpected !!!";
        var scriptAst = Parser.ParseInput(content, out _, out ParseError[] errors);
        var configAst = scriptAst.Find(a => a is ConfigurationDefinitionAst, false) as ConfigurationDefinitionAst;

        Assert.NotNull(configAst);
        Assert.NotEmpty(errors);

        Assert.Empty(CaptureWarnings(errors, configAst));
    }

    [Fact]
    public void ReportParseErrors_WithUnrecoverableErrorInsideTheConfigurationBlock_ShouldThrow()
    {
        const string content = "Configuration Test {\r\n    $x = @{\r\n}";
        var scriptAst = Parser.ParseInput(content, out _, out ParseError[] errors);
        var configAst = scriptAst.Find(a => a is ConfigurationDefinitionAst, false) as ConfigurationDefinitionAst;

        Assert.NotEmpty(errors);

        var exception = Assert.Throws<InvalidOperationException>(() => CaptureWarnings(errors, configAst));
        Assert.Contains("Error parsing configuration", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportParseErrors_WithoutAConfigurationBlock_ShouldTreatEveryErrorAsRelevant()
    {
        const string content = "$x = @{";
        _ = Parser.ParseInput(content, out _, out ParseError[] errors);

        Assert.NotEmpty(errors);

        Assert.Throws<InvalidOperationException>(() => CaptureWarnings(errors, null));
    }

    [Fact]
    public void ReportParseErrors_ShouldPrefixTheMessageWithTheSourceFile()
    {
        const string content = "$x = @{";
        _ = Parser.ParseInput(content, out _, out ParseError[] errors);

        var exception = Assert.Throws<InvalidOperationException>(
            () => CaptureWarnings(errors, null, "C:\\configs\\broken.ps1 - "));

        Assert.StartsWith("C:\\configs\\broken.ps1 - ", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportParseErrors_WithNoErrors_ShouldNotWarn()
    {
        Assert.Empty(CaptureWarnings([], null));
    }
}
