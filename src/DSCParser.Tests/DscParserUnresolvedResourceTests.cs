using System.Collections.ObjectModel;
using System.Management.Automation.Language;
using System.Reflection;
using DSCParser.CSharp;
using Xunit;

namespace DSCParser.Tests;

/// <summary>
/// Covers configurations that reference resources or properties the installed module version no
/// longer defines. An unresolved resource is not a DSC keyword, so it parses the same way with or
/// without a real PowerShell host, which is what makes these paths reachable from a unit test.
/// </summary>
public class DscParserUnresolvedResourceTests
{
    private static readonly MethodInfo _skipUnresolvedResource =
        typeof(DscParser).GetMethod("SkipUnresolvedResource", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("SkipUnresolvedResource method not found");

    private static readonly MethodInfo _isRecoverableParseError =
        typeof(DscParser).GetMethod("IsRecoverableParseError", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("IsRecoverableParseError method not found");

    private static readonly MethodInfo _describeParseError =
        typeof(DscParser).GetMethod("DescribeParseError", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("DescribeParseError method not found");

    private static ReadOnlyCollection<StatementAst> Statements(string source)
    {
        var scriptAst = Parser.ParseInput(source, out _, out ParseError[] errors);
        Assert.Empty(errors);

        return scriptAst.EndBlock.Statements;
    }

    private static (int Consumed, List<string> Warnings) Skip(ReadOnlyCollection<StatementAst> statements, int index)
    {
        List<string> warnings = [];
        DscParser.WarningSink = warnings.Add;
        try
        {
            var consumed = (int)_skipUnresolvedResource.Invoke(null, [statements, index])!;
            return (consumed, warnings);
        }
        finally
        {
            DscParser.WarningSink = null;
        }
    }

    private static ParseError Error(string errorId, string message)
    {
        var extent = Parser.ParseInput("Id", out _, out _).Extent;
        return new ParseError(extent, errorId, message);
    }

    private const string NextLineBrace = """
        GoneResource "InstA"
        {
            Foo = "1"
        }
        """;

    private const string SameLineBrace = """
        GoneResource "InstB" {
            Bar = "2"
        }
        """;

    #region SkipUnresolvedResource

    [Fact]
    public void Skip_WithBraceOnNextLine_ShouldConsumeTheDetachedBody()
    {
        var statements = Statements(NextLineBrace);
        Assert.Equal(2, statements.Count);

        var (consumed, _) = Skip(statements, 0);

        Assert.Equal(1, consumed);
    }

    [Fact]
    public void Skip_WithBraceOnSameLine_ShouldConsumeNothingExtra()
    {
        var statements = Statements(SameLineBrace);
        Assert.Single(statements);

        var (consumed, _) = Skip(statements, 0);

        Assert.Equal(0, consumed);
    }

    [Fact]
    public void Skip_ShouldWarnOnceNamingResourceAndInstance()
    {
        var (_, warnings) = Skip(Statements(NextLineBrace), 0);

        var warning = Assert.Single(warnings);
        Assert.Contains("GoneResource", warning);
        Assert.Contains("InstA", warning);
        Assert.Contains("omitted", warning);
    }

    [Fact]
    public void Skip_WithSameLineBrace_ShouldWarnNamingResourceAndInstance()
    {
        var (_, warnings) = Skip(Statements(SameLineBrace), 0);

        var warning = Assert.Single(warnings);
        Assert.Contains("GoneResource", warning);
        Assert.Contains("InstB", warning);
    }

    [Fact]
    public void Skip_WithTrailingResourceAfterDetachedBody_ShouldNotConsumeIt()
    {
        // Two unresolved resources back to back must not swallow the second one's header.
        var statements = Statements(NextLineBrace + Environment.NewLine + NextLineBrace);
        Assert.Equal(4, statements.Count);

        var (consumed, _) = Skip(statements, 0);

        Assert.Equal(1, consumed);
        Assert.IsType<PipelineAst>(statements[2]);
    }

    [Fact]
    public void Skip_WithBareScriptBlock_ShouldWarnAndConsumeNothing()
    {
        var statements = Statements("{ Foo = \"1\" }");

        var (consumed, warnings) = Skip(statements, 0);

        Assert.Equal(0, consumed);
        Assert.Contains("unrecognized statement", Assert.Single(warnings));
    }

    [Fact]
    public void Skip_WithUnquotedInstanceName_ShouldStillReportIt()
    {
        var statements = Statements("GoneResource $InstanceName" + Environment.NewLine + "{" + Environment.NewLine + "    Foo = \"1\"" + Environment.NewLine + "}");

        var (_, warnings) = Skip(statements, 0);

        Assert.Contains("$InstanceName", Assert.Single(warnings));
    }

    #endregion

    #region Parse error triage

    [Theory]
    [InlineData("ResourceNotDefined")]
    [InlineData("InvalidInstanceProperty")]
    public void IsRecoverableParseError_WithVersionDriftErrorId_ShouldBeTrue(string errorId)
    {
        Assert.True((bool)_isRecoverableParseError.Invoke(null, [Error(errorId, "irrelevant")])!);
    }

    [Fact]
    public void IsRecoverableParseError_WithUnrelatedErrorId_ShouldBeFalse()
    {
        Assert.False((bool)_isRecoverableParseError.Invoke(null, [Error("MissingEndCurlyBrace", "irrelevant")])!);
    }

    [Fact]
    public void IsRecoverableParseError_WithLegacyMessageAndUnknownErrorId_ShouldBeTrue()
    {
        var error = Error("SomeOtherId", "Could not find the module 'Contoso'.");

        Assert.True((bool)_isRecoverableParseError.Invoke(null, [error])!);
    }

    [Fact]
    public void DescribeParseError_WithInvalidInstanceProperty_ShouldNotIncludeTheValidMemberList()
    {
        var error = Error("InvalidInstanceProperty", "The member 'Id' is not valid. Valid members are 'A', 'B', 'C'.");

        var description = (string)_describeParseError.Invoke(null, [error])!;

        Assert.DoesNotContain("Valid members are", description);
        Assert.Contains("Id", description);
    }

    [Fact]
    public void DescribeParseError_WithOtherError_ShouldReturnTheOriginalMessage()
    {
        var error = Error("ResourceNotDefined", "Undefined DSC resource 'Gone'.");

        Assert.Equal("Undefined DSC resource 'Gone'.", (string)_describeParseError.Invoke(null, [error])!);
    }

    #endregion
}
