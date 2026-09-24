# One-response native DM Live window

The operator explicitly authorized this separate live entrypoint. It does not
upgrade `--native-shadow`, construct the legacy app host or start a continuous listener.

```
dotnet run --project src/Filomena.App --no-build -- --native-live <private-live-host.json> --authorize-one-response
```

The host document contains `nativeConfigFile` (the existing native observation
document, with `maxInteractions: 1`), `mcp` (`command`, `arguments`, optional
`workingDirectory`), `transportRoute`, `speakerName`, `maxSinglePartCharacters`,
`capabilities`, `capabilityEvidenceFile`, and `capabilityEvidenceSha256` (uppercase).
The evidence document is operator-owned, not project/model data:

```
version: native-live-capabilities:v1
approvedRevision: <current effective configuration revision>
observationSessionRef: <exact trusted source session ref>
routeBindingRef: <trusted native account/session binding ref>
boundSendContract: whatsapp-bound-text:v1
capabilities: <same reviewed TransportSafetyCapabilities as the host document>
```

The attestation must reflect verified implementation/provisioning evidence, not
merely turn seven flags true. Its hash pins the artifact, not the truth of its
contents. Runtime preflight independently rechecks native session, self, isolation,
health, capture and revision. MCP must advertise `messages_send_bound`, and its
route description must match the expected account and native recipient. The native
physical sender checks that binding again. The MCP process launcher must explicitly
preserve Graphiti startup suppression: the client does not inherit arbitrary environment.

Order: host validation -> native preflight -> Graphiti read-only preflight ->
snapshot anchor -> MCP route check -> one-inbound composition. `/poll` reads
subsequent native events; console text is never injected as an inbound message.
`/quit`/EOF/cancellation closes the runner. No historical snapshot message executes.

Real runtime, batching, frozen project, generator and enforcement are reused.

For an explicitly smaller remaining operational budget, the CLI also accepts
`--authorize-interactions=N` with N from 1 through 5. The CLI budget, live host
document, native document and hashed capability evidence must match exactly.
Existing one-response and five-interaction flags retain their exact budgets.
This does not authorize continuous/unbounded group operation or restore an arm.
Two distinct new peer IDs reject the whole batch before execution. Self, backlog
and duplicates do not spend the window. The host becomes Disabled after the first
admitted interaction, even if silent. At most one transport attempt is permitted,
with native preflight repeated before it. Uncertain outcomes require reconciliation;
no retries or inferred delivery. Reports distinguish `sendBoundaryAttempts` from
confirmed `SENT` receipts.

The native maintenance controller still owns capture/isolation and cleanup. Stop
the runner before disarming, drain and restore original artifacts, launcher and
temporary owner settings using the existing rollback procedure. Never leave the
window armed after closing. Changed revisions require new reviewed evidence.

## Verification and limitations

- Local .NET: 1,005 passed, 45 live experiments skipped; build 0 warnings/errors.
- Native sender: 22 focused tests; package typecheck/build and installed-layout
  LID smoke passed, using declared dependencies only and zero real sends/models.
- Three isolated full-host readiness starts passed, status with capture OFF.
- Frozen v3 hash is unchanged. Its send nodes have `shadowed`, not `sent`, edges.
  Confirmed speech is still committed to history, but the exact frozen graph has
  no post-send transition on that port. This one-response window does not validate
  multi-turn live FSM continuity. No edge/rule was changed or silently aliased.
- No active deploy or real response occurred. Private rollback-directory creation
  was rejected by the tool authorization layer before execution; it remains absent.
