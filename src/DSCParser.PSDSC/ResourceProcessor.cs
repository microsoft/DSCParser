using Microsoft.PowerShell.DesiredStateConfiguration.V2;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Language;
using System.Reflection;
using DscResourceInfo = Microsoft.PowerShell.DesiredStateConfiguration.DscResourceInfo;
using DscResourcePropertyInfo = Microsoft.PowerShell.DesiredStateConfiguration.DscResourcePropertyInfo;
using ImplementedAsType = Microsoft.PowerShell.DesiredStateConfiguration.ImplementedAsType;

namespace DSCParser.PSDSC
{
    /// <summary>
    /// Resource processor for CIM/MOF-based DSC resources
    /// </summary>
    internal static class ResourceProcessor
    {
        // Ignore these properties when processing resources
        private static readonly HashSet<string> IgnoredProperties = new(StringComparer.OrdinalIgnoreCase)
        {
            "ResourceId",
            "ConfigurationName"
        };

        private static readonly string SystemConfigurationPath =
            Path.Combine(Environment.SystemDirectory ?? string.Empty, "configuration");

        /// <summary>
        /// Gets resource from a dynamic keyword (CIM-based resource)
        /// </summary>
        public static DscResourceInfo? GetResourceFromKeyword(
            DynamicKeyword keyword,
            string[] patterns,
            PSModuleInfo[] modules)
        {
            var implementationDetail = "ScriptBased";

            // Check if resource matches patterns
            var matched = DscResourceHelpers.IsPatternMatched(patterns, keyword.ResourceName) ||
                         DscResourceHelpers.IsPatternMatched(patterns, keyword.Keyword);

            if (!matched)
            {
                return null;
            }

            var resource = new DscResourceInfo
            {
                ResourceType = keyword.ResourceName,
                Name = keyword.Keyword,
                FriendlyName = keyword.ResourceName != keyword.Keyword ? keyword.Keyword : null
            };

            var schemaFiles = DscClassCacheReflection.GetFileDefiningClass(keyword.ResourceName);

            if (schemaFiles is not null && schemaFiles.Count > 0)
            {
                string? schemaFileName = null;

                // Find the correct schema file that matches module name and version
                PSModuleInfo? moduleInfo = null;
                foreach (var file in schemaFiles)
                {
                    moduleInfo = DscResourceHelpers.GetModule(modules, file);
                    if (moduleInfo?.Name == keyword.ImplementingModule &&
                        moduleInfo?.Version == keyword.ImplementingModuleVersion)
                    {
                        schemaFileName = file;
                        break;
                    }
                }

                // If not found, fall back to the first schema file and re-resolve its module so that
                // Module, Path and ParentPath all describe the same file.
                if (schemaFileName is null)
                {
                    schemaFileName = schemaFiles[0];
                    moduleInfo = DscResourceHelpers.GetModule(modules, schemaFileName);
                }

                if (!schemaFileName.StartsWith(SystemConfigurationPath, StringComparison.OrdinalIgnoreCase))
                {
                    var classesFromSchema = DscClassCacheReflection.GetCachedClassByFileName(schemaFileName);
                    bool found = classesFromSchema.Any(cimClass => cimClass.CimSystemProperties.ClassName.Equals(keyword.ResourceName, StringComparison.OrdinalIgnoreCase)
                        && (cimClass.CimSuperClassName?.Equals("OMI_BaseResource", StringComparison.OrdinalIgnoreCase) ?? false));

                    if (!found)
                    {
                        return null;
                    }
                }

                resource.Module = moduleInfo;
                resource.Path = DscResourceHelpers.GetImplementingModulePath(schemaFileName);
                resource.ParentPath = Path.GetDirectoryName(schemaFileName);
            }
            else
            {
                // Class-based resource
                implementationDetail = "ClassBased";
                var module = modules.FirstOrDefault(m =>
                    m.Name == keyword.ImplementingModule &&
                    m.Version == keyword.ImplementingModuleVersion);

                if (module is not null && module.ExportedDscResources.Contains(keyword.Keyword))
                {
                    resource.Module = module;
                    resource.Path = module.Path;
                    resource.ParentPath = string.IsNullOrEmpty(module.Path) ? null : Path.GetDirectoryName(module.Path);
                }
            }

            // Determine ImplementedAs
            if (!string.IsNullOrEmpty(resource.Path))
            {
                resource.ImplementedAs = ImplementedAsType.PowerShell;
            }
            else
            {
                implementationDetail = null;
                resource.ImplementedAs = ImplementedAsType.Binary;
            }

            if (resource.Module is not null)
            {
                resource.CompanyName = resource.Module.CompanyName;
            }

            AddPropertiesFromKeyword(resource, keyword);

            // Sort properties: mandatory first, then by name
            var sortedProperties = resource.PropertiesAsResourceInfo
                .OrderByDescending(p => p.IsMandatory)
                .ThenBy(p => p.Name)
                .ToList();

            resource.UpdateProperties(sortedProperties);
            resource.ImplementationDetail = implementationDetail;

            return resource;
        }

        /// <summary>
        /// Gets composite resource from configuration info
        /// </summary>
        public static DscResourceInfo? GetCompositeResource(
            string[] patterns,
            ConfigurationInfo configInfo,
            HashSet<string> ignoreParameters,
            PSModuleInfo[] modules)
        {
            // Check if resource matches patterns
            var matched = DscResourceHelpers.IsPatternMatched(patterns, configInfo.Name);
            if (!matched)
            {
                return null;
            }

            var resource = new DscResourceInfo
            {
                ResourceType = configInfo.Name,
                Name = configInfo.Name,
                ImplementedAs = ImplementedAsType.Composite
            };

            if (configInfo.Module is not null)
            {
                resource.Module = DscResourceHelpers.GetModule(modules, configInfo.Module.Path);
                resource.Module ??= configInfo.Module;

                resource.CompanyName = configInfo.Module.CompanyName;
                resource.Path = configInfo.Module.Path;
                resource.ParentPath = string.IsNullOrEmpty(resource.Path) ? null : Path.GetDirectoryName(resource.Path);
            }

            AddPropertiesFromMetadata(resource, configInfo.Parameters.Values, ignoreParameters);

            resource.ImplementationDetail = null;

            return resource;
        }

        /// <summary>
        /// Adds properties to resource from dynamic keyword
        /// </summary>
        private static void AddPropertiesFromKeyword(
            DscResourceInfo resource,
            DynamicKeyword keyword)
        {
            foreach (var property in keyword.Properties.Values)
            {
                if (IgnoredProperties.Contains(property.Name))
                {
                    continue;
                }

                var dscProperty = new DscResourcePropertyInfo
                {
                    Name = property.Name,
                    PropertyType = DscResourceHelpers.ConvertTypeConstraintToTypeName(property.TypeConstraint),
                    IsMandatory = property.Mandatory
                };

                // Add value map if available
                if (property.ValueMap is not null)
                {
                    foreach (var key in property.ValueMap.Keys.OrderBy(k => k))
                    {
                        dscProperty.Values.Add(key);
                    }
                }

                resource.AddProperty(dscProperty);
            }
        }

        /// <summary>
        /// Adds properties to resource from parameter metadata (composite resources)
        /// </summary>
        private static void AddPropertiesFromMetadata(
            DscResourceInfo resource,
            IEnumerable<ParameterMetadata> parameters,
            HashSet<string> ignoreParameters)
        {
            foreach (var parameter in parameters)
            {
                if (ignoreParameters.Contains(parameter.Name))
                {
                    continue;
                }

                var dscProperty = new DscResourcePropertyInfo
                {
                    Name = parameter.Name,
                    PropertyType = $"[{parameter.ParameterType.Name}]",
                    IsMandatory = parameter.Attributes.Any(a =>
                        a is ParameterAttribute pa && pa.Mandatory)
                };

                resource.AddProperty(dscProperty);
            }
        }

    }
}
