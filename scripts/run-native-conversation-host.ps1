param(
  [Parameter(Mandatory=$true)][string]$OperationsRoot,
  [Parameter(Mandatory=$true)][string]$HostConfig,
  [Parameter(Mandatory=$true)][string]$CredentialFile,
  [Parameter(Mandatory=$true)][string]$MemoryConfig,
  [Parameter(Mandatory=$true)][string]$AppFile,
  [Parameter(Mandatory=$true)][ValidatePattern('^conversation-host-running(?:-[0-9]+)?$')][string]$RunName
)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$ops=[IO.Path]::GetFullPath($OperationsRoot)
$run=Join-Path $ops $RunName
if(Test-Path -LiteralPath $run){throw 'Refuse ambiguous runner restart; use a new evidence directory'}
$others=@(Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" | Where-Object {$_.CommandLine -like '*Filomena.App.dll*'})
if($others.Count){throw 'Existing Filomena worker; drain before replacement'}
$ptr=[IntPtr]::Zero
$secure=$null
$priorTypesafe=$env:TYPESAFE_API_KEY
$priorReader=$env:FILOMENA_NATIVE_OBSERVATION_READER_TOKEN
$priorSuppression=$env:OPENCLAW_GRAPHITI_SUPPRESS_EXTERNAL_STARTUP_PROBES
$priorMemory=$env:FILOMENA_CONVERSATION_MEMORY_FILE
try {
  if(!(Test-Path -LiteralPath $MemoryConfig) -or !(Test-Path -LiteralPath $AppFile)){throw 'Required memory configuration or approved app binary missing'}
  $env:FILOMENA_CONVERSATION_MEMORY_FILE=[IO.Path]::GetFullPath($MemoryConfig)
  $secure=ConvertTo-SecureString ([IO.File]::ReadAllText($CredentialFile))
  $ptr=[Runtime.InteropServices.Marshal]::SecureStringToGlobalAllocUnicode($secure)
  $env:TYPESAFE_API_KEY=[Runtime.InteropServices.Marshal]::PtrToStringUni($ptr)
  [Runtime.InteropServices.Marshal]::ZeroFreeGlobalAllocUnicode($ptr);$ptr=[IntPtr]::Zero
  $native=Get-Content -LiteralPath (Join-Path $ops 'observation-host.next.json') -Raw | ConvertFrom-Json
  $env:FILOMENA_NATIVE_OBSERVATION_READER_TOKEN=$native.readerToken
  $env:OPENCLAW_GRAPHITI_SUPPRESS_EXTERNAL_STARTUP_PROBES='true'
  if(!$env:OPENAI_API_KEY -or !$env:TYPESAFE_API_KEY -or !$env:FILOMENA_NATIVE_OBSERVATION_READER_TOKEN){throw 'Required existing credential unavailable'}
  New-Item -ItemType Directory -Path $run | Out-Null
  $dotnet=(Get-Command dotnet.exe).Source
  & node (Join-Path $repo 'tools/native-conversation-runner.mjs') $run ([IO.Path]::GetFullPath($HostConfig)) ([IO.Path]::GetFullPath($AppFile)) $dotnet $repo
  if($LASTEXITCODE -ne 0){throw 'Conversation runner blocked; inspect private evidence'}
} finally {
  if($ptr -ne [IntPtr]::Zero){[Runtime.InteropServices.Marshal]::ZeroFreeGlobalAllocUnicode($ptr)}
  if($secure){$secure.Dispose()}
  $env:TYPESAFE_API_KEY=$priorTypesafe
  $env:FILOMENA_NATIVE_OBSERVATION_READER_TOKEN=$priorReader
  $env:OPENCLAW_GRAPHITI_SUPPRESS_EXTERNAL_STARTUP_PROBES=$priorSuppression
  $env:FILOMENA_CONVERSATION_MEMORY_FILE=$priorMemory
}
