# Controlled native Shadow handoff

This entrypoint composes the existing Observation reader, durable Shadow inbox,
session runtime and Shadow outbound host. It never constructs the legacy app host
or a transport sender. Frozen project v3 and its model/prompt/FSM configuration
remain unchanged.

`--native-shadow <private-host-document>` uses an operator-owned document with:
`manualConfigFile` (the already pinned v3 configuration), `observationEndpoint`,
`observationTokenEnvironmentVariable` (reader credential only),
`observationSessionRef`, `nativeConversationId`, `approvedRevision`,
`journalDirectory`, and `maxInteractions` (initial window: 5).

The native conversation alias is resolved through the account's trusted
bidirectional PN/LID mapping during provisioning. It changes only the transport
route proof, never the logical session or Graphiti namespace.

Order: native preflight/revision -> Graphiti read-only preflight -> revision
recheck -> OpenObservation -> durable epoch/tail anchor -> new non-resumable run.
Snapshot messages do not execute. Only `/poll` reads subsequent native records;
operator text is never injected as inbound. `/quit`, EOF or cancellation closes
the run. No automatic listener or recovery is started. Real runtime quiet-window
batching remains in force.

Each poll revalidates native capture/isolation/revision and maps the complete
batch before admission. Native IDs and sequences are retained. Self and duplicate
records do not execute. Unknown identity, stale revision, gaps, a closed epoch or
runtime failure stop the run. Journal records remain durable; restarting creates
a new run, never resumes an old execution.

Local homologation evidence may contain complete candidates; keep its directory
private. Normal operator summary uses opaque event refs. WOULD_SEND is not sent
speech and causes no spoken-state transition.

The separate approved native maintenance controller owns arm/disarm. Always stop
this runner before ending that window, disarm capture/isolation, drain safely and
restore the original installation/launcher. A completed preflight from a previous
process is not authority for a fresh window.

## 2026-09-15 local verification and window F

Local verification: Core 501 passed / 10 pre-existing skips; reader 17 passed;
selected command/project/Graphiti regressions 76 passed. Build has zero warnings
or errors. Frozen v3 SHA-256 remains
`47D1D4521BCF013E919DDCB3DE5900485DDA7F8BACEFA7437FEDFBDFFC9BC55E`.

Window F reached healthy/readable native status (7.9 ms), valid revision-bound
isolation of all ten paths, explicit capture arm, and an empty snapshot/poll.
The tool approval layer then rejected starting `--native-shadow` because the
earlier explicit prohibition of brain handoff was still treated as operative.
No .NET ShadowRun or conversational model execution started. Capture/isolation
were disarmed successfully; journal remained healthy. The approved rollback was
invoked. A new explicit user authorization to start the first real-inbound brain
ShadowRun is required before retrying that rejected operation.
