using System.Collections;
using System.Management.Automation.Language;
using DSCParser.CSharp;
using DSCParser.PSDSC;
using Xunit;
using DscResourceInfo = Microsoft.PowerShell.DesiredStateConfiguration.DscResourceInfo;
using DscResourcePropertyInfo = Microsoft.PowerShell.DesiredStateConfiguration.DscResourcePropertyInfo;

namespace DSCParser.Tests;

/// <summary>
/// Coverage of the parse path a host without installed modules uses: keywords come from a
/// serialized schema cache and the configuration's Import-DscResource statements are ignored.
/// </summary>
public class DscParserSchemaCacheKeywordTests : IDisposable
{
    private const string ResourceKeyword = "ContosoPolicy";

    private const string CimKeyword = "MSFT_ContosoAssignment";

    public void Dispose()
    {
        DscKeywordRegistry.ResetSchemaCache();
        DscKeywordRegistry.ClearKeywordTable();
        DscParser.ClearCaches();
        DscParser.WarningSink = null;
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ConvertToDscObject_WithSchemaCacheKeywords_ShouldParseTypedProperties()
    {
        Register();

        const string configuration = """
            Configuration TenantConfig
            {
                Import-DscResource -ModuleName 'Contoso' -ModuleVersion '2.0.0'

                Node localhost
                {
                    ContosoPolicy "Corp"
                    {
                        DisplayName = "Corp policy"
                        Enabled     = $true
                        Threshold   = 3
                        Tags        = @('alpha', 'beta')
                        Ensure      = "Present"
                    }
                }
            }
            """;

        List<DscResourceInstance> result = Parse(configuration);

        DscResourceInstance policy = Assert.Single(result);
        Assert.Equal(ResourceKeyword, policy.ResourceName);
        Assert.Equal("Corp", policy.ResourceInstanceName);
        Assert.Equal("Corp policy", policy.Properties["DisplayName"]);
        Assert.Equal(true, policy.Properties["Enabled"]);
        Assert.Equal(3, policy.Properties["Threshold"]);
        Assert.Equal(new object[] { "alpha", "beta" }, policy.Properties["Tags"]);
    }

    [Fact]
    public void ConvertToDscObject_WithSchemaCacheKeywords_ShouldParseNestedCimInstances()
    {
        Register();

        const string configuration = """
            Configuration TenantConfig
            {
                Import-DscResource -ModuleName 'Contoso'

                Node localhost
                {
                    ContosoPolicy "Corp"
                    {
                        DisplayName = "Corp policy"
                        Assignments = @(
                            MSFT_ContosoAssignment
                            {
                                Target = 'AllUsers'
                            }
                        )
                    }
                }
            }
            """;

        List<DscResourceInstance> result = Parse(configuration);

        object[] assignments = Assert.IsType<object[]>(Assert.Single(result).ToHashtable()["Assignments"]);
        Hashtable assignment = Assert.IsType<Hashtable>(Assert.Single(assignments));
        Assert.Equal("AllUsers", assignment["Target"]);
        Assert.Equal(CimKeyword, assignment["CIMInstance"]);
    }

    [Fact]
    public void ConvertToDscObject_WithSchemaCacheKeywords_ShouldLeaveKeywordTableEmpty()
    {
        Register();

        _ = Parse("""
            Configuration TenantConfig
            {
                Node localhost
                {
                    ContosoPolicy "Corp"
                    {
                        DisplayName = "Corp policy"
                    }
                }
            }
            """);

        Assert.False(DynamicKeyword.ContainsKeyword(ResourceKeyword));
    }

    [Fact]
    public void ConvertToDscObject_WithoutRegisteredKeywords_ShouldThrow()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => Parse("""
            Configuration TenantConfig
            {
                Node localhost
                {
                    ContosoPolicy "Corp"
                    {
                        DisplayName = "Corp policy"
                    }
                }
            }
            """));

        Assert.Contains("RegisterFromSchemaCache", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConvertToDscObject_WithoutNodeStatement_ShouldReadTheWholeContent()
    {
        Register();

        List<DscResourceInstance> result = Parse("""
            ContosoPolicy "Corp"
            {
                DisplayName = "Corp policy"
            }
            """);

        Assert.Equal("Corp", Assert.Single(result).ResourceInstanceName);
    }

    [Fact]
    public void RegisterFromSchemaCache_WithSameKeywordTwice_ShouldNotDuplicate()
    {
        Assert.Equal(2, DscKeywordRegistry.RegisterFromSchemaCache(SchemaCacheEntries()));
        Assert.Equal(2, DscKeywordRegistry.RegisterFromSchemaCache(SchemaCacheEntries()));
    }

    [Fact]
    public void RegisterFromSchemaCache_WithPSObjectEntries_ShouldRegisterKeywords()
    {
        List<object> entries = [.. SchemaCacheEntries().Select(entry =>
            (object)System.Management.Automation.PSObject.AsPSObject(entry))];

        Assert.Equal(2, DscKeywordRegistry.RegisterFromSchemaCache(entries));
        Assert.True(DscKeywordRegistry.HasSchemaCacheKeywords);
    }

    private static List<DscResourceInstance> Parse(string configuration)
    {
        return DscParser.ConvertToDscObject(
            content: configuration,
            options: new DscParseOptions { UseRegisteredKeywords = true },
            dscResources: ResourceDefinitions());
    }

    private static void Register()
    {
        _ = DscKeywordRegistry.RegisterFromSchemaCache(SchemaCacheEntries());
    }

    private static List<object> ResourceDefinitions()
    {
        DscResourceInfo policy = new() { Name = ResourceKeyword, ResourceType = ResourceKeyword };
        policy.AddProperty(new DscResourcePropertyInfo { Name = "DisplayName", PropertyType = "[string]", IsMandatory = true });

        DscResourceInfo assignment = new() { Name = CimKeyword, ResourceType = CimKeyword };
        assignment.AddProperty(new DscResourcePropertyInfo { Name = "Target", PropertyType = "[string]" });

        return [policy, assignment];
    }

    private static List<object> SchemaCacheEntries() =>
    [
        Entry(ResourceKeyword, "NameRequired", new Dictionary<string, string[]>
        {
            ["DisplayName"] = ["String"],
            ["Enabled"] = ["Boolean"],
            ["Threshold"] = ["UInt32"],
            ["Tags"] = ["StringArray"],
            ["Assignments"] = ["ContosoAssignment[]"],
            ["Ensure"] = ["String", "Present", "Absent"],
        }),
        Entry(CimKeyword, "NoName", new Dictionary<string, string[]>
        {
            ["Target"] = ["String"],
        }),
    ];

    private static Hashtable Entry(string keyword, string nameMode, Dictionary<string, string[]> properties)
    {
        Hashtable propertyMap = new(StringComparer.OrdinalIgnoreCase);

        foreach ((string name, string[] typeAndValues) in properties)
        {
            propertyMap[name] = new Hashtable(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = name,
                ["typeConstraint"] = typeAndValues[0],
                ["mandatory"] = false,
                ["isKey"] = false,
                ["attributes"] = Array.Empty<object>(),
                ["values"] = typeAndValues.Skip(1).Cast<object>().ToArray(),
                ["valueMap"] = typeAndValues.Skip(1)
                    .Select(value => new Hashtable(StringComparer.OrdinalIgnoreCase) { ["key"] = value, ["value"] = value })
                    .Cast<object>()
                    .ToArray(),
            };
        }

        return new Hashtable(StringComparer.OrdinalIgnoreCase)
        {
            ["keyword"] = keyword,
            ["resourceName"] = keyword,
            ["implementingModule"] = "Contoso",
            ["implementingModuleVersion"] = "2.0.0",
            ["nameMode"] = nameMode,
            ["bodyMode"] = "Hashtable",
            ["directCall"] = false,
            ["metaStatement"] = false,
            ["properties"] = propertyMap,
        };
    }
}
