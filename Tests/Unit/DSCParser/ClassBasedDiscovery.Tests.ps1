#Requires -Modules Pester

BeforeAll {
    $script:RepositoryRoot = (Resolve-Path -Path (Join-Path -Path $PSScriptRoot -ChildPath '..\..\..')).Path
    $script:FixtureModuleRoot = Join-Path -Path $script:RepositoryRoot -ChildPath 'Tests\Fixtures\Modules'
    $script:OriginalModulePath = $env:PSModulePath

    if ($env:PSModulePath -notlike "*$script:FixtureModuleRoot*")
    {
        $env:PSModulePath = $script:FixtureModuleRoot + [System.IO.Path]::PathSeparator + $env:PSModulePath
    }

    Import-Module -Name (Join-Path -Path $script:RepositoryRoot -ChildPath 'DSCParser\DSCParser.psd1') -Force

    $script:ClassBasedModule = Get-Module -ListAvailable -Name 'DscParserTest.ClassResources'
    $script:Discovered = @(Get-DscResourceV2 -Module 'DscParserTest.ClassResources')
    $script:ExportedNames = @($script:ClassBasedModule.ExportedDscResources)
}

AfterAll {
    [DSCParser.CSharp.DscParser]::ClearCaches()
    $env:PSModulePath = $script:OriginalModulePath
    Remove-Module -Name DSCParser -Force -ErrorAction SilentlyContinue
}

Describe 'Class based resource discovery' {
    It 'Should report the exported resources on the module itself' {
        $script:ExportedNames | Should -Be @('DscParserTestClassApp')
    }

    It 'Should discover every resource the module exports' {
        $notDiscovered = @($script:ExportedNames | Where-Object -FilterScript { $_ -notin $script:Discovered.Name })

        $notDiscovered | Should -BeNullOrEmpty
    }

    It 'Should classify every exported resource as ClassBased' {
        $exported = @($script:Discovered | Where-Object -FilterScript { $_.Name -in $script:ExportedNames })
        $notClassBased = @($exported | Where-Object -FilterScript { $_.ImplementationDetail -ne 'ClassBased' })

        $notClassBased | Should -BeNullOrEmpty
    }

    It 'Should never report a resource as ClassBased that the module does not export' {
        $misclassified = @($script:Discovered |
                Where-Object -FilterScript { $_.ImplementationDetail -eq 'ClassBased' -and $_.Name -notin $script:ExportedNames })

        $misclassified | Should -BeNullOrEmpty
    }

    It 'Should not report the complex type declared alongside the resource' {
        $script:Discovered.Name | Should -Not -Contain 'DscParserTestClassOption'
    }
}

Describe 'Class based resource parsing' {
    BeforeAll {
        $script:ClassConfiguration = @"
Configuration ClassBased
{
    Import-DscResource -ModuleName '$($script:ClassBasedModule.Name)' -ModuleVersion '$($script:ClassBasedModule.Version)'

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
    }

    It 'Should parse a configuration that uses the class based resource' {
        $parsed = @(ConvertTo-DSCObject -Content $script:ClassConfiguration -DscResourceInfo $script:Discovered)

        $parsed.Count | Should -Be 1
        $parsed[0].ResourceName | Should -Be 'DscParserTestClassApp'
        $parsed[0].ResourceInstanceName | Should -Be 'TestInstance'
        $parsed[0].AppName | Should -Be 'Contoso'
    }

    It 'Should parse the configuration when the module version is not pinned' {
        $unpinned = $script:ClassConfiguration -replace " -ModuleVersion '[^']+'", ''

        $parsed = @(ConvertTo-DSCObject -Content $unpinned -DscResourceInfo $script:Discovered)

        $parsed.Count | Should -Be 1
    }
}
