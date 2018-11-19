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
    $noisyTypes = @("NewLine", "StatementSeparator", "GroupStart", "Command", "CommandArgument", "CommandParameter", "Operator", "GroupEnd", "Comment")
    $noisyParts = @("in", "if", "node", "localconfigurationmanager", "for", "foreach", "when", "configuration", "Where", "_")
    $noisyOperators = (".", ",", "")
    
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
        foreach($component in $group)
        {
            if($component.Type -eq "Keyword")
            {
                $result = @{ ResourceName = $component.Content }
                $keywordFound = $true
            }
            elseif($component.Content -notin $noisyOperators -and $keywordFound)
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
                    {$_ -in @("Variable","String","Number")} {$result.$currentProperty = $component.Content}
                }
            }
        }

        if($keywordFound)
        {
            $ParsedResults += $result
        }
        $currentIndex++
    }
    return $ParsedResults
}
