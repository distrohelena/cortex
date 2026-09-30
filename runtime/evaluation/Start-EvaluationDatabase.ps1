param(
    [Parameter()]
    [string]$CredentialRef
)

. (Join-Path $PSScriptRoot 'EvaluationDatabase.Common.ps1')

try {
    if ([string]::IsNullOrWhiteSpace($CredentialRef)) {
        $result = New-EvaluationInstance
    } else {
        $instance = Read-EvaluationInstance $CredentialRef
        $result = Start-ExistingEvaluationInstance $instance
    }
    Write-Output (New-EvaluationDatabaseOutput $result)
    exit 0
} catch {
    [Console]::Error.WriteLine("Evaluation database start failed: $($_.Exception.Message)")
    exit 1
}
