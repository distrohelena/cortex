# Native member-DM wire fixture

`native-member-dm-fixture.json` is the complete JSON emitted by the isolated OpenClaw test:

```text
node node_modules/vitest/vitest.mjs run src/channels/message/native-observation-bootstrap.test.ts --maxWorkers=1 -t 'emits a real group/child/poll HTTP fixture'
```

Copy only the JSON following `NATIVE_MEMBER_DM_FIXTURE:`. Do not reconstruct selected fields or fabricate producer revisions. The captured run was session 44125, exit 0, one selected test passed (2026-09-25).

Real components exercised: effective config owner, WhatsApp effective dispatch producer, isolation owner/reducer, recovery inventory, child control, WhatsApp durable admission/serialization, SQLite observation journal and HTTP reader. The local config permits DM dispatch before isolation; the gate's raw/effective evidence remains in the response.

Synthetic boundaries: account/peer/self identities, membership enrollment input, provider message, plugin registry getter, and the queue drain callback. The callback completes only local test work and constructs no agent/model/sender. This is not a real account, membership-provider or live delivery proof.

The payload retains its LID and native message ID while the trusted scope binds the canonical PN. Its per-admission membership lease is emitted outside the provider payload by the native journal. Producer revision, epoch, cursor, port and timestamps are captured values, not operational configuration.

The .NET host tests consume the response unchanged for positive scope validation and LID mapping. The negative control test explicitly mutates `controlAvailable` to false; it is a counterexample, not a claim about the captured native service.
