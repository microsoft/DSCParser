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

Describe 'ConvertTo-DSCObject diagnostics' {
    Context 'When the configuration imports a module that is not installed' {
        BeforeAll {
            $script:MissingModuleContent = @'
Configuration MissingModule
{
    Import-DscResource -ModuleName 'DscParserTest.NotInstalled' -ModuleVersion '4.5.6'
    Import-DscResource -ModuleName 'DscParserTest.MofResources'

    Node localhost
    {
        DscParserTestFile Present
        {
            Path = 'C:\temp\present.txt'
        }
    }
}
'@
        }

        It 'Should replay the diagnostic on the PowerShell warning stream' {
            $null = ConvertTo-DSCObject -Content $script:MissingModuleContent `
                -DscResourceInfo $script:MofResources `
                -WarningVariable parserWarnings -WarningAction SilentlyContinue

            $parserWarnings | Should -Not -BeNullOrEmpty
            $parserWarnings -join "`n" | Should -Match "Could not find the module '<DscParserTest.NotInstalled, 4.5.6>'"
        }

        It 'Should still return the resources of the modules that are installed' {
            $parsed = @(ConvertTo-DSCObject -Content $script:MissingModuleContent `
                    -DscResourceInfo $script:MofResources -WarningAction SilentlyContinue)

            $parsed.Count | Should -Be 1
            $parsed[0].ResourceInstanceName | Should -Be 'Present'
        }

        It 'Should honour WarningAction SilentlyContinue' {
            $warnings = ConvertTo-DSCObject -Content $script:MissingModuleContent `
                -DscResourceInfo $script:MofResources -WarningAction SilentlyContinue 3>&1 |
                Where-Object -FilterScript { $_ -is [System.Management.Automation.WarningRecord] }

            $warnings | Should -BeNullOrEmpty
        }
    }

    Context 'When the configuration uses a resource the module no longer defines' {
        BeforeAll {
            $script:UnresolvedPath = Join-Path -Path $script:ConfigurationRoot -ChildPath 'UnresolvedResourceConfiguration.ps1'
        }

        It 'Should warn that the resource was omitted' {
            $null = ConvertTo-DSCObject -Path $script:UnresolvedPath `
                -DscResourceInfo $script:MofResources `
                -WarningVariable parserWarnings -WarningAction SilentlyContinue

            $parserWarnings -join "`n" | Should -Match "Resource 'DscParserTestRemovedResource' \(instance 'RemovedInstance'\)"
            $parserWarnings -join "`n" | Should -Match 'omitted from the converted configuration'
        }

        It 'Should prefix the diagnostic with the file it came from' {
            $null = ConvertTo-DSCObject -Path $script:UnresolvedPath `
                -DscResourceInfo $script:MofResources `
                -WarningVariable parserWarnings -WarningAction SilentlyContinue

            $parserWarnings -join "`n" | Should -Match ([System.Text.RegularExpressions.Regex]::Escape($script:UnresolvedPath))
        }

        It 'Should skip the detached body and keep the resources that follow' {
            $parsed = @(ConvertTo-DSCObject -Path $script:UnresolvedPath `
                    -DscResourceInfo $script:MofResources -WarningAction SilentlyContinue)

            $parsed.Count | Should -Be 1
            $parsed[0].ResourceInstanceName | Should -Be 'SurvivingFile'
        }
    }

    Context 'When a resource type is absent from the supplied resource set' {
        It 'Should warn that the resource was not found among the loaded resources' {
            $content = @'
Configuration MissingResource
{
    Import-DscResource -ModuleName 'DscParserTest.MofResources'

    Node localhost
    {
        DscParserTestCimHost OnlyHost
        {
            Name = 'Host1'
        }
    }
}
'@
            $fileOnly = @($script:MofResources | Where-Object -FilterScript { $_.Name -eq 'DscParserTestFile' })
            [DSCParser.CSharp.DscParser]::ClearCaches()

            $parsed = @(ConvertTo-DSCObject -Content $content -DscResourceInfo $fileOnly `
                    -WarningVariable parserWarnings -WarningAction SilentlyContinue)

            $parsed.Count | Should -Be 0
            $parserWarnings -join "`n" | Should -Match "Resource 'DscParserTestCimHost' \(instance 'OnlyHost'\) was not found among the loaded DSC resources"
        }
    }

    Context 'When the configuration cannot be parsed' {
        It 'Should throw for an unrecoverable parse error inside the configuration block' {
            $content = @'
Configuration Broken
{
    Import-DscResource -ModuleName 'DscParserTest.MofResources'

    Node localhost
    {
        DscParserTestFile Broken
        {
            Path = 'C:\temp\broken.txt'
'@
            { ConvertTo-DSCObject -Content $content -DscResourceInfo $script:MofResources -ErrorAction SilentlyContinue } |
                Should -Throw
        }
    }

    Context 'When a conversion completes' {
        It 'Should clear both warning sinks after a successful call' {
            $null = ConvertTo-DSCObject -Path (Join-Path -Path $script:ConfigurationRoot -ChildPath 'ValidConfiguration.ps1') `
                -DscResourceInfo $script:MofResources

            [DSCParser.CSharp.DscParser]::WarningSink | Should -BeNullOrEmpty
            [DSCParser.PSDSC.DscResourceService]::WarningSink | Should -BeNullOrEmpty
        }

        It 'Should clear both warning sinks after a failed call' {
            { ConvertTo-DSCObject `
                    -Path (Join-Path -Path $script:ConfigurationRoot -ChildPath 'NoConfigurationBlock.ps1') `
                    -DscResourceInfo $script:MofResources -ErrorAction SilentlyContinue } | Should -Throw

            [DSCParser.CSharp.DscParser]::WarningSink | Should -BeNullOrEmpty
            [DSCParser.PSDSC.DscResourceService]::WarningSink | Should -BeNullOrEmpty
        }
    }
}
