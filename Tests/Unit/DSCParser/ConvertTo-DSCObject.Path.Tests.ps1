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

    $script:MofResources = @(Get-DscResourceV2 -Module 'DscParserTest.MofResources')
}

AfterAll {
    [DSCParser.CSharp.DscParser]::ClearCaches()
    $env:PSModulePath = $script:OriginalModulePath
    Remove-Module -Name DSCParser -Force -ErrorAction SilentlyContinue
}

Describe 'ConvertTo-DSCObject -Path' {
    Context 'When validating the supplied path' {
        It 'Should reject a path that does not exist' {
            { ConvertTo-DSCObject -Path (Join-Path -Path $script:ConfigurationRoot -ChildPath 'NoSuchConfiguration.ps1') } |
                Should -Throw -ExpectedMessage '*File or folder does not exist*'
        }

        It 'Should reject a folder' {
            { ConvertTo-DSCObject -Path $script:ConfigurationRoot } |
                Should -Throw -ExpectedMessage '*Path argument must be a file*'
        }

        It 'Should accept a relative path' {
            Push-Location -Path $script:ConfigurationRoot
            try
            {
                $resources = @(ConvertTo-DSCObject -Path '.\ValidConfiguration.ps1' -DscResourceInfo $script:MofResources)

                $resources.Count | Should -Be 2
            }
            finally
            {
                Pop-Location
            }
        }
    }

    Context 'When parsing a configuration file from disk' {
        BeforeAll {
            $script:Parsed = @(ConvertTo-DSCObject `
                    -Path (Join-Path -Path $script:ConfigurationRoot -ChildPath 'ValidConfiguration.ps1') `
                    -DscResourceInfo $script:MofResources)
        }

        It 'Should return one hashtable per resource instance' {
            $script:Parsed.Count | Should -Be 2
            $script:Parsed | ForEach-Object -Process { $_ | Should -BeOfType [System.Collections.Hashtable] }
        }

        It 'Should preserve the resource name and instance name of every instance' {
            $script:Parsed[0].ResourceName | Should -Be 'DscParserTestFile'
            $script:Parsed[0].ResourceInstanceName | Should -Be 'FirstFile'
            $script:Parsed[1].ResourceInstanceName | Should -Be 'SecondFile'
        }

        It 'Should convert the property values to their PowerShell types' {
            $script:Parsed[0].Contents | Should -Be 'first'
            $script:Parsed[0].Force | Should -BeTrue
            $script:Parsed[0].Retries | Should -Be 3
            $script:Parsed[0].Tags | Should -Be @('alpha', 'beta')
        }

        It 'Should only return the properties the instance declares' {
            $script:Parsed[1].Keys | Sort-Object | Should -Be @('Ensure', 'Path', 'ResourceInstanceName', 'ResourceName')
        }
    }

    Context 'When the file contains no Configuration block' {
        It 'Should throw' {
            { ConvertTo-DSCObject `
                    -Path (Join-Path -Path $script:ConfigurationRoot -ChildPath 'NoConfigurationBlock.ps1') `
                    -DscResourceInfo $script:MofResources -ErrorAction SilentlyContinue } |
                Should -Throw -ExpectedMessage '*No Configuration definition found*'
        }

        It 'Should name the offending file in the error message' {
            $path = Join-Path -Path $script:ConfigurationRoot -ChildPath 'NoConfigurationBlock.ps1'

            { ConvertTo-DSCObject -Path $path -DscResourceInfo $script:MofResources -ErrorAction SilentlyContinue } |
                Should -Throw
        }
    }
}
