using Filomena.Core.Shadow;

namespace Filomena.App;

/// <summary>Host-owned coordination for independently persisted native conversations.</summary>
public sealed class NativeConversationSupervisor
{
    /// <summary>
    /// Keeps the host's existing group checkpoint in place; dynamic DM scopes use
    /// isolated opaque directories under the host-selected state root.
    /// </summary>
    public static string ResolveSessionDirectory(
        string stateRoot,
        ShadowSessionBinding binding,
        string? existingGroupStateDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(binding);

        if (binding.Kind == Filomena.Core.Flows.Sessions.SessionEntryKind.Group)
        {
            if (string.IsNullOrWhiteSpace(existingGroupStateDirectory))
                throw new InvalidOperationException("existing_group_state_directory_required");
            var groupDirectory = Path.GetFullPath(existingGroupStateDirectory);
            if (!Directory.Exists(groupDirectory))
                throw new DirectoryNotFoundException("existing_group_state_directory_not_found");
            return groupDirectory;
        }

        return PrepareSessionDirectory(stateRoot, binding);
    }

    /// <summary>
    /// Creates a stable, opaque per-binding directory below the host-selected state root.
    /// The root itself must be private to the running host account.
    /// </summary>
    public static string PrepareSessionDirectory(string stateRoot, ShadowSessionBinding binding)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateRoot);
        ArgumentNullException.ThrowIfNull(binding);

        var reference = binding.OpaqueRef;
        if (reference.Length != 64 || reference.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("invalid_native_session_reference");

        var root = Path.GetFullPath(stateRoot);
        Directory.CreateDirectory(root);
        var sessionDirectory = Path.Combine(root, reference);
        if (Directory.Exists(sessionDirectory)
            && (File.GetAttributes(sessionDirectory) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("native_session_directory_must_not_be_linked");
        Directory.CreateDirectory(sessionDirectory);
        return sessionDirectory;
    }
}
