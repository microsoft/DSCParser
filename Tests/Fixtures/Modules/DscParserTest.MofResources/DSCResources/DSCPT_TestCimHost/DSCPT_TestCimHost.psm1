function Get-TargetResource
{
    [CmdletBinding()]
    [OutputType([System.Collections.Hashtable])]
    param
    (
        [Parameter(Mandatory = $true)]
        [System.String]
        $Name
    )

    return @{
        Name           = $Name
        Description    = ''
        DefaultSetting = $null
        Settings       = @()
    }
}

function Set-TargetResource
{
    [CmdletBinding()]
    param
    (
        [Parameter(Mandatory = $true)]
        [System.String]
        $Name,

        [Parameter()]
        [System.String]
        $Description,

        [Parameter()]
        [Microsoft.Management.Infrastructure.CimInstance]
        $DefaultSetting,

        [Parameter()]
        [Microsoft.Management.Infrastructure.CimInstance[]]
        $Settings
    )

    throw 'DscParserTestCimHost is a parsing fixture and cannot be applied.'
}

function Test-TargetResource
{
    [CmdletBinding()]
    [OutputType([System.Boolean])]
    param
    (
        [Parameter(Mandatory = $true)]
        [System.String]
        $Name,

        [Parameter()]
        [System.String]
        $Description,

        [Parameter()]
        [Microsoft.Management.Infrastructure.CimInstance]
        $DefaultSetting,

        [Parameter()]
        [Microsoft.Management.Infrastructure.CimInstance[]]
        $Settings
    )

    return $true
}

Export-ModuleMember -Function *-TargetResource
