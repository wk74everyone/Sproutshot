param([switch]$WriteMigration)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$exportArguments = @((Join-Path $PSScriptRoot 'export-economy-config.cjs'))
if ($WriteMigration) { $exportArguments += '--write-migration' }
& node @exportArguments
if ($LASTEXITCODE -ne 0) { throw 'Economy config export failed.' }
