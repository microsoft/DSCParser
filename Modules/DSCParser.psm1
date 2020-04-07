function ConvertTo-DSCObject
{
    [CmdletBinding()]
    param
    (
        [Parameter(Mandatory = $true,Position = 1)]
        [string]
        $Path
    )

    #region Variables
    $ParsedResults = @()
    #endregion

    # Define components we wish to filter out
    $noisyTypes = @("NewLine", "StatementSeparator", "GroupStart", "Command", "CommandArgument", "CommandParameter", "GroupEnd", "Comment")
    $noisyParts = @("in", "if", "node", "localconfigurationmanager", "for", "foreach", "when", "configuration", "Where", "_")
    $noisyOperators = (".",",", "")
    
    # Tokenize the file's content to break it down into its various components;
    $parsedData = [System.Management.Automation.PSParser]::Tokenize((Get-Content $Path), [ref]$null)

    [array]$componentsArray = @()
    $currentValues = @()
    $nodeKeyWordEncountered = $false
    for ($i = 0; $i -lt $parsedData.Count; $i++)
    {    
        if ($nodeKeyWordEncountered)
        {
            if ($parsedData[$i].Type -notin $noisyTypes -and $parsedData[$i].Content -notin $noisyParts)
            {
                $currentValues += $parsedData[$i]
            }
            elseif (($parsedData[$i].Type -eq "GroupEnd" -and $parsedData[$i-2].Content -ne 'parameter' -and $parsedData[$i].Content -ne '}') -or 
                ($parsedData[$i].Type -eq "GroupStart" -and $parsedData[$i-1].Content -ne 'parameter' -and $parsedData[$i].Content -ne '{'))
            {
                $currentValues += $parsedData[$i]
            }
            elseif($parsedData[$i].Type -eq "GroupEnd" -and $parsedData[$i].Content -eq '}')
            {
                $componentsArray += ,$currentValues
                $currentValues = @()
            }
        }
        elseif ($parsedData[$i].Content -eq 'node')
        {
            $nodeKeyWordEncountered = $true
        }
    }
    
    # Loop through all the Resources identified within our configuration
    $currentIndex = 1
    foreach ($group in ($componentsArray | Sort-Object Start))
    {
        # Display some progress to the user letting him know how many resources there are to be parsed in total;
        if ($currentIndex -gt $componentsArray.Count-2)
        {
            $currentIndex = $componentsArray.Count-2
        }
        Write-Progress -PercentComplete ($currentIndex / ($componentsArray.Count-2) * 100) -Activity "Parsing $($resource.Content) [$($currentIndex)/$($componentsArray.Count-2)]"
        
        $keywordFound = $false
        $currentPropertyIndex = 0
        while ($currentPropertyIndex -lt $group.Count)
        {
            $component = $group[$currentPropertyIndex]
            if ($component.Type -eq "Keyword")
            {
                $result = @{ ResourceName = $component.Content }
                $keywordFound = $true
            }
            elseif ($keywordFound)
            {
                # If the next component is not an operator, that means that the current member is part of the previous property's
                # value;
                if ($group[$currentPropertyIndex + 1].Type -ne "Operator" -and $component.Content -ne "=" -or `
                   ($group[$currentPropertyIndex + 1].Type -eq "Operator" -and $group[$currentPropertyIndex + 1].Content -eq "."))
                {
                    switch ($component.Type)
                    {                    
                        {$_ -in @("String","Number")} {
                            $result.$currentProperty += $component.Content
                        }
                        {$_ -in @("Variable")} {
                            $result.$currentProperty += "`$" + $component.Content
                        }
                        {$_ -in @("Member")} {
                            $result.$currentProperty += "." + $component.Content
                        }
                        {$_ -in @("GroupStart")} {
                            $result.$currentProperty += "@("

                            do
                            {
                                if ($group[$currentPropertyIndex].Content -notin @("@(", ")"))
                                {
                                    $result.$currentProperty += "`"" + $group[$currentPropertyIndex].Content + "`",`""
                                }
                                $currentPropertyIndex++
                            }
                            while ($group[$currentPropertyIndex].Type -ne 'GroupEnd')

                            if (($result.$currentProperty).EndsWith(","))
                            {
                                $result.$currentProperty = ($result.$CurrentProperty).Substring(0, ($result.$CurrentProperty).Length -1) + ")"
                            }
                            else
                            {
                                $result.$currentProperty += ')'
                            }
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