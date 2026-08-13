using System.Management.Automation;
using System.Reflection;

namespace DSCParser.Tests;

/// <summary>
/// Builds <see cref="PSModuleInfo"/> instances for tests that need to exercise module-based
/// branches (module resolution, version matching, schema import) without a PowerShell engine
/// available in the test host. PSModuleInfo has no public constructor, so the internal
/// (name, path, executionContext, sessionState) constructor is used with null context.
/// </summary>
internal static class PsModuleInfoFactory
{
    private static readonly ConstructorInfo Ctor;

    static PsModuleInfoFactory()
    {
        Ctor = typeof(PSModuleInfo).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .First(c =>
            {
                var ps = c.GetParameters();
                return ps.Length == 4
                    && ps[0].ParameterType == typeof(string)
                    && ps[1].ParameterType == typeof(string);
            });
    }

    public static PSModuleInfo Create(string name, string path)
    {
        return (PSModuleInfo)Ctor.Invoke([name, path, null, null]);
    }

    public static PSModuleInfo CreateNameOnly(string name)
    {
        return (PSModuleInfo)Ctor.Invoke([name, null, null, null]);
    }

    public static void SetVersion(PSModuleInfo module, Version version)
    {
        var setter = typeof(PSModuleInfo).GetProperty("Version", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .GetSetMethod(true);
        setter.Invoke(module, [version]);
    }
}