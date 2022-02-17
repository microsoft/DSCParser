Describe 'DSC ConvertTo-DSCObject' {

    $Path = "$PSScriptRoot\Templates\Template1.ps1"

    It 'Objects should match expectation for Template 1' -TestCases @{ Path = $Path } {
        $DSCObjects = ConvertTo-DSCObject -Path $Path -IncludeComments $true

        $DSCObjects.Count | Should -Be 4

        $PropertyNames1 = @(
            'ResourceName'
            'ResourceID'
            'ApplicationEnforcedRestrictionsIsEnabled'
            'BuiltInControls'
            'ClientAppTypes'
            'CloudAppSecurityIsEnabled'
            'CloudAppSecurityType'
            'Credential'
            'DisplayName'
            'Ensure'
            'ExcludeApplications'
            'ExcludeDevices'
            'ExcludeGroups'
            'ExcludeLocations'
            'ExcludePlatforms'
            'ExcludeRoles'
            'ExcludeUsers'
            'GrantControlOperator'
            'Id'
            'IncludeApplications'
            'IncludeDevices'
            'IncludeGroups'
            'IncludeLocations'
            'IncludePlatforms'
            'IncludeRoles'
            'IncludeUserActions'
            'IncludeUsers'
            'PersistentBrowserIsEnabled'
            'PersistentBrowserMode'
            'SignInFrequencyIsEnabled'
            'SignInFrequencyType'
            'SignInRiskLevels'
            'State'
            'UserRiskLevels'
        )
        $DSCObjects[0].Keys | Should -Be $PropertyNames1

        $DSCObjects[0].ResourceName | Should -Be 'AADConditionalAccessPolicy'
        $DSCObjects[0].BuiltInControls | Should -Be @('block')
        $DSCObjects[0].ClientAppTypes | Should -Be @('exchangeActiveSync', 'other')
        $DSCObjects[0].CloudAppSecurityIsEnabled | Should -Be '$false'
        $DSCObjects[0].Credential | Should -Be '$Credscredential'
        $DSCObjects[0].ExcludeDevices | Should -Be @()
        $DSCObjects[0].IncludeUsers | Should -Be @('All')

        $PropertyNames2 = @(
            'ResourceName'
            'ResourceID'
            'Credential'
            'CustomBlockedWordsList'
            'Ensure'
            'IsSingleInstance'
            'PrefixSuffixNamingRequirement'
        )
        $DSCObjects[1].Keys | Should -Be $PropertyNames2

        $PropertyNames3 = @(
            'ResourceName'
            'ResourceID'
            'AdvancedSettings'
            'Comment'
            'Credential'
            'Ensure'
            'ExchangeLocation'
            'Labels'
            'Name'
        )
        $DSCObjects[2].Keys | Should -Be $PropertyNames3


        $DSCObjects[2].AdvancedSettings.GetType().Name | Should -Be 'Object[]'
        $DSCObjects[2].AdvancedSettings.Count | Should -Be 8

        for ($i = 0; $i -lt $DSCObjects[2].AdvancedSettings.Count; $i++) {
            $DSCObjects[2].AdvancedSettings[$i].Keys | Should -Be @('CIMInstance','Key', 'Value')
            $DSCObjects[2].AdvancedSettings[$i].CIMInstance | Should -Be 'MSFT_SCLabelSetting'
        }

        $DSCObjects[2].AdvancedSettings[0].Key | Should -Be 'requiredowngradejustification'
        $DSCObjects[2].AdvancedSettings[0].Key | Should -Be $true # should it be $true or string "$true" as in case first level object

        $DSCObjects[2].Comment | Should -Be ""
        $DSCObjects[2].Ensure | Should -Be "Present"

        $PropertyNames4 = @(
            'ResourceName'
            'ResourceID'
            'AdvancedSettings'
            'Comment'
            'Credential'
            'Disabled'
            'DisplayName'
            'Ensure'
            'LocaleSettings'
            'Name'
            'Priority'
            'Tooltip'
        )
        $DSCObjects[3].Keys | Should -Be $PropertyNames4
    }

    $Path = "$PSScriptRoot\Templates\Template2.ps1"

    It 'Objects should match expectation for Template 2' -TestCases @{ Path = $Path } {
        $DSCObjects = ConvertTo-DSCObject -Path $Path -IncludeComments $false

        $DSCObjects.Count | Should -Be 0
    }
}