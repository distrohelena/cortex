function Invoke-WithJevCredential {
    param([Parameter(Mandatory=$true)][string]$CredentialFile, [Parameter(Mandatory=$true)][scriptblock]$Action)
    $taskPointer = [IntPtr]::Zero
    $taskSecure = $null
    $taskPreviousKey = $env:TYPESAFE_API_KEY
    try {
        $taskSecure = ConvertTo-SecureString ([IO.File]::ReadAllText([IO.Path]::GetFullPath($CredentialFile)).Trim())
        $taskPointer = [Runtime.InteropServices.Marshal]::SecureStringToGlobalAllocUnicode($taskSecure)
        $env:TYPESAFE_API_KEY = [Runtime.InteropServices.Marshal]::PtrToStringUni($taskPointer)
        [Runtime.InteropServices.Marshal]::ZeroFreeGlobalAllocUnicode($taskPointer)
        $taskPointer = [IntPtr]::Zero
        if ([string]::IsNullOrWhiteSpace($env:TYPESAFE_API_KEY)) { throw 'Credencial Jev indisponível.' }
        & $Action
    } finally {
        if ($taskPointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeGlobalAllocUnicode($taskPointer) }
        if ($taskSecure) { $taskSecure.Dispose() }
        $env:TYPESAFE_API_KEY = $taskPreviousKey
    }
}
