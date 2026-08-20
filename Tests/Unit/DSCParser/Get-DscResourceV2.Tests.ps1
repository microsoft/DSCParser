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
}

AfterAll {
    $env:PSModulePath = $script:OriginalModulePath
    Remove-Module -Name DSCParser -Force -ErrorAction SilentlyContinue
}

Describe 'Get-DscResourceV2' {
    Context 'When discovering a MOF based module' {
        BeforeAll {
            $script:MofResources = @(Get-DscResourceV2 -Module 'DscParserTest.MofResources')
            $script:FileResource = $script:MofResources | Where-Object -FilterScript { $_.Name -eq 'DscParserTestFile' }
        }

        It 'Should return every resource the module exports' {
            $script:MofResources.Name | Sort-Object | Should -Be @('DscParserTestCimHost', 'DscParserTestFile')
        }

        It 'Should report the MOF class name as the resource type and the friendly name as the name' {
            $script:FileResource.ResourceType | Should -Be 'DSCPT_TestFile'
            $script:FileResource.Name | Should -Be 'DscParserTestFile'
            $script:FileResource.FriendlyName | Should -Be 'DscParserTestFile'
        }

        It 'Should classify the resource as a script based PowerShell resource' {
            $script:FileResource.ImplementationDetail | Should -Be 'ScriptBased'
            $script:FileResource.ImplementedAs | Should -Be 'PowerShell'
        }

        It 'Should point Path at the implementing script and ParentPath at its folder' {
            $script:FileResource.Path | Should -Be (Join-Path -Path $script:FixtureModuleRoot -ChildPath 'DscParserTest.MofResources\DscResources\DSCPT_TestFile\DSCPT_TestFile.psm1')
            $script:FileResource.ParentPath | Should -Be (Join-Path -Path $script:FixtureModuleRoot -ChildPath 'DscParserTest.MofResources\DscResources\DSCPT_TestFile')
            Test-Path -Path $script:FileResource.Path | Should -BeTrue
        }

        It 'Should carry the owning module and its company name' {
            $script:FileResource.ModuleName | Should -Be 'DscParserTest.MofResources'
            $script:FileResource.Module.Version | Should -Be ([System.Version]'1.0.0')
            $script:FileResource.CompanyName | Should -Be 'DSCParser Test Fixtures'
        }

        It 'Should list the mandatory property first and sort the remaining properties by name' {
            $script:FileResource.Properties.Name | Should -Be @(
                'Path'
                'Contents'
                'DependsOn'
                'Ensure'
                'Force'
                'PsDscRunAsCredential'
                'Retries'
                'Tags'
            )
        }

        It 'Should mark only the key property as mandatory' {
            ($script:FileResource.Properties | Where-Object -FilterScript { $_.IsMandatory }).Name | Should -Be 'Path'
        }

        It 'Should translate the MOF type constraints into PowerShell type names' {
            $types = @{}
            $script:FileResource.Properties | ForEach-Object -Process { $types[$_.Name] = $_.PropertyType }

            $types['Contents'] | Should -Be '[string]'
            $types['Force'] | Should -Be '[bool]'
            $types['Retries'] | Should -Be '[UInt32]'
            $types['Tags'] | Should -Be '[string[]]'
            $types['PsDscRunAsCredential'] | Should -Be '[PSCredential]'
        }

        It 'Should expose the ValueMap of a constrained property in sorted order' {
            $ensure = $script:FileResource.Properties | Where-Object -FilterScript { $_.Name -eq 'Ensure' }

            $ensure.Values | Should -Be @('Absent', 'Present')
        }

        It 'Should describe the embedded CIM instance properties of the CIM host resource' {
            $cimHost = $script:MofResources | Where-Object -FilterScript { $_.Name -eq 'DscParserTestCimHost' }
            $types = @{}
            $cimHost.Properties | ForEach-Object -Process { $types[$_.Name] = $_.PropertyType }

            $types['DefaultSetting'] | Should -Be '[DSCPT_TestSetting]'
            $types['Settings'] | Should -Be '[DSCPT_TestSetting[]]'
        }
    }

    Context 'When discovering a class based module' {
        BeforeAll {
            $script:ClassResources = @(Get-DscResourceV2 -Module 'DscParserTest.ClassResources')
        }

        It 'Should return the resource the module exports' {
            $script:ClassResources.Name | Should -Be 'DscParserTestClassApp'
        }

        It 'Should classify the resource as class based' {
            $script:ClassResources[0].ImplementationDetail | Should -Be 'ClassBased'
            $script:ClassResources[0].ImplementedAs | Should -Be 'PowerShell'
        }

        It 'Should point Path at the module manifest and ParentPath at the module folder' {
            $moduleBase = Join-Path -Path $script:FixtureModuleRoot -ChildPath 'DscParserTest.ClassResources\1.2.0'

            $script:ClassResources[0].Path | Should -Be (Join-Path -Path $moduleBase -ChildPath 'DscParserTest.ClassResources.psd1')
            $script:ClassResources[0].ParentPath | Should -Be $moduleBase
        }

        It 'Should not report the helper class the resource uses as a property type' {
            $script:ClassResources.Name | Should -Not -Contain 'DscParserTestClassOption'
        }

        It 'Should discover the module from its versioned subfolder' {
            $script:ClassResources[0].Module.Version | Should -Be ([System.Version]'1.2.0')
        }
    }

    Context 'When the same module is installed in several versions' {
        It 'Should return the resource only once' {
            $resources = @(Get-DscResourceV2 -Module 'DscParserTest.MultiVersion')

            @($resources | Where-Object -FilterScript { $_.Name -eq 'DscParserTestVersioned' }).Count | Should -Be 1
        }
    }

    Context 'When filtering by resource name' {
        It 'Should return only the named resource' {
            $resources = @(Get-DscResourceV2 -Name 'DscParserTestFile' -Module 'DscParserTest.MofResources')

            $resources.Name | Should -Be 'DscParserTestFile'
        }

        It 'Should match a wildcard against every resource of the module' {
            $resources = @(Get-DscResourceV2 -Name 'DscParserTest*' -Module 'DscParserTest.MofResources')

            $resources.Count | Should -Be 2
        }

        It 'Should write a ResourceNotFound error when an exact name matches nothing' {
            $null = Get-DscResourceV2 -Name 'DscParserTestNoSuchResource' -Module 'DscParserTest.MofResources' -ErrorVariable resourceErrors -ErrorAction SilentlyContinue

            $resourceErrors.FullyQualifiedErrorId | Should -Match 'ResourceNotFound'
        }

        It 'Should not write an error when a wildcard matches nothing' {
            $null = Get-DscResourceV2 -Name 'DscParserTestNoSuch*' -Module 'DscParserTest.MofResources' -ErrorVariable resourceErrors -ErrorAction SilentlyContinue

            $resourceErrors | Should -BeNullOrEmpty
        }
    }

    Context 'When filtering by module' {
        It 'Should accept the module name as a string' {
            @(Get-DscResourceV2 -Module 'DscParserTest.MofResources').Count | Should -Be 2
        }

        It 'Should accept a hashtable carrying a ModuleName key' {
            @(Get-DscResourceV2 -Module @{ ModuleName = 'DscParserTest.MofResources' }).Count | Should -Be 2
        }

        It 'Should accept a ModuleSpecification' {
            $specification = [Microsoft.PowerShell.Commands.ModuleSpecification]::new(
                @{ ModuleName = 'DscParserTest.MofResources'; RequiredVersion = '1.0.0' })

            @(Get-DscResourceV2 -Module $specification).Count | Should -Be 2
        }

        It 'Should return nothing for a module that is not installed' {
            @(Get-DscResourceV2 -Module 'DscParserTestNoSuchModule') | Should -BeNullOrEmpty
        }
    }

    Context 'When requesting the syntax' {
        BeforeAll {
            $script:Syntax = @(Get-DscResourceV2 -Name 'DscParserTestFile' -Module 'DscParserTest.MofResources' -Syntax)
        }

        It 'Should emit strings instead of resource objects' {
            $script:Syntax | Should -BeOfType [System.String]
        }

        It 'Should render the mandatory property without brackets and the optional ones with brackets' {
            $script:Syntax[0] | Should -Match 'DscParserTestFile \[String\] #ResourceName'
            $script:Syntax[0] | Should -Match '\n\s+Path = \[string\]'
            $script:Syntax[0] | Should -Match '\[Force = \[bool\]\]'
        }

        It 'Should render the allowed values of a constrained property' {
            $script:Syntax[0] | Should -Match '\[Ensure = \[string\]\{ Absent \| Present \}\]'
        }
    }

    Context 'When discovering composite resources' {
        It 'Should not return a composite resource because configurations are never autoloaded' {
            @(Get-DscResourceV2 -Module 'DscParserTest.Composite') | Should -BeNullOrEmpty
        }
    }
}
