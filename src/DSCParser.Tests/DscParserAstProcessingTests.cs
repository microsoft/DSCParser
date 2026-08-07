using System.Collections;
using System.Management.Automation.Language;
using System.Reflection;
using DSCParser.CSharp;
using Xunit;

namespace DSCParser.Tests;

/// <summary>
/// Exercises the AST processing helpers directly. These paths cannot be reached through
/// ConvertToDscObject in a unit test, because that requires DSC keywords registered by a real
/// PowerShell host.
/// </summary>
public class DscParserAstProcessingTests
{
    private static readonly MethodInfo _processArrayExpressionAst =
        typeof(DscParser).GetMethod("ProcessArrayExpressionAst", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ProcessArrayExpressionAst method not found");

    private static readonly MethodInfo _processHashtableExpressionAst =
        typeof(DscParser).GetMethod("ProcessHashtableExpressionAst", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ProcessHashtableExpressionAst method not found");

    private static T FindAst<T>(string source) where T : Ast
    {
        var scriptAst = Parser.ParseInput(source, out _, out ParseError[] errors);
        Assert.Empty(errors);

        return scriptAst.Find(a => a is T, true) as T
            ?? throw new InvalidOperationException($"No {typeof(T).Name} found in '{source}'");
    }

    private static List<object> ProcessArray(string source, bool includeCimInstanceInfo = true)
    {
        return (List<object>)_processArrayExpressionAst.Invoke(
            null, [FindAst<ArrayExpressionAst>(source), includeCimInstanceInfo])!;
    }

    private static Hashtable ProcessHashtable(string source, bool includeCimInstanceInfo = true)
    {
        return (Hashtable)_processHashtableExpressionAst.Invoke(
            null, [FindAst<HashtableAst>(source), includeCimInstanceInfo])!;
    }

    #region ProcessArrayExpressionAst

    [Fact]
    public void ProcessArray_WithOnlyArrayLiteral_ShouldReturnAllElements()
    {
        var result = ProcessArray("@('a','b')");

        Assert.Equal(["a", "b"], result);
    }

    [Fact]
    public void ProcessArray_WithElementBeforeArrayLiteral_ShouldKeepPrecedingElements()
    {
        var source = "@(" + Environment.NewLine + "  'first'" + Environment.NewLine + "  'a','b'" + Environment.NewLine + ")";

        var result = ProcessArray(source);

        Assert.Equal(["first", "a", "b"], result);
    }

    [Fact]
    public void ProcessArray_WithElementAfterArrayLiteral_ShouldKeepTrailingElements()
    {
        var source = "@(" + Environment.NewLine + "  'a','b'" + Environment.NewLine + "  'last'" + Environment.NewLine + ")";

        var result = ProcessArray(source);

        Assert.Equal(["a", "b", "last"], result);
    }

    [Fact]
    public void ProcessArray_Empty_ShouldReturnEmptyList()
    {
        Assert.Empty(ProcessArray("@()"));
    }

    #endregion

    #region ProcessHashtableExpressionAst

    [Fact]
    public void ProcessHashtable_ShouldReadSimpleValues()
    {
        var result = ProcessHashtable("@{ Plain = 'y'; Number = 3 }");

        Assert.Equal("y", result["Plain"]);
        Assert.Equal(3, result["Number"]);
    }

    [Fact]
    public void ProcessHashtable_WithCimInstanceValue_ShouldIncludeCimInstanceKeyWhenRequested()
    {
        var result = ProcessHashtable("@{ Cred = MSFT_Credential { UserName = 'x' } }", includeCimInstanceInfo: true);

        var credential = Assert.IsType<Dictionary<string, object?>>(result["Cred"]);
        Assert.Equal("MSFT_Credential", credential["CIMInstance"]);
        Assert.Equal("x", credential["UserName"]);
    }

    [Fact]
    public void ProcessHashtable_WithCimInstanceValue_ShouldHonourIncludeCimInstanceInfoFalse()
    {
        var result = ProcessHashtable("@{ Cred = MSFT_Credential { UserName = 'x' } }", includeCimInstanceInfo: false);

        var credential = Assert.IsType<Dictionary<string, object?>>(result["Cred"]);
        Assert.False(credential.ContainsKey("CIMInstance"));
        Assert.Equal("x", credential["UserName"]);
    }

    #endregion
}
