using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace DSCParser.PSDSC
{
    /// <summary>
    /// Runs the few PowerShell commands discovery needs on the caller's runspace when there is
    /// one, and caches the installed modules it resolves for the lifetime of the process.
    /// </summary>
    internal static class PowerShellInvoker
    {
        private static readonly Dictionary<string, PSModuleInfo[]> ModuleCatalog = new(StringComparer.OrdinalIgnoreCase);

        private static readonly object CatalogLock = new object();

        private static string? s_catalogModulePath;

        public static Collection<PSObject> Invoke(Action<PowerShell> configure)
        {
            if (Runspace.DefaultRunspace is { RunspaceStateInfo.State: RunspaceState.Opened })
            {
                try
                {
                    using PowerShell nested = PowerShell.Create(RunspaceMode.CurrentRunspace);
                    configure(nested);
                    return nested.Invoke();
                }
                catch (InvalidOperationException)
                {
                }
            }

            using PowerShell ps = PowerShell.Create();
            configure(ps);
            return ps.Invoke();
        }

        public static PSModuleInfo[] ListAvailableModules(IEnumerable<string> names)
        {
            string[] requested = names.Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (requested.Length == 0)
            {
                return [];
            }

            lock (CatalogLock)
            {
                string modulePath = Environment.GetEnvironmentVariable("PSModulePath") ?? string.Empty;
                if (!string.Equals(modulePath, s_catalogModulePath, StringComparison.Ordinal))
                {
                    ModuleCatalog.Clear();
                    s_catalogModulePath = modulePath;
                }

                string[] missing = requested.Where(n => !ModuleCatalog.ContainsKey(n)).ToArray();
                if (missing.Length > 0)
                {
                    PSModuleInfo[] found = Invoke(ps => ps.AddCommand("Get-Module")
                            .AddParameter("ListAvailable", true)
                            .AddParameter("Name", missing))
                        .Select(r => r.BaseObject)
                        .OfType<PSModuleInfo>()
                        .ToArray();

                    foreach (string name in missing)
                    {
                        ModuleCatalog[name] = WildcardPattern.ContainsWildcardCharacters(name)
                            ? MatchWildcard(found, name)
                            : found.Where(m => name.Equals(m.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
                    }
                }

                return requested.SelectMany(n => ModuleCatalog[n]).Distinct().ToArray();
            }
        }

        public static void ClearModuleCatalog()
        {
            lock (CatalogLock)
            {
                ModuleCatalog.Clear();
                s_catalogModulePath = null;
            }
        }

        private static PSModuleInfo[] MatchWildcard(PSModuleInfo[] modules, string pattern)
        {
            WildcardPattern wildcard = WildcardPattern.Get(pattern, WildcardOptions.IgnoreCase | WildcardOptions.CultureInvariant);
            return modules.Where(m => wildcard.IsMatch(m.Name)).ToArray();
        }
    }
}
