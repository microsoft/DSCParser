enum DscParserTestEnsure
{
    Present
    Absent
}

class DscParserTestClassOption
{
    [DscProperty(Key)]
    [System.String]
    $Name

    [DscProperty()]
    [System.String]
    $Value
}

[DscResource()]
class DscParserTestClassApp
{
    [DscProperty(Key)]
    [System.String]
    $AppName

    [DscProperty()]
    [System.String]
    $DisplayName

    [DscProperty()]
    [DscParserTestEnsure]
    $Ensure

    [DscProperty()]
    [System.Int32]
    $InstanceCount

    [DscProperty()]
    [System.String[]]
    $Features

    [DscProperty()]
    [DscParserTestClassOption[]]
    $Options

    [DscParserTestClassApp] Get()
    {
        return $this
    }

    [System.Boolean] Test()
    {
        return $true
    }

    [void] Set()
    {
        throw 'DscParserTestClassApp is a parsing fixture and cannot be applied.'
    }
}
