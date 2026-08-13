#Requires -Modules Pester

BeforeAll {
    $script:RepositoryRoot = (Resolve-Path -Path (Join-Path -Path $PSScriptRoot -ChildPath '..\..\..')).Path

    Import-Module -Name (Join-Path -Path $script:RepositoryRoot -ChildPath 'DSCParser\DSCParser.psd1') -Force
}

AfterAll {
    Remove-Module -Name DSCParser -Force -ErrorAction SilentlyContinue
}

Describe 'ConvertFrom-DSCObject' {
    Context 'When rendering a single resource' {
        BeforeAll {
            $script:Rendered = ConvertFrom-DSCObject -DSCResources @(
                @{
                    ResourceName         = 'DscParserTestFile'
                    ResourceInstanceName = 'Rendered'
                    Path                 = 'C:\temp\rendered.txt'
                    Retries              = 7
                    Force                = $true
                }
            )
        }

        It 'Should open the block with the resource name and the quoted instance name' {
            $script:Rendered | Should -Match '^DscParserTestFile "Rendered"\r?\n\{'
        }

        It 'Should quote string values and leave numbers and booleans unquoted' {
            $script:Rendered | Should -Match 'Path\s+= "C:\\temp\\rendered.txt"'
            $script:Rendered | Should -Match 'Retries\s+= 7'
            $script:Rendered | Should -Match 'Force\s+= \$True'
        }

        It 'Should not render the resource name and instance name as properties' {
            $script:Rendered | Should -Not -Match 'ResourceName\s+='
            $script:Rendered | Should -Not -Match 'ResourceInstanceName\s+='
        }

        It 'Should produce text that parses without errors' {
            $parseErrors = $null
            $null = [System.Management.Automation.Language.Parser]::ParseInput($script:Rendered, [ref]$null, [ref]$parseErrors)

            $parseErrors | Should -BeNullOrEmpty
        }
    }

    Context 'When rendering several resources' {
        It 'Should render one block per resource' {
            $rendered = ConvertFrom-DSCObject -DSCResources @(
                @{ ResourceName = 'DscParserTestFile'; ResourceInstanceName = 'One'; Path = 'a' }
                @{ ResourceName = 'DscParserTestFile'; ResourceInstanceName = 'Two'; Path = 'b' }
            )

            ([regex]::Matches($rendered, 'DscParserTestFile "')).Count | Should -Be 2
        }
    }

    Context 'When rendering nested values' {
        It 'Should render an array of scalars one item per line' {
            $rendered = ConvertFrom-DSCObject -DSCResources @(
                @{ ResourceName = 'R'; ResourceInstanceName = 'I'; Tags = @('one', 'two') }
            )

            $rendered | Should -Match 'Tags\s+= @\(\r?\n\s+"one"\r?\n\s+"two"\r?\n\s+\)'
        }

        It 'Should render an empty array inline' {
            $rendered = ConvertFrom-DSCObject -DSCResources @(
                @{ ResourceName = 'R'; ResourceInstanceName = 'I'; Tags = @() }
            )

            $rendered | Should -Match 'Tags\s+= @\(\)'
        }

        It 'Should render an embedded CIM instance using its CIMInstance name' {
            $rendered = ConvertFrom-DSCObject -DSCResources @(
                @{
                    ResourceName         = 'R'
                    ResourceInstanceName = 'I'
                    DefaultSetting       = @{ CIMInstance = 'DSCPT_TestSetting'; Name = 'Timeout'; Value = '30' }
                }
            )

            $rendered | Should -Match 'DefaultSetting\s+= DSCPT_TestSetting\{'
            $rendered | Should -Match 'Name\s+= "Timeout"'
        }

        It 'Should render an array of embedded CIM instances' {
            $rendered = ConvertFrom-DSCObject -DSCResources @(
                @{
                    ResourceName         = 'R'
                    ResourceInstanceName = 'I'
                    Settings             = @(
                        @{ CIMInstance = 'DSCPT_TestSetting'; Name = 'A' }
                        @{ CIMInstance = 'DSCPT_TestSetting'; Name = 'B' }
                    )
                }
            )

            ([regex]::Matches($rendered, 'DSCPT_TestSetting\{')).Count | Should -Be 2
        }

        It 'Should omit a property whose value is null' {
            $rendered = ConvertFrom-DSCObject -DSCResources @(
                @{ ResourceName = 'R'; ResourceInstanceName = 'I'; Path = 'a'; Ensure = $null }
            )

            $rendered | Should -Not -Match 'Ensure'
        }
    }

    Context 'When values need escaping' {
        It 'Should produce text that parses without errors' {
            $rendered = ConvertFrom-DSCObject -DSCResources @(
                @{
                    ResourceName         = 'R'
                    ResourceInstanceName = 'I'
                    Quotes               = 'a "quoted" value'
                    Dollar               = 'costs $100'
                    Backtick             = 'a `backtick`'
                }
            )

            $parseErrors = $null
            $null = [System.Management.Automation.Language.Parser]::ParseInput($rendered, [ref]$null, [ref]$parseErrors)

            $parseErrors | Should -BeNullOrEmpty
        }
    }

    Context 'When a child level is supplied' {
        It 'Should indent the block by four spaces per level' {
            $rendered = ConvertFrom-DSCObject -ChildLevel 1 -DSCResources @(
                @{ ResourceName = 'R'; ResourceInstanceName = 'I'; Path = 'a' }
            )

            $rendered | Should -Match '(?m)^    '
        }

        It 'Should render the resource name as a property above child level zero' {
            $rendered = ConvertFrom-DSCObject -ChildLevel 1 -DSCResources @(
                @{ ResourceName = 'R'; ResourceInstanceName = 'I'; Path = 'a' }
            )

            $rendered | Should -Match 'ResourceName\s+= "R"'
        }
    }

    Context 'When resources arrive over the pipeline' {
        It 'Should return the same text as passing them as a parameter' {
            $resources = @(
                @{ ResourceName = 'R'; ResourceInstanceName = 'One'; Path = 'a' }
                @{ ResourceName = 'R'; ResourceInstanceName = 'Two'; Path = 'b' }
            )

            $piped = $resources | ConvertFrom-DSCObject
            $direct = ConvertFrom-DSCObject -DSCResources $resources

            $piped | Should -Be $direct
        }

        It 'Should accumulate every pipeline item into a single string' {
            $piped = @(
                @{ ResourceName = 'R'; ResourceInstanceName = 'One'; Path = 'a' }
                @{ ResourceName = 'R'; ResourceInstanceName = 'Two'; Path = 'b' }
            ) | ConvertFrom-DSCObject

            $piped | Should -BeOfType [System.String]
            ([regex]::Matches($piped, 'R "')).Count | Should -Be 2
        }
    }

    Context 'When a resource carries no resource name' {
        It 'Should render an anonymous block for an empty hashtable' {
            ConvertFrom-DSCObject -DSCResources @(@{}) | Should -Match '^@\{\r?\n\}'
        }

        It 'Should render the properties without a resource header' {
            $rendered = ConvertFrom-DSCObject -DSCResources @(@{ ResourceInstanceName = 'I'; Path = 'a' })

            $rendered | Should -Match '^@\{'
            $rendered | Should -Match 'Path\s+= "a"'
        }
    }

    Context 'When the input is empty' {
        It 'Should reject an empty array on the parameter because it is mandatory' {
            { ConvertFrom-DSCObject -DSCResources @() } | Should -Throw -ExpectedMessage '*empty array*'
        }

        It 'Should return an empty string when nothing arrives over the pipeline' {
            @() | ConvertFrom-DSCObject | Should -Be ''
        }
    }
}
