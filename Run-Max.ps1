#requires -Version 7.4
# Uses PowerShell's bundled C# compiler. This runs the current source directly.
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$MaxArguments)
$ErrorActionPreference = 'Stop'
$sources = Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' |
    Where-Object Name -ne 'Compiler.cs' |
    Select-Object -ExpandProperty FullName
Add-Type -Path $sources -CompilerOptions '/langversion:12', '/nullable:enable'
[Max.Studio]::SourceDirectory = $PSScriptRoot
exit [Max.Program]::Main($MaxArguments)
