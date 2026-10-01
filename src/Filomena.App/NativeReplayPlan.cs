using Cortex.OpenClaw.Observation;

namespace Filomena.App;

// Host-only finite reconciliation scope. Never a transcript or synthetic inbound.
// Current transport authorization is checked separately from this historical cursor.
public sealed record NativeReplayPlan(NativeObservationCursor After, long ThroughSequence)
{
    public void Validate(string session, string expectedVersion = "inbound-observation:v1")
    {
        if (expectedVersion is not ("inbound-observation:v1" or "inbound-observation:v2")
            || After is null || After.Version != expectedVersion || After.SessionRef != session
            || string.IsNullOrWhiteSpace(After.Epoch) || After.Sequence < 0
            || ThroughSequence <= After.Sequence || ThroughSequence > 9007199254740991
            || ThroughSequence - After.Sequence > 20)
            throw new InvalidDataException("invalid_native_recovery_scope");
    }

    public NativeObservationBatch Next(NativeObservationBatch batch, NativeObservationCursor cursor)
    {
        Validate(cursor.SessionRef, cursor.Version);
        if (cursor.Version != After.Version || cursor.Epoch != After.Epoch
            || cursor.Sequence < After.Sequence || cursor.Sequence >= ThroughSequence
            || !batch.EpochClosed || batch.Cursor != (After with { Sequence = ThroughSequence })
            || batch.Records.Count != ThroughSequence - cursor.Sequence)
            throw new InvalidDataException("native_recovery_boundary_changed");
        var expected = cursor.Sequence;
        foreach (var record in batch.Records)
            if (record.Sequence != ++expected || record.SessionRef != After.SessionRef || record.JournalEpoch != After.Epoch)
                throw new InvalidDataException("native_recovery_identity_or_order");
        // Keep exact native record. Serial admission uses the existing runtime/batching engine.
        return new([batch.Records[0]], cursor with { Sequence = cursor.Sequence + 1 }, true);
    }
}
