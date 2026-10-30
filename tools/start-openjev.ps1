<#
.SYNOPSIS
Runs a pinned local llama-server for the openjev provider, outside the tests.

.DESCRIPTION
Reads config/local-models.json (the same pins as the LocalModels tests), downloads anything missing into the
same cache (CORTEX_LOCAL_MODELS_DIR, or %LOCALAPPDATA%\Cortex\local-models), checks every SHA-256, and runs
llama-server in the foreground on loopback. Stop it with Ctrl+C.

.EXAMPLE
powershell -File tools/start-openjev.ps1 -Model qwen3-0.6b -Port 8080
#>
param(
    [string]$Model = "minicpm5-2b",
    [ValidateRange(1, 65535)][int]$Port = 8080
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$repository = Split-Path -Parent $PSScriptRoot
$manifest = Get-Content -Raw -LiteralPath (Join-Path $repository "config\local-models.json") | ConvertFrom-Json
$pin = $manifest.gguf.$Model
if ($null -eq $pin) {
    throw "Unknown model '$Model'. Pinned: $(($manifest.gguf.PSObject.Properties | ForEach-Object Name) -join ', ')."
}

$root = $env:CORTEX_LOCAL_MODELS_DIR
if ([string]::IsNullOrWhiteSpace($root)) { $root = Join-Path $env:LOCALAPPDATA "Cortex\local-models" }
New-Item -ItemType Directory -Force -Path $root | Out-Null

function Get-Sha256([string]$Path) { (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant() }

# Download to *.partial, verify, then rename. A mismatch deletes the partial and fails, like LocalModelCache.
function Get-PinnedFile([string]$Url, [string]$Sha256, [string]$Path) {
    if (Test-Path -LiteralPath $Path) {
        if ((Get-Sha256 $Path) -eq $Sha256) { return $Path }
        Remove-Item -LiteralPath $Path -Force
    }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Path) | Out-Null
    $partial = "$Path.partial"
    try {
        Write-Host "Downloading $Url"
        Invoke-WebRequest -UseBasicParsing -Uri $Url -OutFile $partial
        $actual = Get-Sha256 $partial
        if ($actual -ne $Sha256) { throw "SHA-256 mismatch for ${Url}: expected $Sha256, downloaded $actual. The partial download was deleted." }
        Move-Item -LiteralPath $partial -Destination $Path -Force
        return $Path
    }
    finally {
        if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial -Force }
    }
}

# Same cross-process lock file as LocalModelCache, so the script and running tests never write over each other.
$lockPath = Join-Path $root ".lock"
$lock = $null
while ($null -eq $lock) {
    try { $lock = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None) }
    catch [IO.IOException] { Start-Sleep -Milliseconds 250 }
}
try {
    $llama = Join-Path $root "llama.cpp"
    $server = Join-Path $llama "llama-server.exe"
    $stamp = Join-Path $llama ".tag"
    $current = if (Test-Path -LiteralPath $stamp) { (Get-Content -Raw -LiteralPath $stamp).Trim() } else { "" }
    if (-not (Test-Path -LiteralPath $server) -or $current -ne $manifest.llamaCpp.tag) {
        if (Test-Path -LiteralPath $llama) { Remove-Item -LiteralPath $llama -Recurse -Force }
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        foreach ($asset in $manifest.llamaCpp.assets) {
            $zip = Get-PinnedFile $asset.url $asset.sha256 (Join-Path $llama ([Uri]$asset.url).Segments[-1])
            [IO.Compression.ZipFile]::ExtractToDirectory($zip, $llama)
            Remove-Item -LiteralPath $zip -Force
        }
        if (-not (Test-Path -LiteralPath $server)) { throw "llama.cpp $($manifest.llamaCpp.tag) assets did not contain llama-server.exe at the archive root." }
        [IO.File]::WriteAllText($stamp, $manifest.llamaCpp.tag)
    }

    $gguf = Get-PinnedFile "https://huggingface.co/$($pin.repo)/resolve/$($pin.revision)/$($pin.file)" $pin.sha256 (Join-Path $root "models\$($pin.file)")
}
finally {
    $lock.Dispose()
}

Write-Host "Endpoint: http://127.0.0.1:$Port/  Model: $Model"
& $server -m $gguf --alias $Model --host 127.0.0.1 --port $Port -ngl 999 -c 2048
exit $LASTEXITCODE
