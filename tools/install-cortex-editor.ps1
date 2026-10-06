param([string]$ProfileDirectory, [switch]$ConfigureDemo)
$ErrorActionPreference = 'Stop'
$taskRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $ProfileDirectory) { $ProfileDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Cortex/editor' }
$taskProfile = [System.IO.Path]::GetFullPath($ProfileDirectory)
$taskGeneration = 'install-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$taskInstall = Join-Path $taskProfile $taskGeneration
& dotnet publish (Join-Path $taskRoot 'src/Cortex.Editor/Cortex.Editor.csproj') -c Release -o $taskInstall
if ($LASTEXITCODE -ne 0) { throw 'Falha ao publicar o editor.' }
& dotnet build (Join-Path $taskRoot 'src/Cortex.Video/Cortex.Video.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o plugin de vídeo.' }
$taskPlugin = Join-Path $taskInstall 'plugins/video'
New-Item -ItemType Directory -Force -Path $taskPlugin | Out-Null
foreach ($taskFile in @('Cortex.Video.dll', 'Cortex.Video.deps.json', 'Cortex.Inference.dll', 'cortex-video.plugin.json')) {
    Copy-Item -LiteralPath (Join-Path $taskRoot "src/Cortex.Video/bin/Release/net10.0/$taskFile") -Destination (Join-Path $taskPlugin $taskFile)
}
$taskSchemas = Join-Path $taskPlugin 'schemas'
New-Item -ItemType Directory -Force -Path $taskSchemas | Out-Null
Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src/Cortex.Video/bin/Release/net10.0/Editing/Schemas') -Filter '*.schema.json' -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $taskSchemas }
$taskUtf8 = New-Object System.Text.UTF8Encoding($false)
function Write-AtomicJson([string]$Path, $Value) {
    $taskTemporary = $Path + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    [IO.File]::WriteAllText($taskTemporary, ($Value | ConvertTo-Json -Depth 32), $taskUtf8)
    if ([IO.File]::Exists($Path)) { [IO.File]::Replace($taskTemporary, $Path, $Path + '.previous-' + [Guid]::NewGuid().ToString('N') + '.json') }
    else { [IO.File]::Move($taskTemporary, $Path) }
}
Write-AtomicJson (Join-Path $taskProfile 'installation.json') ([ordered]@{schemaVersion=1;editorPath=(Join-Path $taskInstall 'Cortex.Editor.dll');pluginDirectory=(Join-Path $taskInstall 'plugins')})
$taskSettingsPath = Join-Path $taskProfile 'settings.json'
if (-not (Test-Path -LiteralPath $taskSettingsPath)) {
    Write-AtomicJson $taskSettingsPath ([ordered]@{schemaVersion=1;plugins=@([ordered]@{id='video';enabled=$false;configuration=[ordered]@{whisperExecutable='';modelFile='';ffmpegExecutable='ffmpeg';ffprobeExecutable='ffprobe'}})})
}
if ($ConfigureDemo) {
    $taskSettings = Get-Content -LiteralPath $taskSettingsPath -Raw | ConvertFrom-Json
    if ($taskSettings.schemaVersion -ne 1) { throw 'Versão de settings incompatível; perfil preservado.' }
    $taskEntry = @($taskSettings.plugins) | Where-Object { $_.id -eq 'video' } | Select-Object -First 1
    if (-not $taskEntry) {
        $taskEntry = [PSCustomObject]@{id='video';enabled=$false;configuration=[PSCustomObject]@{}}
        $taskSettings.plugins = @($taskSettings.plugins) + @($taskEntry)
    }
    $taskEntry.enabled = $true
    $taskMediaTools = [System.IO.Path]::GetFullPath((Join-Path $taskRoot '../depois-do-slogan/.local/tools'))
    $taskEntry.configuration | Add-Member -NotePropertyName whisperExecutable -NotePropertyValue (Join-Path $taskMediaTools 'whisper-v1.9.2-cuda12/Release/whisper-cli.exe') -Force
    $taskEntry.configuration | Add-Member -NotePropertyName modelFile -NotePropertyValue (Join-Path $taskMediaTools 'ggml-small.bin') -Force
    $taskEntry.configuration | Add-Member -NotePropertyName ffmpegExecutable -NotePropertyValue 'ffmpeg' -Force
    $taskEntry.configuration | Add-Member -NotePropertyName ffprobeExecutable -NotePropertyValue 'ffprobe' -Force
    Write-AtomicJson $taskSettingsPath $taskSettings
}
Write-Output "Editor instalado: $taskInstall"
Write-Output "Settings compartilhadas: $taskSettingsPath"
