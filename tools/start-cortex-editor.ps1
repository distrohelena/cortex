param([string]$Graph, [string]$Entry, [string]$Settings, [int]$Port = 0, [switch]$NoOpen)
$ErrorActionPreference = 'Stop'
$taskProfile = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Cortex/editor'
$taskInstallation = Get-Content -LiteralPath (Join-Path $taskProfile 'installation.json') -Raw | ConvertFrom-Json
$taskArguments = @($taskInstallation.editorPath)
if ($Graph) { $taskArguments += @('--graph', [System.IO.Path]::GetFullPath($Graph)) }
if ($Entry) { $taskArguments += @('--entry', $Entry) }
if ($Settings) { $taskArguments += @('--settings', [System.IO.Path]::GetFullPath($Settings)) }
if ($Port) { $taskArguments += @('--port', [string]$Port) }
if ($NoOpen) { $taskArguments += '--no-open' }
& dotnet @taskArguments
exit $LASTEXITCODE
