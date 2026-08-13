Configuration UnresolvedResourceConfiguration
{
    Import-DscResource -ModuleName 'DscParserTest.MofResources'

    Node localhost
    {
        DscParserTestRemovedResource RemovedInstance
        {
            Path   = 'C:\DscParserTests\removed.txt'
            Ensure = 'Present'
        }

        DscParserTestFile SurvivingFile
        {
            Path     = 'C:\DscParserTests\surviving.txt'
            Contents = 'still here'
        }
    }
}
