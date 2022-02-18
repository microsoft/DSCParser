Clear-Host
Import-Module .\DSCParser.psd1 -Force

$ProgressPreference = 'SilentlyContinue'

$DSCObjects = ConvertTo-DSCObject -Path $PSScriptRoot\Test.ps1 -IncludeComments $true
$DSCObjects | Format-Table