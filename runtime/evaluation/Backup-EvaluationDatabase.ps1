param(
    [Parameter(Mandatory = $true)]
    [string]$CredentialRef,
    [Parameter(Mandatory = $true)]
    [string]$Destination
)

. (Join-Path $PSScriptRoot 'EvaluationDatabase.Common.ps1')

try {
    $instance = Read-EvaluationInstance $CredentialRef
    $metadata = $instance.Metadata
    $container = Get-EvaluationContainer $metadata
    if ($container.State.Status -ne 'running' -or $container.State.Health.Status -ne 'healthy') {
        throw 'Backup requires the owned evaluation database to be running and healthy.'
    }
    $destinationPath = [IO.Path]::GetFullPath($Destination)
    if (Test-Path -LiteralPath $destinationPath) { throw 'Backup destination already exists; overwrite is refused.' }
    $parent = Split-Path -Parent $destinationPath
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) { throw 'Backup destination directory does not exist.' }

    Invoke-EvaluationSqlBinary $metadata.containerRef @('pg_dump', '-U', 'evaluation_owner', '-d', 'evaluation', '-Fc') `
        -OutputPath $destinationPath
    $hash = (Get-FileHash -LiteralPath $destinationPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Output (([ordered]@{ backupRef = $destinationPath; sha256 = $hash }) | ConvertTo-Json -Compress)
    exit 0
} catch {
    [Console]::Error.WriteLine("Evaluation database backup failed: $($_.Exception.Message)")
    exit 1
}
