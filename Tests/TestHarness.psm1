function Get-RepositoryRoot
{
    [CmdletBinding()]
    [OutputType([System.String])]
    param()

    return (Join-Path -Path $PSScriptRoot -ChildPath '..' -Resolve)
}

function Get-CoberturaSummary
{
    [CmdletBinding()]
    [OutputType([System.Collections.Hashtable])]
    param
    (
        [Parameter(Mandatory = $true)]
        [System.String]
        $Path
    )

    if (-not (Test-Path -Path $Path))
    {
        return @{
            CoveragePercent = 0
            LinesCovered    = 0
            LinesValid      = 0
            Classes         = @()
        }
    }

    $document = [System.Xml.XmlDocument]::new()
    $document.Load($Path)

    $byType = [ordered]@{}
    $totalLines = 0
    $coveredLines = 0

    foreach ($class in $document.SelectNodes('//class'))
    {
        $typeName = $class.name -replace '[.+]<>.*$', '' -replace '[.+]<[^>]+>d__\d+$', ''

        if (-not $byType.Contains($typeName))
        {
            $byType[$typeName] = @{ Covered = 0; Total = 0 }
        }

        foreach ($line in $class.SelectNodes('lines/line'))
        {
            $byType[$typeName].Total++
            $totalLines++

            if ([int]$line.hits -gt 0)
            {
                $byType[$typeName].Covered++
                $coveredLines++
            }
        }
    }

    $classes = foreach ($typeName in $byType.Keys)
    {
        $entry = $byType[$typeName]
        [PSCustomObject]@{
            Name            = $typeName
            LinesCovered    = $entry.Covered
            LinesValid      = $entry.Total
            CoveragePercent = if ($entry.Total -gt 0) { [System.Math]::Round($entry.Covered / $entry.Total * 100, 2) } else { 0 }
        }
    }

    return @{
        CoveragePercent = if ($totalLines -gt 0) { [System.Math]::Round($coveredLines / $totalLines * 100, 2) } else { 0 }
        LinesCovered    = $coveredLines
        LinesValid      = $totalLines
        Classes         = @($classes | Sort-Object -Property CoveragePercent)
    }
}

function Get-TrxSummary
{
    [CmdletBinding()]
    [OutputType([System.Collections.Hashtable])]
    param
    (
        [Parameter(Mandatory = $true)]
        [System.String]
        $Path
    )

    $summary = @{
        Total   = 0
        Passed  = 0
        Failed  = 0
        Skipped = 0
    }

    if (-not (Test-Path -Path $Path))
    {
        return $summary
    }

    $document = [System.Xml.XmlDocument]::new()
    $document.Load($Path)

    $namespaceManager = [System.Xml.XmlNamespaceManager]::new($document.NameTable)
    $namespaceManager.AddNamespace('trx', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')

    $counters = $document.SelectSingleNode('//trx:ResultSummary/trx:Counters', $namespaceManager)
    if ($null -eq $counters)
    {
        return $summary
    }

    $summary.Total = [int]$counters.total
    $summary.Passed = [int]$counters.passed
    $summary.Failed = [int]$counters.failed
    $summary.Skipped = [int]$counters.total - [int]$counters.executed

    return $summary
}

function Invoke-DotNetTest
{
    [CmdletBinding()]
    [OutputType([System.Collections.Hashtable])]
    param
    (
        [Parameter(Mandatory = $true)]
        [System.String]
        $RepositoryRoot,

        [Parameter(Mandatory = $true)]
        [System.String]
        $Configuration,

        [Parameter()]
        [Switch]
        $IgnoreCodeCoverage
    )

    $projectPath = Join-Path -Path $RepositoryRoot -ChildPath 'src\DSCParser.Tests\DSCParser.Tests.csproj'
    $resultsDirectory = Join-Path -Path $RepositoryRoot -ChildPath 'TestResults'
    $trxFileName = 'DSCParser.Tests.trx'
    $coverageFileName = 'coverage.cobertura.xml'

    if (-not (Test-Path -Path $resultsDirectory))
    {
        New-Item -Path $resultsDirectory -ItemType Directory -Force | Out-Null
    }

    Remove-Item -Path (Join-Path -Path $resultsDirectory -ChildPath $trxFileName) -Force -ErrorAction SilentlyContinue
    Remove-Item -Path (Join-Path -Path $resultsDirectory -ChildPath $coverageFileName) -Force -ErrorAction SilentlyContinue

    $arguments = @(
        'test'
        '--project', $projectPath
        '-c', $Configuration
        '--no-build'
        '--results-directory', $resultsDirectory
        '--report-trx'
        '--report-trx-filename', $trxFileName
    )

    if (-not $IgnoreCodeCoverage.IsPresent)
    {
        $arguments += @(
            '--coverage'
            '--coverage-output-format', 'cobertura'
            '--coverage-output', $coverageFileName
        )
    }

    Write-Host -Object 'Running all DSCParser C# Unit Tests'
    & dotnet @arguments | Out-Host

    $result = Get-TrxSummary -Path (Join-Path -Path $resultsDirectory -ChildPath $trxFileName)
    $result.Coverage = if ($IgnoreCodeCoverage.IsPresent)
    {
        $null
    }
    else
    {
        Get-CoberturaSummary -Path (Join-Path -Path $resultsDirectory -ChildPath $coverageFileName)
    }

    return $result
}

function Invoke-PesterTest
{
    [CmdletBinding()]
    param
    (
        [Parameter(Mandatory = $true)]
        [System.String]
        $RepositoryRoot,

        [Parameter()]
        [System.String]
        $TestResultsFile,

        [Parameter()]
        [System.String]
        $DscTestsPath,

        [Parameter()]
        [Switch]
        $IgnoreCodeCoverage
    )

    $modulePath = Join-Path -Path $RepositoryRoot -ChildPath 'DSCParser\DSCParser.psd1'
    if (-not (Test-Path -Path $modulePath))
    {
        throw "The DSCParser module has not been staged at '$modulePath'. Run Invoke-TestHarness without -SkipBuild."
    }

    $fixtureModulePath = Join-Path -Path $RepositoryRoot -ChildPath 'Tests\Fixtures\Modules'
    $originalModulePath = $env:PSModulePath
    $originalLocation = Get-Location

    try
    {
        $env:PSModulePath = $fixtureModulePath + [System.IO.Path]::PathSeparator + $env:PSModulePath
        Set-Location -Path $RepositoryRoot

        Import-Module -Name $modulePath -Force

        $filesToExecute = if ([System.String]::IsNullOrEmpty($DscTestsPath))
        {
            @(Get-ChildItem -Path (Join-Path -Path $RepositoryRoot -ChildPath 'Tests\Unit') -Recurse -Filter '*.Tests.ps1').FullName
        }
        else
        {
            @($DscTestsPath)
        }

        $container = New-PesterContainer -Path $filesToExecute

        $configuration = [PesterConfiguration]@{
            Run    = @{
                Container = $container
                PassThru  = $true
            }
            Output = @{
                Verbosity = 'Normal'
            }
            Should = @{
                ErrorAction = 'Continue'
            }
        }

        if (-not [System.String]::IsNullOrEmpty($TestResultsFile))
        {
            $configuration.TestResult.Enabled = $true
            $configuration.TestResult.OutputFormat = 'NUnitXml'
            $configuration.TestResult.OutputPath = $TestResultsFile
        }

        if (-not $IgnoreCodeCoverage.IsPresent)
        {
            $configuration.CodeCoverage.Enabled = $true
            $configuration.CodeCoverage.Path = (Join-Path -Path $RepositoryRoot -ChildPath 'DSCParser\DSCParser.psm1')
            $configuration.CodeCoverage.OutputPath = (Join-Path -Path $RepositoryRoot -ChildPath 'CodeCov.xml')
            $configuration.CodeCoverage.OutputFormat = 'JaCoCo'
            $configuration.CodeCoverage.UseBreakpoints = $false
        }

        Write-Host -Object 'Running all DSCParser PowerShell Unit Tests'
        return Invoke-Pester -Configuration $configuration
    }
    finally
    {
        Set-Location -Path $originalLocation
        $env:PSModulePath = $originalModulePath
        Remove-Module -Name DSCParser -Force -ErrorAction SilentlyContinue
    }
}

<#
.SYNOPSIS
    Runs the DSCParser C# and PowerShell test suites and collects their code coverage.

.DESCRIPTION
    Builds the solution, stages the PowerShell module, runs the xUnit suite with Cobertura
    coverage and the Pester suite with JaCoCo coverage, and returns both results as a single
    object. Test failures are reported rather than thrown so that the caller decides how to
    fail the build.

.PARAMETER TestResultsFile
    Path of an NUnit XML file to write the Pester results to. No file is written when omitted.

.PARAMETER DscTestsPath
    Path of a single Pester test file to run instead of the whole Tests\Unit tree.

.PARAMETER IgnoreCodeCoverage
    Skips coverage collection for both suites.

.PARAMETER SkipBuild
    Uses the existing build output instead of rebuilding the solution and restaging the module.

.PARAMETER SkipPesterTests
    Runs only the C# suite.

.PARAMETER SkipDotNetTests
    Runs only the PowerShell suite.

.PARAMETER Configuration
    Build configuration to compile and test against. Defaults to Release.

.EXAMPLE
    $results = Invoke-TestHarness
    Write-TestHarnessSummary -Result $results

.EXAMPLE
    Invoke-TestHarness -DscTestsPath .\Tests\Unit\DSCParser\RoundTrip.Tests.ps1 -SkipDotNetTests -SkipBuild
#>
function Invoke-TestHarness
{
    [CmdletBinding()]
    [OutputType([PSCustomObject])]
    param
    (
        [Parameter()]
        [System.String]
        $TestResultsFile,

        [Parameter()]
        [System.String]
        $DscTestsPath,

        [Parameter()]
        [Switch]
        $IgnoreCodeCoverage,

        [Parameter()]
        [Switch]
        $SkipBuild,

        [Parameter()]
        [Switch]
        $SkipPesterTests,

        [Parameter()]
        [Switch]
        $SkipDotNetTests,

        [Parameter()]
        [ValidateSet('Debug', 'Release')]
        [System.String]
        $Configuration = 'Release'
    )

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $repositoryRoot = Get-RepositoryRoot

    if (-not $SkipBuild.IsPresent)
    {
        Write-Host -Object "Building the DSCParser solution ($Configuration)"
        & dotnet build (Join-Path -Path $repositoryRoot -ChildPath 'src\DSCParser.sln') -c $Configuration --nologo | Out-Host

        if ($LASTEXITCODE -ne 0)
        {
            throw "Building the DSCParser solution failed with exit code $LASTEXITCODE."
        }

        & (Join-Path -Path $repositoryRoot -ChildPath 'Utilities\Build.ps1') -Configuration $Configuration -SkipClean | Out-Host
    }

    $dotNetResults = $null
    if (-not $SkipDotNetTests.IsPresent)
    {
        $dotNetResults = Invoke-DotNetTest -RepositoryRoot $repositoryRoot `
            -Configuration $Configuration `
            -IgnoreCodeCoverage:$IgnoreCodeCoverage
    }

    $pesterResults = $null
    if (-not $SkipPesterTests.IsPresent)
    {
        $pesterResults = Invoke-PesterTest -RepositoryRoot $repositoryRoot `
            -TestResultsFile $TestResultsFile `
            -DscTestsPath $DscTestsPath `
            -IgnoreCodeCoverage:$IgnoreCodeCoverage
    }

    $stopwatch.Stop()

    $message = 'Running the tests took {0} hours, {1} minutes, {2} seconds' -f $stopwatch.Elapsed.Hours, $stopwatch.Elapsed.Minutes, $stopwatch.Elapsed.Seconds
    Write-Host -Object $message

    $result = [PSCustomObject]@{
        Pester   = $pesterResults
        DotNet   = $dotNetResults
        Duration = $stopwatch.Elapsed
    }

    Write-TestHarnessSummary -Result $result

    return $result
}

<#
.SYNOPSIS
    Renders the results of Invoke-TestHarness as a report.

.DESCRIPTION
    Writes a combined test and coverage report for both suites. Without a path the report goes
    to the console, with a path it is appended as GitHub flavoured markdown, which makes it
    usable as a GitHub Actions step summary.

.PARAMETER Result
    The object returned by Invoke-TestHarness.

.PARAMETER Path
    File to append the markdown report to.

.EXAMPLE
    Write-TestHarnessSummary -Result $results -Path $env:GITHUB_STEP_SUMMARY
#>
function Write-TestHarnessSummary
{
    [CmdletBinding()]
    [OutputType([System.Void])]
    param
    (
        [Parameter(Mandatory = $true)]
        [PSCustomObject]
        $Result,

        [Parameter()]
        [System.String]
        $Path
    )

    $lines = [System.Collections.Generic.List[System.String]]::new()

    if ($null -ne $Result.DotNet)
    {
        $lines.Add('## C# Unit Test Results')
        $lines.Add('')
        $lines.Add('| Passed | Failed | Skipped |')
        $lines.Add('| ---: | ---: | ---: |')
        $lines.Add("| $($Result.DotNet.Passed) | $($Result.DotNet.Failed) | $($Result.DotNet.Skipped) |")
        $lines.Add('')

        if ($null -ne $Result.DotNet.Coverage)
        {
            $coverage = $Result.DotNet.Coverage
            $lines.Add('## C# Code Coverage')
            $lines.Add('')
            $lines.Add("**$($coverage.CoveragePercent)%** of $($coverage.LinesValid) lines covered.")
            $lines.Add('')
            $lines.Add('| Type | Covered | Missed |')
            $lines.Add('| :--- | ---: | ---: |')

            foreach ($class in $coverage.Classes)
            {
                $lines.Add("| $($class.Name) | $($class.CoveragePercent)% | $($class.LinesValid - $class.LinesCovered) |")
            }

            $lines.Add('')
        }
    }

    if ($null -ne $Result.Pester)
    {
        $lines.Add('## PowerShell Unit Test Results')
        $lines.Add('')
        $lines.Add('| Passed | Failed | Skipped |')
        $lines.Add('| ---: | ---: | ---: |')
        $lines.Add("| $($Result.Pester.PassedCount) | $($Result.Pester.FailedCount) | $($Result.Pester.SkippedCount) |")
        $lines.Add('')

        $coverage = $Result.Pester.CodeCoverage
        if ($null -ne $coverage)
        {
            $lines.Add('## PowerShell Code Coverage')
            $lines.Add('')
            $lines.Add("**$([System.Math]::Round($coverage.CoveragePercent, 2))%** of $($coverage.CommandsAnalyzedCount) commands covered.")
            $lines.Add('')
            $lines.Add('| File | Covered | Missed |')
            $lines.Add('| :--- | ---: | ---: |')

            $perFile = @{}
            foreach ($command in @($coverage.CommandsExecuted) + @($coverage.CommandsMissed))
            {
                if ($null -eq $command)
                {
                    continue
                }

                if (-not $perFile.ContainsKey($command.File))
                {
                    $perFile[$command.File] = @{ Analyzed = 0; Missed = 0 }
                }

                $perFile[$command.File].Analyzed++
            }

            foreach ($command in @($coverage.CommandsMissed))
            {
                if ($null -eq $command)
                {
                    continue
                }

                $perFile[$command.File].Missed++
            }

            foreach ($file in ($perFile.Keys | Sort-Object))
            {
                $analyzed = $perFile[$file].Analyzed
                $missed = $perFile[$file].Missed
                $percentage = [System.Math]::Round(($analyzed - $missed) / $analyzed * 100, 2)
                $lines.Add("| $(Split-Path -Path $file -Leaf) | $percentage% | $missed |")
            }

            $lines.Add('')
        }
    }

    if ([System.String]::IsNullOrEmpty($Path))
    {
        $lines | ForEach-Object { Write-Host -Object $_ }
    }
    else
    {
        $lines | Out-File -FilePath $Path -Append -Encoding utf8
    }
}

Export-ModuleMember -Function Invoke-TestHarness, Write-TestHarnessSummary
