#requires -Version 7

<#
.SYNOPSIS
    Times the phases of a ConvertTo-DSCObject call.

.DESCRIPTION
    Splits a conversion into module import, resource discovery, the C# call and the hashtable
    projection, so a change can be attributed to a phase before reaching for a profiler. Repeats
    the conversion so the first call, which pays for keyword registration, can be told apart from
    the steady state.

.PARAMETER ConfigPath
    An exported DSC configuration to convert.

.PARAMETER ModulePath
    The DSCParser manifest to test. Defaults to the build output in this repository.

.PARAMETER Repeat
    How many conversions to time after the first.

.EXAMPLE
    PS> .\Measure-ConvertToDscObject.ps1 -ConfigPath D:\testbed\M365TenantConfig.ps1
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [System.String]
    $ConfigPath,

    [Parameter()]
    [System.String]
    $ModulePath = (Join-Path -Path $PSScriptRoot -ChildPath '../../../DSCParser/DSCParser.psd1' -Resolve),

    [Parameter()]
    [System.Int32]
    $Repeat = 3
)

$ErrorActionPreference = 'Stop'

function Measure-Phase
{
    param(
        [System.String] $Name,
        [System.Management.Automation.ScriptBlock] $Action
    )

    [System.GC]::Collect()
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $result = & $Action
    $stopwatch.Stop()
    Write-Host ('{0,-46} {1,10:N0} ms' -f $Name, $stopwatch.Elapsed.TotalMilliseconds)
    return $result
}

Write-Host "=== PS $($PSVersionTable.PSVersion) | $ConfigPath ===`n"

$null = Measure-Phase 'Import-Module DSCParser' { Import-Module $ModulePath -Force -PassThru }

$resources = Measure-Phase 'Get-DscResourceV2 (cold)' { Get-DscResourceV2 }
Write-Host ("  -> {0} resources discovered" -f $resources.Count)

$null = Measure-Phase 'Get-DscResourceV2 (second call)' { Get-DscResourceV2 }

$content = [System.IO.File]::ReadAllText($ConfigPath)

for ($i = 1; $i -le $Repeat; $i++)
{
    Write-Host "`n--- ConvertTo-DSCObject pass $i ---"
    $objects = Measure-Phase 'ConvertTo-DSCObject' {
        ConvertTo-DSCObject -Content $content -DscResourceInfo $resources -WarningAction SilentlyContinue
    }
    Write-Host ("  -> {0} resource instances" -f $objects.Count)
}

$options = [DSCParser.CSharp.DscParseOptions]::new()
$options.IncludeComments = $false
$options.IncludeCIMInstanceInfo = $true

$raw = Measure-Phase 'DscParser::ConvertToDscObject (raw C#)' {
    [DSCParser.CSharp.DscParser]::ConvertToDscObject($null, $content, $options, $resources)
}

$null = Measure-Phase 'ToHashtable projection (PowerShell loop)' {
    $output = [System.Collections.Generic.List[System.Collections.Hashtable]]::new($raw.Count)
    foreach ($item in $raw)
    {
        $output.Add($item.ToHashtable())
    }
    $output
}
