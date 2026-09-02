using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Reflection;

namespace DSCParser.Tests;

/// <summary>
/// Defining a real Configuration needs Microsoft.PowerShell.Management, which the test host does
/// not carry, so the internal constructor is called with a null execution context instead. The
/// parameter metadata the composite processor reads comes from the script block either way.
/// </summary>
internal static class ConfigurationInfoFactory
{
    private static readonly ConstructorInfo Ctor =
        typeof(ConfigurationInfo).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .First(c =>
            {
                var parameters = c.GetParameters();
                return parameters.Length == 3
                    && parameters[0].ParameterType == typeof(string)
                    && parameters[1].ParameterType == typeof(ScriptBlock);
            });

    private static readonly PropertyInfo ModuleProperty =
        typeof(CommandInfo).GetProperty("Module", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;

    private static Runspace? _runspace;

    public static ConfigurationInfo Create(string name, string parameterBlock)
    {
        Runspace? previous = Runspace.DefaultRunspace;
        EnsureDefaultRunspace();

        try
        {
            return (ConfigurationInfo)Ctor.Invoke([name, ScriptBlock.Create(parameterBlock), null]);
        }
        finally
        {
            Runspace.DefaultRunspace = previous;
        }
    }

    public static void SetModule(ConfigurationInfo configuration, PSModuleInfo? module)
    {
        ModuleProperty.GetSetMethod(true)!.Invoke(configuration, [module]);
    }

    private static void EnsureDefaultRunspace()
    {
        if (Runspace.DefaultRunspace is not null)
        {
            return;
        }

        _runspace ??= RunspaceFactory.CreateRunspace(InitialSessionState.CreateDefault2());

        if (_runspace.RunspaceStateInfo.State != RunspaceState.Opened)
        {
            _runspace.Open();
        }

        Runspace.DefaultRunspace = _runspace;
    }
}
