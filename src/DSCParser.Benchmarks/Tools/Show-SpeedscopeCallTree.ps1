#requires -Version 7

<#
.SYNOPSIS
    Prints an aggregated call tree from a dotnet-trace speedscope profile.

.DESCRIPTION
    dotnet-trace emits speedscope profiles in the evented format: one open and close event per
    frame, per thread. This walks those events and aggregates inclusive and self time per frame,
    rooted at the first frame matching -RootPattern.

    Prefer this over 'dotnet-trace report topN'. That report sums every thread, so a profile of a
    PowerShell host is dominated by idle worker threads waiting on handles and the pipeline thread
    doing the actual work is buried.

.PARAMETER Path
    The .speedscope.json file produced by 'dotnet-trace collect --format Speedscope'.

.PARAMETER RootPattern
    Regular expression matching the frame to root the tree at.

.PARAMETER MinMs
    Branches below this inclusive time are pruned.

.PARAMETER ThreadFilter
    Substring of the thread name to analyse. Defaults to the thread with the most events.

.EXAMPLE
    PS> .\Show-SpeedscopeCallTree.ps1 -Path .\parse.speedscope.json -MinMs 100
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [System.String]
    $Path,

    [Parameter()]
    [System.String]
    $RootPattern = 'DscParser\.ConvertToDscObject',

    [Parameter()]
    [System.Double]
    $MinMs = 60,

    [Parameter()]
    [System.Int32]
    $MaxDepth = 14,

    [Parameter()]
    [System.String]
    $ThreadFilter
)

$ErrorActionPreference = 'Stop'

$json = [System.IO.File]::ReadAllText($Path) | ConvertFrom-Json -Depth 64
$frames = $json.shared.frames

$target = if ($ThreadFilter)
{
    $json.profiles | Where-Object name -Like "*$ThreadFilter*" | Select-Object -First 1
}
else
{
    $json.profiles | Sort-Object { $_.events.Count } -Descending | Select-Object -First 1
}

if ($null -eq $target)
{
    throw "No matching thread found in '$Path'."
}

function Get-ShortName
{
    param([System.String] $FullName)

    $name = $FullName -replace '^[^!]+!', ''
    $parenthesis = $name.IndexOf('(')
    if ($parenthesis -gt 0)
    {
        $name = $name.Substring(0, $parenthesis)
    }
    return $name
}

function New-Node
{
    param([System.String] $Name)

    return [pscustomobject]@{
        Name     = $Name
        Incl     = [System.Double] 0
        Self     = [System.Double] 0
        Children = @{}
    }
}

$tree = New-Node 'ROOT'
$stack = [System.Collections.Generic.List[System.Object]]::new()
$nodes = [System.Collections.Generic.List[System.Object]]::new()
$lastAt = $target.startValue
$rootDepth = -1

foreach ($event in $target.events)
{
    $delta = $event.at - $lastAt
    $lastAt = $event.at

    if ($nodes.Count -gt 0 -and $delta -gt 0)
    {
        $nodes[$nodes.Count - 1].Self += $delta
        foreach ($node in $nodes)
        {
            $node.Incl += $delta
        }
    }

    if ($event.type -eq 'O')
    {
        $name = Get-ShortName $frames[$event.frame].name
        $stack.Add($name)

        if ($rootDepth -lt 0)
        {
            if ($name -match $RootPattern)
            {
                $rootDepth = $stack.Count - 1
                $nodes.Add($tree)
            }
        }
        elseif ($nodes.Count -gt 0)
        {
            $parent = $nodes[$nodes.Count - 1]
            $child = $parent.Children[$name]
            if ($null -eq $child)
            {
                $child = New-Node $name
                $parent.Children[$name] = $child
            }
            $nodes.Add($child)
        }
    }
    else
    {
        if ($stack.Count -eq 0)
        {
            continue
        }

        $depth = $stack.Count - 1
        $stack.RemoveAt($depth)

        if ($rootDepth -ge 0 -and $depth -ge $rootDepth -and $nodes.Count -gt 0)
        {
            $nodes.RemoveAt($nodes.Count - 1)
        }
        if ($depth -eq $rootDepth)
        {
            $rootDepth = -1
        }
    }
}

function Show-Node
{
    param(
        [System.Object] $Node,
        [System.Int32] $Depth
    )

    if ($Depth -gt $MaxDepth)
    {
        return
    }

    $selfTag = if ($Node.Self -ge 1) { '  [self {0:N0}]' -f $Node.Self } else { '' }
    Write-Host ('{0,8:N0} ms  {1}{2}{3}' -f $Node.Incl, ('  ' * $Depth), $Node.Name, $selfTag)

    foreach ($child in ($Node.Children.Values | Sort-Object Incl -Descending))
    {
        if ($child.Incl -lt $MinMs)
        {
            continue
        }
        Show-Node -Node $child -Depth ($Depth + 1)
    }
}

Write-Host "`n=== Call tree under /$RootPattern/ on '$($target.name)' (>= $MinMs ms) ===`n"
Show-Node -Node $tree -Depth 0
