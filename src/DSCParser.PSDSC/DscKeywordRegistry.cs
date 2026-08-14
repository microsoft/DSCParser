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
    /// the registration alive lets callers strip the Import-DSCResource statement and skip that
    /// work entirely.
    /// </remarks>
    public static class DscKeywordRegistry
    {
        [ThreadStatic]
        private static HashSet<string>? t_importedModules;

        private static HashSet<string> ImportedModules => t_importedModules ??= new(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, DynamicKeyword> SchemaCacheKeywords =
            new(StringComparer.OrdinalIgnoreCase);

        private static readonly object SchemaCacheLock = new object();

        [ThreadStatic]
        private static bool t_defaultKeywordsLoaded;

        [ThreadStatic]
        private static bool t_engineUnsupported;

        [ThreadStatic]
        private static List<DynamicKeyword>? t_defaultTableKeywords;

        [ThreadStatic]
        private static bool t_staleWarningIssued;

        [ThreadStatic]
        private static int t_expectedCachedKeywordCount;

        // Registered into the class cache by LoadDefaultCimKeywords and dies with it, so its
        // absence while the bookkeeping claims otherwise means the engine wiped the cache.
        private const string ClassCacheSentinel = "OMI_ConfigurationDocument";

        // Lives in the DynamicKeyword table (not the class cache) and is re-added by
        // LoadDefaultCimKeywords, so it tells whether the table currently holds the defaults.
        private const string NodeKeyword = "Node";

        private static bool EngineStateIsFresh
        {
            get
            {
                if (!DscClassCacheReflection.HasCachedClass(ClassCacheSentinel))
                {
                    return false;
                }

                return t_expectedCachedKeywordCount == 0
                    || CurrentCachedKeywordCount() >= t_expectedCachedKeywordCount;
            }
        }

        private static int CurrentCachedKeywordCount()
        {
            return DscClassCacheReflection.GetCachedKeywords()?.Count() ?? 0;
        }

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

            t_expectedCachedKeywordCount = CurrentCachedKeywordCount();
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

            // Heals a wiped engine cache before the fast path below can return a stale answer.
            EnsureDefaultKeywordsLoaded();

            if (ImportedModules.Contains(GetModuleKey(moduleName, version)))
            {
                return true;
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
        /// Drops every registered keyword and the underlying class cache on the current thread.
        /// </summary>
        public static void Reset()
        {
            ImportedModules.Clear();
            t_defaultKeywordsLoaded = false;
            t_engineUnsupported = false;
            t_defaultTableKeywords = null;
            t_expectedCachedKeywordCount = 0;
            DscClassCacheReflection.ResetDynamicKeywords();
            DscClassCacheReflection.ClearCache();
        }

        internal static void EnsureDefaultKeywordsLoaded()
        {
            _ = HandleExternalCacheReset();

            if (t_defaultKeywordsLoaded)
            {
                return;
            }

            DscClassCacheReflection.LoadDefaultCimKeywords();
            t_defaultKeywordsLoaded = true;
            t_engineUnsupported = !EngineStateIsFresh;

            List<DynamicKeyword> snapshot = [];
            IEnumerable<string> defaultNames =
                (DscClassCacheReflection.GetCachedKeywords()?.Select(k => k.Keyword) ?? [])
                .Concat([NodeKeyword, "Import-DscResource"]);
            foreach (string name in defaultNames.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (DynamicKeyword.GetKeyword(name) is { } keyword)
                {
                    snapshot.Add(keyword);
                }
            }

            t_defaultTableKeywords = snapshot;
            t_expectedCachedKeywordCount = CurrentCachedKeywordCount();
        }

        /// <summary>
        /// The DSC engine clears its internal class cache and DynamicKeyword table whenever a
        /// Configuration block is compiled to MOF in this process. This means that this class
        /// then no longer matches engine state, resulting in no resource results although
        /// shown as imported.
        /// </summary>
        public static bool HandleExternalCacheReset()
        {
            if (t_engineUnsupported || !DscClassCacheReflection.IsDscClassCacheAvailable)
            {
                return false;
            }

            if (ImportedModules.Count == 0 && !t_defaultKeywordsLoaded)
            {
                return false;
            }

            if (EngineStateIsFresh)
            {
                return false;
            }

            if (!t_staleWarningIssued)
            {
                t_staleWarningIssued = true;
                DscResourceService.ReportWarning(
                    "The PowerShell engine cleared its internal DSC caches (typically caused by compiling a "
                    + "Configuration to MOF). DSCParser re-imported its DSC resource keywords automatically.");
            }

            Reset();
            return true;
        }

        /// <summary>
        /// Fills the engine's DynamicKeyword table with the default CIM keywords (including Node)
        /// and every keyword the class cache holds, so a configuration can be parsed without any
        /// Import-DscResource statement. Pair with <see cref="ClearKeywordTable"/> once parsing is
        /// done.
        /// </summary>
        public static void MaterializeKeywordTable()
        {
            EnsureDefaultKeywordsLoaded();

            List<DynamicKeyword>? cachedKeywords = DscClassCacheReflection.GetCachedKeywords()?.ToList();

            if (!DynamicKeyword.ContainsKeyword(NodeKeyword))
            {
                if (t_defaultTableKeywords is { Count: > 0 } defaults)
                {
                    foreach (DynamicKeyword keyword in defaults)
                    {
                        if (!DynamicKeyword.ContainsKeyword(keyword.Keyword))
                        {
                            DynamicKeyword.AddKeyword(keyword);
                        }
                    }
                }
                else if (ImportedModules.Count == 0)
                {
                    DscClassCacheReflection.LoadDefaultCimKeywords();
                }
            }

            if (cachedKeywords is not null)
            {
                foreach (DynamicKeyword keyword in cachedKeywords)
                {
                    if (!DynamicKeyword.ContainsKeyword(keyword.Keyword))
                    {
                        DynamicKeyword.AddKeyword(keyword);
                    }
                }
            }
        }

        /// <summary>
        /// Empties the engine's DynamicKeyword table while keeping the class cache. Every public
        /// operation must end with this.
        /// </summary>
        public static void ClearKeywordTable()
        {
            DscClassCacheReflection.ResetDynamicKeywords();
        }

        /// <summary>
        /// Registers the resources described by a serialized DSC schema cache.
        /// </summary>
        /// <param name="keywords">
        /// The entries of the cache's keywords array, each a map of the shape
        /// ConvertTo-DscKeywordSchemaObject produces.
        /// </param>
        /// <returns>The number of keywords now registered from schema caches.</returns>
        /// <remarks>
        /// This is the only registration path that reaches neither the file system nor a runspace,
        /// which is what lets a plain .NET host parse a configuration for a module it does not have
        /// installed.
        /// </remarks>
        public static int RegisterFromSchemaCache(IEnumerable<object> keywords)
        {
            if (keywords is null)
            {
                throw new ArgumentNullException(nameof(keywords));
            }

            lock (SchemaCacheLock)
            {
                foreach (object entry in keywords)
                {
                    DynamicKeyword keyword = DscSchemaCacheKeywords.Build(entry);
                    SchemaCacheKeywords[keyword.Keyword] = keyword;
                }

                return SchemaCacheKeywords.Count;
            }
        }

        /// <summary>
        /// Fills the engine's DynamicKeyword table with the keywords registered from schema caches.
        /// Pair with <see cref="ClearKeywordTable"/> once parsing is done.
        /// </summary>
        public static void MaterializeSchemaCacheKeywords()
        {
            lock (SchemaCacheLock)
            {
                foreach (DynamicKeyword keyword in SchemaCacheKeywords.Values)
                {
                    if (!DynamicKeyword.ContainsKeyword(keyword.Keyword))
                    {
                        DynamicKeyword.AddKeyword(keyword);
                    }
                }
            }
        }

        /// <summary>
        /// Whether any schema cache has been registered.
        /// </summary>
        public static bool HasSchemaCacheKeywords
        {
            get
            {
                lock (SchemaCacheLock)
                {
                    return SchemaCacheKeywords.Count > 0;
                }
            }
        }

        /// <summary>
        /// Drops every keyword registered from a schema cache.
        /// </summary>
        public static void ResetSchemaCache()
        {
            lock (SchemaCacheLock)
            {
                SchemaCacheKeywords.Clear();
            }
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
