using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Language;
using DscResourceInfo = Microsoft.PowerShell.DesiredStateConfiguration.DscResourceInfo;

namespace DSCParser.PSDSC
{
    /// <summary>
    /// Static entry point for DSC resource discovery that can be called from C# code.
    /// Provides programmatic access to DSC resources without requiring PowerShell cmdlet invocation.
    /// </summary>
    public static class DscResourceService
    {
        // Parameters to ignore for composite resources
        private static readonly HashSet<string> IgnoreResourceParameters = new(StringComparer.OrdinalIgnoreCase)
        {
            "InstanceName", "OutputPath", "ConfigurationData", "Verbose", "Debug",
            "ErrorAction", "WarningAction", "InformationAction", "ErrorVariable",
            "WarningVariable", "InformationVariable", "OutVariable", "OutBuffer",
            "PipelineVariable", "WhatIf", "Confirm"
        };

        /// <summary>
        /// Receives non-fatal diagnostics. Hosts such as the Get-DscResourceV2 cmdlet wire this to
        /// their warning stream. When unset, diagnostics are dropped rather than written to the console.
        /// </summary>
        public static Action<string>? WarningSink { get; set; }

        internal static void ReportWarning(string message) => WarningSink?.Invoke(message);

        /// <summary>
        /// Gets DSC resources on the machine with optional filtering.
        /// </summary>
        /// <param name="resourceNames">Optional array of resource names to filter on (supports wildcards)</param>
        /// <param name="moduleName">Optional module name to filter on</param>
        /// <param name="includeCompositeResources">Whether to include composite (configuration-based) resources</param>
        /// <returns>List of discovered DSC resources</returns>
        public static List<DscResourceInfo> GetDscResources(
            string[]? resourceNames = null,
            string? moduleName = null,
            bool includeCompositeResources = true)
        {
            var resources = new List<DscResourceInfo>();
            resourceNames ??= [];

            try
            {
                DscKeywordRegistry.EnsureDefaultKeywordsLoaded();

                var modules = GetModuleList(moduleName) ?? [];

                if (modules.Length > 0)
                {
                    DscKeywordRegistry.ImportModules(modules);
                }

                foreach (var keyword in GetCachedKeywords(moduleName))
                {
                    var resource = ResourceProcessor.GetResourceFromKeyword(keyword, resourceNames, modules);

                    if (resource is not null)
                    {
                        resources.Add(resource);
                    }
                }

                // Get composite resources (configurations) if requested
                if (includeCompositeResources)
                {
                    foreach (var config in GetConfigurations())
                    {
                        var resource = ResourceProcessor.GetCompositeResource(
                            resourceNames,
                            config,
                            IgnoreResourceParameters,
                            modules);

                        if (resource is not null &&
                            (string.IsNullOrEmpty(moduleName) ||
                             (resource.ModuleName is not null &&
                              resource.ModuleName.Equals(moduleName, StringComparison.OrdinalIgnoreCase))) &&
                            !string.IsNullOrEmpty(resource.Path) &&
                            Path.GetFileName(resource.Path).Equals($"{resource.Name}.schema.psm1", StringComparison.OrdinalIgnoreCase))
                        {
                            resources.Add(resource);
                        }
                    }
                }

                // Sort by module and name, dropping duplicates
                var seen = new HashSet<(string, string)>();
                var uniqueResources = new List<DscResourceInfo>(resources.Count);

                foreach (var resource in resources.OrderBy(r => r.ModuleName ?? string.Empty).ThenBy(r => r.Name))
                {
                    if (seen.Add((resource.ModuleName ?? string.Empty, resource.Name ?? string.Empty)))
                    {
                        uniqueResources.Add(resource);
                    }
                }

                return uniqueResources;
            }
            finally
            {
                DscResourceHelpers.ClearModuleCache();
                DscKeywordRegistry.ClearKeywordTable();
            }
        }

        /// <summary>
        /// Gets the syntax string for a DSC resource.
        /// </summary>
        /// <param name="resource">The DSC resource to get syntax for</param>
        /// <returns>Formatted syntax string</returns>
        public static string GetResourceSyntax(DscResourceInfo resource)
        {
            return DscResourceHelpers.GetSyntax(resource);
        }

        #region Private Helper Methods

        private static PSModuleInfo[]? GetModuleList(string? moduleName)
        {
            try
            {
                IEnumerable<string> names = string.IsNullOrEmpty(moduleName)
                    ? DscResourceHelpers.GetDscResourceModules()
                    : [moduleName!];

                PSModuleInfo[] modules = PowerShellInvoker.ListAvailableModules(names);
                return modules.Length == 0 && string.IsNullOrEmpty(moduleName) ? null : modules;
            }
            catch (Exception ex)
            {
                ReportWarning($"Failed to enumerate modules. Error message: {ex.Message}");
                return null;
            }
        }

        private static IEnumerable<DynamicKeyword> GetCachedKeywords(string? moduleName)
        {
            return DscKeywordRegistry.GetKeywordSnapshot().Where(k =>
                !k.IsReservedKeyword &&
                !string.IsNullOrEmpty(k.ResourceName) &&
                !DscResourceHelpers.IsHiddenResource(k.ResourceName) &&
                (string.IsNullOrEmpty(moduleName) ||
                    k.ImplementingModule.Equals(moduleName, StringComparison.OrdinalIgnoreCase)));
        }

        private static ConfigurationInfo[] GetConfigurations()
        {
            try
            {
                return PowerShellInvoker.Invoke(ps => ps.AddCommand("Get-Command")
                        .AddParameter("CommandType", "Configuration")
                        .AddParameter("ListImported", true))
                    .Select(r => r.BaseObject)
                    .OfType<ConfigurationInfo>()
                    .ToArray();
            }
            catch (Exception ex)
            {
                ReportWarning($"Failed to get commands by command type 'Configuration'. Error message: {ex.Message}");
                return [];
            }
        }
        #endregion
    }
}
