Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:EvaluationImage = 'postgres@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193'
$script:EvaluationOwnerLabel = 'evaluation-workbench'
$script:EvaluationToolLabel = 'filomena-eval'
$script:ComposeFile = Join-Path $PSScriptRoot 'compose.yml'

function Get-EvaluationRoot {
    $configured = $env:FILOMENA_EVALUATION_HOME
    if ([string]::IsNullOrWhiteSpace($configured)) {
        $configured = Join-Path $env:LOCALAPPDATA 'Filomena\EvaluationDatabase'
    }
    if (-not [IO.Path]::IsPathRooted($configured)) {
        throw 'Evaluation state path must be absolute.'
    }
    $root = [IO.Path]::GetFullPath($configured)
    $pathRoot = [IO.Path]::GetPathRoot($root)
    if ($root.TrimEnd('\', '/') -eq $pathRoot.TrimEnd('\', '/')) {
        throw 'Evaluation state path must be a dedicated child directory, not a filesystem root.'
    }
    return $root
}

function Set-EvaluationPrivateAcl([string]$Path, [switch]$Directory) {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    $arguments = @($Path, '/inheritance:r', '/grant:r')
    if ($Directory) {
        $arguments += @("$($identity):(OI)(CI)F", 'SYSTEM:(OI)(CI)F')
    } else {
        $arguments += @("$($identity):F", 'SYSTEM:F')
    }
    $result = Invoke-EvaluationProcess 'icacls.exe' $arguments
    if ($result.ExitCode -ne 0) {
        throw "Could not apply private ACL to evaluation state (exit code $($result.ExitCode))."
    }
}

function ConvertTo-EvaluationProcessArgument([string]$Value) {
    if ($Value.Length -gt 0 -and $Value -notmatch '[\s"]') { return $Value }
    $builder = [Text.StringBuilder]::new()
    [void]$builder.Append('"')
    $slashes = 0
    foreach ($character in $Value.ToCharArray()) {
        if ($character -eq '\') { $slashes++; continue }
        if ($character -eq '"') {
            [void]$builder.Append(('\' * (2 * $slashes + 1))).Append('"')
        } else {
            if ($slashes -gt 0) { [void]$builder.Append(('\' * $slashes)) }
            [void]$builder.Append($character)
        }
        $slashes = 0
    }
    if ($slashes -gt 0) { [void]$builder.Append(('\' * (2 * $slashes))) }
    [void]$builder.Append('"')
    return $builder.ToString()
}

function Invoke-EvaluationProcess([string]$Executable, [string[]]$Arguments, [switch]$BinaryStdout, [string]$OutputPath) {
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.Arguments = (($Arguments | ForEach-Object { ConvertTo-EvaluationProcessArgument $_ }) -join ' ')
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    try {
        if (-not $process.Start()) { throw "Could not start $([IO.Path]::GetFileName($Executable))." }
        $stderrTask = $process.StandardError.ReadToEndAsync()
        if ($BinaryStdout) {
            $file = [IO.File]::Open($OutputPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
            try {
                Set-EvaluationPrivateAcl -Path $OutputPath
                $process.StandardOutput.BaseStream.CopyTo($file)
            } finally { $file.Dispose() }
            $process.WaitForExit()
        } else {
            $stdoutTask = $process.StandardOutput.ReadToEndAsync()
            $process.WaitForExit()
            $stdout = $stdoutTask.GetAwaiter().GetResult()
        }
        $stderr = $stderrTask.GetAwaiter().GetResult()
        return [pscustomobject]@{ ExitCode = $process.ExitCode; Output = $(if ($BinaryStdout) { '' } else { $stdout }); Error = $stderr }
    } finally { $process.Dispose() }
}

function Invoke-EvaluationDocker([string[]]$Arguments) {
    $result = Invoke-EvaluationProcess 'docker.exe' $Arguments
    if ($result.ExitCode -ne 0) {
        $operation = if ($Arguments.Count -gt 0) { $Arguments[0] } else { 'command' }
        throw "Docker $operation failed (exit code $($result.ExitCode)); output was suppressed to protect private values."
    }
    return $result.Output.Trim()
}

function Get-EvaluationDockerResult([string[]]$Arguments) {
    return Invoke-EvaluationProcess 'docker.exe' $Arguments
}

function Write-EvaluationPrivateText([string]$Path, [string]$Text) {
    [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false))
}

function New-EvaluationSecret {
    $bytes = [byte[]]::new(48)
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generator.GetBytes($bytes) } finally { $generator.Dispose() }
    return [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', 'A').Replace('/', 'B')
}

function ConvertTo-EvaluationSqlLiteral([string]$Value) {
    return "'" + $Value.Replace("'", "''") + "'"
}

function New-EvaluationInstance {
    $root = Get-EvaluationRoot
    if (-not (Test-Path -LiteralPath $root)) {
        [void][IO.Directory]::CreateDirectory($root)
        Set-EvaluationPrivateAcl -Path $root -Directory
    } else {
        Set-EvaluationPrivateAcl -Path $root -Directory
    }

    $instanceId = [Guid]::NewGuid().ToString('N')
    $stateDirectory = Join-Path $root $instanceId
    [void][IO.Directory]::CreateDirectory($stateDirectory)
    Set-EvaluationPrivateAcl -Path $stateDirectory -Directory
    $containerName = "filomena-eval-$instanceId"
    $projectName = "filomena-eval-$instanceId"
    $networkName = "filomena-eval-net-$instanceId"
    $volumeName = "filomena-eval-data-$instanceId"
    $adminPassword = New-EvaluationSecret
    $ownerPassword = New-EvaluationSecret
    $appPassword = New-EvaluationSecret
    $credentialRef = Join-Path $stateDirectory 'credentials.json'

    Write-EvaluationPrivateText (Join-Path $stateDirectory 'bootstrap-password') $adminPassword
    $sql = @"
CREATE ROLE evaluation_owner LOGIN PASSWORD $(ConvertTo-EvaluationSqlLiteral $ownerPassword);
CREATE ROLE evaluation_app LOGIN PASSWORD $(ConvertTo-EvaluationSqlLiteral $appPassword);
ALTER DATABASE evaluation OWNER TO evaluation_owner;
GRANT CONNECT ON DATABASE evaluation TO evaluation_app;
GRANT USAGE ON SCHEMA public TO evaluation_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO evaluation_app;
GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO evaluation_app;
ALTER DEFAULT PRIVILEGES FOR ROLE evaluation_owner IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO evaluation_app;
ALTER DEFAULT PRIVILEGES FOR ROLE evaluation_owner IN SCHEMA public GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO evaluation_app;
"@
    Write-EvaluationPrivateText (Join-Path $stateDirectory 'init.sql') $sql

    $credentials = [ordered]@{
        instanceId = $instanceId
        adminUser = 'evaluation_admin'
        adminPassword = $adminPassword
        ownerUser = 'evaluation_owner'
        ownerPassword = $ownerPassword
        appUser = 'evaluation_app'
        appPassword = $appPassword
        database = 'evaluation'
    } | ConvertTo-Json -Depth 4
    Write-EvaluationPrivateText $credentialRef $credentials
    Set-EvaluationPrivateAcl -Path $credentialRef

    $stateForCompose = $stateDirectory.Replace('\', '/')
    $composeEnv = @(
        "INSTANCE_ID=$instanceId"
        "CONTAINER_NAME=$containerName"
        "NETWORK_NAME=$networkName"
        "VOLUME_NAME=$volumeName"
        "STATE_DIRECTORY=`"$stateForCompose`""
    ) -join "`n"
    Write-EvaluationPrivateText (Join-Path $stateDirectory 'compose.env') ($composeEnv + "`n")

    $metadata = [ordered]@{
        instanceId = $instanceId
        containerRef = ''
        containerName = $containerName
        projectName = $projectName
        networkName = $networkName
        volumeName = $volumeName
        stateDirectory = $stateDirectory
        credentialRef = $credentialRef
        port = 0
        host = '127.0.0.1'
        image = $script:EvaluationImage
    }
    Save-EvaluationMetadata $metadata
    Start-EvaluationCompose $metadata
    return Complete-EvaluationStart $metadata
}

function Save-EvaluationMetadata($Metadata) {
    $path = Join-Path $Metadata.stateDirectory 'instance.json'
    Write-EvaluationPrivateText $path ($Metadata | ConvertTo-Json -Depth 5)
}

function Read-EvaluationInstance([string]$CredentialRef) {
    if ([string]::IsNullOrWhiteSpace($CredentialRef)) { throw 'CredentialRef is required.' }
    $credentialPath = [IO.Path]::GetFullPath($CredentialRef)
    $root = (Get-EvaluationRoot).TrimEnd('\') + '\'
    if (-not $credentialPath.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($credentialPath) -ne 'credentials.json' -or -not (Test-Path -LiteralPath $credentialPath -PathType Leaf)) {
        throw 'CredentialRef is not a credential file under the private evaluation state directory.'
    }
    $stateDirectory = Split-Path -Parent $credentialPath
    $metadataPath = Join-Path $stateDirectory 'instance.json'
    if (-not (Test-Path -LiteralPath $metadataPath -PathType Leaf)) { throw 'Evaluation instance metadata is missing.' }
    $metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
    if ([IO.Path]::GetFullPath($metadata.stateDirectory) -ne [IO.Path]::GetFullPath($stateDirectory) -or
        [IO.Path]::GetFullPath($metadata.credentialRef) -ne $credentialPath -or
        $metadata.instanceId -notmatch '^[0-9a-f]{32}$' -or
        $metadata.containerName -ne "filomena-eval-$($metadata.instanceId)" -or
        $metadata.projectName -ne "filomena-eval-$($metadata.instanceId)" -or
        $metadata.networkName -ne "filomena-eval-net-$($metadata.instanceId)" -or
        $metadata.volumeName -ne "filomena-eval-data-$($metadata.instanceId)") {
        throw 'Evaluation instance metadata failed its ownership checks.'
    }
    $credentials = Get-Content -LiteralPath $credentialPath -Raw | ConvertFrom-Json
    if ($credentials.instanceId -ne $metadata.instanceId -or $credentials.ownerUser -ne 'evaluation_owner' -or
        $credentials.appUser -ne 'evaluation_app' -or $credentials.database -ne 'evaluation') {
        throw 'Evaluation credentials failed their ownership checks.'
    }
    return [pscustomobject]@{ Metadata = $metadata; Credentials = $credentials }
}

function Start-EvaluationCompose($Metadata) {
    $existing = Get-EvaluationDockerResult @('inspect', $Metadata.containerName)
    if ($existing.ExitCode -eq 0) { throw 'The generated evaluation container name is already in use; no existing container was changed.' }
    $envPath = Join-Path $Metadata.stateDirectory 'compose.env'
    $arguments = @('compose', '--ansi', 'never', '--env-file', $envPath, '-f', $script:ComposeFile,
        '-p', $Metadata.projectName, 'up', '--pull', 'never', '-d', 'database')
    [void](Invoke-EvaluationDocker $arguments)
    $Metadata.containerRef = (Invoke-EvaluationDocker @('compose', '--ansi', 'never', '--env-file', $envPath,
        '-f', $script:ComposeFile, '-p', $Metadata.projectName, 'ps', '-q', 'database')).Trim()
    if ($Metadata.containerRef -notmatch '^[0-9a-f]{64}$') {
        throw 'Compose did not return the immutable ID of the newly provisioned evaluation container.'
    }
    Complete-EvaluationStart $Metadata | Out-Null
}

function Get-EvaluationContainer($Metadata) {
    if ([string]::IsNullOrWhiteSpace($Metadata.containerRef)) { throw 'Evaluation container ID has not been recorded.' }
    $inspectJson = Invoke-EvaluationDocker @('inspect', $Metadata.containerRef)
    $container = (ConvertFrom-Json -InputObject $inspectJson)[0]
    $labels = $container.Config.Labels
    if ($container.Id -ne $Metadata.containerRef -or $container.Name -ne "/$($Metadata.containerName)" -or
        $labels.'io.filomena.evaluation.owner' -ne $script:EvaluationOwnerLabel -or
        $labels.'io.filomena.evaluation.tool' -ne $script:EvaluationToolLabel -or
        $labels.'io.filomena.evaluation.instance' -ne $Metadata.instanceId -or
        ([string]$labels.'io.filomena.evaluation.state-directory').Replace('/', '\') -ne ([string]$Metadata.stateDirectory).Replace('/', '\')) {
        throw 'Container labels, name, ID, or state path do not match this evaluation instance.'
    }
    $mount = @($container.Mounts | Where-Object { $_.Destination -eq '/var/lib/postgresql/data' -and $_.Type -eq 'volume' })
    if ($mount.Count -ne 1 -or $mount[0].Name -ne $Metadata.volumeName) {
        throw 'Evaluation container data volume does not match its private instance metadata.'
    }
    $networkNames = @($container.NetworkSettings.Networks.PSObject.Properties.Name)
    if ($networkNames.Count -ne 1 -or $networkNames[0] -ne $Metadata.networkName) {
        throw 'Evaluation container is attached to an unexpected network.'
    }
    $networkJson = Invoke-EvaluationDocker @('network', 'inspect', $Metadata.networkName)
    $network = (ConvertFrom-Json -InputObject $networkJson)[0]
    if (-not $network.Internal -or $network.Name -ne $Metadata.networkName -or
        $network.Labels.'io.filomena.evaluation.instance' -ne $Metadata.instanceId) {
        throw 'Evaluation network is not the expected isolated internal network.'
    }
    $portBindings = @($container.HostConfig.PortBindings.'5432/tcp')
    if ($portBindings.Count -ne 1 -or $portBindings[0].HostIp -ne '127.0.0.1' -or
        [int]$portBindings[0].HostPort -lt 1 -or [int]$portBindings[0].HostPort -gt 65535) {
        throw 'Evaluation database does not have exactly one valid loopback port binding.'
    }
    $Metadata.port = [int]$portBindings[0].HostPort
    $Metadata.containerRef = $container.Id
    return $container
}

function Complete-EvaluationStart($Metadata) {
    $container = Get-EvaluationContainer $Metadata
    Save-EvaluationMetadata $Metadata
    Wait-EvaluationReady $Metadata
    return [pscustomobject]@{
        containerRef = $Metadata.containerRef
        host = '127.0.0.1'
        port = $Metadata.port
        credentialRef = $Metadata.credentialRef
    }
}

function Wait-EvaluationReady($Metadata) {
    while ($true) {
        $container = Get-EvaluationContainer $Metadata
        if ($container.State.Status -ne 'running') { throw 'Evaluation database container stopped before becoming ready.' }
        if ($container.State.Health.Status -eq 'unhealthy') { throw 'Evaluation database container reported unhealthy.' }
        $probe = Get-EvaluationDockerResult @('exec', $Metadata.containerRef, 'pg_isready', '-U', 'evaluation_admin', '-d', 'evaluation')
        if ($probe.ExitCode -eq 0 -and $container.State.Health.Status -eq 'healthy') { return }
        Start-Sleep -Milliseconds 500
    }
}

function Start-ExistingEvaluationInstance($Instance) {
    $metadata = $Instance.Metadata
    $containerResult = Get-EvaluationDockerResult @('inspect', $metadata.containerRef)
    if ($containerResult.ExitCode -ne 0) { throw 'The owned evaluation container no longer exists.' }
    $container = Get-EvaluationContainer $metadata
    if ($container.State.Status -ne 'running') {
        [void](Invoke-EvaluationDocker @('start', $metadata.containerRef))
    }
    return Complete-EvaluationStart $metadata
}

function New-EvaluationDatabaseOutput($InstanceResult) {
    return ($InstanceResult | ConvertTo-Json -Compress -Depth 4)
}

function Invoke-EvaluationSqlBinary([string]$ContainerRef, [string[]]$Arguments, [string]$InputPath, [string]$OutputPath) {
    $dockerArgs = @('exec')
    if ($InputPath) { $dockerArgs += '-i' }
    $dockerArgs += @('--user', 'postgres', $ContainerRef) + $Arguments
    if ($InputPath) {
        $start = [Diagnostics.ProcessStartInfo]::new('docker.exe')
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardInput = $true
        $start.RedirectStandardOutput = [bool]$OutputPath
        $start.RedirectStandardError = $true
        $start.Arguments = (($dockerArgs | ForEach-Object { ConvertTo-EvaluationProcessArgument $_ }) -join ' ')
        $process = [Diagnostics.Process]::new(); $process.StartInfo = $start
        try {
            [void]$process.Start(); $stderrTask = $process.StandardError.ReadToEndAsync()
            $input = [IO.File]::OpenRead($InputPath)
            try { $input.CopyTo($process.StandardInput.BaseStream) } finally { $input.Dispose(); $process.StandardInput.Close() }
            $process.WaitForExit(); $stderr = $stderrTask.GetAwaiter().GetResult()
            if ($process.ExitCode -ne 0) { throw "Docker restore command failed (exit code $($process.ExitCode)); output was suppressed." }
        } finally { $process.Dispose() }
        return
    }
    $result = Invoke-EvaluationProcess 'docker.exe' $dockerArgs -BinaryStdout -OutputPath $OutputPath
    if ($result.ExitCode -ne 0) { throw "Docker backup command failed (exit code $($result.ExitCode)); output was suppressed." }
}

function Assert-EvaluationRestoreTarget($Metadata) {
    $container = Get-EvaluationContainer $Metadata
    if ($container.State.Status -ne 'running' -or $container.Image -ne $script:EvaluationImage) {
        throw 'Restore target is not a running, newly provisioned evaluation container.'
    }
    $tables = Invoke-EvaluationDocker @('exec', '--user', 'postgres', $Metadata.containerRef,
        'psql', '-X', '-v', 'ON_ERROR_STOP=1', '-A', '-t', '-U', 'evaluation_admin', '-d', 'evaluation',
        '-c', "SELECT count(*) FROM pg_class WHERE relkind IN ('r','p') AND relname NOT LIKE 'pg_%' AND relname <> 'sql_features';")
    if ($tables.Trim() -ne '0') { throw 'Restore target is not empty; refusing to restore over existing data.' }
}
