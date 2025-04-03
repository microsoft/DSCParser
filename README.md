
# DSC Parser

Receives a DSC configuration script (.ps1) as an input, and parses all containes resources and properties into logical PSObject. Usage scenario include: analyzing DSC config for best practices, getting quick stats out of a config, etc.

## Installation

DSCParser is available from the PowerShell Gallery, simply run:

```powershell
Install-Module DSCParser
```

## Usage

Parsing a DSC File with a single configuration item and the comments:
```powershell
$DSCObjects = ConvertTo-DSCObject -Path $PSScriptRoot\..\Tests\Templates\Template1.ps1 -IncludeComments $true
$DSCObjects | Format-Table
```

Parsing a DSC File with a multiple configuration item without the comments:

```powershell
$ast = [System.Management.Automation.Language.Parser]::ParseFile('$PSScriptRoot\..\Tests\Templates\Template2.ps1', [ref]$null, [ref]$null)
$configurations = $ast.FindAll({ $args[0] -is [System.Management.Automation.Language.ConfigurationDefinitionAst] }, $false)
$DSCObjects = $configurations.Extent.text | ForEach-Object {
    ConvertTo-DSCObject -Content $_
}
$DSCObjects | Format-Table
```
