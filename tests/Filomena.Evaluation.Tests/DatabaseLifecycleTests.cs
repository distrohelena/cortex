using System.Diagnostics;
using System.Text.Json;
using System.Text;
using Filomena.Evaluation.Contracts;
using Filomena.Evaluation.Storage;
using Npgsql;
using Xunit;

namespace Filomena.Evaluation.Tests;

public sealed class DatabaseLifecycleTests
{
    [Fact]
    [Trait("Category", "Postgres")]
    public async Task Instance_survives_restart_and_custom_format_backup_restore_with_isolation_intact()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("FILOMENA_EVALUATION_POSTGRES"), "1", StringComparison.Ordinal))
            Assert.Skip("Set FILOMENA_EVALUATION_POSTGRES=1 to create disposable, isolated PostgreSQL instances.");

        var root = FindRepositoryRoot();
        var evaluationScripts = Path.Combine(root, "runtime", "evaluation");
        var createdInstances = new List<DatabaseInstance>();
        var backupPath = Path.Combine(Path.GetTempPath(), $"filomena-evaluation-{Guid.NewGuid():N}.dump");
        try
        {
            var started = await RunPowerShellAsync(Path.Combine(evaluationScripts, "Start-EvaluationDatabase.ps1"));
            AssertScriptSucceeded(started);
            var original = ParseInstance(started.StandardOutput);
            createdInstances.Add(original);

            await AssertIsolationAsync(original);
            await ExecuteSqlAsync(original, "CREATE TABLE public.lifecycle_sentinel (payload text PRIMARY KEY); INSERT INTO public.lifecycle_sentinel VALUES ('durable-task-3-sentinel');");
            Assert.Equal("durable-task-3-sentinel", await QueryAsync(original, "SELECT payload FROM public.lifecycle_sentinel;"));
            await AssertAppRoleCannotCreateTablesAsync(original);
            var evidence = await CreateEvaluationEvidenceAsync(original);
            await AssertEvaluationEvidenceAsync(original, evidence);

            var stopped = await RunDockerAsync("stop", original.ContainerRef);
            Assert.Equal(0, stopped.ExitCode);
            var restartedResult = await RunPowerShellAsync(Path.Combine(evaluationScripts, "Start-EvaluationDatabase.ps1"), "-CredentialRef", original.CredentialRef);
            AssertScriptSucceeded(restartedResult);
            var restarted = ParseInstance(restartedResult.StandardOutput);
            Assert.Equal(original, restarted);
            await AssertIsolationAsync(restarted);
            Assert.Equal("durable-task-3-sentinel", await QueryAsync(restarted, "SELECT payload FROM public.lifecycle_sentinel;"));
            await AssertEvaluationEvidenceAsync(restarted, evidence);

            var backupResult = await RunPowerShellAsync(Path.Combine(evaluationScripts, "Backup-EvaluationDatabase.ps1"),
                "-CredentialRef", restarted.CredentialRef, "-Destination", backupPath);
            AssertScriptSucceeded(backupResult);
            Assert.True(File.Exists(backupPath));
            Assert.True(new FileInfo(backupPath).Length > 0);

            var restoredResult = await RunPowerShellAsync(Path.Combine(evaluationScripts, "Restore-EvaluationDatabase.ps1"),
                "-BackupPath", backupPath);
            AssertScriptSucceeded(restoredResult);
            var restored = ParseInstance(restoredResult.StandardOutput);
            createdInstances.Add(restored);
            Assert.NotEqual(original.ContainerRef, restored.ContainerRef);
            await AssertIsolationAsync(restored);
            Assert.Equal("durable-task-3-sentinel", await QueryAsync(restored, "SELECT payload FROM public.lifecycle_sentinel;"));
            await AssertAppRoleCannotCreateTablesAsync(restored);
            await AssertEvaluationEvidenceAsync(restored, evidence);
        }
        finally
        {
            foreach (var instance in createdInstances)
                await StopOwnedInstanceAsync(instance.ContainerRef);
            if (File.Exists(backupPath))
                File.Delete(backupPath);
        }
    }

    private sealed record EvaluationEvidence(EvaluationScope Scope, RevisionRef Revision, EvidenceArtifactRef Artifact, Guid CaseId);

    private static async Task<EvaluationEvidence> CreateEvaluationEvidenceAsync(DatabaseInstance instance)
    {
        var token = TestContext.Current.CancellationToken;
        await using (var owner = await EvidenceSourceAsync(instance, true))
            await new MigrationRunner(owner).ApplyAsync(token);
        await using var source = await EvidenceSourceAsync(instance, false);
        var store = new PostgresEvaluationStore(source);
        var scope = new EvaluationScope(Guid.NewGuid(), Guid.NewGuid());
        var access = EvaluationAccess.FromHost("lifecycle-fixture", scope, EvaluationRole.Proposer);
        var revision = await store.PutRevisionAsync(access,
            new(1, "lifecycle-fixture", JsonSerializer.SerializeToElement(new { prompt = "controlled fixture", ordinal = 1 })), token);
        var artifact = await store.RegisterArtifactAsync(access,
            new("lifecycle-fixture", false, Encoding.UTF8.GetBytes("controlled evidence bytes")), token);
        var caseId = await store.RegisterCaseAsync(access, new("lifecycle-fixture", artifact), token);
        return new(scope, revision, artifact, caseId);
    }

    private static async Task AssertEvaluationEvidenceAsync(DatabaseInstance instance, EvaluationEvidence expected)
    {
        var token = TestContext.Current.CancellationToken;
        // Reapplying the real runner checks the restored migration checksum chain and
        // does not replace it with a sentinel's survival as schema acceptance evidence.
        await using (var owner = await EvidenceSourceAsync(instance, true))
            await new MigrationRunner(owner).ApplyAsync(token);
        await using var source = await EvidenceSourceAsync(instance, false);
        var store = new PostgresEvaluationStore(source);
        var access = EvaluationAccess.FromHost("lifecycle-fixture", expected.Scope, EvaluationRole.Proposer);
        var revision = await store.GetRevisionAsync(access, expected.Revision, token);
        Assert.NotNull(revision);
        Assert.Equal(expected.Revision.Sha256, RevisionHasher.Hash(revision));
        Assert.Equal("controlled fixture", revision.Payload.GetProperty("prompt").GetString());
        var artifact = await store.GetArtifactAsync(access, expected.Artifact, token);
        Assert.Equal(expected.Artifact, artifact.Reference);
        Assert.Equal("controlled evidence bytes", Encoding.UTF8.GetString(artifact.Document.Content.Span));
        var storedCase = await store.GetCaseAsync(access, expected.CaseId, token);
        Assert.Equal(expected.Artifact, storedCase.Document.Artifact);
        Assert.Equal("lifecycle-fixture", storedCase.Document.CaseKey);
        var foreign = EvaluationAccess.FromHost("lifecycle-fixture", new(expected.Scope.WorkspaceId, Guid.NewGuid()), EvaluationRole.Proposer);
        Assert.Null(await store.GetRevisionAsync(foreign, expected.Revision, token));
        Assert.Empty((await store.ListCasesAsync(foreign, 100, null, token)).Items);
    }

    private static async Task<NpgsqlDataSource> EvidenceSourceAsync(DatabaseInstance instance, bool migrationOwner)
    {
        // Invoked only inside this explicitly opted-in lifecycle test, after script
        // ownership/isolation checks. Never print this private document or credentials.
        using var credentials = JsonDocument.Parse(await File.ReadAllBytesAsync(instance.CredentialRef, TestContext.Current.CancellationToken));
        var root = credentials.RootElement;
        var username = migrationOwner ? "evaluation_owner" : "evaluation_app";
        Assert.Equal(username, root.GetProperty(migrationOwner ? "ownerUser" : "appUser").GetString());
        Assert.Equal("evaluation", root.GetProperty("database").GetString());
        var connection = new NpgsqlConnectionStringBuilder
        {
            Host = instance.Host,
            Port = instance.Port,
            Database = "evaluation",
            Username = username,
            Password = root.GetProperty(migrationOwner ? "ownerPassword" : "appPassword").GetString(),
            Pooling = false,
            IncludeErrorDetail = false,
            PersistSecurityInfo = false
        };
        return NpgsqlDataSource.Create(connection.ConnectionString);
    }

    private static async Task AssertIsolationAsync(DatabaseInstance instance)
    {
        Assert.Equal("127.0.0.1", instance.Host);
        Assert.InRange(instance.Port, 1, 65535);
        Assert.DoesNotContain("password", instance.RawJson, StringComparison.OrdinalIgnoreCase);

        var inspect = await RunDockerAsync("inspect", instance.ContainerRef);
        Assert.Equal(0, inspect.ExitCode);
        using var container = JsonDocument.Parse(inspect.StandardOutput);
        var item = container.RootElement[0];
        var labels = item.GetProperty("Config").GetProperty("Labels");
        Assert.Equal("evaluation-workbench", labels.GetProperty("io.filomena.evaluation.owner").GetString());
        Assert.Equal("filomena-eval", labels.GetProperty("io.filomena.evaluation.tool").GetString());

        var bindings = item.GetProperty("HostConfig").GetProperty("PortBindings").GetProperty("5432/tcp");
        Assert.Single(bindings.EnumerateArray());
        Assert.Equal("127.0.0.1", bindings[0].GetProperty("HostIp").GetString());
        Assert.Equal(instance.Port.ToString(), bindings[0].GetProperty("HostPort").GetString());

        var networks = item.GetProperty("NetworkSettings").GetProperty("Networks");
        Assert.Single(networks.EnumerateObject());
        var networkName = networks.EnumerateObject().Single().Name;
        var network = await RunDockerAsync("network", "inspect", networkName);
        Assert.Equal(0, network.ExitCode);
        using var networkInfo = JsonDocument.Parse(network.StandardOutput);
        Assert.True(networkInfo.RootElement[0].GetProperty("Internal").GetBoolean());

        Assert.Equal("f|f|f", await QueryAsync(instance,
            "SELECT rolsuper, rolcreatedb, rolcreaterole FROM pg_roles WHERE rolname = 'evaluation_app';"));
    }

    private static async Task AssertAppRoleCannotCreateTablesAsync(DatabaseInstance instance)
    {
        var result = await RunDockerAsync("exec", "--user", "postgres", instance.ContainerRef,
            "psql", "-X", "-v", "ON_ERROR_STOP=1", "-A", "-t", "-U", "evaluation_app", "-d", "evaluation",
            "-c", "CREATE TABLE public.app_role_must_not_ddl (id integer);");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("permission denied", result.StandardError, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task ExecuteSqlAsync(DatabaseInstance instance, string sql)
    {
        var result = await RunDockerAsync("exec", "--user", "postgres", instance.ContainerRef,
            "psql", "-X", "-v", "ON_ERROR_STOP=1", "-U", "evaluation_owner", "-d", "evaluation", "-c", sql);
        Assert.Equal(0, result.ExitCode);
    }

    private static async Task<string> QueryAsync(DatabaseInstance instance, string sql)
    {
        var result = await RunDockerAsync("exec", "--user", "postgres", instance.ContainerRef,
            "psql", "-X", "-v", "ON_ERROR_STOP=1", "-A", "-t", "-U", "evaluation_owner", "-d", "evaluation", "-c", sql);
        Assert.Equal(0, result.ExitCode);
        return result.StandardOutput.Trim();
    }

    private static async Task StopOwnedInstanceAsync(string containerRef)
    {
        var inspect = await RunDockerAsync("inspect", containerRef);
        if (inspect.ExitCode != 0)
            return;
        try
        {
            using var container = JsonDocument.Parse(inspect.StandardOutput);
            var item = container.RootElement[0];
            var labels = item.GetProperty("Config").GetProperty("Labels");
            var name = item.GetProperty("Name").GetString();
            if (labels.GetProperty("io.filomena.evaluation.owner").GetString() == "evaluation-workbench" &&
                labels.GetProperty("io.filomena.evaluation.tool").GetString() == "filomena-eval" &&
                name?.StartsWith("/filomena-eval-", StringComparison.Ordinal) == true)
                _ = await RunDockerAsync("stop", containerRef);
        }
        catch (Exception) when (inspect.ExitCode == 0)
        {
            // Leave any instance whose ownership cannot be proven untouched.
        }
    }

    private static DatabaseInstance ParseInstance(string output)
    {
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        return new DatabaseInstance(root.GetProperty("containerRef").GetString()!, root.GetProperty("host").GetString()!,
            root.GetProperty("port").GetInt32(), root.GetProperty("credentialRef").GetString()!, output);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Filomena.slnx")) ||
                File.Exists(Path.Combine(directory.FullName, "runtime", "docker-compose.yml")))
                return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the Filomena repository root from the test output directory.");
    }

    private static Task<ProcessResult> RunPowerShellAsync(string script, params string[] arguments) =>
        RunProcessAsync("powershell.exe", ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, .. arguments]);

    private static Task<ProcessResult> RunDockerAsync(params string[] arguments) => RunProcessAsync("docker", arguments);

    private static async Task<ProcessResult> RunProcessAsync(string executable, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {Path.GetFileName(executable)}.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode, await stdout, await stderr);
    }

    private static void AssertScriptSucceeded(ProcessResult result) =>
        Assert.True(result.ExitCode == 0, $"PowerShell script failed ({result.ExitCode}). {result.StandardError}");

    private sealed record DatabaseInstance(string ContainerRef, string Host, int Port, string CredentialRef, string RawJson);
    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
