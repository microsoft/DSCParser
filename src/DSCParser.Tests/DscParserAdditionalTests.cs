using System.Management.Automation.Language;
using System.Reflection;
using DSCParser.CSharp;
using Xunit;

namespace DSCParser.Tests;

/// <summary>
/// Additional private-helper coverage for <see cref="DscParser"/> paths that the main private-method
/// tests cannot reach without a PowerShell engine: reporting parse errors that live outside the
/// Configuration block, and the version-resolution path that trips over the unavailable engine.
/// </summary>
public class DscParserAdditionalTests
{
    private static readonly MethodInfo _reportParseErrors =
        typeof(DscParser).GetMethod("ReportParseErrors", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ReportParseErrors method not found");

    private static readonly MethodInfo _getSingleVersionModules =
        typeof(DscParser).GetMethod("GetSingleVersionModules", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("GetSingleVersionModules method not found");

    [Fact]
    public void ReportParseErrors_WithErrorOutsideConfig_ShouldSkip()
    {
        const string content = "Configuration Test { }\r\n!!! unexpected !!!";
        var scriptAst = Parser.ParseInput(content, out _, out ParseError[] errors);
        var configAst = scriptAst.Find(a => a is ConfigurationDefinitionAst, false) as ConfigurationDefinitionAst;

        Assert.NotNull(configAst);
        Assert.NotEmpty(errors);

        bool warned = false;
        DscParser.WarningSink = _ => warned = true;
        try
        {
            _ = _reportParseErrors.Invoke(null, [errors, configAst, string.Empty]);
        }
        finally
        {
            DscParser.WarningSink = null;
        }

        Assert.False(warned);
    }

    [Fact]
    public void GetSingleVersionModules_WithUnresolvedModule_ShouldThrowFromEngine()
    {
        // With the engine snap-in unavailable, resolving modules falls through to PowerShell.Create(),
        // which throws in the test host. The pre-flight bookkeeping before the throw is still covered.
        Assert.ThrowsAny<Exception>(() =>
            _getSingleVersionModules.Invoke(null, [new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "NoSuchModule" }]));
    }
}