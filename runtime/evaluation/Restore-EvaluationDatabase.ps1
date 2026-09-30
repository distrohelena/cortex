param(
    [Parameter(Mandatory = $true)]
    [string]$BackupPath
)

. (Join-Path $PSScriptRoot 'EvaluationDatabase.Common.ps1')

try {
    $source = [IO.Path]::GetFullPath($BackupPath)
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw 'Backup file does not exist.' }
    if ((Get-Item -LiteralPath $source).Length -le 0) { throw 'Backup file is empty.' }

    $instanceResult = New-EvaluationInstance
    $instance = Read-EvaluationInstance $instanceResult.credentialRef
    Assert-EvaluationRestoreTarget $instance.Metadata
    Invoke-EvaluationSqlBinary $instance.Metadata.containerRef `
        @('pg_restore', '--exit-on-error', '-U', 'evaluation_owner', '-d', 'evaluation') `
        -InputPath $source

    $check = Invoke-EvaluationDocker @('exec', '--user', 'postgres', $instance.Metadata.containerRef,
        'psql', '-X', '-v', 'ON_ERROR_STOP=1', '-A', '-t', '-U', 'evaluation_admin', '-d', 'evaluation', '-c', 'SELECT 1;')
    if ($check.Trim() -ne '1') { throw 'Restored database failed its post-restore readiness query.' }

    Write-Output (New-EvaluationDatabaseOutput $instanceResult)
    exit 0
} catch {
    [Console]::Error.WriteLine("Evaluation database restore failed: $($_.Exception.Message)")
    exit 1
}
