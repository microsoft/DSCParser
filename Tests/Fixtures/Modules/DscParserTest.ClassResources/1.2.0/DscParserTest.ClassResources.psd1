@{
    RootModule           = 'DscParserTest.ClassResources.psm1'
    ModuleVersion        = '1.2.0'
    GUID                 = '2b8e4d90-9a7f-4c31-8f52-6d3a1e7b4c22'
    Author               = 'Microsoft365DSC Team'
    CompanyName          = 'DSCParser Test Fixtures'
    Copyright            = '(c) 2026 Microsoft365DSC'
    Description          = 'Class based DSC resources used by the DSCParser test suite.'
    PowerShellVersion    = '5.1'
    FunctionsToExport    = @()
    CmdletsToExport      = @()
    DscResourcesToExport = @('DscParserTestClassApp')
}
