Configuration M365TenantConfig
{
    Import-DscResource -ModuleName 'Microsoft365DSC'

    Node localhost
    {
        SCLabelPolicy 99ef4d19-e250-4009-9a4e-70659fe2a34a {
            # This should be comment
            AdvancedSettings = @(
                # This should be comment
                MSFT_SCLabelSetting {
                    Key   = 'requiredowngradejustification'
                    Value = $null
                }
                MSFT_SCLabelSetting {
                    Key   = 'customurl'
                    Value = $true
                }
            );
            Credential       = $Credscredential;
            Ensure           = $true
            ExchangeLocation =$null
            Labels           = @("Confidential", $true, $null, "Eurofins Internal");
            Name             = "Default Label Policy";
        }
    }
}