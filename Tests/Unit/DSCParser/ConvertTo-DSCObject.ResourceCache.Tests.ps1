#Requires -Modules Pester

BeforeAll {
    $script:RepositoryRoot = (Resolve-Path -Path (Join-Path -Path $PSScriptRoot -ChildPath '..\..\..')).Path
    $script:FixtureModuleRoot = Join-Path -Path $script:RepositoryRoot -ChildPath 'Tests\Fixtures\Modules'
    $script:ConfigurationRoot = Join-Path -Path $script:RepositoryRoot -ChildPath 'Tests\Fixtures\Configurations'
    $script:OriginalModulePath = $env:PSModulePath

    if ($env:PSModulePath -notlike "*$script:FixtureModuleRoot*")
    {
        $env:PSModulePath = $script:FixtureModuleRoot + [System.IO.Path]::PathSeparator + $env:PSModulePath
    }

    Import-Module -Name (Join-Path -Path $script:RepositoryRoot -ChildPath 'DSCParser\DSCParser.psd1') -Force

    $script:ValidConfigurationPath = Join-Path -Path $script:ConfigurationRoot -ChildPath 'ValidConfiguration.ps1'
}

AfterAll {
    [DSCParser.CSharp.DscParser]::ClearCaches()
    InModuleScope -ModuleName DSCParser { $Script:DscResourceCache = $null }
    $env:PSModulePath = $script:OriginalModulePath
    Remove-Module -Name DSCParser -Force -ErrorAction SilentlyContinue
}

Describe 'ConvertTo-DSCObject resource cache' {
    BeforeEach {
        InModuleScope -ModuleName DSCParser { $Script:DscResourceCache = $null }
        [DSCParser.CSharp.DscParser]::ClearCaches()
    }

    Context 'When DscResourceInfo is supplied' {
        It 'Should use the supplied resources as the cache' {
            $supplied = @(Get-DscResourceV2 -Module 'DscParserTest.MofResources')

            $null = ConvertTo-DSCObject -Path $script:ValidConfigurationPath -DscResourceInfo $supplied

            InModuleScope -ModuleName DSCParser {
                $Script:DscResourceCache.Count | Should -Be 2
                $Script:DscResourceCache.Name | Sort-Object | Should -Be @('DscParserTestCimHost', 'DscParserTestFile')
            }
        }

        It 'Should replace a cache that a previous call populated' {
            $both = @(Get-DscResourceV2 -Module 'DscParserTest.MofResources')
            $null = ConvertTo-DSCObject -Path $script:ValidConfigurationPath -DscResourceInfo $both

            $fileOnly = @($both | Where-Object -FilterScript { $_.Name -eq 'DscParserTestFile' })
            $null = ConvertTo-DSCObject -Path $script:ValidConfigurationPath -DscResourceInfo $fileOnly

            InModuleScope -ModuleName DSCParser {
                $Script:DscResourceCache.Count | Should -Be 1
                $Script:DscResourceCache.Name | Should -Be 'DscParserTestFile'
            }
        }
    }

    Context 'When DscResourceInfo is omitted' {
        It 'Should populate the cache from a full discovery on the first call' {
            $null = ConvertTo-DSCObject -Path $script:ValidConfigurationPath -WarningAction SilentlyContinue

            InModuleScope -ModuleName DSCParser {
                $Script:DscResourceCache | Should -Not -BeNullOrEmpty
                $Script:DscResourceCache.Name | Should -Contain 'DscParserTestFile'
            }
        }

        It 'Should reuse the existing cache instead of discovering again' {
            $null = ConvertTo-DSCObject -Path $script:ValidConfigurationPath -WarningAction SilentlyContinue

            InModuleScope -ModuleName DSCParser {
                $Script:DscResourceCache = @($Script:DscResourceCache |
                        Where-Object -FilterScript { $_.Name -eq 'DscParserTestFile' })
            }

            $null = ConvertTo-DSCObject -Path $script:ValidConfigurationPath -WarningAction SilentlyContinue

            InModuleScope -ModuleName DSCParser {
                $Script:DscResourceCache.Count | Should -Be 1
            }
        }

        It 'Should discover only the modules the configuration imports and add others on demand' {
            $classModule = Get-Module -ListAvailable -Name 'DscParserTest.ClassResources'
            $classConfiguration = @"
Configuration ClassBased
{
    Import-DscResource -ModuleName '$($classModule.Name)' -ModuleVersion '$($classModule.Version)'

    Node localhost
    {
        DscParserTestClassApp TestInstance
        {
            AppName       = 'Contoso'
            InstanceCount = 2
        }
    }
}
"@

            $null = ConvertTo-DSCObject -Path $script:ValidConfigurationPath -WarningAction SilentlyContinue

            InModuleScope -ModuleName DSCParser {
                $Script:DscResourceCache.Name | Should -Contain 'DscParserTestFile'
                $Script:DscResourceCache.Name | Should -Not -Contain 'DscParserTestClassApp'
            }

            $parsed = @(ConvertTo-DSCObject -Content $classConfiguration -WarningAction SilentlyContinue)
            $parsed.Count | Should -Be 1

            InModuleScope -ModuleName DSCParser {
                $Script:DscResourceCache.Name | Should -Contain 'DscParserTestFile'
                $Script:DscResourceCache.Name | Should -Contain 'DscParserTestClassApp'
            }
        }

        It 'Should keep a cache that an earlier call supplied explicitly' {
            $fileOnly = @(Get-DscResourceV2 -Module 'DscParserTest.MofResources' -Name 'DscParserTestFile')
            $null = ConvertTo-DSCObject -Path $script:ValidConfigurationPath -DscResourceInfo $fileOnly

            $null = ConvertTo-DSCObject -Path $script:ValidConfigurationPath

            InModuleScope -ModuleName DSCParser {
                $Script:DscResourceCache.Count | Should -Be 1
                $Script:DscResourceCache.Name | Should -Be 'DscParserTestFile'
            }
        }
    }
}
