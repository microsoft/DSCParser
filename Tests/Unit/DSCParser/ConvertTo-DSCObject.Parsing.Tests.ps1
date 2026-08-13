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
    $script:VersionedResources = @(Get-DscResourceV2 -Module 'DscParserTest.MultiVersion')

    function New-TestConfiguration
    {
        param
        (
            [Parameter(Mandatory = $true)]
            [System.String]
            $Body,

            [Parameter()]
            [System.String]
            $ModuleName = 'DscParserTest.MofResources',

            [Parameter()]
            [System.String]
            $ModuleVersion
        )

        $import = if ([System.String]::IsNullOrEmpty($ModuleVersion))
        {
            "Import-DscResource -ModuleName '$ModuleName'"
        }
        else
        {
            "Import-DscResource -ModuleName '$ModuleName' -ModuleVersion '$ModuleVersion'"
        }

        return @"
Configuration TestConfiguration
{
    $import

    Node localhost
    {
$Body
    }
}
"@
    }
}

AfterAll {
    [DSCParser.CSharp.DscParser]::ClearCaches()
    $env:PSModulePath = $script:OriginalModulePath
    Remove-Module -Name DSCParser -Force -ErrorAction SilentlyContinue
}

Describe 'ConvertTo-DSCObject -Content' {
    Context 'When converting scalar property values' {
        BeforeAll {
            $content = New-TestConfiguration -Body @'
        DscParserTestFile Scalars
        {
            Path     = 'C:\temp\scalars.txt'
            Contents = "double quoted"
            Ensure   = 'Present'
            Force    = $false
            Retries  = 42
        }
'@
            $script:Scalars = (@(ConvertTo-DSCObject -Content $content -DscResourceInfo $script:MofResources))[0]
        }

        It 'Should keep single quoted and double quoted strings as plain strings' {
            $script:Scalars.Path | Should -Be 'C:\temp\scalars.txt'
            $script:Scalars.Contents | Should -Be 'double quoted'
        }

        It 'Should convert a boolean literal to a boolean' {
            $script:Scalars.Force | Should -BeOfType [System.Boolean]
            $script:Scalars.Force | Should -BeFalse
        }

        It 'Should convert a numeric literal to a number' {
            $script:Scalars.Retries | Should -Be 42
        }
    }

    Context 'When converting arrays' {
        It 'Should return an array for a multi element array' {
            $content = New-TestConfiguration -Body @'
        DscParserTestFile Arrays
        {
            Path = 'C:\temp\arrays.txt'
            Tags = @('one', 'two', 'three')
        }
'@
            $parsed = (@(ConvertTo-DSCObject -Content $content -DscResourceInfo $script:MofResources))[0]

            $parsed.Tags | Should -Be @('one', 'two', 'three')
        }

        It 'Should return an empty collection for an empty array' {
            $content = New-TestConfiguration -Body @'
        DscParserTestFile EmptyArray
        {
            Path = 'C:\temp\empty.txt'
            Tags = @()
        }
'@
            $parsed = (@(ConvertTo-DSCObject -Content $content -DscResourceInfo $script:MofResources))[0]

            @($parsed.Tags).Count | Should -Be 0
        }
    }

    Context 'When converting embedded CIM instances' {
        BeforeAll {
            $script:CimParsed = (@(ConvertTo-DSCObject `
                        -Path (Join-Path -Path $script:ConfigurationRoot -ChildPath 'NestedCimConfiguration.ps1') `
                        -DscResourceInfo $script:MofResources))[0]
        }

        It 'Should convert a single embedded instance into a hashtable' {
            $script:CimParsed.DefaultSetting | Should -BeOfType [System.Collections.Hashtable]
            $script:CimParsed.DefaultSetting.Name | Should -Be 'Timeout'
            $script:CimParsed.DefaultSetting.Enforced | Should -BeTrue
        }

        It 'Should convert an array of embedded instances into one hashtable per item' {
            @($script:CimParsed.Settings).Count | Should -Be 2
            $script:CimParsed.Settings[0].Name | Should -Be 'Retries'
            $script:CimParsed.Settings[1].Value | Should -Be 'westeurope'
        }

        It 'Should record the CIM instance type name by default' {
            $script:CimParsed.DefaultSetting.CIMInstance | Should -Be 'DSCPT_TestSetting'
            $script:CimParsed.Settings[0].CIMInstance | Should -Be 'DSCPT_TestSetting'
        }
    }

    Context 'When IncludeCIMInstanceInfo is disabled' {
        It 'Should omit the CIMInstance key from every embedded instance' {
            $parsed = (@(ConvertTo-DSCObject `
                        -Path (Join-Path -Path $script:ConfigurationRoot -ChildPath 'NestedCimConfiguration.ps1') `
                        -DscResourceInfo $script:MofResources `
                        -IncludeCIMInstanceInfo $false))[0]

            $parsed.DefaultSetting.Keys | Should -Not -Contain 'CIMInstance'
            $parsed.Settings[0].Keys | Should -Not -Contain 'CIMInstance'
        }
    }

    Context 'When IncludeComments is enabled and the instance name is quoted' {
        BeforeAll {
            $content = New-TestConfiguration -Body @'
        DscParserTestFile "Commented"
        {
            Path     = 'C:\temp\commented.txt'
            # a comment on its own line belongs to no property
            Contents = 'body' # explains the contents
            Ensure   = 'Present'
        }
'@
            $script:Commented = (@(ConvertTo-DSCObject -Content $content -DscResourceInfo $script:MofResources -IncludeComments $true))[0]
            $script:Uncommented = (@(ConvertTo-DSCObject -Content $content -DscResourceInfo $script:MofResources))[0]
        }

        It 'Should attach a trailing comment as metadata for the property it follows' {
            $script:Commented['_metadata_Contents'] | Should -Be '# explains the contents'
        }

        It 'Should not attach a comment that sits on a line of its own' {
            $script:Commented['_metadata_Path'] | Should -BeNullOrEmpty
            @($script:Commented.Keys | Where-Object -FilterScript { $_ -like '_metadata_*' }) | Should -Be @('_metadata_Contents')
        }

        It 'Should not attach metadata when comments are not requested' {
            $script:Uncommented.Keys | Where-Object -FilterScript { $_ -like '_metadata_*' } | Should -BeNullOrEmpty
        }

        It 'Should attach each comment to the instance that owns it' {
            $content = New-TestConfiguration -Body @'
        DscParserTestFile "First"
        {
            Path     = 'C:\temp\first.txt'
            Contents = 'one' # first comment
        }

        DscParserTestFile "Second"
        {
            Path     = 'C:\temp\second.txt'
            Contents = 'two' # second comment
        }
'@
            $parsed = @(ConvertTo-DSCObject -Content $content -DscResourceInfo $script:MofResources -IncludeComments $true)

            $parsed[0]['_metadata_Contents'] | Should -Be '# first comment'
            $parsed[1]['_metadata_Contents'] | Should -Be '# second comment'
        }
    }

    Context 'When IncludeComments is enabled and the instance name is a bare word' {
        It 'Should not attach any metadata because the instance name is not a quoted string' {
            $content = New-TestConfiguration -Body @'
        DscParserTestFile BareWord
        {
            Path     = 'C:\temp\bareword.txt'
            Contents = 'body' # explains the contents
        }
'@
            $parsed = (@(ConvertTo-DSCObject -Content $content -DscResourceInfo $script:MofResources -IncludeComments $true))[0]

            $parsed.Keys | Where-Object -FilterScript { $_ -like '_metadata_*' } | Should -BeNullOrEmpty
        }
    }

    Context 'When the configuration imports a module installed in a single version' {
        It 'Should parse even though the requested version is not the installed one' {
            $content = New-TestConfiguration -ModuleVersion '99.0.0' -Body @'
        DscParserTestFile StrippedVersion
        {
            Path = 'C:\temp\stripped.txt'
        }
'@
            $parsed = @(ConvertTo-DSCObject -Content $content -DscResourceInfo $script:MofResources -WarningAction SilentlyContinue)

            $parsed.Count | Should -Be 1
            $parsed[0].ResourceInstanceName | Should -Be 'StrippedVersion'
        }
    }

    Context 'When the configuration imports a module installed in several versions' {
        It 'Should parse the resource of the requested version' {
            $content = New-TestConfiguration -ModuleName 'DscParserTest.MultiVersion' -ModuleVersion '2.0.0' -Body @'
        DscParserTestVersioned KeptVersion
        {
            Name  = 'item'
            Value = 'two'
        }
'@
            $parsed = @(ConvertTo-DSCObject -Content $content -DscResourceInfo $script:VersionedResources)

            $parsed.Count | Should -Be 1
            $parsed[0].Value | Should -Be 'two'
        }
    }

    Context 'When a configuration has no Node statement' {
        It 'Should throw' {
            $content = @'
Configuration NoNode
{
    Import-DscResource -ModuleName 'DscParserTest.MofResources'

    DscParserTestFile Orphan
    {
        Path = 'C:\temp\orphan.txt'
    }
}
'@
            { ConvertTo-DSCObject -Content $content -DscResourceInfo $script:MofResources -ErrorAction SilentlyContinue } |
                Should -Throw -ExpectedMessage '*No Node statement found*'
        }
    }

    Context 'When a schema is supplied' {
        It 'Should accept the schema and return the same resources as without it' {
            $content = New-TestConfiguration -Body @'
        DscParserTestFile WithSchema
        {
            Path = 'C:\temp\schema.txt'
        }
'@
            $withSchema = @(ConvertTo-DSCObject -Content $content -DscResourceInfo $script:MofResources -Schema 'DscParserTestSchema')
            $withoutSchema = @(ConvertTo-DSCObject -Content $content -DscResourceInfo $script:MofResources)

            $withSchema.Count | Should -Be 1
            $withSchema[0].Keys | Sort-Object | Should -Be ($withoutSchema[0].Keys | Sort-Object)
        }
    }

    Context 'When neither Path nor Content is supplied' {
        It 'Should refuse to bind the parameters' {
            { ConvertTo-DSCObject -DscResourceInfo $script:MofResources -ErrorAction Stop } | Should -Throw
        }
    }
}
