using Microsoft.PowerShell.DesiredStateConfiguration.V2;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Text;
using System.Text.RegularExpressions;
using DscResourceInfo = Microsoft.PowerShell.DesiredStateConfiguration.DscResourceInfo;

namespace DSCParser.PSDSC
{
    /// <summary>
    /// Helper methods for DSC resource operations
    /// </summary>
    internal static class DscResourceHelpers
    {
        private const string SchemaMofExtension = ".schema.mof";
        private const string SchemaPsm1Extension = ".schema.psm1";

        private static readonly string[] ResourceModuleExtensions = [".psd1", ".psm1", ".dll", ".cdxml"];

        private static readonly Regex DscResourcesToExportRegex =
            new(@"^\s*DscResourcesToExport\s*=", RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        // Schema file path -> owning module, for the duration of one discovery run. GetModule probes
        // the file system, and hundreds of keywords typically resolve to a handful of module bases.
        private static readonly Dictionary<string, PSModuleInfo?> ModuleCache = new(StringComparer.OrdinalIgnoreCase);

        // Hidden resources that should not be returned to users
        private static readonly HashSet<string> HiddenResources = new(StringComparer.OrdinalIgnoreCase)
        {
            "OMI_BaseResource",
            "MSFT_KeyValuePair",
            "MSFT_BaseConfigurationProviderRegistration",
            "MSFT_CimConfigurationProviderRegistration",
            "MSFT_PSConfigurationProviderRegistration",
            "OMI_ConfigurationDocument",
            "MSFT_Credential",
            "MSFT_DSCMetaConfiguration",
            "OMI_ConfigurationDownloadManager",
            "OMI_ResourceModuleManager",
            "OMI_ReportManager",
            "MSFT_FileDownloadManager",
            "MSFT_WebDownloadManager",
            "MSFT_FileResourceManager",
            "MSFT_WebResourceManager",
            "MSFT_WebReportManager",
            "OMI_MetaConfigurationResource",
            "MSFT_PartialConfiguration",
            "MSFT_DSCMetaConfigurationV2"
        };

        // Type conversion map for MOF types to PowerShell types
        private static readonly Dictionary<string, string> ConvertTypeMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "MSFT_Credential", "[PSCredential]" },
            { "MSFT_KeyValuePair", "[HashTable]" },
            { "MSFT_KeyValuePair[]", "[HashTable]" }
        };

        /// <summary>
        /// Checks whether a resource is hidden and should not be shown to users
        /// </summary>
        public static bool IsHiddenResource(string resourceName) => HiddenResources.Contains(resourceName);

        /// <summary>
        /// Checks whether an input name matches one of the patterns. Patterns use PowerShell
        /// wildcard syntax, matching the -Name parameter contract of Get-DscResourceV2.
        /// </summary>
        public static bool IsPatternMatched(string[] patterns, string name)
        {
            if (patterns is null || patterns.Length == 0)
            {
                return true;
            }

            foreach (var pattern in patterns)
            {
                if (WildcardPattern.Get(pattern, WildcardOptions.IgnoreCase | WildcardOptions.CultureInvariant).IsMatch(name))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Gets implementing module path from schema file path
        /// </summary>
        public static string? GetImplementingModulePath(string schemaFileName)
        {
            if (string.IsNullOrEmpty(schemaFileName))
            {
                return null;
            }

            var stem = TrimSuffix(schemaFileName, SchemaMofExtension);

            var moduleFileName = stem + ".psd1";
            if (File.Exists(moduleFileName))
            {
                return moduleFileName;
            }

            moduleFileName = stem + ".psm1";
            return File.Exists(moduleFileName)
                ? moduleFileName
                : null;
        }

        private static string TrimSuffix(string value, string suffix)
        {
            return value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                ? value.Substring(0, value.Length - suffix.Length)
                : value;
        }

        /// <summary>
        /// Drops the per-discovery-run module resolution cache. Must be called when discovery ends so
        /// a later run does not observe stale module or file-system state.
        /// </summary>
        public static void ClearModuleCache() => ModuleCache.Clear();

        /// <summary>
        /// Gets module for a DSC resource from schema file
        /// </summary>
        public static PSModuleInfo? GetModule(PSModuleInfo[] modules, string? schemaFileName)
        {
            if (string.IsNullOrEmpty(schemaFileName) || modules is null || modules.Length == 0)
            {
                return null;
            }

            if (ModuleCache.TryGetValue(schemaFileName!, out var cached))
            {
                return cached;
            }

            var resolved = ResolveModule(modules, schemaFileName!);
            ModuleCache[schemaFileName!] = resolved;
            return resolved;
        }

        private static PSModuleInfo? ResolveModule(PSModuleInfo[] modules, string schemaFileName)
        {
            string? schemaFileExt = null;
            if (schemaFileName.Contains(SchemaMofExtension, StringComparison.OrdinalIgnoreCase))
            {
                schemaFileExt = SchemaMofExtension;
            }
            else if (schemaFileName.Contains(SchemaPsm1Extension, StringComparison.OrdinalIgnoreCase))
            {
                schemaFileExt = SchemaPsm1Extension;
            }

            if (schemaFileExt is null)
            {
                return null;
            }

            // Get module from parent directory
            // Desired structure is: <Module-directory>/DscResources/<schema file directory>/schema.File
            try
            {
                var schemaDirectory = Path.GetDirectoryName(schemaFileName);
                if (string.IsNullOrEmpty(schemaDirectory))
                {
                    return null;
                }

                var subDirectory = Directory.GetParent(schemaDirectory);
                if (subDirectory is null ||
                    !subDirectory.Name.Equals("DscResources", StringComparison.OrdinalIgnoreCase) ||
                    subDirectory.Parent is null)
                {
                    return null;
                }

                var moduleBase = subDirectory.Parent.FullName;
                var result = modules.FirstOrDefault(m =>
                    m.ModuleBase is not null &&
                    m.ModuleBase.Equals(moduleBase, StringComparison.OrdinalIgnoreCase));

                if (result is not null && ValidateResourceModule(schemaFileName, schemaFileExt))
                {
                    return result;
                }
            }
            catch
            {
                // Return null on any error
            }

            return null;
        }

        /// <summary>
        /// Validates that a schema file corresponds to a proper DSC resource module
        /// </summary>
        private static bool ValidateResourceModule(string schemaFileName, string schemaFileExt)
        {
            // Log Resource is internally handled - special case
            if (schemaFileName.Contains("MSFT_LogResource", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var stem = TrimSuffix(schemaFileName, schemaFileExt);
            foreach (var ext in ResourceModuleExtensions)
            {
                if (File.Exists(stem + ext))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Gets DSC resource modules from PSModulePath
        /// </summary>
        public static HashSet<string> GetDscResourceModules()
        {
            var dscModuleFolderList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var psModulePath = Environment.GetEnvironmentVariable("PSModulePath");

            if (string.IsNullOrEmpty(psModulePath))
            {
                return dscModuleFolderList;
            }

            var listPSModuleFolders = psModulePath.Split([Path.PathSeparator], StringSplitOptions.RemoveEmptyEntries);

            foreach (var folder in listPSModuleFolders)
            {
                if (!Directory.Exists(folder))
                {
                    continue;
                }

                try
                {
                    foreach (var moduleFolder in Directory.GetDirectories(folder))
                    {
                        var addModule = false;
                        var moduleName = Path.GetFileName(moduleFolder);
                        string[]? subFolders = null;

                        // Check for DscResources folder
                        if (Directory.Exists(Path.Combine(moduleFolder, "DscResources")))
                        {
                            addModule = true;
                        }
                        else
                        {
                            // Check for nested DscResources folders (one level deep)
                            subFolders = Directory.GetDirectories(moduleFolder);
                            foreach (var subFolder in subFolders)
                            {
                                if (Directory.Exists(Path.Combine(subFolder, "DscResources")))
                                {
                                    addModule = true;
                                    break;
                                }
                            }
                        }

                        // Check .psd1 files for DscResourcesToExport
                        if (!addModule)
                        {
                            var psd1Pattern = $"{moduleName}.psd1";
                            var psd1Files = Directory.GetFiles(moduleFolder, psd1Pattern, SearchOption.TopDirectoryOnly);

                            if (psd1Files.Length == 0)
                            {
                                foreach (var subFolder in subFolders ?? Directory.GetDirectories(moduleFolder))
                                {
                                    psd1Files = Directory.GetFiles(subFolder, psd1Pattern, SearchOption.TopDirectoryOnly);
                                    if (psd1Files.Length > 0) break;
                                }
                            }

                            foreach (var psd1File in psd1Files)
                            {
                                try
                                {
                                    if (DscResourcesToExportRegex.IsMatch(File.ReadAllText(psd1File)))
                                    {
                                        addModule = true;
                                        break;
                                    }
                                }
                                catch
                                {
                                    // Ignore file read errors
                                }
                            }
                        }

                        if (addModule)
                        {
                            dscModuleFolderList.Add(moduleName);
                        }
                    }
                }
                catch
                {
                    // Ignore directory access errors
                }
            }

            return dscModuleFolderList;
        }

        /// <summary>
        /// Converts MOF type constraint to PowerShell type name
        /// </summary>
        public static string ConvertTypeConstraintToTypeName(string typeConstraint)
        {
            if (ConvertTypeMap.TryGetValue(typeConstraint, out var mappedType))
            {
                return mappedType;
            }

            var type = LanguagePrimitives.ConvertTypeNameToPSTypeName(typeConstraint);

            return string.IsNullOrEmpty(type)
                ? $"[{typeConstraint}]"
                : type;
        }

        /// <summary>
        /// Generates syntax string for a DSC resource
        /// </summary>
        public static string GetSyntax(DscResourceInfo resource)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{resource.Name} [String] #ResourceName");
            sb.AppendLine("{");

            foreach (var property in resource.PropertiesAsResourceInfo)
            {
                sb.Append("    ");

                if (!property.IsMandatory)
                {
                    sb.Append('[');
                }

                sb.Append(property.Name);
                sb.Append(" = ");
                sb.Append(property.PropertyType);

                // Add possible values
                if (property.Values.Count > 0)
                {
                    sb.Append("{ ");
                    sb.Append(string.Join(" | ", property.Values));
                    sb.Append(" }");
                }

                if (!property.IsMandatory)
                {
                    sb.Append(']');
                }

                sb.AppendLine();
            }

            sb.AppendLine("}");
            return sb.ToString();
        }
    }
}
