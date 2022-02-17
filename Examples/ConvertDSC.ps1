Clear-Host
Import-Module .\DSCParser.psd1 -Force

$ProgressPreference = 'SilentlyContinue'

$DSCObjects = ConvertTo-DSCObject -Path $PSScriptRoot\..\Tests\Templates\Template1.ps1 -IncludeComments $true
$DSCObjects | Format-Table

$DSCObjectsEmpty = ConvertTo-DSCObject -Path $PSScriptRoot\..\Tests\Templates\Template2.ps1 -IncludeComments $true
$DSCObjectsEmpty | Format-Table

#$Test | Out-HtmlView -ScrollX -AllProperties -FlattenObject