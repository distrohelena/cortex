# Shadow Bootstrap + Preflight implementation

Scope approved: infrastructure only; no listener, send, bridge changes, live mode, model calls or external mutations.

1. Add tests for a frozen host binding, exact identity validation, durable inbox and crash quarantine.
2. Implement a small inbox journal and manual handoff to the existing session runtime, not a new batching loop.
3. Add read-only Graphiti probe contracts and tests for namespace, canaries and outage. Never substitute an empty result for failure.
4. Add a separate early-return CLI preflight, static by default. The current transport remains NOT READY: no safe live inbound adapter can be inferred from transcript events.
5. Document source comparison, minimum bridge contract, restart semantics and remaining operational proofs. Run focused and structural tests.

Safety boundaries: host configuration is separate from project JSON; no API starts a listener; no outbound transport dependency; no memory writing. Current source capability failures cannot be overridden by project/host JSON booleans. Live checks require explicit opt-in and trusted bindings; absent evidence is FAIL, not a warning. No commit of unrelated experimental history.

Completed: tests-first inbox, pure preflight checks, immutable pin, internal runtime handoff, read-only Graphiti probe and isolated CLI. Source implementation stops at the proven contract gap: there is no eligible real adapter and no listener/start action. Overall status NOT READY. Local verification: 474 Core, 52 backend, 102 frontend passed; solution build/format/diff checks passed. See docs/shadow-bootstrap-preflight.md for contract blockers, restart limitation and evidence. No live calls or commit.
