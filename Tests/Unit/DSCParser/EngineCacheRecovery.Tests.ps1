#Requires -Modules Pester

$script:PSDesiredStateConfigurationAvailable = $null -ne (Get-Module -ListAvailable -Name 'PSDesiredStateConfiguration')

BeforeAll {
    $script:RepositoryRoot = (Resolve-Path -Path (Join-Path -Path $PSScriptRoot -ChildPath '..\..\..')).Path
    $script:FixtureModuleRoot = Join-Path -Path $script:RepositoryRoot -ChildPath 'Tests\Fixtures\Modules'
    $script:OriginalModulePath = $env:PSModulePath

    if ($env:PSModulePath -notlike "*$script:FixtureModuleRoot*")
    {
        $env:PSModulePath = $script:FixtureModuleRoot + [System.IO.Path]::PathSeparator + $env:PSModulePath
    }

    Import-Module -Name (Join-Path -Path $script:RepositoryRoot -ChildPath 'DSCParser\DSCParser.psd1') -Force

    $script:CacheReflection = [DSCParser.PSDSC.DscResourceService].Assembly.GetType('DSCParser.PSDSC.DscClassCacheReflection')

    $script:Configuration = @'
Configuration CacheRecovery
{
    Import-DscResource -ModuleName 'DscParserTest.MofResources'

    Node localhost
    {
        DscParserTestFile Recovered
        {
            Path     = 'C:\temp\recovered.txt'
            Contents = 'still parsing'
        }
    }
}
'@

    function Clear-EngineDscCache
    {
        $script:CacheReflection.GetMethod('ClearCache').Invoke($null, $null)
        $script:CacheReflection.GetMethod('ResetDynamicKeywords').Invoke($null, $null)
    }
}

AfterAll {
    [DSCParser.CSharp.DscParser]::ClearCaches()
    $env:PSModulePath = $script:OriginalModulePath
    Remove-Module -Name DSCParser -Force -ErrorAction SilentlyContinue
}

Describe 'Recovery from a wiped engine DSC cache' {
    Context 'When the engine caches are cleared between calls' {
        BeforeAll {
            $script:Baseline = @(Get-DscResourceV2 -Module 'DscParserTest.MofResources')
        }

        It 'Should let ConvertTo-DSCObject parse again after a wipe' {
            Clear-EngineDscCache

            $parsed = @(ConvertTo-DSCObject -Content $script:Configuration -DscResourceInfo $script:Baseline)

            $parsed.Count | Should -Be 1
            $parsed[0].ResourceName | Should -Be 'DscParserTestFile'
        }

        It 'Should let Get-DscResourceV2 rediscover the same resources after a wipe' {
            Clear-EngineDscCache

            $rediscovered = @(Get-DscResourceV2 -Module 'DscParserTest.MofResources')

            $rediscovered.Name | Sort-Object | Should -Be ($script:Baseline.Name | Sort-Object)
        }

        It 'Should recover when the wipe happens between two conversions' {
            $null = ConvertTo-DSCObject -Content $script:Configuration -DscResourceInfo $script:Baseline
            Clear-EngineDscCache
            $parsed = @(ConvertTo-DSCObject -Content $script:Configuration -DscResourceInfo $script:Baseline)

            $parsed.Count | Should -Be 1
        }
    }

    Context 'When discovery and conversion are interleaved' {
        It 'Should discover after converting' {
            $resources = @(Get-DscResourceV2 -Module 'DscParserTest.MofResources')
            $null = ConvertTo-DSCObject -Content $script:Configuration -DscResourceInfo $resources

            @(Get-DscResourceV2 -Module 'DscParserTest.MofResources').Count | Should -Be $resources.Count
        }

        It 'Should convert after discovering' {
            $null = ConvertTo-DSCObject -Content $script:Configuration -DscResourceInfo @(Get-DscResourceV2 -Module 'DscParserTest.MofResources')
            $null = Get-DscResourceV2 -Module 'DscParserTest.ClassResources'

            $parsed = @(ConvertTo-DSCObject -Content $script:Configuration -DscResourceInfo @(Get-DscResourceV2 -Module 'DscParserTest.MofResources'))

            $parsed.Count | Should -Be 1
        }

        It 'Should leave the dynamic keyword table empty so the engine can compile configurations' {
            $null = ConvertTo-DSCObject -Content $script:Configuration -DscResourceInfo @(Get-DscResourceV2 -Module 'DscParserTest.MofResources')

            [System.Management.Automation.Language.DynamicKeyword]::ContainsKeyword('DscParserTestFile') | Should -BeFalse
        }
    }

    Context 'When a configuration is compiled to MOF in the same process' -Skip:(-not $script:PSDesiredStateConfigurationAvailable) {
        BeforeAll {
            $script:Baseline = @(Get-DscResourceV2 -Module 'DscParserTest.MofResources')
            $script:CompileOutput = Join-Path -Path ([System.IO.Path]::GetTempPath()) 'dscparser-pester-wipe'
        }

        AfterAll {
            Remove-Item -Path $script:CompileOutput -Recurse -Force -ErrorAction SilentlyContinue
        }

        It 'Should compile without failing after DSCParser has been used' {
            $null = ConvertTo-DSCObject -Content $script:Configuration -DscResourceInfo $script:Baseline

            { Invoke-Expression -Command "Configuration DscParserPesterWipeTest { Node localhost { } }`nDscParserPesterWipeTest -OutputPath '$script:CompileOutput'" | Out-Null } |
                Should -Not -Throw
        }

        It 'Should let ConvertTo-DSCObject parse again after the compile' {
            $parsed = @(ConvertTo-DSCObject -Content $script:Configuration -DscResourceInfo $script:Baseline)

            $parsed.Count | Should -Be 1
            $parsed[0].ResourceInstanceName | Should -Be 'Recovered'
        }

        It 'Should let Get-DscResourceV2 rediscover the same resources after the compile' {
            @(Get-DscResourceV2 -Module 'DscParserTest.MofResources').Name | Sort-Object |
                Should -Be ($script:Baseline.Name | Sort-Object)
        }
    }
}
