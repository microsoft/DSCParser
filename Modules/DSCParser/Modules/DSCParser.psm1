function Update-DSCResultWithMetadata
{
    [CmdletBinding()]
    [OutputType([Array])]
    param(
        [Parameter(Mandatory = $true)]
        [Array]
        $Tokens,

        [Parameter(Mandatory = $true)]
        [Array]
        $ParsedObject
    )

    # Find the location of the Node token. This is to ensure
    # we only look at comments that come after.
    $i = 0
    do
    {
        $i++
    } while (($tokens[$i].Kind -ne 'DynamicKeyword' -and $tokens[$i].Extent -ne 'Node') -and $i -le $tokens.Length)
    $tokenPositionOfNode = $i

    for ($i = $tokenPositionOfNode; $i -le $tokens.Length; $i++)
    {
        $percent = (($i - $tokenPositionOfNode) / ($tokens.Length - $tokenPositionOfNode) * 100)
        Write-Progress -Status "Processing $percent%" `
                       -Activity "Parsing Comments" `
                       -PercentComplete $percent
        if ($tokens[$i].Kind -eq 'Comment')
        {
            # Found a comment. Backtrack to find what resource it is part of.
            $stepback = 1
            do
            {
                $stepback++
            } while ($tokens[$i-$stepback].Kind -ne 'DynamicKeyword')

            $commentResourceType         = $tokens[$i-$stepback].Text
            $commentResourceInstanceName = $tokens[$i-$stepback + 1].Value

            # Backtrack to find what property it is associated with.
            $stepback = 1
            do
            {
                $stepback++
            } while ($tokens[$i-$stepback].Kind -ne 'Identifier')
            $commentAssociatedProperty = $tokens[$i-$stepback].Text

            # Loop through all instances in the ParsedObject to retrieve
            # the one associated with the comment.
            for ($j = 0; $j -le $ParsedObject.Length; $j++)
            {
                if ($ParsedObject[$j].ResourceName -eq $commentResourceType -and `
                    $ParsedObject[$j].ResourceInstanceName -eq $commentResourceInstanceName -and `
                    $ParsedObject[$j].Keys.Contains($commentAssociatedProperty))
                {
                    $ParsedObject[$j].Add("_metadata_$commentAssociatedProperty", $tokens[$i].Text)
                }
            }
        }
    }
    Write-Progress -Completed `
                   -Activity "Parsing Comments"
    return $ParsedObject
}

function ConvertFrom-CIMInstanceToHashtable
{
    [CMdletBinding()]
    [OutputType([system.Collections.Hashtable])]
    param(
        [Parameter(Mandatory = $true)]
        [System.Object]
        $ChildObject,

        [Parameter(Mandatory = $true)]
        [System.String]
        $ResourceName,

        [Parameter()]
        [System.String]
        $Schema,

        [Parameter()]
        [System.Boolean]
        $IncludeCIMInstanceInfo = $true
    )

    $SchemaJSONObject = $null
    # Case we have an array of CIMInstances
    if ($ChildObject.GetType().Name -eq 'PipelineAst')
    {
        $result = @()
        $statements = $ChildObject.PipelineElements.Expression.SubExpression.Statements
        foreach ($statement in $statements)
        {
            $result += ConvertFrom-CIMInstanceToHashtable -ChildObject $statement `
                                                          -ResourceName $ResourceName `
                                                          -Schema $Schema `
                                                          -IncludeCIMInstanceInfo $IncludeCIMInstanceInfo
        }
    }
    else
    {
        $result = @()
        for ($i = 1; $i -le $ChildObject.CommandElements.Count / 3; $i++)
        {
            $currentResult = @{}
            $KeyPairs = $ChildObject.CommandElements[$i*3-1].KeyValuePairs
            $CIMInstanceName = $ChildObject.CommandElements[($i-1)*3].Value

            # If a schema definition isn't provided, use the CIM classes
            # cmdlets to retrieve information about parameter types.
            if ([System.String]::IsNullOrEmpty($Schema))
            {
                # Get the CimClass associated with the current CimInstanceName
                $CIMClassObject = Get-CimClass -ClassName $CimInstanceName `
                                            -Namespace 'ROOT/Microsoft/Windows/DesiredStateConfiguration' `
                                            -ErrorAction SilentlyContinue

                if ($null -eq $CIMClassObject)
                {
                    $dscResourceInfo = Get-DSCResource -Name $ResourceName
                    $InvokeParams = @{
                        Name        = $ResourceName
                        Method      = 'Get'
                        Property    = @{
                            'dummyValue' = 'dummyValue'
                        }
                        ModuleName  = @{
                            ModuleName    = $dscResourceInfo.ModuleName
                            ModuleVersion = $dscResourceInfo.Version
                        }
                        ErrorAction = 'SilentlyContinue'
                    }

                    try
                    {
                        Invoke-DscResource @InvokeParams | Out-Null
                    
                        $CIMClassObject = Get-CimClass -ClassName $CimInstanceName `
                                            -Namespace 'ROOT/Microsoft/Windows/DesiredStateConfiguration' `
                                            -ErrorAction SilentlyContinue

                        $breaker = 5
                        while ($null -eq $CIMCLassObject -and $breaker -gt 0)
                        {
                            Start-Sleep -Seconds 1
                            $CIMClassObject = Get-CimClass -ClassName $CimInstanceName `
                                                           -Namespace 'ROOT/Microsoft/Windows/DesiredStateConfiguration' `
                                                           -ErrorAction SilentlyContinue
                            $breaker--
                        }
                    }
                    catch
                    {
                        # We only care if the resource can't be found, not if it fails while executing
                        if ($_.Exception.Message -match '(Resource \w+ was not found|The PowerShell DSC resource .+ does not exist at the PowerShell module path nor is it registered as a WMI DSC resource)')
                        {
                            throw $_
                        }
                    }
                }
                $CIMClassProperties = $CIMClassObject.CimClassProperties
            }
            else
            {
                # Schema definition was provided.
                if ($null -eq $SchemaJSONObject)
                {
                    $SchemaJSONObject = ConvertFrom-Json $Schema
                }
                $CIMClassObject = $SchemaJSONObject | Where-Object -FilterScript {$_.ClassName -eq $CIMInstanceName}
                $CIMClassProperties = $CIMClassObject.Parameters
            }

            if ($IncludeCIMInstanceInfo)
            {
                $currentResult.Add("CIMInstance", $CIMInstanceName)
            }
            foreach ($entry in $keyPairs)
            {
                $associatedCIMProperty = $CIMClassProperties | Where-Object -FilterScript {$_.Name -eq $entry.Item1.ToString()}
                if ($null -ne $entry.Item2.PipelineElements)
                {
                    $staticType = $entry.Item2.PipelineElements.Expression.StaticType.ToString()
                    $subExpression = $entry.Item2.PipelineElements.Expression.SubExpression

                    if ([System.String]::IsNullOrEmpty($subExpression))
                    {
                        if ([System.String]::IsNullOrEmpty($entry.Item2.PipelineElements.Expression.Value))
                        {
                            $subExpression = $entry.Item2.PipelineElements.Expression.ToString()
                        }
                        else
                        {
                            $subExpression = $entry.Item2.PipelineElements.Expression.Value
                        }
                    }
                }
                elseif ($null -ne $entry.Item2.CommandElements)
                {
                    $staticType    = $entry.Item2.CommandElements[2].StaticType.ToString()
                    $subExpression = $entry.Item2.CommandElements[0].Value
                }

                # Case where the item is an array of Sub-CIMInstances.
                if ($staticType -eq 'System.Object[]' -and `
                    $subExpression.ToString().StartsWith('MSFT_'))
                {
                    $subResult = @()
                    foreach ($subItem in $subExpression)
                    {
                        $subResult += ConvertFrom-CIMInstanceToHashtable -ChildObject $subItem.Statements `
                                                                         -ResourceName $ResourceName `
                                                                         -Schema $Schema `
                                                                         -IncludeCIMInstanceInfo $IncludeCIMInstanceInfo
                    }
                    $currentResult.Add($entry.Item1.ToString(), $subResult)
                }
                # Case the item is a single CIMInstance.
                elseif (($staticType -eq 'System.Collections.Hashtable' -and `
                    $subExpression.ToString().StartsWith('MSFT_')) -or `
                    $associatedCIMProperty.CIMType -eq 'InstanceArray')
                {
                    $isArray = $false
                    if ($entry.Item2.ToString().StartsWith('@('))
                    {
                        $isArray = $true
                    }
                    $subResult = ConvertFrom-CIMInstanceToHashtable -ChildObject $entry.Item2 `
                                                                    -ResourceName $ResourceName `
                                                                    -Schema $Schema `
                                                                    -IncludeCIMInstanceInfo $IncludeCIMInstanceInfo
                    if ($isArray)
                    {
                        $subResult = @($subResult)
                    }
                    $currentResult.Add($entry.Item1.ToString(), $subResult)
                }
                elseif ($associatedCIMProperty.CIMType -eq 'stringArray' -or `
                        $associatedCIMProperty.CIMType -eq 'string[]')
                {
                    $subExpression = $subExpression.ToString().Replace("',`"", "`r`n").Replace("`",'", "`r`n").Replace("`",`"", "`r`n").Replace("',`'", "`r`n").Replace("',", "`r`n").Replace("`"", "`r`n")
                    $subExpression = (-split $subExpression).Trim("`"").Trim("'")
                    $currentResult.Add($entry.Item1.ToString(), $subExpression)
                }
                elseif ($associatedCIMProperty.CIMType -eq 'boolean' -and `
                        $subExpression.GetType().Name -eq 'string')
                {
                    if ($subExpression -eq "`$true")
                    {
                        $subExpression = $true
                    }
                    else
                    {
                        $subExpression = $false
                    }
                    $currentResult.Add($entry.Item1.ToString(), $subExpression)
                }
                else
                {
                    if ($associatedCIMProperty.CIMType -ne 'string' -and `
                        $associatedCIMProperty.CIMType -ne 'stringArray' -and `
                        $associatedCIMProperty.CIMType -ne 'string[]')
                    {
                        # Try to parse the value based on the retrieved type.
                        $scriptBlock = @"
                                        `$typeStaticMethods = [$($associatedCIMProperty.CIMType)] | gm -static
                                        if (`$typeStaticMethods.Name.Contains('TryParse'))
                                        {
                                            [$($associatedCIMProperty.CIMType)]::TryParse(`$subExpression, [ref]`$subExpression) | Out-Null
                                        }
"@
                        Invoke-Expression -Command $scriptBlock | Out-Null
                    }
                    $currentResult.Add($entry.Item1.ToString(), $subExpression)
                }
            }
            $result += $currentResult
        }
    }

    return $result
}

function ConvertTo-DSCObject
{
    [CmdletBinding(DefaultParameterSetName = 'Path')]
    [OutputType([Array])]
    param
    (
        [Parameter(Mandatory = $true,
            ParameterSetName = 'Path')]
        [ValidateScript({
                if (-Not ($_ | Test-Path) ) {
                    throw "File or folder does not exist"
                }
                if (-Not ($_ | Test-Path -PathType Leaf) ) {
                    throw "The Path argument must be a file. Folder paths are not allowed."
                }
                return $true
            })]
        [System.String]
        $Path,

        [Parameter(Mandatory = $true,
            ParameterSetName = 'Content')]
        [System.String]
        $Content,

        [Parameter(ParameterSetName = 'Path')]
        [Parameter(ParameterSetName = 'Content')]
        [System.Boolean]
        $IncludeComments = $false,

        [Parameter(ParameterSetName = 'Path')]
        [Parameter(ParameterSetName = 'Content')]
        [System.String]
        $Schema,

        [Parameter(ParameterSetName = 'Path')]
        [Parameter(ParameterSetName = 'Content')]
        [System.Boolean]
        $IncludeCIMInstanceInfo = $true
    )

    $result = @()
    $Tokens      = $null
    $ParseErrors = $null

    # Use the AST to parse the DSC configuration
    if (-not [System.String]::IsNullOrEmpty($Path) -and [System.String]::IsNullOrEmpty($Content))
    {
        $Content = Get-Content $Path -Raw
    }
    $AST = [System.Management.Automation.Language.Parser]::ParseInput($Content, [ref]$Tokens, [ref]$ParseErrors)

    # Look up the Configuration definition ("")
    $Config = $AST.Find({$Args[0].GetType().Name -eq 'ConfigurationDefinitionAst'}, $False)

    # Retrieve information about the DSC Modules imported in the config
    # and get the list of their associated resources.
    $ModulesToLoad = @()
    foreach ($statement in $config.body.ScriptBlock.EndBlock.Statements)
    {
        if ($null -ne $statement.CommandElements -and $null -ne $statement.CommandElements[0].Value -and `
            $statement.CommandElements[0].Value -eq 'Import-DSCResource')
        {
            $currentModule = @{}
            for ($i = 0; $i -le $statement.CommandElements.Count; $i++)
            {
                if ($statement.CommandElements[$i].ParameterName -eq 'ModuleName' -and `
                    ($i+1) -lt $statement.CommandElements.Count)
                {
                    $moduleName = $statement.CommandElements[$i+1].Value      
                    $currentModule.Add('ModuleName', $moduleName)
                }
                elseif ($statement.CommandElements[$i].ParameterName -eq 'ModuleVersion' -and `
                    ($i+1) -lt $statement.CommandElements.Count)
                {
                    $moduleVersion = $statement.CommandElements[$i+1].Value 
                    $currentModule.Add('ModuleVersion', $moduleVersion)
                }
            }
            $ModulesToLoad += $currentModule
        }
    }
    $DSCResources = @()
    foreach ($moduleToLoad in $ModulesToLoad)
    {
        $loadedModuleTest = Get-Module -Name $moduleToLoad.ModuleName -ListAvailable | Where-Object -FilterScript {$_.Version -eq $moduleToLoad.ModuleVersion}
        
        if ($null -eq $loadedModuleTest -and -not [System.String]::IsNullOrEmpty($moduleToLoad.ModuleVersion))
        {
            throw "Module {$($moduleToLoad.ModuleName)} version {$($moduleToLoad.ModuleVersion)} specified in the configuration isn't installed on the machine/agent. Install it by running: Install-Module -Name '$($moduleToLoad.ModuleName)' -RequiredVersion '$($moduleToLoad.ModuleVersion)'"
        }
        else
        {
            $currentResources = Get-DSCResource -Module $moduleToLoad.ModuleName

            if (-not [System.String]::IsNullOrEmpty($moduleToLoad.ModuleVersion))
            {
                $currentResources = $currentResources | Where-Object -FilterScript {$_.Version -eq $moduleToLoad.ModuleVersion}
            }
            $DSCResources += $currentResources
        }
    }

    # Drill down
    # Body.ScriptBlock is the part after "Configuration <InstanceName> {"
    # EndBlock is the actual code within that Configuration block
    # Find the first DynamicKeywordStatement that has a word "Node" in it, find all "NamedBlockAst" elements, these are the DSC resource definitions
    try
    {
        $resourceInstances = $Config.Body.ScriptBlock.EndBlock.Statements.Find({$Args[0].GetType().Name -eq 'DynamicKeywordStatementAst' -and $Args[0].CommandElements[0].StringConstantType -eq 'BareWord' -and $Args[0].CommandElements[0].Value -eq 'Node'}, $False).commandElements[2].ScriptBlock.Find({$Args[0].GetType().Name -eq 'NamedBlockAst'}, $False).Statements
    }
    catch
    {
        $resourceInstances = $Config.Body.ScriptBlock.EndBlock.Statements | Where-Object -FilterScript {$null -ne $_.CommandElements -and $_.CommandElements[0].Value -ne 'Import-DscResource'}
    }

    # Get the name of the configuration.
    $configurationName = $Config.InstanceName.Value

    $totalCount = 1
    foreach ($resource in $resourceInstances)
    {
        $currentResourceInfo = @{}

        # CommandElements
        # 0 - Resource Type
        # 1 - Resource Instance Name
        # 2 - Key/Pair Value list of parameters.
        $resourceType         = $resource.CommandElements[0].Value
        $resourceInstanceName = $resource.CommandElements[1].Value

        $percent = ($totalCount / ($resourceInstances.Count) * 100)
        Write-Progress -Status "[$totalCount/$($resourceInstances.Count)] $resourceType - $resourceInstanceName" `
                       -PercentComplete $percent `
                       -Activity "Parsing Resources"
        $currentResourceInfo.Add("ResourceName", $resourceType)
        $currentResourceInfo.Add("ResourceInstanceName", $resourceInstanceName)

        # Get a reference to the current resource.
        $currentResource = $DSCResources | Where-Object -FilterScript {$_.Name -eq $resourceType}

        # Loop through all the key/pair value
        foreach ($keyValuePair in $resource.CommandElements[2].KeyValuePairs)
        {
            $isVariable = $false
            $key        = $keyValuePair.Item1.Value

            if ($null -ne $keyValuePair.Item2.PipelineElements)
            {
                if ($null -eq $keyValuePair.Item2.PipelineElements.Expression.Value)
                {
                    if ($null -ne $keyValuePair.Item2.PipelineElements.Expression)
                    {
                        if ($keyValuePair.Item2.PipelineElements.Expression.StaticType.Name -eq 'Object[]')
                        {
                            $value = $keyValuePair.Item2.PipelineElements.Expression.SubExpression
                            $newValue = @()
                            foreach ($expression in $value.Statements.PipelineElements.Expression)
                            {
                                if ($null -ne $expression.Elements)
                                {
                                    foreach ($element in $expression.Elements)
                                    {
                                        if ($null -ne $element.VariablePath)
                                        {
                                            $newValue += "`$" + $element.VariablePath.ToString()
                                        }
                                        elseif ($null -ne $element.Value)
                                        {
                                            $newValue += $element.Value
                                        }
                                    }
                                }
                                else
                                {
                                    $newValue += $expression.Value
                                }
                            }
                            $value = $newValue
                        }
                        else
                        {
                            $value = $keyValuePair.Item2.PipelineElements.Expression.ToString()
                        }
                    }
                    else
                    {
                        $value = $keyValuePair.Item2.PipelineElements.Parent.ToString()
                    }

                    if ($value.GetType().Name -eq 'String' -and $value.StartsWith('$'))
                    {
                        $isVariable = $true
                    }
                }
                else
                {
                    $value = $keyValuePair.Item2.PipelineElements.Expression.Value
                }
            }

            # Retrieve the current property's type based on the resource's schema.
            $currentPropertyInResourceSchema = $currentResource.Properties | Where-Object -FilterScript { $_.Name -eq $key }
            $valueType = $currentPropertyInResourceSchema.PropertyType

            # If the value type is null, then the parameter doesn't exist
            # in the resource's schema and we throw a warning
            $propertyFound = $true
            if ($null -eq $valueType)
            {
                $propertyFound = $false
                Write-Warning "Defined property {$key} was not found in resource {$resourceType}"
            }

            if ($propertyFound)
            {
                # If the current property is not a CIMInstance
                if (-not $valueType.StartsWith('[MSFT_') -and `
                    $valueType -ne '[string]' -and `
                    $valueType -ne '[string[]]' -and `
                    -not $isVariable)
                {
                    # Try to parse the value based on the retrieved type.
                    $scriptBlock = @"
                                    `$typeStaticMethods = $valueType | gm -static
                                    if (`$typeStaticMethods.Name.Contains('TryParse'))
                                    {
                                        $valueType::TryParse(`$value, [ref]`$value) | Out-Null
                                    }
"@
                    Invoke-Expression -Command $scriptBlock | Out-Null
                }
                elseif ($valueType -eq '[String]' -or $isVariable)
                {
                    if ($isVariable -and [Boolean]::TryParse($value.TrimStart('$'), [ref][Boolean]))
                    {
                        if ($value -eq "`$true")
                        {
                            $value = $true
                        }
                        else
                        {
                            $value = $false
                        }
                    }
                    else
                    {
                        $value = $value
                    }
                }
                elseif ($valueType -eq '[string[]]')
                {
                    # If the property is an array but there's only one value
                    # specified as a string (not specifying the @()) then
                    # we need to create the array.
                    if ($value.GetType().Name -eq 'String' -and -not $value.StartsWith('@('))
                    {
                        $value = @($value)
                    }
                }
                else
                {
                    $isArray = $false
                    if ($keyValuePair.Item2.ToString().StartsWith('@('))
                    {
                        $isArray = $true
                    }
                    $value = ConvertFrom-CIMInstanceToHashtable -ChildObject $keyValuePair.Item2 `
                                                                -ResourceName $resourceType `
                                                                -Schema $Schema `
                                                                -IncludeCIMInstanceInfo $IncludeCIMInstanceInfo
                    if ($isArray)
                    {
                        $value = @($value)
                    }
                }
                $currentResourceInfo.Add($key, $value) | Out-Null
            }
        }
        
        $result += $currentResourceInfo
        $totalCount++
    }
    Write-Progress -Completed `
                   -Activity "Parsing Resources"

    if ($IncludeComments)
    {
        $result = Update-DSCResultWithMetadata -Tokens $Tokens `
                                               -ParsedObject $result
    }

    return [Array]$result
}

function ConvertFrom-DSCObject
{
    [CmdletBinding()]
    [OutputType([System.String])]

    Param(
        [parameter(Mandatory = $true)]
        [System.Collections.Hashtable[]]
        $DSCResources,

        [parameter(Mandatory = $false)]
        [System.Int32]
        $ChildLevel = 0
    )

    $results = [System.Text.StringBuilder]::New()
    $ParametersToSkip = @('ResourceInstanceName', 'ResourceName', 'CIMInstance')
    $childSpacer = ""
    for ($i = 0; $i -lt $ChildLevel; $i++)
    {
        $childSpacer += "    "
    }
    foreach ($entry in $DSCResources)
    {
        $longuestParameter = [int]($entry.Keys | Measure-Object -Maximum -Property Length).Maximum

        if ($entry.'CIMInstance')
        {
            [void]$results.AppendLine($childSpacer + $entry.CIMInstance + "{")
        }
        else
        {
            [void]$results.AppendLine($childSpacer + $entry.ResourceName + " `"$($entry.ResourceInstanceName)`"")
            [void]$results.AppendLine("$childSpacer{")
        }

        $entry.Keys = $entry.Keys | Sort-Object
        foreach ($property in $entry.Keys)
        {
            if ($property -notin $ParametersToSkip)
            {
                $additionalSpaces = " "
                for ($i = $property.Length; $i -lt $longuestParameter; $i++)
                {
                    $additionalSpaces += " "
                }

                if ($property -eq 'Credential')
                {
                    [void]$results.AppendLine("$childSpacer    $property$additionalSpaces= $($entry.$property)")
                }
                else
                {
                    switch -regex ($entry.$property.GetType().Name)
                    {
                        "String"
                        {
                            if ($entry.$property[0] -ne "$")
                            {
                                [void]$results.AppendLine("$childSpacer    $property$additionalSpaces= `"$($entry.$property.Replace('"', '`"'))`"")
                            }
                            else
                            {
                                [void]$results.AppendLine("$childSpacer    $property$additionalSpaces= $($entry.$property.Replace('"', '`"'))")
                            }
                        }
                        "Int32"
                        {
                            [void]$results.AppendLine("$childSpacer    $property$additionalSpaces= $($entry.$property)")
                        }
                        "Boolean"
                        {
                            [void]$results.AppendLine("$childSpacer    $property$additionalSpaces= `$$($entry.$property)")
                        }
                        "Object\[\]|OrderedDictionary"
                        {
                            if ($entry.$property.Length -gt 0)
                            {
                                $objectToTest = $entry.$property
                                if ($null -ne $objectToTest -and $objectToTest.Keys.Length -gt 0)
                                {
                                    if ($objectToTest.'CIMInstance')
                                    {
                                        if ($entry.$property.Length -gt 1)
                                        {
                                            $subResult = ConvertFrom-DSCObject -DSCResources $entry.$property -ChildLevel ($ChildLevel + 2)
                                            # Remove carriage return from last line
                                            $subResult = $subResult.Substring(0, $subResult.Length - 1)
                                            [void]$results.AppendLine("$childSpacer    $property$additionalSpaces= @(")
                                            [void]$results.AppendLine("$subResult")
                                            [void]$results.AppendLine("$childSpacer    )")
                                        }
                                        else
                                        {
                                            $subResult = ConvertFrom-DSCObject -DSCResources $entry.$property -ChildLevel ($ChildLevel + 1)
                                            # Remove carriage return from last line and trim empty spaces before equal sign
                                            $subResult = $subResult.Substring(0, $subResult.Length - 1).Trim()
                                            [void]$results.AppendLine("$childSpacer    $property$additionalSpaces= $subResult")
                                        }
                                    }
                                }
                                else
                                {
                                    switch($entry.$property[0].GetType().Name)
                                    {
                                        "String"
                                        {
                                            [void]$results.Append("$childSpacer    $property$additionalSpaces= @(")
                                            $tempArrayContent = ""
                                            foreach ($item in $entry.$property)
                                            {
                                                $tempArrayContent += "`"$($item.Replace('"', '`"'))`","
                                            }
                                            $tempArrayContent = $tempArrayContent.Remove($tempArrayContent.Length-1, 1)
                                            [void]$results.Append($tempArrayContent + ")`r`n")
                                        }
                                        "Int32"
                                        {
                                            [void]$results.Append("$childSpacer    $property$additionalSpaces= @(")
                                            $tempArrayContent = ""
                                            foreach ($item in $entry.$property)
                                            {
                                                $tempArrayContent += "$item,"
                                            }
                                            $tempArrayContent = $tempArrayContent.Remove($tempArrayContent.Length-1, 1)
                                            [void]$results.Append($tempArrayContent + ")`r`n")
                                        }
                                    }
                                }
                             }
                        }
                    }
                }
            }
        }
        [void]$results.AppendLine("$childSpacer}")
    }

    return $results.ToString()
}
