#Requires -Modules Pester

BeforeAll {
    $script:RepositoryRoot = (Resolve-Path -Path (Join-Path -Path $PSScriptRoot -ChildPath '..\..\..')).Path
    $script:ManifestPath = Join-Path -Path $script:RepositoryRoot -ChildPath 'DSCParser\DSCParser.psd1'

    Import-Module -Name $script:ManifestPath -Force
}

AfterAll {
    Remove-Module -Name DSCParser -Force -ErrorAction SilentlyContinue
}

Describe 'DSCParser module manifest' {
    It 'Should be a valid module manifest' {
        { Test-ModuleManifest -Path $script:ManifestPath -ErrorAction Stop } | Should -Not -Throw
    }

    It 'Should export exactly the functions declared in the manifest' {
        $declared = (Import-PowerShellDataFile -Path $script:ManifestPath).FunctionsToExport

        (Get-Module -Name DSCParser).ExportedFunctions.Keys | Sort-Object |
            Should -Be ($declared | Sort-Object)
    }

    It 'Should export exactly the cmdlets declared in the manifest' {
        $declared = (Import-PowerShellDataFile -Path $script:ManifestPath).CmdletsToExport

        (Get-Module -Name DSCParser).ExportedCmdlets.Keys | Sort-Object |
            Should -Be ($declared | Sort-Object)
    }

    It 'Should export ConvertTo-DSCObject, ConvertFrom-DSCObject and Get-DscResourceV2' {
        Get-Command -Module DSCParser -Name 'ConvertTo-DSCObject' | Should -Not -BeNullOrEmpty
        Get-Command -Module DSCParser -Name 'ConvertFrom-DSCObject' | Should -Not -BeNullOrEmpty
        Get-Command -Module DSCParser -Name 'Get-DscResourceV2' | Should -Not -BeNullOrEmpty
    }
}

Describe 'DSCParser assembly initialization' {
    It 'Should report the assembly as loaded after import' {
        InModuleScope -ModuleName DSCParser {
            $Script:AssemblyLoaded | Should -BeTrue
        }
    }

    It 'Should resolve the public parser types from the loaded assemblies' {
        [DSCParser.CSharp.DscParser] | Should -Not -BeNullOrEmpty
        [DSCParser.CSharp.DscParseOptions] | Should -Not -BeNullOrEmpty
        [DSCParser.PSDSC.DscResourceService] | Should -Not -BeNullOrEmpty
    }

    It 'Should return true without reloading when the assembly is already initialized' {
        InModuleScope -ModuleName DSCParser {
            Initialize-DscParserAssembly | Should -BeTrue
            Initialize-DscParserAssembly | Should -BeTrue
        }
    }

    It 'Should leave both warning sinks unset while no conversion is running' {
        [DSCParser.CSharp.DscParser]::WarningSink | Should -BeNullOrEmpty
        [DSCParser.PSDSC.DscResourceService]::WarningSink | Should -BeNullOrEmpty
    }
}

Describe 'DSCParser module without its backing assembly' {
    BeforeAll {
        $script:StagingRoot = Join-Path -Path ([System.IO.Path]::GetTempPath()) ('dscparser_noassembly_' + [System.Guid]::NewGuid().ToString('N'))
        New-Item -Path $script:StagingRoot -ItemType Directory -Force | Out-Null
        Copy-Item -Path (Join-Path -Path $script:RepositoryRoot -ChildPath 'DSCParser\DSCParser.psm1') -Destination $script:StagingRoot -Force

        $probeScript = Join-Path -Path $script:StagingRoot -ChildPath 'Probe.ps1'
        @'
param($ModulePath)
$module = Import-Module -Name $ModulePath -Force -PassThru -ErrorAction SilentlyContinue -WarningAction SilentlyContinue 2>$null
"AssemblyLoaded=$(& $module { $Script:AssemblyLoaded })"
try
{
    & $module { ConvertTo-DSCObject -Content 'Configuration C { }' -ErrorAction Stop } 2>$null
    'ConvertToThrew=False'
}
catch
{
    "ConvertToThrew=True;Message=$($_.Exception.Message)"
}
try
{
    & $module { ConvertFrom-DSCObject -DSCResources @(@{ ResourceName = 'X' }) -ErrorAction Stop } 2>$null
    'ConvertFromThrew=False'
}
catch
{
    'ConvertFromThrew=True'
}
'@ | Set-Content -Path $probeScript

        $script:ProbeOutput = (& pwsh -NoProfile -File $probeScript -ModulePath (Join-Path -Path $script:StagingRoot -ChildPath 'DSCParser.psm1')) -join "`n"
    }

    AfterAll {
        Remove-Item -Path $script:StagingRoot -Recurse -Force -ErrorAction SilentlyContinue
    }

    It 'Should report the assembly as not loaded' {
        $script:ProbeOutput | Should -Match 'AssemblyLoaded=False'
    }

    It 'Should make ConvertTo-DSCObject throw that module initialization failed' {
        $script:ProbeOutput | Should -Match 'ConvertToThrew=True'
        $script:ProbeOutput | Should -Match 'assembly is not loaded'
    }

    It 'Should make ConvertFrom-DSCObject throw' {
        $script:ProbeOutput | Should -Match 'ConvertFromThrew=True'
    }
}
