using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Language;

namespace DSCParser.PSDSC
{
    /// <summary>
    /// Registers a module's DSC resources as dynamic keywords and remembers which modules have
    /// already been registered.
    /// </summary>
    /// <remarks>
    /// PowerShell rebuilds this registration from scratch every time it parses a configuration that
    /// contains an Import-DSCResource statement, probing the file system for a schema file per
    /// resource. On a module the size of Microsoft365DSC that dominates the cost of parsing. Keeping
    /// the registration for the lifetime of the process lets callers strip the Import-DSCResource
    /// statement and skip that work entirely.
    /// </remarks>
    public static class DscKeywordRegistry
    {
        private static readonly HashSet<string> ImportedModules = new(StringComparer.OrdinalIgnoreCase);

        private static bool _defaultKeywordsLoaded;

        /// <summary>
        /// Registers the resources of the supplied modules, skipping modules already registered.
        /// </summary>
        public static void ImportModules(IEnumerable<PSModuleInfo> modules)
        {
            if (modules is null)
            {
                return;
            }

            EnsureDefaultKeywordsLoaded();

            foreach (PSModuleInfo module in modules)
            {
                if (!ImportedModules.Add(GetModuleKey(module.Name, module.Version)))
                {
                    continue;
                }

                ImportModule(module);

                // A later request that names no version is satisfied by any registered version.
                _ = ImportedModules.Add(GetModuleKey(module.Name, null));
            }
        }

        /// <summary>
        /// Ensures the resources of a module are registered as dynamic keywords.
        /// </summary>
        /// <param name="moduleName">The module to register.</param>
        /// <param name="version">The required version, or null for every installed version.</param>
        /// <returns>False when no matching module is installed.</returns>
        public static bool EnsureRegistered(string moduleName, Version? version)
        {
            if (string.IsNullOrEmpty(moduleName))
            {
                return false;
            }

            if (ImportedModules.Contains(GetModuleKey(moduleName, version)))
            {
                // Bookkeeping says imported, but only trust that while the engine still holds the
                // keywords. A previous call restores the parser state after use, dropping the live
                // keyword instances; without re-importing, parsing would no longer recognize the
                // module's resources. Treat that as a stale import and forget the bookkeeping.
                if (DscClassCacheReflection.GetCachedKeywords() is IEnumerable<DynamicKeyword> cached &&
                    cached.Any(k => k.ImplementingModule is not null &&
                                    k.ImplementingModule.Equals(moduleName, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }

                _ = ImportedModules.Remove(GetModuleKey(moduleName, version));
                _ = ImportedModules.Remove(GetModuleKey(moduleName, null));
            }

            PSModuleInfo[] modules = ResolveModules(moduleName, version);
            if (modules.Length == 0)
            {
                return false;
            }

            ImportModules(modules);
            _ = ImportedModules.Add(GetModuleKey(moduleName, version));
            return true;
        }

        /// <summary>
        /// Drops every registered keyword and the underlying class cache.
        /// </summary>
        public static void Reset()
        {
            ImportedModules.Clear();
            _defaultKeywordsLoaded = false;
            DscClassCacheReflection.ResetDynamicKeywords();
            DscClassCacheReflection.ClearCache();
        }

        /// <summary>
        /// Clears the parser-scoped dynamic keyword instances the engine creates from the imported
        /// resources. Once a keyword definition is registered in a session, the tokenizer recognizes
        /// the word everywhere, and any later script that uses an equivalent name as an ordinary
        /// command breaks - for example the trailing "configName -ConfigurationData ..." invocation
        /// that blueprint exports append, or a call such as "ResourceName @Parameters". The cached
        /// keyword definitions are left in place so resources remain discoverable and
        /// ConvertTo-DSCObject keeps mapping resource instances within this process.
        /// </summary>
        public static void RestoreParserState()
        {
            DscClassCacheReflection.ResetDynamicKeywords();
        }

        internal static void EnsureDefaultKeywordsLoaded()
        {
            if (_defaultKeywordsLoaded)
            {
                return;
            }

            DscClassCacheReflection.LoadDefaultCimKeywords();
            _defaultKeywordsLoaded = true;
        }

        /// <summary>
        /// The DSC engine clears its internal class cache whenever a Configuration block is
        /// compiled to MOF in this process. The import bookkeeping in this class then no longer
        /// matches engine state: resources appear already imported, so discovery skips re-importing
        /// them and returns zero results. Detects that mismatch and resets every cached import so
        /// the next call rebuilds the class cache. Returns true when a reset was performed.
        /// </summary>
        public static bool HandleExternalCacheReset()
        {
            if (!DscClassCacheReflection.IsDscClassCacheAvailable)
            {
                return false;
            }

            if (ImportedModules.Count == 0 && !_defaultKeywordsLoaded)
            {
                return false;
            }

            if (DscClassCacheReflection.GetCachedKeywords() is IEnumerable<DynamicKeyword> keywords)
            {
                foreach (DynamicKeyword _ in keywords)
                {
                    return false;
                }
            }

            Reset();
            return true;
        }

        private static string GetModuleKey(string moduleName, Version? version)
        {
            return version is null ? moduleName : $"{moduleName}|{version}";
        }

        private static void ImportModule(PSModuleInfo module)
        {
            if (module.ExportedDscResources.Count > 0)
            {
                DscClassCacheReflection.ImportClassResourcesFromModule(module, module.ExportedDscResources);
            }

            string dscResourcesPath = Path.Combine(module.ModuleBase, "DscResources");
            if (!Directory.Exists(dscResourcesPath))
            {
                return;
            }

            foreach (string resourceDir in Directory.GetDirectories(dscResourcesPath))
            {
                string resourceName = Path.GetFileName(resourceDir);

                // A MOF-based resource is defined by its schema file. If the schema file
                // does not exist (e.g. for class-based resources), skip importing keywords.
                if (!File.Exists(Path.Combine(resourceDir, $"{resourceName}.schema.mof")))
                {
                    continue;
                }

                DscClassCacheReflection.ImportCimKeywordsFromModule(module, resourceName);
                DscClassCacheReflection.ImportScriptKeywordsFromModule(module, resourceName);
            }
        }

        private static PSModuleInfo[] ResolveModules(string moduleName, Version? version)
        {
            try
            {
                using PowerShell ps = PowerShell.Create();
                _ = ps.AddCommand("Get-Module")
                    .AddParameter("Name", moduleName)
                    .AddParameter("ListAvailable");

                IEnumerable<PSModuleInfo> found = ps.Invoke()
                    .Select(r => r.BaseObject)
                    .OfType<PSModuleInfo>();

                if (version is not null)
                {
                    found = found.Where(m => version.Equals(m.Version));
                }

                return found.ToArray();
            }
            catch (Exception ex)
            {
                DscResourceService.ReportWarning($"Failed to resolve module '{moduleName}'. Error message: {ex.Message}");
                return [];
            }
        }
    }
}
