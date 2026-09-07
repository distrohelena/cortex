# Typed-flow slice 2 acceptance fixture

`tests/fixtures/typed-flow-slice2.json` is a project-v1, graph-v3 fixture for fake session ingress only. Its default is the stable state ID `inativa`; the labels intentionally differ from those IDs.

Every state accepts DM and group ingress into the same `accumulate` node. The selected-entry compiler retains only the producer for the selected entry; it never chooses an arbitrary first producer or merges operational outputs. The accumulator is the explicit first operational boundary immediately after an entry, with one accumulator on each selected entry path. Runtime node IDs are deliberately the same in every state: `accumulate`, `history`, `ai`, `switch`, and `exit`, allowing a fake completion queue keyed by `ai` to continue after a state transition. A released batch is passed with a bounded, pre-batch history snapshot to one structured instruction. The instruction exposes `acao`, `proximo_estado`, and `motivo`; only `responder` reaches the switch-mode exit, whose exact enum values map to stable state IDs. The fixture contains no send node, provider configuration, or live-generation configuration.

Acceptance coverage belongs in `SessionSliceAcceptanceTests`: fake-clock quiet, count, and wait release behavior; provenance and duplicate identity; session/node buffer isolation; immutable history cutoffs; per-session continuation serialization; project snapshot stability; state transition success/failure; and safe structural traces. Unit tests use `SessionFlowRuntime` directly with fake ingress, fake completions, and `FakeTimeProvider`; HTTP tests use the local fake-ingress API described in [typed-flow-slice2-api.md](typed-flow-slice2-api.md). Neither test path uses wall-clock delays or live services.

Session runtime state is ephemeral: stopping/restarting a runtime discards sessions, history, buffers, and transitions. A fresh runtime begins at the project `initialStateId`. The editor's standalone Run control remains the single-flow slice-1 preview; session nodes have no standalone Run UI. The HTTP fake session API is the interactive path for slice-2 ingress, while fake-clock Core tests cover timing deterministically.

## State transitions take effect at batch boundaries

A batch captures its project/graph snapshot, state ID and state version when it opens. An operational transition updates the session store immediately, but cannot change, move or artificially close a batch that is already open. Further messages complete that batch under its original limits and snapshot. Closing produces exactly one continuation using that original graph; the next batch consults the then-current operational state.

For example, a count-only batch opens in `inativa` (version 0) with its first message. A preceding execution changes the session to `atenta` (version 1). Messages two and three still complete the old three-message batch in `inativa`/0. A fourth message opens a batch in `atenta`/1. There is no duplicate admission or implicit carry into another graph.

Trace distinguishes `batchStateId`/`batchStateVersion` from `currentSessionStateId`/`currentSessionStateVersion`. The session values describe the state when the trace event was recorded, not when its batch opened. The old continuation cannot overwrite a newer state using a stale transition: version-guarded transition validation still applies.

This routing rule applies only within the same administrative project publication. It does **not** pin later messages to a superseded project version. Existing immutable project snapshots and publication boundaries remain separate; administrative updates with incomplete count-only buffers need their own policy before live use.
