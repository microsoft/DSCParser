using Microsoft.Management.Infrastructure;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Management.Automation.Language;
using System.Reflection;

namespace DSCParser.PSDSC
{
    /// <summary>
    /// Typed wrappers around the internal PowerShell DscClassCache. The type and its methods are
    /// resolved once and cached, because several of these are called once per discovered resource.
    /// Every member tolerates the internal type being absent on the running PowerShell edition.
    /// </summary>
    internal static class DscClassCacheReflection
    {
        private const string DscClassCacheTypeName =
            "Microsoft.PowerShell.DesiredStateConfiguration.Internal.DscClassCache, System.Management.Automation";

        private static readonly Type? CacheType = Type.GetType(DscClassCacheTypeName, throwOnError: false);

        private static readonly MethodInfo? LoadDefaultCimKeywordsMethod =
            CacheType?.GetMethod("LoadDefaultCimKeywords", [typeof(Collection<Exception>), typeof(bool)]);

        private static readonly MethodInfo? ImportClassResourcesFromModuleMethod =
            CacheType?.GetMethod("ImportClassResourcesFromModule", BindingFlags.Public | BindingFlags.Static);

        private static readonly MethodInfo? ImportCimKeywordsFromModuleMethod =
            CacheType?.GetMethod("ImportCimKeywordsFromModule",
                [typeof(PSModuleInfo), typeof(string), typeof(string).MakeByRefType(), typeof(Dictionary<string, ScriptBlock>), typeof(Collection<Exception>)]);

        private static readonly MethodInfo? ImportScriptKeywordsFromModuleMethod =
            CacheType?.GetMethod("ImportScriptKeywordsFromModule",
                [typeof(PSModuleInfo), typeof(string), typeof(string).MakeByRefType(), typeof(Dictionary<string, ScriptBlock>)]);

        private static readonly MethodInfo? GetCachedClassByFileNameMethod =
            CacheType?.GetMethod("GetCachedClassByFileName", BindingFlags.Public | BindingFlags.Static);

        private static readonly MethodInfo? GetCachedKeywordsMethod =
            CacheType?.GetMethod("GetCachedKeywords", BindingFlags.Public | BindingFlags.Static);

        private static readonly MethodInfo? GetFileDefiningClassMethod =
            CacheType?.GetMethod("GetFileDefiningClass", BindingFlags.Public | BindingFlags.Static, null, [typeof(string)], null);

        private static readonly MethodInfo? ClearCacheMethod =
            CacheType?.GetMethod("ClearCache", BindingFlags.Public | BindingFlags.Static);

        private static readonly MethodInfo? ResetDynamicKeywordsMethod =
            typeof(DynamicKeyword).GetMethod("Reset", BindingFlags.Public | BindingFlags.Static);

        public static void LoadDefaultCimKeywords()
        {
            try
            {
                _ = LoadDefaultCimKeywordsMethod?.Invoke(null, [new Collection<Exception>(), true]);
            }
            catch
            {
                // The internal cache is best-effort; discovery still works from module manifests.
            }
        }

        public static void ImportClassResourcesFromModule(PSModuleInfo module)
        {
            _ = ImportClassResourcesFromModuleMethod?.Invoke(
                null,
                [module, new List<string> { "*" }, NewFunctionTable()]);
        }

        public static void ImportCimKeywordsFromModule(PSModuleInfo module, string resourceName)
        {
            _ = ImportCimKeywordsFromModuleMethod?.Invoke(
                null,
                [module, resourceName, null, NewFunctionTable(), new Collection<Exception>()]);
        }

        public static void ImportScriptKeywordsFromModule(PSModuleInfo module, string resourceName)
        {
            _ = ImportScriptKeywordsFromModuleMethod?.Invoke(
                null,
                [module, resourceName, null, NewFunctionTable()]);
        }

        public static List<CimClass> GetCachedClassByFileName(string fileName)
        {
            return GetCachedClassByFileNameMethod?.Invoke(null, [fileName]) as List<CimClass> ?? [];
        }

        public static IEnumerable<DynamicKeyword>? GetCachedKeywords()
        {
            return GetCachedKeywordsMethod?.Invoke(null, null) as IEnumerable<DynamicKeyword>;
        }

        public static List<string>? GetFileDefiningClass(string className)
        {
            return GetFileDefiningClassMethod?.Invoke(null, [className]) as List<string>;
        }

        public static void ClearCache()
        {
            try
            {
                _ = ClearCacheMethod?.Invoke(null, null);
            }
            catch
            {
                // Ignore errors
            }
        }

        public static void ResetDynamicKeywords()
        {
            _ = ResetDynamicKeywordsMethod?.Invoke(null, null);
        }

        private static Dictionary<string, ScriptBlock> NewFunctionTable()
        {
            return new Dictionary<string, ScriptBlock>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
