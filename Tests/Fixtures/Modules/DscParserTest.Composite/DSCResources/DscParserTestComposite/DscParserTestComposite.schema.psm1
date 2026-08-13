Configuration DscParserTestComposite
{
    param
    (
        [Parameter(Mandatory = $true)]
        [System.String]
        $SiteName,

        [Parameter()]
        [System.Int32]
        $Port,

        [Parameter()]
        [System.String[]]
        $Bindings
    )

    Import-DscResource -ModuleName 'DscParserTest.MofResources'

    Node localhost
    {
        DscParserTestFile CompositeFile
        {
            Path     = "C:\Sites\$SiteName.txt"
            Contents = $SiteName
            Ensure   = 'Present'
        }
    }
}
