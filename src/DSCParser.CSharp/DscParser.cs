using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Language;
using System.Text;
using System.Text.RegularExpressions;
using DscResourceInfo = Microsoft.PowerShell.DesiredStateConfiguration.DscResourceInfo;
using DscResourcePropertyInfo = Microsoft.PowerShell.DesiredStateConfiguration.DscResourcePropertyInfo;

namespace DSCParser.CSharp
{
    /// <summary>
    /// Main DSC Parser class that converts DSC configurations to/from objects
    /// </summary>
    public static class DscParser
    {
        private static readonly Dictionary<string, DscResourceInfo> _dscResources = new(StringComparer.OrdinalIgnoreCase);

        // Module name -> whether more than one version is installed. Enumerating PSModulePath is
        // expensive and it does not change within a process, so the verdict is reused across the many
        // ConvertToDscObject calls a single export produces. ClearCaches resets it.
        private static readonly Dictionary<string, bool> _moduleHasMultipleVersions = new(StringComparer.OrdinalIgnoreCase);

        private static readonly Regex ImportDscResourceVersionRegex = new(
            @"(import-dscresource\b[^\n]*?)\s+-moduleversion\s+(?:""[^""]*""|'[^']*'|\S+)([^\n]*)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>
        /// Receives non-fatal diagnostics such as unresolvable modules. When unset, they are dropped
        /// rather than written to the console, which would corrupt a PowerShell host's output stream.
        /// </summary>
        public static Action<string>? WarningSink { get; set; }

        /// <summary>
        /// Clears the process-wide resource, property and module caches.
        /// </summary>
        public static void ClearCaches()
        {
            _dscResources.Clear();
            _moduleHasMultipleVersions.Clear();
        }

        private static void ReportWarning(string message) => WarningSink?.Invoke(message);

        /// <summary>
        /// Converts a DSC configuration file or content to DSC objects
        /// </summary>
        public static List<DscResourceInstance> ConvertToDscObject(string? path = null, string content = "", DscParseOptions? options = null, List<object>? dscResources = null)
        {
            options ??= new DscParseOptions();

            if (_dscResources.Count == 0 && dscResources == null)
            {
                throw new InvalidOperationException("No DSC resources loaded. Please provide DSC resources to parse the configuration.");
            }

            List<DscResourceInfo> dscResourcesConverted;
            if (dscResources is not null)
            {
                dscResourcesConverted = new List<DscResourceInfo>(dscResources.Count);
                foreach (object resource in dscResources)
                {
                    DscResourceInfo mapped = DscResourceInfoMapper.MapPSObjectToResourceInfo(resource);
                    if (string.IsNullOrEmpty(mapped.Name))
                    {
                        throw new InvalidOperationException("A supplied DSC resource has no Name and cannot be used for parsing.");
                    }

                    _dscResources[mapped.Name!] = mapped;
                    dscResourcesConverted.Add(mapped);
                }
            }
            else
            {
                dscResourcesConverted = [.. _dscResources.Values];
            }

            if (string.IsNullOrEmpty(path) && string.IsNullOrEmpty(content))
            {
                throw new ArgumentException("Either path or content must be provided");
            }

            string dscContent = string.IsNullOrEmpty(content) ? File.ReadAllText(path!) : content;
            string errorPrefix = string.IsNullOrEmpty(path) ? string.Empty : $"{path} - ";

            HashSet<string> referencedModules = new(StringComparer.OrdinalIgnoreCase);
            foreach (DscResourceInfo resource in dscResourcesConverted)
            {
                string? moduleName = resource.Module?.Name;
                if (!string.IsNullOrEmpty(moduleName))
                {
                    _ = referencedModules.Add(moduleName!);
                }
            }

            List<string> modulesToRemoveVersionFrom = GetSingleVersionModules(referencedModules);

            dscContent = RemoveModuleVersionInfo(dscContent, modulesToRemoveVersionFrom);

            // Parse the DSC configuration using PowerShell AST
            ScriptBlockAst ast = Parser.ParseInput(dscContent, out Token[] tokens, out ParseError[] parseErrors);

            // Check for parse errors
            foreach (ParseError error in parseErrors)
            {
                if (error.Message.Contains("Could not find the module") ||
                    error.Message.Contains("Undefined DSC resource"))
                {
                    ReportWarning($"{errorPrefix}Failed to find module or DSC resource: {error.Message}");
                }
                else
                {
                    throw new InvalidOperationException($"{errorPrefix}Error parsing configuration: {error.Message}");
                }
            }

            // Find the Configuration definition
            if (ast.Find(a => a is ConfigurationDefinitionAst, false) is not ConfigurationDefinitionAst configAst)
            {
                throw new InvalidOperationException("No Configuration definition found in the DSC content");
            }

            List<ModuleReference> modulesToLoad = GetModulesToLoad(configAst);

            // Initialize DSC resources
            InitializeDscResources(modulesToLoad, dscResourcesConverted);

            // Get resource instances
            List<DscResourceInstance> resourceInstances = GetResourceInstances(configAst, options);

            // Add comment metadata if requested
            List<DscResourceInstance> result = resourceInstances;
            if (options.IncludeComments)
            {
                result = UpdateWithMetadata(tokens, resourceInstances);
            }

            return result;
        }

        /// <summary>
        /// Converts DSC objects back to DSC configuration text
        /// </summary>
        public static string ConvertFromDscObject(IEnumerable<Hashtable> dscResources, int childLevel = 0)
        {
            StringBuilder result = new();
            AppendDscObjects(result, dscResources, childLevel);
            return result.ToString();
        }

        /// <summary>
        /// Renders resources into <paramref name="result"/>. Nested hashtables and arrays recurse into
        /// the same builder, so no intermediate strings are produced per nesting level.
        /// </summary>
        private static void AppendDscObjects(StringBuilder result, IEnumerable<Hashtable> dscResources, int childLevel)
        {
            string childSpacer = new(' ', childLevel * 4);

            foreach (Hashtable entry in dscResources)
            {
                List<string> sortedKeys = [];
                int longestParameter = 0;
                foreach (string key in entry.Keys)
                {
                    sortedKeys.Add(key);
                    if (key.Length > longestParameter)
                    {
                        longestParameter = key.Length;
                    }
                }
                sortedKeys.Sort(StringComparer.Ordinal);

                if (entry.ContainsKey("CIMInstance"))
                {
                    _ = result.Append(childSpacer).Append(entry["CIMInstance"]).AppendLine("{");
                }
                else if (entry.ContainsKey("ResourceName") && entry.ContainsKey("ResourceInstanceName"))
                {
                    _ = result.Append(childSpacer).Append(entry["ResourceName"]).Append(" \"").Append(entry["ResourceInstanceName"]).AppendLine("\"");
                    _ = result.Append(childSpacer).AppendLine("{");
                }
                else
                {
                    _ = result.Append(childSpacer).AppendLine("@{");
                }

                foreach (string property in sortedKeys)
                {
                    if (property is "ResourceInstanceName" or "CIMInstance" ||
                        (childLevel == 0 && property is "ResourceName"))
                    {
                        continue;
                    }

                    string additionalSpaces = new(' ', longestParameter - property.Length + 1);
                    AppendProperty(result, property, entry[property], additionalSpaces, childSpacer, childLevel);
                }

                _ = result.Append(childSpacer).Append('}').Append(Environment.NewLine);
            }
        }

        private static string RemoveModuleVersionInfo(string content, List<string>? uniqueModules = null)
        {
            if (uniqueModules is null || uniqueModules.Count == 0)
            {
                return content;
            }

            return ImportDscResourceVersionRegex.Replace(content, match =>
            {
                string fullLine = match.Value;
                foreach (string module in uniqueModules)
                {
                    if (fullLine.IndexOf(module, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return match.Groups[1].Value + match.Groups[2].Value;
                    }
                }
                return fullLine;
            });
        }

        /// <summary>
        /// Returns the subset of <paramref name="moduleNames"/> that has exactly one version installed.
        /// Only those may have their -ModuleVersion stripped from the configuration; for modules with
        /// several versions installed the version is what selects the right one.
        /// </summary>
        private static List<string> GetSingleVersionModules(HashSet<string> moduleNames)
        {
            List<string> unresolved = [];
            foreach (string moduleName in moduleNames)
            {
                if (!_moduleHasMultipleVersions.ContainsKey(moduleName))
                {
                    unresolved.Add(moduleName);
                }
            }

            if (unresolved.Count > 0)
            {
                Dictionary<string, HashSet<string>> versionsByModule = new(StringComparer.OrdinalIgnoreCase);

                using (PowerShell ps = PowerShell.Create())
                {
                    _ = ps.AddCommand("Get-Module")
                        .AddParameter("Name", unresolved.ToArray())
                        .AddParameter("ListAvailable");

                    foreach (PSObject module in ps.Invoke())
                    {
                        string? name = module.Members["Name"]?.Value?.ToString();
                        string? version = module.Members["Version"]?.Value?.ToString();
                        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(version))
                        {
                            continue;
                        }

                        if (!versionsByModule.TryGetValue(name!, out HashSet<string>? versions))
                        {
                            versions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            versionsByModule[name!] = versions;
                        }
                        _ = versions.Add(version!);
                    }
                }

                foreach (string moduleName in unresolved)
                {
                    _moduleHasMultipleVersions[moduleName] =
                        versionsByModule.TryGetValue(moduleName, out HashSet<string>? found) && found.Count > 1;
                }
            }

            List<string> singleVersionModules = [];
            foreach (string moduleName in moduleNames)
            {
                if (!_moduleHasMultipleVersions[moduleName])
                {
                    singleVersionModules.Add(moduleName);
                }
            }

            return singleVersionModules;
        }

        private static List<ModuleReference> GetModulesToLoad(ConfigurationDefinitionAst configAst)
        {
            List<ModuleReference> modulesToLoad = [];
            IEnumerable<DynamicKeywordStatementAst> statements = configAst.Body.ScriptBlock.EndBlock.Statements
                .OfType<DynamicKeywordStatementAst>();

            foreach (DynamicKeywordStatementAst statement in statements)
            {
                ReadOnlyCollection<CommandElementAst> elements = statement.CommandElements;

                if (elements.Count == 0 ||
                    elements[0] is not StringConstantExpressionAst keyword ||
                    !keyword.Value.Equals("Import-DSCResource", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string? moduleName = null;
                Version? moduleVersion = null;

                for (int i = 1; i < elements.Count - 1; i++)
                {
                    if (elements[i] is not CommandParameterAst param ||
                        elements[i + 1] is not StringConstantExpressionAst value)
                    {
                        continue;
                    }

                    if (param.ParameterName.Equals("ModuleName", StringComparison.OrdinalIgnoreCase))
                    {
                        moduleName = value.Value;
                    }
                    else if (param.ParameterName.Equals("ModuleVersion", StringComparison.OrdinalIgnoreCase) &&
                             Version.TryParse(value.Value, out Version? parsed))
                    {
                        moduleVersion = parsed;
                    }
                }

                if (moduleName is not null)
                {
                    modulesToLoad.Add(new ModuleReference(moduleName, moduleVersion));
                }
            }

            return modulesToLoad;
        }

        private static void InitializeDscResources(List<ModuleReference> modulesToLoad, List<DscResourceInfo> allDscResources)
        {
            if (modulesToLoad.Count == 0)
            {
                return;
            }

            foreach (DscResourceInfo resource in allDscResources)
            {
                PSModuleInfo? module = resource.Module;
                if (module is null || string.IsNullOrEmpty(resource.Name) || _dscResources.ContainsKey(resource.Name!))
                {
                    continue;
                }

                foreach (ModuleReference reference in modulesToLoad)
                {
                    if (module.Name.Equals(reference.Name, StringComparison.OrdinalIgnoreCase) &&
                        (reference.Version is null || reference.Version.Equals(module.Version)))
                    {
                        _dscResources.Add(resource.Name!, resource);
                        break;
                    }
                }
            }
        }

        private readonly struct ModuleReference(string name, Version? version)
        {
            public string Name { get; } = name;

            public Version? Version { get; } = version;
        }

        private static List<DscResourceInstance> GetResourceInstances(ConfigurationDefinitionAst configAst, DscParseOptions? options = null)
        {
            // Try to find Node statement first
            DynamicKeywordStatementAst dynamicNodeStatement = configAst.Body.ScriptBlock.EndBlock.Statements
                .OfType<DynamicKeywordStatementAst>()
                .FirstOrDefault(dynAst =>
                        dynAst.CommandElements.Count > 0 &&
                        dynAst.CommandElements[0] is StringConstantExpressionAst constant &&
                        constant.StringConstantType == StringConstantType.BareWord &&
                        constant.Value.Equals("Node", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("No Node statement found in the DSC configuration");

            List<DscResourceInstance> result = [];

            ScriptBlockExpressionAst nodeBody = dynamicNodeStatement.CommandElements[2] as ScriptBlockExpressionAst
                ?? throw new InvalidOperationException("Failed to parse Node body in DSC configuration.");
            NamedBlockAst? scriptBlockBody = nodeBody.ScriptBlock.Find(ast => ast is NamedBlockAst, false) as NamedBlockAst
                ?? throw new InvalidOperationException("Failed to parse Node body statements in DSC configuration.");
            ReadOnlyCollection<StatementAst> resourceInstancesInNode = scriptBlockBody.Statements;

            foreach (DynamicKeywordStatementAst resource in resourceInstancesInNode.Cast<DynamicKeywordStatementAst>())
            {
                DscResourceInstance currentResourceInfo = new();
                Dictionary<string, object?> currentResourceProperties = [];

                // CommandElements
                // 0 - Resource Type
                // 1 - Resource Instance Name
                // 2 - Key/Pair Value list of parameters.
                string resourceType = resource.CommandElements[0].ToString();
                string resourceInstanceName = string.Empty;
                if (resource.CommandElements[1] is StringConstantExpressionAst resourceInstanceNameAst)
                {
                    resourceInstanceName = resourceInstanceNameAst.Value;
                }
                else if (resource.CommandElements[1] is ExpandableStringExpressionAst resourceInstanceNameExpAst)
                {
                    resourceInstanceName = resourceInstanceNameExpAst.Value;
                }
                else
                {
                    throw new InvalidOperationException("Failed to parse resource instance name in DSC configuration.");
                }

                currentResourceInfo.ResourceName = resourceType;
                currentResourceInfo.ResourceInstanceName = resourceInstanceName;

                if (!_dscResources.ContainsKey(resourceType))
                {
                    throw new InvalidOperationException(
                        $"Resource type '{resourceType}' (instance '{resourceInstanceName}') was not found among the loaded DSC resources.");
                }

                foreach (Tuple<ExpressionAst, StatementAst> keyValuePair in ((HashtableAst)resource.CommandElements[2]).KeyValuePairs)
                {
                    string key = keyValuePair.Item1.ToString();
                    object? value = null;

                    // Process every kind of property except single CIM instance assignments like:
                    // PsDscRunAsCredential = MSFT_Credential{
                    //    UserName = $ConfigurationData.NonNodeData.AdminUserName
                    //    Password = $ConfigurationData.NonNodeData.AdminPassword
                    // };
                    if (keyValuePair.Item2 is PipelineAst pip)
                    {
                        value = ProcessPipelineAst(pip, options?.IncludeCIMInstanceInfo ?? true);
                    }
                    else if (keyValuePair.Item2 is DynamicKeywordStatementAst dynamicStatement)
                    {
                        value = ProcessDynamicKeywordStatementAst(dynamicStatement, options?.IncludeCIMInstanceInfo ?? true);
                    }
                    currentResourceProperties.Add(key, value!);
                }

                currentResourceInfo.Properties = currentResourceProperties;
                result.Add(currentResourceInfo);
            }

            return result;
        }

        private static object? ProcessPipelineAst(PipelineAst pip, bool includeCimInstanceInfo)
        {
            // CommandExpressionAst is for Strings, Integers, Arrays, Variables, the "basic" types in a PowerShell DSC configuration
            if (pip.PipelineElements[0] is not CommandExpressionAst expr)
            {
                // CommandAst is for "complex" objects like CIMInstances, e.g. PsDscRunAsCredential or commands like New-Object System.Management.Automation.PSCredential('Password', (ConvertTo-SecureString ((New-Guid).ToString()) -AsPlainText -Force));
                CommandAst ast = pip.PipelineElements[0] as CommandAst ?? throw new InvalidOperationException("Unexpected AST structure in DSC configuration parsing.");
                return ProcessCommandAst(ast, includeCimInstanceInfo).Item2;
            }

            return expr.Expression is not null
                ? ProcessExpressionAst(expr.Expression, includeCimInstanceInfo)
                : pip.Parent.ToString();
        }

        private static (string, object?) ProcessCommandAst(CommandAst commandAst, bool includeCimInstanceInfo)
        {
            Dictionary<string, object?> result = [];
            ReadOnlyCollection<CommandElementAst>? elements = commandAst.CommandElements;

            // A single CIM instance is defined as a CommandAst with a ScriptBlockExpressionAst body
            if (elements.Count >= 2)
            {
                ScriptBlockExpressionAst? cimInstanceBody = elements.Count is 2 or 3
                    ? elements[1] as ScriptBlockExpressionAst
                    : elements[elements.Count - 1] as ScriptBlockExpressionAst;

                if (cimInstanceBody is not null)
                {
                    StringConstantExpressionAst? cimInstanceNameExpression = elements.Count is 2 or 3
                        ? elements[0] as StringConstantExpressionAst
                        : elements[elements.Count - 2] as StringConstantExpressionAst;

                    string cimInstanceName = cimInstanceNameExpression is not null
                    ? cimInstanceNameExpression.Value
                    : throw new InvalidOperationException("CIM Instance name not found in DSC configuration.");

                    if (includeCimInstanceInfo)
                    {
                        result.Add("CIMInstance", cimInstanceName);
                    }

                    // Each line in the script block (the contents of the scriptblock is defined as a "NamedBlockAst") is a PipelineAst
                    ReadOnlyCollection<StatementAst> propertyStatementsInCimInstanceBody = cimInstanceBody.ScriptBlock.EndBlock.Statements;
                    foreach (StatementAst statement in propertyStatementsInCimInstanceBody)
                    {
                        PipelineAst pipelineAst = statement as PipelineAst
                            ?? throw new InvalidOperationException("Failed to parse as pipeline statement in CIM instance scriptblock.");

                        CommandAst propertyStatement = pipelineAst.PipelineElements[0] as CommandAst
                            ?? throw new InvalidOperationException("Failed to parse property statement in CIM instance scriptblock.");

                        // Evaluate each property assignment
                        (string, object?) res = ProcessCommandAst(propertyStatement, includeCimInstanceInfo);
                        result.Add(res.Item1, res.Item2);
                    }

                    string propertyName = string.Empty;
                    // If the CIM instance is part of a property assignment, the property name is the first element
                    // This is the same logic as below, but simplified. We assume it is a property assignment if there are more than 3 elements
                    if (elements.Count > 3)
                    {
                        propertyName = ((StringConstantExpressionAst)elements[0]).Value;
                    }
                    return (propertyName, result);
                }

                // If however it is a property assignment inside of a CIM instance, it can either be a StringConstantExpression with the value "="
                // Example: PsDscRunAsCredential = MSFT_Credential{
                //             UserName = $ConfigurationData.NonNodeData.AdminUserName <-- This is such a thing
                //             Password = $ConfigurationData.NonNodeData.AdminPassword <-- And this is one too
                //          };
                // Or it can be a real command expression. If the cound is equal to 3 and the second element is an equal sign, then it is a property assignment
                // In the other cases, we treat is a command execution
                ConstantExpressionAst assignmentOperator = elements[1] as ConstantExpressionAst
                ?? throw new InvalidOperationException($"Failed to find a matching type for statement '{commandAst}'.");

                if (assignmentOperator.Value.Equals("="))
                {
                    StringConstantExpressionAst key = (StringConstantExpressionAst)elements[0];
                    return (key.Value, ProcessExpressionAst((ExpressionAst)elements[2], includeCimInstanceInfo));
                }

                return ("", commandAst.ToString());
            }

            return ("", commandAst.ToString());
        }

        private static object ProcessExpressionAst(ExpressionAst expr, bool includeCimInstanceInfo)
        {
            return expr switch
            {
                // A variable like $varName. Is either a normal variable or $true/$false
                VariableExpressionAst variable => ProcessVariableExpressionAst(variable),
                // A constant like "stringValue" or 123
                ConstantExpressionAst constant => ProcessConstantExpressionAst(constant),
                // A member of an object like $obj.Property. Used for configuration data, e.g. $ConfigurationData.NonNodeData.ApplicationId
                MemberExpressionAst member => ProcessMemberExpressionAst(member),
                // An array like @("value1", "value2")
                ArrayExpressionAst array => ProcessArrayExpressionAst(array, includeCimInstanceInfo),
                // An expandable string like "https://$OrganizationName/"
                ExpandableStringExpressionAst expString => expString.Value,
                // A hashtable like @{key=value; key2=value2}
                HashtableAst hashtable => ProcessHashtableExpressionAst(hashtable, includeCimInstanceInfo),
                _ => expr.ToString()
            };
        }

        private static List<object> ProcessArrayExpressionAst(ArrayExpressionAst arrayAst, bool includeCimInstanceInfo)
        {
            StatementBlockAst arrayDefinition = arrayAst.SubExpression;

            if (arrayDefinition.Statements.Count == 0)
            {
                return [];
            }

            // Arrays can contain strings, integers, variables, and CIM instances
            // Strings, integers and variables are represented as a PipelineAst
            PipelineAst? firstArrayValue = arrayDefinition.Statements[0] as PipelineAst;
            if (firstArrayValue is not null)
            {
                List<object> returnList = [];
                foreach (PipelineAst pipelineArrayValue in arrayDefinition.Statements.Cast<PipelineAst>())
                {
                    if (pipelineArrayValue.PipelineElements[0] is not CommandExpressionAst arrayElementDefinition)
                    {
                        // Complex array items, defined e.g. for Intune assignments
                        // Assignments = @(
                        //     MSFT_DeviceManagementManagedGooglePlayMobileAppAssignment{
                        //         groupDisplayName = "AADGroup_10"
                        //         deviceAndAppManagementAssignmentFilterType = "none"
                        //         dataType = "#microsoft.graph.groupAssignmentTarget"
                        //         intent = "required"
                        //         assignmentSettings = MSFT_DeviceManagementManagedGooglePlayMobileAppAssignmentSettings{
                        //             odataType = "#microsoft.graph.androidManagedStoreAppAssignmentSettings"
                        //             autoUpdateMode = "priority"
                        //         }
                        //     }
                        // );
                        (string, object?) complexArrayItemTuple = ProcessCommandAst((CommandAst)pipelineArrayValue.PipelineElements[0], includeCimInstanceInfo);
                        returnList.Add(complexArrayItemTuple.Item2!);
                        continue;
                    }
                    switch (arrayElementDefinition.Expression)
                    {
                        // Array literals are arrays of strings like @("value1", "value2"), integers like @(1,2,3)
                        // variables like @($var1, $var2), expandable strings like @("https://$OrganizationName/", "https://$TenantGuid/")
                        // or more types of elements
                        case ArrayLiteralAst arrayLiteral:
                            foreach (ExpressionAst element in arrayLiteral.Elements)
                            {
                                returnList.Add(ProcessExpressionAst(element, includeCimInstanceInfo));
                            }
                            break;
                        // Any other type of expression inside the array
                        case ExpressionAst expression:
                            returnList.Add(ProcessExpressionAst(expression, includeCimInstanceInfo));
                            break;
                        default:
                            break;
                    }
                }
                return returnList;
            }

            // Arrays containing CIM instances are represented as DynamicKeywordStatementAst
            List<object> arrayCimInstances = [];
            foreach (DynamicKeywordStatementAst arrayCimInstance in arrayDefinition.Statements.Cast<DynamicKeywordStatementAst>())
            {
                arrayCimInstances.Add(ProcessDynamicKeywordStatementAst(arrayCimInstance, includeCimInstanceInfo));
            }
            return arrayCimInstances;
        }

        private static Dictionary<string, object?> ProcessDynamicKeywordStatementAst(
            DynamicKeywordStatementAst commandAst,
            bool includeCimInstanceInfo)
        {
            ReadOnlyCollection<CommandElementAst>? elements = commandAst.CommandElements;

            // Process in groups of 3: CIMInstanceName, dash, Hashtable
            Dictionary<string, object?> currentResult = [];

            if (elements[0] is StringConstantExpressionAst cimInstanceNameAst &&
                elements[2] is HashtableAst hashtableAst)
            {
                string cimInstanceName = cimInstanceNameAst.Value;

                if (includeCimInstanceInfo)
                {
                    currentResult["CIMInstance"] = cimInstanceName;
                }

                foreach (Tuple<ExpressionAst, StatementAst> kvp in hashtableAst.KeyValuePairs)
                {
                    string key = kvp.Item1.ToString().Trim('"', '\'');

                    object? value = null;
                    if (kvp.Item2 is PipelineAst pip)
                    {
                        value = ProcessPipelineAst(pip, includeCimInstanceInfo);
                    }
                    else if (kvp.Item2 is DynamicKeywordStatementAst dynamicStatement)
                    {
                        value = ProcessDynamicKeywordStatementAst(dynamicStatement, includeCimInstanceInfo);
                    }
                    currentResult[key] = value;
                }
            }

            return currentResult;
        }

        private static object ProcessVariableExpressionAst(VariableExpressionAst variableAst)
        {
            string text = variableAst.ToString();

            return text.Equals("$true", StringComparison.OrdinalIgnoreCase) || text.Equals("$false", StringComparison.OrdinalIgnoreCase)
                ? bool.Parse(text.TrimStart('$'))
                : text;
        }

        private static object ProcessConstantExpressionAst(ConstantExpressionAst constantAst) => constantAst.Value;

        private static string ProcessMemberExpressionAst(MemberExpressionAst memberAst) => memberAst.ToString();

        private static Hashtable ProcessHashtableExpressionAst(HashtableAst hashtableAst, bool includeCimInstanceInfo)
        {
            Hashtable result = [];
            foreach (Tuple<ExpressionAst, StatementAst> kvp in hashtableAst.KeyValuePairs)
            {
                string key = kvp.Item1.ToString();
                object? value = null;
                if (kvp.Item2 is PipelineAst pip)
                {
                    value = ProcessPipelineAst(pip, includeCimInstanceInfo);
                }
                else if (kvp.Item2 is DynamicKeywordStatementAst dynamicStatement)
                {
                    value = ProcessDynamicKeywordStatementAst(dynamicStatement, includeCimInstanceInfo);
                }
                result[key] = value;
            }
            return result;
        }

        private static List<DscResourceInstance> UpdateWithMetadata(Token[] tokens, List<DscResourceInstance> parsedObjects)
        {
            // Find Node token position
            int tokenPositionOfNode = 0;
            for (int i = 0; i < tokens.Length; i++)
            {
                if (tokens[i].Kind == TokenKind.DynamicKeyword && tokens[i].Text == "Node")
                {
                    tokenPositionOfNode = i;
                    break;
                }
            }

            // Process comments after Node
            for (int i = tokenPositionOfNode; i < tokens.Length; i++)
            {
                if (tokens[i].Kind is not TokenKind.Comment)
                {
                    continue;
                }

                int keywordIndex = i - 1;
                while (keywordIndex >= 0 && tokens[keywordIndex].Kind is not TokenKind.DynamicKeyword)
                {
                    keywordIndex--;
                }

                // A comment with no enclosing resource declaration has nothing to attach to
                if (keywordIndex < 0 || keywordIndex + 1 >= tokens.Length ||
                    tokens[keywordIndex + 1] is not StringExpandableToken resourceInstanceName)
                {
                    continue;
                }

                string commentResourceType = tokens[keywordIndex].Text;
                string commentResourceInstanceName = resourceInstanceName.Value;

                // Backtrack to find associated property
                int propertyIndex = i;
                while (propertyIndex >= 0 && tokens[propertyIndex].Kind is not TokenKind.Identifier and not TokenKind.NewLine)
                {
                    propertyIndex--;
                }

                if (propertyIndex < 0 || tokens[propertyIndex].Kind is not TokenKind.Identifier)
                {
                    continue;
                }

                string commentAssociatedProperty = tokens[propertyIndex].Text;

                foreach (DscResourceInstance parsedObject in parsedObjects)
                {
                    if (parsedObject.ResourceName.Equals(commentResourceType, StringComparison.OrdinalIgnoreCase) &&
                        parsedObject.ResourceInstanceName.Equals(commentResourceInstanceName, StringComparison.Ordinal) &&
                        parsedObject.Properties.ContainsKey(commentAssociatedProperty))
                    {
                        parsedObject.AddProperty($"_metadata_{commentAssociatedProperty}", tokens[i].Text);
                    }
                }
            }

            return parsedObjects;
        }

        private static void AppendProperty(StringBuilder result, string property, object? value, string additionalSpaces, string childSpacer, int childLevel)
        {
            switch (value)
            {
                case string strValue:
                    AppendPropertyPrefix(result, property, additionalSpaces, childSpacer);
                    // A string starting with $ and containing no spaces is a variable reference, not a literal
                    if (strValue.StartsWith("$", StringComparison.Ordinal) && !strValue.StartsWith("$($", StringComparison.Ordinal) && !strValue.Contains(' '))
                    {
                        _ = result.AppendLine(strValue);
                    }
                    else if (strValue.StartsWith("New-Object", StringComparison.Ordinal))
                    {
                        _ = result.AppendLine(strValue.TrimStart('"').TrimEnd('"'));
                    }
                    else
                    {
                        _ = AppendQuoted(result, strValue).AppendLine();
                    }
                    break;

                case int intValue:
                    AppendPropertyPrefix(result, property, additionalSpaces, childSpacer);
                    _ = result.Append(intValue).AppendLine();
                    break;

                case bool boolValue:
                    AppendPropertyPrefix(result, property, additionalSpaces, childSpacer);
                    _ = result.Append('$').Append(boolValue).AppendLine();
                    break;

                // Covers Hashtable and the Dictionary instances the parser produces for CIM instances
                case IDictionary dictionary:
                    AppendPropertyPrefix(result, property, additionalSpaces, childSpacer);
                    int contentStart = result.Length;
                    AppendDscObjects(result, [AsHashtable(dictionary)], childLevel + 1);
                    StripIndentOfOpeningLine(result, contentStart);
                    break;

                case IEnumerable sequence:
                    AppendPropertyPrefix(result, property, additionalSpaces, childSpacer);
                    AppendArray(result, sequence, childSpacer, childLevel);
                    break;

                default:
                    if (value != null)
                    {
                        AppendPropertyPrefix(result, property, additionalSpaces, childSpacer);
                        _ = result.Append(value).AppendLine();
                    }
                    break;
            }
        }

        private static void AppendPropertyPrefix(StringBuilder result, string property, string additionalSpaces, string childSpacer)
        {
            _ = result.Append(childSpacer).Append("    ").Append(property).Append(additionalSpaces).Append("= ");
        }

        private static StringBuilder AppendQuoted(StringBuilder result, string value)
        {
            _ = result.Append('"');
            foreach (char character in value)
            {
                if (character is '`' or '"')
                {
                    _ = result.Append('`');
                }
                _ = result.Append(character);
            }
            return result.Append('"');
        }

        private static void AppendArray(StringBuilder result, IEnumerable sequence, string childSpacer, int childLevel)
        {
            _ = result.Append("@(");

            List<object?> items = [];
            bool isSimpleArray = true;
            foreach (object? item in sequence)
            {
                items.Add(item);
                if (item is IDictionary)
                {
                    isSimpleArray = false;
                }
            }

            if (items.Count == 0)
            {
                _ = result.AppendLine(")");
                return;
            }

            if (isSimpleArray && items.Count == 1)
            {
                AppendArrayItem(result, items[0]);
                _ = result.AppendLine(")");
                return;
            }

            string itemIndent = new(' ', childSpacer.Length + 8);

            _ = result.AppendLine();
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0)
                {
                    _ = result.Append(Environment.NewLine);
                }

                if (items[i] is IDictionary nested)
                {
                    AppendDscObjects(result, [AsHashtable(nested)], childLevel + 2);
                    result.Length -= Environment.NewLine.Length;
                }
                else
                {
                    _ = result.Append(itemIndent);
                    AppendArrayItem(result, items[i]);
                }
            }
            _ = result.AppendLine();

            _ = result.Append(childSpacer).AppendLine("    )");
        }

        private static Hashtable AsHashtable(IDictionary dictionary)
        {
            if (dictionary is Hashtable hashtable)
            {
                return hashtable;
            }

            Hashtable converted = new(dictionary.Count);
            foreach (DictionaryEntry entry in dictionary)
            {
                converted[entry.Key] = entry.Value;
            }
            return converted;
        }

        private static void AppendArrayItem(StringBuilder result, object? item)
        {
            if (item is string text)
            {
                _ = AppendQuoted(result, text);
            }
            else
            {
                _ = result.Append(item);
            }
        }

        /// <summary>
        /// Removes the leading indentation of the first rendered line when that line opens a block, so
        /// the nested object starts directly after the "= " of its property assignment.
        /// </summary>
        private static void StripIndentOfOpeningLine(StringBuilder result, int contentStart)
        {
            int lineEnd = contentStart;
            while (lineEnd < result.Length && result[lineEnd] is not '\r' and not '\n')
            {
                lineEnd++;
            }

            int indent = contentStart;
            while (indent < lineEnd && result[indent] == ' ')
            {
                indent++;
            }

            if (indent > contentStart && lineEnd > contentStart && result[lineEnd - 1] == '{')
            {
                _ = result.Remove(contentStart, indent - contentStart);
            }
        }
    }
}
