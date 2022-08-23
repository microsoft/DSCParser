﻿function ConvertTo-DSCObject
{
    [CmdletBinding()]
    param
    (
        [Parameter()]
        [System.String]
        $Path,

        [Parameter()]
        [System.String]
        $Content,

        [Parameter()]
        [System.Boolean]
        $IncludeComments = $false
    )

    #region Variables
    $ParsedResults = @()
    #endregion

    # Define components we wish to filter out
    $noisyTypes = @(
        "StatementSeparator", "CommandArgument", "CommandParameter"
        if (-not $IncludeComments){
            "Comment"
        }
    )

    $NoisyOperators = (",", "")

    # Tokenize the file's content to break it down into its various components;
    if (([System.String]::IsNullOrEmpty($Path) -and [System.String]::IsNullOrEmpty($Content)) -or `
        (![System.String]::IsNullOrEmpty($Path) -and ![System.String]::IsNullOrEmpty($Content)))
    {
        throw "You need to specify either Path or Content as parameters."
    }
    elseif (![System.String]::IsNullOrEmpty($Path))
    {
        $parsedData = [System.Management.Automation.PSParser]::Tokenize((Get-Content $Path), [ref]$null)
    }
    elseif (![System.String]::IsNullOrEmpty($Content))
    {
        $parsedData = [System.Management.Automation.PSParser]::Tokenize($Content, [ref]$null)
    }

    [array]$componentsArray = @()
    $currentValues = @()
    $nodeKeyWordEncountered = $false
    $ObjectsToClose = 0
    for ($i = 0; $i -lt $parsedData.Count; $i++)
    {
        if ($nodeKeyWordEncountered)
        {
            if ($parsedData[$i].Type -eq "GroupStart" -or $parsedData[$i].Content -eq '{')
            {
                $ObjectsToClose++
            }
            elseif (($parsedData[$i].Type -eq "GroupEnd" -or $parsedData[$i].Content -eq '}') -and $ObjectsToClose -gt 0)
            {
                $ObjectsToClose--
            }

            if ($parsedData[$i].Type -notin $noisyTypes -and $parsedData[$i].Content -notin $noisyParts)
            {
                $currentValues += $parsedData[$i]
                if($parsedData[$i].Type -eq "GroupEnd" -and $parsedData[$i].Content -eq '}' -and $ObjectsToClose -eq 0)
                {
                    $componentsArray += ,$currentValues
                    $currentValues = @()
                    $ObjectsToClose = 0
                }
            }
            elseif (($parsedData[$i].Type -eq "GroupEnd" -and $parsedData[$i-2].Content -ne 'parameter' -and $parsedData[$i].Content -ne '}') -or
                ($parsedData[$i].Type -eq "GroupStart" -and $parsedData[$i-1].Content -ne 'parameter' -and $parsedData[$i].Content -ne '{'))
            {
                $currentValues += $parsedData[$i]
            }
        }
        elseif ($parsedData[$i].Content -eq 'node')
        {
            $nodeKeyWordEncountered = $true
            $newIndexPosition = $i+1
            while ($parsedData[$newIndexPosition].Type -ne 'Keyword' -and $i -lt $parsedData.Count)
            {
                $i++
                $newIndexPosition = $i+1
            }
        }
    }

    $ParsedResults = $null
    if ($componentsArray.Count -gt 0)
    {
        $ParsedResults = Get-HashtableFromGroup -Groups $componentsArray -Path $Path -IncludeComments:$IncludeComments -NoisyOperators $NoisyOperators
    }
    return $ParsedResults
}

function Get-HashtableFromGroup
{
    [CmdletBinding()]
    [OutputType([System.Collections.IDictionary])]
    param(
        [Parameter(Mandatory = $true)]
        [System.Array]
        $Groups,

        [Parameter(Mandatory = $true)]
        [System.Array]
        $Path,

        [Parameter()]
        [System.Boolean]
        $IsSubGroup = $false,

        [Array] $NoisyOperators,

        [switch] $IncludeComments
    )

    # Loop through all the Resources identified within our configuration
    $currentIndex = 1
    $result = @()
    $ParsedResults = @()
    foreach ($group in $Groups)
    {
        $keywordFound = $false
        if (-not $IsSubGroup -and $currentIndex -le $Groups.Count-2)
        {
            Write-Progress -PercentComplete ($currentIndex / ($Groups.Count-2) * 100) -Activity "Parsing $Path [$($currentIndex)/$($Groups.Count-2)]"
        }
        $currentPropertyIndex = 0
        $currentProperty = ''
        while ($currentPropertyIndex -lt $group.Count)
        {
            $component = $group[$currentPropertyIndex]
            if ($component.Type -eq "Keyword" -and -not $keywordFound)
            {
                $result = [ordered] @{ ResourceName = $component.Content }
                $keywordFound = $true
            }
            elseif ($keywordFound)
            {
                # If the next component is a keyword and we've already identified a keyword for this group, that means that we are
                # looking at a CIMInstance property;
                if ($component.Type -eq "Keyword")
                {
                    $currentGroupEndFound = $false
                    $currentPosition = $currentPropertyIndex + 1
                    $subGroup = @($component)
                    $ObjectsToClose = 0
                    $allSubGroups = @()
                    [Array]$subResult = @()
                    while (!$currentGroupEndFound)
                    {
                        $currentSubComponent = $group[$currentPosition]
                        if ($currentSubComponent.Type -eq 'GroupStart' -or $currentSubComponent.Content -eq '{')
                        {
                            $ObjectsToClose++
                        }
                        elseif ($currentSubComponent.Type -eq 'GroupEnd' -or $currentSubComponent.Content -eq '}')
                        {
                            $ObjectsToClose--
                        }

                        $subGroup += $group[$currentPosition]
                        if ($ObjectsToClose -eq 0 -and $group[$currentPosition+1].Type -ne 'Keyword' -and `
                            $group[$currentPosition].Type -ne 'Keyword')
                        {
                            $currentGroupEndFound = $true
                        }

                        if ($ObjectsToClose -eq 0 -and $group[$currentPosition].Type -ne 'Keyword')
                        {
                            $allSubGroups += ,$subGroup
                            $subGroup = @()
                        }
                        $currentPosition++
                    }
                    $currentPropertyIndex = $currentPosition
                    $subResult = Get-HashtableFromGroup -Groups $allSubGroups -IsSubGroup $true -Path $Path -IncludeComments:$IncludeComments.IsPresent -NoisyOperators $NoisyOperators
                    $allSubGroups = @()
                    $subGroup = @()
                    $result.$currentProperty += $subResult
                }
                # If the next component is not an operator, that means that the current member is part of the previous property's
                # value;
                elseif ($group[$currentPropertyIndex + 1].Type -ne "Operator" -and $component.Content -ne "=" -and `
                        $component.Content -ne '{' -and $component.Content -ne '}' -and $group[$currentPropertyIndex + 1].Type -ne 'Keyword')
                {
                    switch ($component.Type)
                    {
                        { $_ -in @("String") } {
                            $result.$currentProperty += $component.Content
                            break
                        }
                        { $_ -in @("Number") } {
                            $CultureEnUS=[System.Globalization.CultureInfo]::new('en-us')
                            try {
                                # AST already determined that this is a number. For added safety, we're doing this in a try/catch block
                                $ContentAsDecimal=[System.Decimal]$Component.Content
                            }
                            catch {
                                $ContentAsDecimal=$null
                            }
                            if ($null -ne $ContentAsDecimal -and $ContentAsDecimal.ToString('',$CultureEnUS) -eq $Component.Content) {
                                # The content converted from decimal back to string matches the original string content, so we can safely return the decimal object
                                $result.$currentProperty += $ContentAsDecimal
                            } Else {
                                # For some reason, the content converted from decimal back to string does not match the original content. Return the original (string) content
                                $result.$currentProperty += $component.Content
                            }
                            break
                        }
                        {$_ -in @("Variable")} {
                            # Based on the logic if if it's TRUE or FALSE we keep it as a boolean
                            # if it's other type of variable we keep it as a string with added $ character
                            if ($component.Content.ToLower() -eq 'true')
                            {
                                $result.$currentProperty += $true
                            }
                            elseif ($component.Content.ToLower() -eq 'false')
                            {
                                $result.$currentProperty += $false
                            }
                            elseif ($component.Content.ToLower() -eq 'null')
                            {
                                $result.$currentProperty += $null
                            }
                            else {
                                $result.$currentProperty += "`$" + $component.Content
                            }
                            break
                        }
                        {$_ -in @("Member")} {
                            $result.$currentProperty += "." + $component.Content
                            break
                        }
                        {$_ -in @("Command")} {
                            $result.ResourceID += $component.Content
                            break
                        }
                        {$_ -in @("Comment")} {
                            if ($IncludeComments) {
                                $result.$("_metadata_" + $currentProperty) += $component.Content
                            }
                            break
                        }
                        {$_ -in @("GroupStart")} {

                            # Property is an Array
                            $result.$currentProperty = @()

                            do {
                                # we will need to wait till we find end of array rather than terminating the loop early on
                                $currentPropertyIndex++
                                while ($group[$currentPropertyIndex].Type -eq 'NewLine')
                                {
                                    $currentPropertyIndex++
                                }

                                switch ($group[$currentPropertyIndex].Type)
                                {
                                    # Property is an array of string or integer
                                    {$_ -in @("String", "Number", "Variable")} {
                                        do
                                        {
                                            $ValueToSet = $group[$currentPropertyIndex].Content
                                            if ($group[$currentPropertyIndex].Content -notin $noisyOperators)
                                            {
                                                $Type = $group[$currentPropertyIndex].Type
                                                if ($Type -eq "Variable") {
                                                    # Based on the logic if if it's TRUE or FALSE we keep it as a boolean
                                                    # if it's other type of variable we keep it as a string with added $ character

                                                    if ($ValueToSet.ToLower() -eq 'true')
                                                    {
                                                        $ValueToSet = $true
                                                    }
                                                    elseif ($ValueToSet.ToLower() -eq 'false')
                                                    {
                                                        $ValueToSet = $false
                                                    }
                                                    elseif ($ValueToSet.ToLower() -eq 'null')
                                                    {
                                                        $ValueToSet = $null
                                                    }
                                                    else {
                                                        $ValueToSet = "`$" + $ValueToSet
                                                        # Supports variable name escape syntax within arrays
                                                        Do {
                                                            $currentPropertyIndex++
                                                            $ValueToSet += $group[$CurrentPropertyIndex].Content
                                                        } until (($group[$CurrentPropertyIndex + 1].Type -eq 'Operator' -and $group[$CurrentPropertyIndex + 1].Content -eq ',') -or $group[$currentPropertyIndex + 1].Type -eq 'GroupEnd')
                                                    }
                                                }

                                                $result.$currentProperty += $ValueToSet
                                            }
                                            $currentPropertyIndex++
                                        }
                                        while ($group[$currentPropertyIndex].Type -ne 'GroupEnd')
                                        break
                                    }

                                    # Property is an array of CIMInstance
                                    "Keyword"{
                                        $CimInstanceComponents = @()
                                        $GroupsToClose = 0
                                        $FoundOneGroup = $false
                                        do
                                        {
                                            if ($group[$currentPropertyIndex].Type -eq 'GroupStart')
                                            {
                                                $FoundOneGroup = $true
                                                $GroupsToClose ++
                                            }
                                            elseif ($group[$currentPropertyIndex].Type -eq 'GroupEnd')
                                            {
                                                $GroupsToClose --
                                            }
                                            if ($group[$currentPropertyIndex].Content -notin $noisyOperators)
                                            {
                                                $CimInstanceComponents += $group[$currentPropertyIndex]
                                            }
                                            $currentPropertyIndex++
                                        }
                                        while ($group[$currentPropertyIndex-1].Type -ne 'GroupEnd' -or $GroupsToClose -ne 0 -or -not $FoundOneGroup)
                                        $CimInstanceObject = Convert-CIMInstanceToPSObject -CIMInstance $CimInstanceComponents -NoisyOperators $NoisyOperators
                                        $result.$CurrentProperty += $CimInstanceObject
                                        break
                                    }
                                }

                            } while ($group[$currentPropertyIndex].Type -ne 'GroupEnd' -and $token.Content -ne ')')
                            break
                        }
                    }
                }
                elseif ($component.Content -notin $noisyOperators)
                {
                    switch ($component.Type)
                    {
                        "Member" {
                            $currentProperty = $component.Content.ToString()

                            if(!$result.Contains($currentProperty))
                            {
                                $result.Add($currentProperty, $null)
                            }
                        }
                        "Variable" {
                            # This is added to handle advanced variables such as $Test.Nested.Variable on 1st level
                            $result.$currentProperty += "`$" + $component.Content
                            Do {
                                $currentPropertyIndex++
                                $result.$currentProperty += $group[$CurrentPropertyIndex].Content
                            } until ($group[$CurrentPropertyIndex + 1].Type -eq 'NewLine')
                        }
                    }
                }
            }
            $currentPropertyIndex++
        }

        if($keywordFound)
        {
            $ParsedResults += $result
        }
        $currentIndex++
    }
    return $ParsedResults
}

function Convert-CIMInstanceToPSObject
{
    [CmdletBinding()]
    [OutputType([System.Collections.IDictionary])]
    Param(
        [Parameter(Mandatory = $true)]
        [System.Object[]]
        $CIMInstance,

        [Array] $NoisyOperators
    )

    $result = [ordered] @{}
    $index = 0
    $CurrentMemberName = $null
    while($index -lt $CimInstance.Count)
    {
        $token = $CIMInstance[$index]
        switch ($token.Type)
        {
            # The main token for the CIMInstance
            "Keyword" {
                $result.Add("CIMInstance", $token.Content)
                break
            }
            "Member" {
                $result.Add($token.Content, "")
                $CurrentMemberName = $token.Content
                break
            }
            { $_ -in "String", "Number"} {
                $result.$CurrentMemberName = $token.Content
                break
            }
            {$_ -in @("Variable")} {
                # Based on the logic if if it's TRUE or FALSE we keep it as a boolean
                # if it's other type of variable we keep it as a string with added $ character
                if ($token.Content.ToLower() -eq 'true')
                {
                    $result.$CurrentMemberName = $true
                }
                elseif ($token.Content.ToLower() -eq 'false')
                {
                    $result.$CurrentMemberName = $false
                }
                elseif ($token.Content.ToLower() -eq 'null')
                {
                    $result.$CurrentMemberName = $null
                }
                else {
                    $result.$CurrentMemberName = "`$" + $token.Content
                    # Supports variable name escape syntax within ciminstances
                    Do {
                        $index++
                        $result.$CurrentMemberName += $CIMInstance[$index].Content
                    } until ($CIMInstance[$index + 1].Type -eq 'NewLine')
                }
                break
            }
            {$_ -eq "GroupStart" -and $token.Content -eq '@('} {
                $result.$CurrentMemberName = @()
                $index++
                while ($CimInstance[$index].Type -eq 'NewLine')
                {
                    $index++
                }
                switch ($CIMInstance[$index].Type)
                {
                    { $_ -in "String", "Number", "Variable"} {
                        $arrayContent = @()
                        do {
                            $content = $CIMInstance[$index].Content

                            if ($_ -in @("Variable")) {
                                # Based on the logic if if it's TRUE or FALSE we keep it as a boolean
                                # if it's other type of variable we keep it as a string with added $ character
                                if ($content.ToLower() -eq 'true')
                                {
                                    $content = $true
                                }
                                elseif ($content.ToLower() -eq 'false')
                                {
                                    $content = $false
                                }
                                elseif ($content.ToLower() -eq 'null')
                                {
                                    $content = $null
                                }
                                else {
                                    $content = "`$" + $content
                                }
                            } elseif ($_ -in @("String", "Number")) {
                                $content = $content
                            }
                            $arrayContent += $content
                            $index++
                        } while ($CIMInstance[$index].Type -ne 'GroupEnd')
                        $result.$CurrentMemberName = $arrayContent
                    }

                    # The Content of the Array is yet again CIMInstances. Recursively call into the current method;
                    "Keyword" {
                        while ($CIMInstance[$index].Content -ne ')' -and $CImInstance[$index].Type -ne 'GroupEnd')
                        {
                            $CIMInstanceComponents = @()
                            $GroupsToClose = 0
                            $FoundOneGroup = $false
                            do
                            {
                                if ($CIMInstance[$index].Type -eq 'GroupStart')
                                {
                                    $FoundOneGroup = $true
                                    $GroupsToClose ++
                                }
                                elseif ($CIMInstance[$index].Type -eq 'GroupEnd')
                                {
                                    $GroupsToClose --
                                }
                                if ($CIMInstance[$index].Content -notin $noisyOperators)
                                {
                                    $CimInstanceComponents += $CIMInstance[$index]
                                }
                                $index++
                            }
                            while ($CIMInstance[$index-1].Type -ne 'GroupEnd' -or $GroupsToClose -ne 0 -or -not $FoundOneGroup)
                            $CimInstanceObject = Convert-CIMInstanceToPSObject -CIMInstance $CimInstanceComponents -NoisyOperators $NoisyOperators
                            $result.$CurrentMemberName += $CimInstanceObject
                            $index++
                        }
                    }
                }
            }
        }
        $index++
    }
    return $result
}
