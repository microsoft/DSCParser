# Generated with Microsoft365DSC version 1.22.105.1
# For additional information on how to use Microsoft365DSC, please visit https://aka.ms/M365DSC
param (
    [parameter()]
    [System.Management.Automation.PSCredential]
    $Credential
)

Configuration M365TenantConfig
{
    param (
        [parameter()]
        [System.Management.Automation.PSCredential]
        $Credential
    )

    if ($null -eq $Credential)
    {
        <# Credentials #>
        $Credscredential = Get-Credential -Message "Credentials"
        $Credscertificatepassword = Get-Credential -Message "Credentials"

    }
    else
    {
        $CredsCredential = $Credential
    }

    $OrganizationName = $CredsCredential.UserName.Split('@')[1]
    Import-DscResource -ModuleName 'Microsoft365DSC' #-ModuleVersion '1.22.105.1'

    Node localhost
    {
        AADConditionalAccessPolicy 706192c4-1a75-465c-9592-479a9e90858e
        {
            ApplicationEnforcedRestrictionsIsEnabled = $False;
            BuiltInControls                          = @("block");
            ClientAppTypes                           = @("exchangeActiveSync","other");
            CloudAppSecurityIsEnabled                = $False;
            CloudAppSecurityType                     = "";
            Credential                               = $Credscredential;
            DisplayName                              = "All - Deny Basic authentication";
            Ensure                                   = "Present";
            ExcludeApplications                      = @();
            ExcludeDevices                           = @();
            ExcludeGroups                            = @();
            ExcludeLocations                         = @();
            ExcludePlatforms                         = @();
            ExcludeRoles                             = @();
            ExcludeUsers                             = @("admin@$OrganizationName");
            GrantControlOperator                     = "OR";
            Id                                       = "77000763-8b9e-485a-8cfe-735b2bde5f50";
            IncludeApplications                      = @("All");
            IncludeDevices                           = @();
            IncludeGroups                            = @();
            IncludeLocations                         = @();
            IncludePlatforms                         = @();
            IncludeRoles                             = @();
            IncludeUserActions                       = @();
            IncludeUsers                             = @("All");
            PersistentBrowserIsEnabled               = $False;
            PersistentBrowserMode                    = "";
            SignInFrequencyIsEnabled                 = $False;
            SignInFrequencyType                      = "";
            SignInRiskLevels                         = @();
            State                                    = "enabled";
            UserRiskLevels                           = @();
        }
        AADGroupsNamingPolicy 3a126f61-1632-4923-89b7-bfce0936d8b4
        {
            Credential                    = $Credscredential;
            CustomBlockedWordsList        = @();
            Ensure                        = "Present";
            IsSingleInstance              = "Yes";
            PrefixSuffixNamingRequirement = "O365_[GroupName]";
        }
        SCLabelPolicy 99ef4d19-e250-4009-9a4e-70659fe2a34a
        {
            AdvancedSettings     = @(
                MSFT_SCLabelSetting
                {
                    Key   = 'requiredowngradejustification'
                    Value = 'True'
                }
                MSFT_SCLabelSetting
                {
                    Key   = 'customurl'
                    Value = 'https://efsz.sharepoint.com/sites/EurofinsSupportZone/SitePages/Welcome-to-Unified-Label-Classification.aspx'
                }
                MSFT_SCLabelSetting
                {
                    Key   = 'siteandgroupmandatory'
                    Value = 'true'
                }
                MSFT_SCLabelSetting
                {
                    Key   = 'mandatory'
                    Value = 'true'
                }
                MSFT_SCLabelSetting
                {
                    Key   = 'outlookdefaultlabel'
                    Value = 'none'
                }
                MSFT_SCLabelSetting
                {
                    Key   = 'disablemandatoryinoutlook'
                    Value = 'true'
                }
                MSFT_SCLabelSetting
                {
                    Key   = 'siteandgroupdefaultlabelid'
                    Value = '4fee205e-ae73-42b0-96e5-0a59f6f532ac'
                }
                MSFT_SCLabelSetting
                {
                    Key   = 'defaultlabelid'
                    Value = '4fee205e-ae73-42b0-96e5-0a59f6f532ac'
                }
            );
            Comment              = "";
            Credential           = $Credscredential;
            Ensure               = "Present";
            ExchangeLocation     = "All";
            Labels               = @("Confidential","Secret","Public","Eurofins Internal");
            Name                 = "Default Label Policy";
        }
        SCSensitivityLabel f4877aa6-cbc0-4ec5-b3ef-c35ba7581fd6
        {
            AdvancedSettings     = @(
                MSFT_SCLabelSetting
                {
                    Key   = 'contenttype'
                    Value = 'File  Email  Site  UnifiedGroup'
                }
                MSFT_SCLabelSetting
                {
                    Key   = 'tooltip'
                    Value = 'Information intended to be made public and formatted as such'
                }
            );
            Comment              = "";
            Credential           = $Credscredential;
            Disabled             = $False;
            DisplayName          = "Public";
            Ensure               = "Present";
            LocaleSettings       = @(
                MSFT_SCLabelLocaleSettings
                {
                    LocaleKey = 'displayName'
                    Settings  = @(
                        MSFT_SCLabelSetting
                        {
                            Key   = 'default'
                            Value = 'Public'
                        }
                    )
                }
                MSFT_SCLabelLocaleSettings
                {
                    LocaleKey = 'tooltip'
                    Settings  = @(
                        MSFT_SCLabelSetting
                        {
                            Key   = 'default'
                            Value = 'Information intended to be made public and formatted as such'
                        }
                    )
                }
            );
            Name                 = "Public";
            Priority             = 0;
            Tooltip              = "Information intended to be made public and formatted as such";
        }
    }
}