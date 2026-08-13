Configuration ValidConfiguration
{
    Import-DscResource -ModuleName 'DscParserTest.MofResources' -ModuleVersion '1.0.0'

    Node localhost
    {
        DscParserTestFile FirstFile
        {
            Path     = 'C:\DscParserTests\first.txt'
            Contents = 'first'
            Ensure   = 'Present'
            Force    = $true
            Retries  = 3
            Tags     = @('alpha', 'beta')
        }

        DscParserTestFile SecondFile
        {
            Path   = 'C:\DscParserTests\second.txt'
            Ensure = 'Absent'
        }
    }
}
