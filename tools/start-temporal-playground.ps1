param(
    [string]$FiloRoot = 'C:/dev/filo',
    [string]$InstallationId = 'cortex-local',
    [string]$OperatorRef = 'local-operator'
)
$ErrorActionPreference = 'Stop'
$cortexRoot = Split-Path $PSScriptRoot -Parent
$compose = Join-Path $cortexRoot 'runtime/docker-compose.temporal.yml'
$project = Join-Path $FiloRoot 'src/Filomena.App/Filomena.App.csproj'
if (-not (Test-Path -LiteralPath $project)) { throw "Filo project missing: $project" }
docker compose -f $compose up -d --wait
if ($LASTEXITCODE -ne 0) { throw 'Temporal Docker startup failed.' }
$env:CortexRoot = $cortexRoot
$env:SessionBackend = 'temporal'
$env:Temporal__InstallationId = $InstallationId
$env:Temporal__TargetHost = '127.0.0.1:7233'
$env:Temporal__Namespace = 'cortex-local'
$env:Temporal__WorkflowTaskQueue = 'cortex-local-workflows'
$env:Temporal__EffectTaskQueue = 'cortex-local-effects'
$env:Temporal__ArtifactDirectory = Join-Path $cortexRoot 'state/temporal/artifacts'
$env:Temporal__OperatorRef = $OperatorRef
dotnet run --project $project -- --playground
exit $LASTEXITCODE
