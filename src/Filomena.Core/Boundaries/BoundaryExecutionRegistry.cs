using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Filomena.Core.Flows;
using Filomena.Core.Memory;

namespace Filomena.Core.Boundaries;

// One instance per execution. Registration is internal, reachable only from the trusted memory-read boundary.
internal sealed class BoundaryExecutionRegistry(string conversationNamespace, TimeProvider? clock = null)
{
    private readonly Dictionary<string, (MemoryItem Snapshot, JsonElement Visual)> memories = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (Boundary Boundary, JsonElement Visual)> boundaries = new(StringComparer.Ordinal);
    internal int PublishedBoundaryCount => boundaries.Count;

    internal JsonElement RegisterMemories(IReadOnlyList<MemoryItem> items)
    {
        var output = new List<JsonElement>();
        foreach (var item in items)
        {
            var snapshot = item with
            {
                Subjects = Array.AsReadOnly(item.Subjects.Select(s => s with { }).ToArray()),
                EvidenceEpisodeIds = Array.AsReadOnly((item.EvidenceEpisodeIds ?? []).ToArray())
            };
            var handle = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var visual = JsonNode.Parse(MemoryValues.Serialize([snapshot])[0].GetRawText())!;
            visual["_memory"] = handle;
            var value = JsonSerializer.SerializeToElement(visual);
            memories.Add(handle, (snapshot, value));
            output.Add(value);
        }
        return JsonSerializer.SerializeToElement(output);
    }

    internal Task<JsonElement> CompileAsync(JsonElement values, BoundaryCompilerSettings settings,
        DateTimeOffset now, CancellationToken token)
    {
        settings.Validate();
        return CompileSetAsync(values, source =>
        {
            var parsed = BoundarySourceParserV1.Parse(source.Content);
            if (parsed.Status != "compiled")
                throw new BoundaryCompilationException(source.Id, parsed.Status, parsed.DiagnosticCode!);
            return Task.FromResult(BoundarySourceParserV1.Proposal(source.Content));
        }, now, token);
    }

    // Historical interpreter experiments only. The V1 graph runner never calls this path.
    internal Task<JsonElement> InterpretAsync(JsonElement values, IFlowCompletion completion,
        BoundaryInterpreterSettings settings, DateTimeOffset now, CancellationToken token)
    {
        settings.Validate();
        return CompileSetAsync(values, source => BoundaryInterpreter.ProposeAsync(source.Content, source.Id, completion, settings, token), now, token);
    }

    private async Task<JsonElement> CompileSetAsync(JsonElement values, Func<MemoryItem, Task<BoundaryProposal>> propose,
        DateTimeOffset now, CancellationToken token)
    {
        if (values.ValueKind != JsonValueKind.Array) throw Failure("", "memory-list-required");
        // Validate the entire source set before paying for any model call.
        var sources = values.EnumerateArray().Select(value => ResolveMemory(value, clock?.GetUtcNow() ?? now)).ToArray();
        var pending = new List<Boundary>();
        foreach (var source in sources)
        {
            token.ThrowIfCancellationRequested();
            var proposal = await propose(source);
            var admission = BoundaryAdmission.Admit(source.Content, proposal);
            if (admission.Status != "compiled")
                throw new BoundaryCompilationException(source.Id, admission.Status, admission.DiagnosticCode!);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(source))));
            pending.Add(new(source, hash, admission));
        }
        token.ThrowIfCancellationRequested();
        foreach (var source in sources)
            ValidateActive(source.Id, source.ValidAt, source.InvalidAt, source.ExpiredAt, clock?.GetUtcNow() ?? now);
        // Atomic publication: neither handles nor partial results escape before every source is admitted.
        var output = new List<JsonElement>();
        foreach (var boundary in pending)
        {
            var handle = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var visual = JsonSerializer.SerializeToElement(new
            {
                status = boundary.Status,
                kind = boundary.Kind,
                originalText = boundary.OriginalText,
                topicEvidence = boundary.TopicEvidence,
                constraintEvidence = boundary.ConstraintEvidence,
                _boundary = handle
            });
            boundaries.Add(handle, (boundary, visual));
            output.Add(visual);
        }
        return JsonSerializer.SerializeToElement(output);
    }

    internal IReadOnlyList<Boundary> ResolveBoundaries(JsonElement values, DateTimeOffset now)
    {
        if (values.ValueKind != JsonValueKind.Array) throw Failure("", "boundary-list-required");
        return values.EnumerateArray().Select(value =>
        {
            if (!Handle(value, "_boundary", out var handle) || !boundaries.TryGetValue(handle, out var entry) ||
                !JsonElement.DeepEquals(value, entry.Visual)) throw Failure("", "untrusted-boundary-value");
            var source = entry.Boundary.Source;
            if (source.ConversationNamespace != conversationNamespace) throw Failure(source.MemoryId, "source-namespace-mismatch");
            ValidateActive(source.MemoryId, source.ValidAt, source.InvalidAt, source.ExpiredAt, now);
            return entry.Boundary;
        }).ToArray();
    }

    internal JsonElement ForModel(JsonElement values, DateTimeOffset now, IDictionary<string, string> references)
    {
        return JsonSerializer.SerializeToElement(ResolveBoundaries(values, now).Select(boundary =>
        {
            var person = boundary.Source.CanonicalSubject;
            var subject = FlowDomain.ForModel(JsonSerializer.SerializeToElement(new { nome = person.DisplayName ?? "", stableId = person.ActorStableId }), FlowDomain.Person, references);
            if (string.IsNullOrWhiteSpace(person.DisplayName))
            {
                var safe = JsonNode.Parse(subject.GetRawText())!;
                safe["nome"] = subject.GetProperty("ref").GetString();
                subject = JsonSerializer.SerializeToElement(safe);
            }
            return new
            {
                subject,
                original_boundary = boundary.OriginalText,
                compiled_boundary = new
                {
                    kind = boundary.Kind,
                    topic = boundary.TopicEvidence,
                    policy_template = boundary.Compilation.TemplateId,
                    silent_enforcement = true,
                    policy = BoundaryPolicies.TopicV1,
                    disclosure_policy = BoundaryPolicies.DisclosureV1
                }
            };
        }).ToArray());
    }

    private MemoryItem ResolveMemory(JsonElement value, DateTimeOffset now)
    {
        if (!Handle(value, "_memory", out var handle) || !memories.TryGetValue(handle, out var entry) ||
            !JsonElement.DeepEquals(value, entry.Visual)) throw Failure("", "untrusted-memory-value");
        var source = entry.Snapshot;
        if (source.ConversationNamespace != conversationNamespace) throw Failure(source.Id, "source-namespace-mismatch");
        if (source.Category != MemoryCategory.Boundary) throw Failure(source.Id, "source-not-boundary");
        if (string.IsNullOrWhiteSpace(source.Id) || string.IsNullOrWhiteSpace(source.Content) || string.IsNullOrWhiteSpace(source.SourceKind))
            throw Failure(source.Id, "invalid-source-metadata");
        if (source.Subjects.Count != 1) throw Failure(source.Id, "source-subject-not-unique");
        var person = source.Subjects[0];
        try
        {
            var expected = MemoryIdentity.Person(conversationNamespace, person.ActorStableId, person.DisplayName);
            if (person.ParticipantUuid != expected.ParticipantUuid || person.ActorStableId != expected.ActorStableId)
                throw Failure(source.Id, "source-subject-not-canonical");
        }
        catch (InvalidDataException) { throw Failure(source.Id, "source-subject-not-canonical"); }
        ValidateActive(source.Id, source.ValidAt, source.InvalidAt, source.ExpiredAt, now);
        return source;
    }

    private static void ValidateActive(string id, DateTimeOffset? valid, DateTimeOffset? invalid, DateTimeOffset? expired, DateTimeOffset now)
    {
        if (expired is not null || invalid <= now || valid > now) throw Failure(id, "source-not-active");
    }

    private static bool Handle(JsonElement value, string name, out string handle)
    {
        handle = "";
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
            return false;
        handle = property.GetString()!;
        return true;
    }

    private static BoundaryCompilationException Failure(string id, string code) => new(id, "unsupported", code);
}
