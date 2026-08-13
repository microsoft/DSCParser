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
    $script:ClassResources = @(Get-DscResourceV2 -Module 'DscParserTest.ClassResources')

    function ConvertTo-Configuration
    {
        param
        (
            [Parameter(Mandatory = $true)]
            [System.String]
            $Body,

            [Parameter(Mandatory = $true)]
            [System.String]
            $ModuleName
        )

        return @"
Configuration RoundTrip
{
    Import-DscResource -ModuleName '$ModuleName'

    Node localhost
    {
$Body
    }
}
"@
    }

    function Assert-EquivalentResource
    {
        param
        (
            [Parameter(Mandatory = $true)]
            [System.Collections.Hashtable]
            $Expected,

            [Parameter(Mandatory = $true)]
            [System.Collections.Hashtable]
            $Actual
        )

        $Actual.Keys | Sort-Object | Should -Be ($Expected.Keys | Sort-Object)

        foreach ($key in $Expected.Keys)
        {
            if ($Expected[$key] -is [System.Collections.Hashtable])
            {
                Assert-EquivalentResource -Expected $Expected[$key] -Actual $Actual[$key]
            }
            elseif ($Expected[$key] -is [System.Array] -and $Expected[$key].Count -gt 0 -and $Expected[$key][0] -is [System.Collections.Hashtable])
            {
                @($Actual[$key]).Count | Should -Be @($Expected[$key]).Count
                for ($index = 0; $index -lt @($Expected[$key]).Count; $index++)
                {
                    Assert-EquivalentResource -Expected $Expected[$key][$index] -Actual $Actual[$key][$index]
                }
            }
            else
            {
                $Actual[$key] | Should -Be $Expected[$key] -Because "property '$key' must survive the round trip"
            }
        }
    }
}

AfterAll {
    [DSCParser.CSharp.DscParser]::ClearCaches()
    $env:PSModulePath = $script:OriginalModulePath
    Remove-Module -Name DSCParser -Force -ErrorAction SilentlyContinue
}

Describe 'ConvertTo-DSCObject and ConvertFrom-DSCObject round trip' {
    Context 'When the configuration uses MOF based resources' {
        BeforeAll {
            $script:Original = @(ConvertTo-DSCObject `
                    -Path (Join-Path -Path $script:ConfigurationRoot -ChildPath 'ValidConfiguration.ps1') `
                    -DscResourceInfo $script:MofResources)

            $script:Rendered = ConvertFrom-DSCObject -DSCResources $script:Original

            $script:Reparsed = @(ConvertTo-DSCObject `
                    -Content (ConvertTo-Configuration -Body $script:Rendered -ModuleName 'DscParserTest.MofResources') `
                    -DscResourceInfo $script:MofResources)
        }

        It 'Should return the same number of resources' {
            $script:Reparsed.Count | Should -Be $script:Original.Count
        }

        It 'Should preserve every property of every resource' {
            for ($index = 0; $index -lt $script:Original.Count; $index++)
            {
                Assert-EquivalentResource -Expected $script:Original[$index] -Actual $script:Reparsed[$index]
            }
        }
    }

    Context 'When the configuration uses nested CIM instances' {
        BeforeAll {
            $script:OriginalCim = @(ConvertTo-DSCObject `
                    -Path (Join-Path -Path $script:ConfigurationRoot -ChildPath 'NestedCimConfiguration.ps1') `
                    -DscResourceInfo $script:MofResources)

            $script:ReparsedCim = @(ConvertTo-DSCObject `
                    -Content (ConvertTo-Configuration `
                        -Body (ConvertFrom-DSCObject -DSCResources $script:OriginalCim) `
                        -ModuleName 'DscParserTest.MofResources') `
                    -DscResourceInfo $script:MofResources)
        }

        It 'Should preserve the single embedded instance and its CIM type name' {
            $script:ReparsedCim[0].DefaultSetting.CIMInstance | Should -Be 'DSCPT_TestSetting'
            $script:ReparsedCim[0].DefaultSetting.Name | Should -Be 'Timeout'
            $script:ReparsedCim[0].DefaultSetting.Enforced | Should -BeTrue
        }

        It 'Should preserve every item of the embedded instance array in order' {
            @($script:ReparsedCim[0].Settings).Count | Should -Be 2
            $script:ReparsedCim[0].Settings[0].Name | Should -Be 'Retries'
            $script:ReparsedCim[0].Settings[1].Name | Should -Be 'Region'
        }

        It 'Should preserve every property of the resource' {
            Assert-EquivalentResource -Expected $script:OriginalCim[0] -Actual $script:ReparsedCim[0]
        }
    }

    Context 'When the configuration uses a class based resource' {
        BeforeAll {
            $content = ConvertTo-Configuration -ModuleName 'DscParserTest.ClassResources' -Body @'
        DscParserTestClassApp Contoso
        {
            AppName       = 'Contoso'
            DisplayName   = 'Contoso Application'
            Ensure        = 'Present'
            InstanceCount = 3
            Features      = @('search', 'index')
        }
'@
            $script:OriginalClass = @(ConvertTo-DSCObject -Content $content -DscResourceInfo $script:ClassResources)

            $script:ReparsedClass = @(ConvertTo-DSCObject `
                    -Content (ConvertTo-Configuration `
                        -Body (ConvertFrom-DSCObject -DSCResources $script:OriginalClass) `
                        -ModuleName 'DscParserTest.ClassResources') `
                    -DscResourceInfo $script:ClassResources)
        }

        It 'Should preserve every property of the resource' {
            Assert-EquivalentResource -Expected $script:OriginalClass[0] -Actual $script:ReparsedClass[0]
        }

        It 'Should keep the numeric and array property types' {
            $script:ReparsedClass[0].InstanceCount | Should -Be 3
            $script:ReparsedClass[0].Features | Should -Be @('search', 'index')
        }
    }

    Context 'When the rendered text is reparsed twice' {
        It 'Should be stable after the first render' {
            $original = @(ConvertTo-DSCObject `
                    -Path (Join-Path -Path $script:ConfigurationRoot -ChildPath 'ValidConfiguration.ps1') `
                    -DscResourceInfo $script:MofResources)

            $firstRender = ConvertFrom-DSCObject -DSCResources $original

            $reparsed = @(ConvertTo-DSCObject `
                    -Content (ConvertTo-Configuration -Body $firstRender -ModuleName 'DscParserTest.MofResources') `
                    -DscResourceInfo $script:MofResources)

            ConvertFrom-DSCObject -DSCResources $reparsed | Should -Be $firstRender
        }
    }
}
