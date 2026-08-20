Configuration NestedCimConfiguration
{
    Import-DscResource -ModuleName 'DscParserTest.MofResources'

    Node localhost
    {
        DscParserTestCimHost PrimaryHost
        {
            Name           = 'Primary'
            Description    = 'The primary host'
            DefaultSetting = DSCPT_TestSetting
            {
                Name     = 'Timeout'
                Value    = '30'
                Enforced = $true
            }
            Settings       = @(
                DSCPT_TestSetting
                {
                    Name     = 'Retries'
                    Value    = '5'
                    Enforced = $false
                }
                DSCPT_TestSetting
                {
                    Name     = 'Region'
                    Value    = 'westeurope'
                    Enforced = $true
                }
            )
        }
    }
}
