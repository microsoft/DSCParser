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

    $componentsArray = @()
    $currentValues = @()
    for($i = 0; $i -lt $parsedData.Count; $i++)
    {
    
        if($parsedData[$i].Type -notin $noisyTypes -and $parsedData[$i].Content -notin $noisyParts)
        {
            $currentValues += $parsedData[$i]
        }
        elseif($parsedData[$i].Type -eq "GroupEnd")
        {
            $componentsArray+= ,$currentValues
            $currentValues = @()
        }
    }
    
    # Loop through all the Resources identified within our configuration
    $currentIndex = 1
    foreach($group in $componentsArray)
    {
        # Display some progress to the user letting him know how many resources there are to be parsed in total;
        Write-Progress -PercentComplete ($currentIndex / $componentsArray.Count * 100) -Activity "Parsing $($resource.Content) [$($currentIndex)/$($componentsArray.Count)]"
        
        $keywordFound = $false
        $currentPropertyIndex = 0
        foreach($component in $group)
        {
            if($component.Type -eq "Keyword")
            {
                $result = @{ ResourceName = $component.Content }
                $keywordFound = $true
            }
            elseif($keywordFound)
            {
                # If the next component is not an operator, that means that the current member is part of the previous property's
                # value;
                if($group[$currentPropertyIndex + 1].Type -ne "Operator" -and $component.Content -ne "=" -or `
                  ($group[$currentPropertyIndex + 1].Type -eq "Operator" -and $group[$currentPropertyIndex + 1].Content -eq "."))
                {
                    switch($component.Type)
                    {                    
                        {$_ -in @("String","Number")} {
                            $result.$currentProperty += $component.Content
                        }
                        {$_ -in @("Variable")} {
                            $result.$currentProperty += "`$" + $component.Content
                        }
                    }
                }
                elseif($component.Content -notin $noisyOperators)
                {
                    switch($component.Type)
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