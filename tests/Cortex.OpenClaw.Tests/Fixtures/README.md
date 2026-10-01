# Native group V2 wire fixture

`native-group-v2.json` contains HTTP responses from the real native reader, not a
manually reproduced DTO. All identities, content, config evidence and accounts are
synthetic. No WhatsApp socket, model, sender or memory service is created.

Source generator in the isolated OpenClaw checkout:

```powershell
node scripts/build-observation-group-contract.mjs
node diagnostics/group-contract/fixture.mjs <new-output-json-path>
```

The generator bundles the real reader, journal, queue, registry composition and
isolation reducer with esbuild; packages remain external. Both native SQLite schema
assets accompany the bundle. The producer supplies explicit synthetic config evidence;
the real owner/reducer computes effective exclusion. This is a contract test, not a
live registry/config attestation or the full production host bootstrap.

Sequence: startup OFF, explicit local arm, Open at C0=0, four admissions (Alice PN,
Alice LID, Bob PN, self LID), Poll, identical replay, disarm, closed-epoch replay.
The generator asserts HTTP success, no initial snapshot records, identity-preserving
replay, capture OFF after disarm and preserved records. Temporary SQLite is removed.
Output uses exclusive creation to avoid overwriting existing evidence.

The C# test consumes these exact envelopes through the real HTTP adapter, using the
host-owned participant map. It checks canonical aliases, self/fromMe, timestamps,
native IDs, durable sequences, reader recreation and replay after disarm. Mutated copies
must fail on version, session, actor, epoch, sequence and revision mismatches.

Regeneration changes epoch UUIDs, timestamps and gate lifetime IDs. Keep those native
values intact; do not normalize authority or cursor fields to make a test pass.
