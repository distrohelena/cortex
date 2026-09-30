using Filomena.Core.Flows;
using Filomena.Core.Flows.Sessions;

namespace Filomena.Core.Inference;

public sealed record HostSessionInferenceBinding(SessionKey Session, string ProjectId, long ProjectVersion,
    string ProjectRevision, HostInferenceAuthority Authority, HostInferenceGrant Grant, InferenceScope Scope,
    IReadOnlyDictionary<string, string> NodeProfiles);

/// <summary>Explicit host bindings only. No discovery, implicit scope, or latest-revision fallback.</summary>
public sealed class HostBoundSessionCompletionFactory : ISessionFlowCompletionFactory
{
    private readonly Dictionary<BindingKey, HostSessionInferenceBinding> bindings = new();

    public HostBoundSessionCompletionFactory(IReadOnlyList<HostSessionInferenceBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        foreach (var binding in bindings)
        {
            ArgumentNullException.ThrowIfNull(binding.Session);
            ArgumentNullException.ThrowIfNull(binding.Authority);
            ArgumentNullException.ThrowIfNull(binding.Grant);
            ArgumentNullException.ThrowIfNull(binding.Scope);
            ArgumentException.ThrowIfNullOrWhiteSpace(binding.ProjectId);
            ArgumentException.ThrowIfNullOrWhiteSpace(binding.ProjectRevision);
            var profiles = binding.NodeProfiles.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            if (profiles.Count == 0) throw new ArgumentException("Node profiles required.", nameof(bindings));
            foreach (var (node, profile) in profiles)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(node);
                ArgumentException.ThrowIfNullOrWhiteSpace(profile);
            }
            if (!this.bindings.TryAdd(new(binding.Session, binding.ProjectId, binding.ProjectVersion, binding.ProjectRevision),
                    binding with { NodeProfiles = profiles }))
                throw new InferenceDeniedException("ambiguous_session_binding");
        }
    }

    public IFlowCompletion Create(SessionCompletionContext context)
    {
        if (!bindings.TryGetValue(new(context.Session, context.ProjectId, context.ProjectVersion, context.ProjectRevision), out var binding))
            throw new InferenceDeniedException("session_binding_unavailable");
        var nodes = binding.NodeProfiles.ToDictionary(p => p.Key,
            p => binding.Authority.Bind(binding.Grant, binding.Scope, p.Value,
                new(context.ExecutionId, p.Key, context.ProjectRevision)), StringComparer.Ordinal);
        return new NodeCompletion(nodes);
    }

    private sealed record BindingKey(SessionKey Session, string ProjectId, long ProjectVersion, string Revision);

    private sealed class NodeCompletion(Dictionary<string, IFlowCompletion> nodes) : IFlowCompletion
    {
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token) =>
            nodes.TryGetValue(request.NodeId, out var completion)
                ? completion.CompleteAsync(request, token)
                : throw new InferenceDeniedException("node_not_authorized");
    }
}
