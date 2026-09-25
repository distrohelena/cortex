using Filomena.App;
using Filomena.Core.Flows.Sessions;
using Filomena.Core.Identity;
using Filomena.Core.Outbound;
using Filomena.Core.Shadow;
using Filomena.OpenClaw.Observation;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class NativeConversationSupervisorTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("native-conversations-").FullName;

    [Fact]
    public void Group_and_two_DMs_get_stable_private_paths_and_independent_cursors()
    {
        var group = Binding("120363318973251043@g.us", SessionEntryKind.Group);
        var firstDm = Binding("15550001@s.whatsapp.net", SessionEntryKind.Dm);
        var secondDm = Binding("15550002@s.whatsapp.net", SessionEntryKind.Dm);

        var groupDirectory = NativeConversationSupervisor.PrepareSessionDirectory(root, group);
        var firstDmDirectory = NativeConversationSupervisor.PrepareSessionDirectory(root, firstDm);
        var secondDmDirectory = NativeConversationSupervisor.PrepareSessionDirectory(root, secondDm);

        Assert.Equal(firstDmDirectory, NativeConversationSupervisor.PrepareSessionDirectory(root, firstDm));
        Assert.Equal(3, new[] { groupDirectory, firstDmDirectory, secondDmDirectory }.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var directory in new[] { groupDirectory, firstDmDirectory, secondDmDirectory })
        {
            Assert.Equal(Path.GetFullPath(root), Directory.GetParent(directory)!.FullName);
            Assert.Matches("\\A[0-9A-F]{64}\\z", Path.GetFileName(directory));
            Assert.DoesNotContain("@", directory, StringComparison.Ordinal);
        }

        SaveAndResolve(groupDirectory, group, "inbound-observation:v2", "group-epoch", 2);
        SaveAndResolve(firstDmDirectory, firstDm, "inbound-observation:v1", "first-dm-epoch", 4);
        SaveAndResolve(secondDmDirectory, secondDm, "inbound-observation:v1", "second-dm-epoch", 7);
    }

    [Fact]
    public void Existing_group_directory_is_preserved_while_DM_uses_opaque_scope_directory()
    {
        var group = Binding("120363318973251043@g.us", SessionEntryKind.Group);
        var dm = Binding("15550001@s.whatsapp.net", SessionEntryKind.Dm);
        var existingGroupDirectory = Directory.CreateDirectory(Path.Combine(root, "existing-group-state")).FullName;
        var historyCheckpoint = Path.Combine(existingGroupDirectory, "history-checkpoint.json");
        File.WriteAllText(historyCheckpoint, "existing-group-history");

        var selectedGroupDirectory = NativeConversationSupervisor.ResolveSessionDirectory(root, group, existingGroupDirectory);
        var dmDirectory = NativeConversationSupervisor.ResolveSessionDirectory(root, dm);

        Assert.Equal(existingGroupDirectory, selectedGroupDirectory);
        Assert.Equal("existing-group-history", File.ReadAllText(historyCheckpoint));
        Assert.Equal(Path.Combine(Path.GetFullPath(root), dm.OpaqueRef), dmDirectory);
        Assert.NotEqual(selectedGroupDirectory, dmDirectory);
    }

    [Fact]
    public void Group_directory_must_be_explicit_and_already_exist()
    {
        var group = Binding("120363318973251043@g.us", SessionEntryKind.Group);

        Assert.Throws<InvalidOperationException>(() => NativeConversationSupervisor.ResolveSessionDirectory(root, group));
        Assert.Throws<DirectoryNotFoundException>(() => NativeConversationSupervisor.ResolveSessionDirectory(
            root, group, Path.Combine(root, "not-migrated")));
    }

    [Fact]
    public async Task Revalidated_member_lease_is_not_accepted_until_old_worker_drains()
    {
        var original = new MemberDmBoundTextBinding("15550001@s.whatsapp.net", "dm-scope", "dm-route", "native-rev-7", "lease-1");
        var refreshed = original with { MembershipLeaseRef = "lease-2" };
        var drain = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var replacement = NativeConversationSupervisor.AcceptRevalidatedMemberDmLeaseAsync(original, refreshed, drain.Task,
            TestContext.Current.CancellationToken);

        Assert.False(replacement.IsCompleted);
        drain.SetResult();
        Assert.Equal(refreshed, await replacement);
    }

    private static ShadowSessionBinding Binding(string nativeId, SessionEntryKind kind)
    {
        var session = new SessionKey("runtime", "filomena", "whatsapp", "account-a", nativeId);
        var conversationNamespace = FilomenaIdentity.WhatsAppConversationNamespace(nativeId);
        return new(session, conversationNamespace, ["whatsapp:15559999@s.whatsapp.net"],
            ["whatsapp:" + nativeId], kind);
    }

    private static void SaveAndResolve(string directory, ShadowSessionBinding binding, string version, string epoch, long sequence)
    {
        var store = new NativeLiveReadCursorStore(directory, binding.OpaqueRef, "pinned-R1");
        var expected = new NativeObservationCursor(version, epoch, binding.OpaqueRef, sequence);
        store.Save(expected);
        Assert.Equal(expected, new NativeLiveReadCursorStore(directory, binding.OpaqueRef, "pinned-R1")
            .Resolve(expected with { Sequence = sequence + 3 }, expected with { Sequence = 0 }));
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
