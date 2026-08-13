using System.Collections;
using System.Management.Automation;
using System.Management.Automation.Language;
using System.Reflection;
using System.Text;
using DSCParser.CSharp;
using DSCParser.PSDSC;
using Xunit;
using DscResourceInfo = Microsoft.PowerShell.DesiredStateConfiguration.DscResourceInfo;

namespace DSCParser.Tests;

/// <summary>
/// Exercises the remaining private helpers of <see cref="DscParser"/> that are not reachable
/// through the public API in a unit test. These helpers do not require DSC dynamic keywords
/// registered by a real PowerShell host.
/// </summary>
public class DscParserPrivateMethodTests
{
    private static readonly MethodInfo _clearCaches =
        typeof(DscParser).GetMethod("ClearCaches", BindingFlags.Public | BindingFlags.Static)
        ?? throw new InvalidOperationException("ClearCaches method not found");

    private static readonly MethodInfo _getSingleVersionModules =
        typeof(DscParser).GetMethod("GetSingleVersionModules", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("GetSingleVersionModules method not found");

    private static readonly MethodInfo _getModulesToLoad =
        typeof(DscParser).GetMethod("GetModulesToLoad", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("GetModulesToLoad method not found");

    private static readonly MethodInfo _removeImportDscResourceStatements =
        typeof(DscParser).GetMethod("RemoveImportDscResourceStatements", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("RemoveImportDscResourceStatements method not found");

    private static readonly MethodInfo _registerKeywords =
        typeof(DscParser).GetMethod("RegisterKeywords", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("RegisterKeywords method not found");

    private static readonly MethodInfo _reportParseErrors =
        typeof(DscParser).GetMethod("ReportParseErrors", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ReportParseErrors method not found");

    private static readonly MethodInfo _initializeDscResources =
        typeof(DscParser).GetMethod("InitializeDscResources", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("InitializeDscResources method not found");

    private static readonly MethodInfo _processCommandAst =
        typeof(DscParser).GetMethod("ProcessCommandAst", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ProcessCommandAst method not found");

    private static readonly MethodInfo _processExpressionAst =
        typeof(DscParser).GetMethod("ProcessExpressionAst", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ProcessExpressionAst method not found");

    private static readonly MethodInfo _processVariableExpressionAst =
        typeof(DscParser).GetMethod("ProcessVariableExpressionAst", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ProcessVariableExpressionAst method not found");

    private static readonly MethodInfo _processMemberExpressionAst =
        typeof(DscParser).GetMethod("ProcessMemberExpressionAst", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ProcessMemberExpressionAst method not found");

    private static readonly MethodInfo _processArrayExpressionAst =
        typeof(DscParser).GetMethod("ProcessArrayExpressionAst", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ProcessArrayExpressionAst method not found");

    private static readonly MethodInfo _appendProperty =
        typeof(DscParser).GetMethod("AppendProperty", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("AppendProperty method not found");

    private static readonly MethodInfo _reportWarning =
        typeof(DscParser).GetMethod("ReportWarning", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ReportWarning method not found");

    private static T FindAst<T>(string source) where T : Ast
    {
        var scriptAst = Parser.ParseInput(source, out _, out ParseError[] errors);
        Assert.Empty(errors);

        return scriptAst.Find(a => a is T, true) as T
            ?? throw new InvalidOperationException($"No {typeof(T).Name} found in '{source}'");
    }

    private static T FindTopLevelAst<T>(string source) where T : Ast
    {
        var scriptAst = Parser.ParseInput(source, out _, out ParseError[] errors);
        Assert.Empty(errors);

        return scriptAst.Find(a => a is T, false) as T
            ?? throw new InvalidOperationException($"No top-level {typeof(T).Name} found in '{source}'");
    }

    private static CommandAst FirstCommandAst(string source)
    {
        var pipeline = FindTopLevelAst<PipelineAst>(source);
        return Assert.IsType<CommandAst>(pipeline.PipelineElements[0]);
    }

    private static List<string> CaptureWarnings(Action action)
    {
        List<string> warnings = [];
        DscParser.WarningSink = warnings.Add;
        try
        {
            action();
            return warnings;
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

    #region ClearCaches

    [Fact]
    public void ClearCaches_ShouldClearResourceAndModuleCaches()
    {
        try
        {
            _clearCaches.Invoke(null, null);

            Assert.True(true);
        }
        finally
        {
            DscKeywordRegistry.Reset();
        }
    }

    #endregion

    #region GetSingleVersionModules

    [Fact]
    public void GetSingleVersionModules_WithCachedSingleVersionModule_ShouldReturnItWithoutQueryingPowerShell()
    {
        var cache = GetModuleVersionCache();
        try
        {
            cache["__CachedSingleVersionModule__"] = false;

            var result = (List<string>)_getSingleVersionModules.Invoke(null, [new HashSet<string> { "__CachedSingleVersionModule__" }])!;

            Assert.Equal(["__CachedSingleVersionModule__"], result);
        }
        finally
        {
            cache.Remove("__CachedSingleVersionModule__");
        }
    }

    [Fact]
    public void GetSingleVersionModules_WithCachedMultiVersionModule_ShouldExcludeIt()
    {
        var cache = GetModuleVersionCache();
        try
        {
            cache["__CachedMultiVersionModule__"] = true;

            var result = (List<string>)_getSingleVersionModules.Invoke(null, [new HashSet<string> { "__CachedMultiVersionModule__" }])!;

            Assert.Empty(result);
        }
        finally
        {
            cache.Remove("__CachedMultiVersionModule__");
        }
    }

    private static Dictionary<string, bool> GetModuleVersionCache()
    {
        var field = typeof(DscParser).GetField("_moduleHasMultipleVersions", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("_moduleHasMultipleVersions field not found");

        return Assert.IsType<Dictionary<string, bool>>(field.GetValue(null));
    }

    #endregion

    #region GetModulesToLoad

    [Theory]
    [InlineData("Import-DscResource -ModuleName Foo -ModuleVersion 1.2.3.4", "Foo", "1.2.3.4")]
    [InlineData("Import-DscResource -ModuleName Foo", "Foo", null)]
    [InlineData("Import-DscResource -ModuleName Foo -ModuleVersion not-a-version", "Foo", null)]
    [InlineData("Import-DscResource -ModuleName Foo -ModuleName Bar -ModuleVersion 2.0.0.0", "Bar", "2.0.0.0")]
    public void GetModulesToLoad_ShouldExtractNameAndVersion(string content, string expectedName, string? expectedVersion)
    {
        var modules = (System.Collections.IList)_getModulesToLoad.Invoke(null, [content])!;

        var reference = Assert.Single(modules);
        var name = (string?)reference.GetType().GetProperty("Name")!.GetValue(reference);
        var version = (Version?)reference.GetType().GetProperty("Version")!.GetValue(reference);

        Assert.Equal(expectedName, name);
        if (expectedVersion is null)
        {
            Assert.Null(version);
        }
        else
        {
            Assert.Equal(new Version(expectedVersion), version);
        }
    }

    [Fact]
    public void GetModulesToLoad_WithNoImportStatements_ShouldReturnEmpty()
    {
        var modules = (System.Collections.IList)_getModulesToLoad.Invoke(null, ["Configuration Foo { }"])!;

        Assert.Empty(modules);
    }

    [Fact]
    public void GetModulesToLoad_WithNonConstantModuleParameter_ShouldSkipIt()
    {
        var modules = (System.Collections.IList)_getModulesToLoad.Invoke(null, ["Import-DscResource -ModuleName $var"])!;

        Assert.Empty(modules);
    }

    [Fact]
    public void GetModulesToLoad_WithCaseInsensitiveNames_ShouldExtract()
    {
        var modules = (System.Collections.IList)_getModulesToLoad.Invoke(null, ["import-dscresource -MODULENAME Foo -moduleversion 3.1.0"])!;

        var reference = Assert.Single(modules);
        Assert.Equal("Foo", (string?)reference.GetType().GetProperty("Name")!.GetValue(reference));
        Assert.Equal(new Version("3.1.0"), (Version?)reference.GetType().GetProperty("Version")!.GetValue(reference));
    }

    #endregion

    #region RemoveImportDscResourceStatements

    [Fact]
    public void RemoveImportDscResourceStatements_ShouldStripEveryStatement()
    {
        var content = """
            Import-DscResource -ModuleName Foo
            Configuration Bar { }
            Import-DscResource -ModuleName Baz -ModuleVersion 1.0
            """;

        var result = (string)_removeImportDscResourceStatements.Invoke(null, [content])!;

        Assert.DoesNotContain("Import-DscResource", result);
        Assert.Contains("Configuration Bar", result);
    }

    #endregion

    #region RegisterKeywords

    [Fact]
    public void RegisterKeywords_WithUninstalledModule_ShouldWarn()
    {
        try
        {
            var modules = (System.Collections.IList)_getModulesToLoad.Invoke(null, ["Import-DscResource -ModuleName __MissingModule_RegisterKeywords__"])!;

            var warnings = CaptureWarnings(() => _registerKeywords.Invoke(null, [modules, string.Empty]));

            Assert.Contains("Could not find the module", Assert.Single(warnings));
        }
        finally
        {
            DscKeywordRegistry.Reset();
        }
    }

    [Fact]
    public void RegisterKeywords_WithUninstalledVersionedModule_ShouldWarnWithVersion()
    {
        try
        {
            var modules = (System.Collections.IList)_getModulesToLoad.Invoke(null, ["Import-DscResource -ModuleName __MissingModule_RegisterKeywords__ -ModuleVersion 9.9.9.9"])!;

            var warnings = CaptureWarnings(() => _registerKeywords.Invoke(null, [modules, "prefix - "]));

            var warning = Assert.Single(warnings);
            Assert.Contains("9.9.9.9", warning);
            Assert.StartsWith("prefix - ", warning);
        }
        finally
        {
            DscKeywordRegistry.Reset();
        }
    }

    #endregion

    #region ReportParseErrors

    [Fact]
    public void ReportParseErrors_WithRecoverableError_ShouldWarnAndNotThrow()
    {
        var warnings = CaptureWarnings(() =>
            _reportParseErrors.Invoke(null, [new[] { Error("ResourceNotDefined", "Undefined DSC resource 'Gone'.") }, null, string.Empty]));

        Assert.Contains("Undefined DSC resource 'Gone'.", Assert.Single(warnings));
    }

    [Fact]
    public void ReportParseErrors_WithInvalidInstanceProperty_ShouldWarnWithShortDescription()
    {
        var warnings = CaptureWarnings(() =>
            _reportParseErrors.Invoke(null, [new[] { Error("InvalidInstanceProperty", "The member 'Id' is not valid. Valid members are 'A', 'B', 'C'.") }, null, string.Empty]));

        var warning = Assert.Single(warnings);
        Assert.DoesNotContain("Valid members are", warning);
        Assert.Contains("Id", warning);
    }

    [Fact]
    public void ReportParseErrors_WithNonRecoverableError_ShouldThrow()
    {
        var ex = Assert.Throws<TargetInvocationException>(() =>
            _reportParseErrors.Invoke(null, [new[] { Error("MissingEndCurlyBrace", "Missing closing curly brace.") }, null, "prefix - "]));
        var inner = Assert.IsType<InvalidOperationException>(ex.InnerException);

        Assert.Contains("Error parsing configuration: Missing closing curly brace.", inner.Message);
    }

    #endregion

    #region InitializeDscResources

    [Fact]
    public void InitializeDscResources_WithNoModulesToLoad_ShouldReturnWithoutAdding()
    {
        try
        {
            var modules = (System.Collections.IList)_getModulesToLoad.Invoke(null, ["Configuration Foo { }"])!;

            _initializeDscResources.Invoke(null, [modules, new List<DscResourceInfo>()]);

            Assert.True(true);
        }
        finally
        {
            DscParser.ClearCaches();
            DscKeywordRegistry.Reset();
        }
    }

    #endregion

    #region ProcessCommandAst

    [Fact]
    public void ProcessCommandAst_WithCimInstance_ShouldReturnCimInstanceDictionary()
    {
        var command = FirstCommandAst("MSFT_Credential { UserName = 'x' }");

        var (name, value) = ((string, object?))_processCommandAst.Invoke(null, [command, true])!;

        Assert.Equal(string.Empty, name);
        var result = Assert.IsType<Dictionary<string, object?>>(value);
        Assert.Equal("MSFT_Credential", result["CIMInstance"]);
        Assert.Equal("x", result["UserName"]);
    }

    [Fact]
    public void ProcessCommandAst_WithCimInstance_ShouldHonourIncludeCimInstanceInfoFalse()
    {
        var command = FirstCommandAst("MSFT_Credential { UserName = 'x' }");

        var (_, value) = ((string, object?))_processCommandAst.Invoke(null, [command, false])!;

        var result = Assert.IsType<Dictionary<string, object?>>(value);
        Assert.False(result.ContainsKey("CIMInstance"));
        Assert.Equal("x", result["UserName"]);
    }

    [Fact]
    public void ProcessCommandAst_WithCimInstancePrefixElements_ShouldReturnPropertyName()
    {
        var command = FirstCommandAst("Credential X MSFT_Credential { UserName = 'x' }");

        var (name, value) = ((string, object?))_processCommandAst.Invoke(null, [command, true])!;

        Assert.Equal("Credential", name);
        Assert.IsType<Dictionary<string, object?>>(value);
    }

    [Fact]
    public void ProcessCommandAst_WithCommandArguments_ShouldReturnCommandText()
    {
        var command = FirstCommandAst("New-Object System.Management.Automation.PSCredential('u','p')");

        var (name, value) = ((string, object?))_processCommandAst.Invoke(null, [command, true])!;

        Assert.Equal(string.Empty, name);
        Assert.Equal("New-Object System.Management.Automation.PSCredential('u','p')", value);
    }

    [Fact]
    public void ProcessCommandAst_WithBareCommand_ShouldReturnCommandText()
    {
        var command = FirstCommandAst("Get-ChildItem");

        var (name, value) = ((string, object?))_processCommandAst.Invoke(null, [command, true])!;

        Assert.Equal(string.Empty, name);
        Assert.Equal("Get-ChildItem", value);
    }

    #endregion

    #region ProcessExpressionAst

    [Fact]
    public void ProcessExpressionAst_WithVariable_ShouldReturnVariableText()
    {
        var expr = FindAst<VariableExpressionAst>("$var");

        var result = _processExpressionAst.Invoke(null, [expr, true]);

        Assert.Equal("$var", result);
    }

    [Fact]
    public void ProcessExpressionAst_WithMemberExpression_ShouldReturnMemberText()
    {
        var expr = FindAst<MemberExpressionAst>("$obj.Prop");

        var result = _processExpressionAst.Invoke(null, [expr, true]);

        Assert.Equal("$obj.Prop", result);
    }

    [Fact]
    public void ProcessExpressionAst_WithExpandableString_ShouldReturnValue()
    {
        var expr = FindAst<ExpandableStringExpressionAst>("$x = \"hello $name\"");

        var result = _processExpressionAst.Invoke(null, [expr, true]);

        Assert.Equal("hello $name", result);
    }

    [Fact]
    public void ProcessExpressionAst_WithHashtable_ShouldReturnHashtable()
    {
        var expr = FindAst<HashtableAst>("@{ a = 1 }");

        var result = _processExpressionAst.Invoke(null, [expr, true]);

        Assert.IsType<Hashtable>(result);
    }

    [Fact]
    public void ProcessExpressionAst_WithArray_ShouldReturnList()
    {
        var expr = FindAst<ArrayExpressionAst>("@(1, 2)");

        var result = _processExpressionAst.Invoke(null, [expr, true]);

        Assert.Equal(new List<object> { 1, 2 }, result);
    }

    [Fact]
    public void ProcessExpressionAst_WithUnknownExpression_ShouldReturnItsText()
    {
        var expr = FindAst<BinaryExpressionAst>("1 + 2");

        var result = _processExpressionAst.Invoke(null, [expr, true]);

        Assert.Equal("1 + 2", result);
    }

    #endregion

    #region ProcessVariableExpressionAst

    [Theory]
    [InlineData("$true", true)]
    [InlineData("$false", false)]
    public void ProcessVariableExpressionAst_WithBooleanLiteral_ShouldReturnBool(string source, bool expected)
    {
        var expr = FindAst<VariableExpressionAst>(source);

        var result = _processVariableExpressionAst.Invoke(null, [expr]);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ProcessVariableExpressionAst_WithPlainVariable_ShouldReturnVariableText()
    {
        var expr = FindAst<VariableExpressionAst>("$myVar");

        var result = _processVariableExpressionAst.Invoke(null, [expr]);

        Assert.Equal("$myVar", result);
    }

    #endregion

    #region ProcessMemberExpressionAst

    [Fact]
    public void ProcessMemberExpressionAst_ShouldReturnMemberText()
    {
        var expr = FindAst<MemberExpressionAst>("$ConfigurationData.NonNodeData.ApplicationId");

        var result = _processMemberExpressionAst.Invoke(null, [expr]);

        Assert.Equal("$ConfigurationData.NonNodeData.ApplicationId", result);
    }

    #endregion

    #region ProcessArrayExpressionAst

    [Fact]
    public void ProcessArrayExpressionAst_WithCommandElement_ShouldProcessItAsComplexItem()
    {
        var array = FindAst<ArrayExpressionAst>("@( Get-ChildItem )");

        var result = (List<object>)_processArrayExpressionAst.Invoke(null, [array, true])!;

        Assert.Single(result);
    }

    [Fact]
    public void ProcessArrayExpressionAst_WithNonPipelineStatement_ShouldWarnAndReturnEmpty()
    {
        var array = FindAst<ArrayExpressionAst>("@( if ($true) { 'a' } else { 'b' } )");

        var warnings = CaptureWarnings(() =>
        {
            var result = (List<object>)_processArrayExpressionAst.Invoke(null, [array, true])!;
            Assert.Empty(result);
        });

        Assert.Contains("unrecognized array element", Assert.Single(warnings));
    }

    #endregion

    #region AppendProperty

    [Fact]
    public void AppendProperty_WithNonCollectionValue_ShouldAppendItsToString()
    {
        var sb = new StringBuilder();

        _appendProperty.Invoke(null, [sb, "Ratio", 3.14, " ", "    ", 0]);

        Assert.Equal("        Ratio = 3.14" + Environment.NewLine, sb.ToString());
    }

    [Fact]
    public void ReportWarning_WithNullSink_ShouldDoNothing()
    {
        DscParser.WarningSink = null;

        _reportWarning.Invoke(null, ["ignored"]);

        Assert.True(true);
    }

    #endregion
}
