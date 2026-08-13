function Get-TargetResource
{
    [CmdletBinding()]
    [OutputType([System.Collections.Hashtable])]
    param
    (
        [Parameter(Mandatory = $true)]
        [System.String]
        $Path
    )

    return @{
        Path     = $Path
        Contents = ''
        Ensure   = 'Absent'
        Force    = $false
        Retries  = 0
        Tags     = @()
    }
}

function Set-TargetResource
{
    [CmdletBinding()]
    param
    (
        [Parameter(Mandatory = $true)]
        [System.String]
        $Path,

        [Parameter()]
        [System.String]
        $Contents,

        [Parameter()]
        [ValidateSet('Present', 'Absent')]
        [System.String]
        $Ensure,

        [Parameter()]
        [System.Boolean]
        $Force,

        [Parameter()]
        [System.UInt32]
        $Retries,

        [Parameter()]
        [System.String[]]
        $Tags
    )

    throw 'DscParserTestFile is a parsing fixture and cannot be applied.'
}

function Test-TargetResource
{
    [CmdletBinding()]
    [OutputType([System.Boolean])]
    param
    (
        [Parameter(Mandatory = $true)]
        [System.String]
        $Path,

        [Parameter()]
        [System.String]
        $Contents,

        [Parameter()]
        [ValidateSet('Present', 'Absent')]
        [System.String]
        $Ensure,

        [Parameter()]
        [System.Boolean]
        $Force,

        [Parameter()]
        [System.UInt32]
        $Retries,

        [Parameter()]
        [System.String[]]
        $Tags
    )

    return $true
}

Export-ModuleMember -Function *-TargetResource
