param([string]$Graph, [string]$Entry, [string]$Settings, [int]$Port = 0, [switch]$NoOpen, [string]$CredentialFile, [switch]$Use, [string]$ApplicationRoot, [string]$PublicOrigin)
$ErrorActionPreference = 'Stop'
$taskProfile = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Cortex/editor'
$taskInstallation = Get-Content -LiteralPath (Join-Path $taskProfile 'installation.json') -Raw | ConvertFrom-Json
$taskArguments = @($taskInstallation.editorPath)
if ($Graph) { $taskArguments += @('--graph', [System.IO.Path]::GetFullPath($Graph)) }
if ($Entry) { $taskArguments += @('--entry', $Entry) }
if ($Settings) { $taskArguments += @('--settings', [System.IO.Path]::GetFullPath($Settings)) }
if ($Port) { $taskArguments += @('--port', [string]$Port) }
if ($ApplicationRoot) { $taskArguments += @('--application-root', [IO.Path]::GetFullPath($ApplicationRoot)) }
if ($PublicOrigin) { $taskArguments += @('--public-origin', $PublicOrigin) }
if ($Use) { $taskArguments += '--use' }
if ($NoOpen) { $taskArguments += '--no-open' }
if ($CredentialFile) {
    . (Join-Path $PSScriptRoot 'jev-credential.ps1')
    Invoke-WithJevCredential -CredentialFile $CredentialFile -Action { & dotnet @taskArguments }
} else { & dotnet @taskArguments }
exit $LASTEXITCODE
