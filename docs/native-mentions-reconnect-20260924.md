# Native self mentions and idle reconnect

## Reproduced failures

The group event at sequence 35 carried a current native `mentionedJid` matching the account's LID. The adapter retained only its numeric textual token, so participation received an unidentified mention and returned `nenhum`/`calar`. The event was completed with no send; it must not be automatically replayed as new work.

A WhatsApp 503 disconnect at 18:44:29 caused the idle reader preflight to see unavailable dispatch proof. The runtime aborted; WhatsApp reconnected at 18:44:33, but the runtime was already stopped. This was not a generation or delivery timeout.

## Changes

- Optional host-owned self mention identity contains PN, LID and display name. PN is checked against the native opaque self proof during preflight. LID is provisioned from the same trusted account credentials, not from message text/model output.
- Only current supported message context `mentionedJid` entries matching those identities may render their exact textual mention token as the agent's display name. Foreign, absent and quoted-only mentions are not promoted. Native raw journal is unchanged.
- Idle read-only revalidation can yield no approval (`null`) while dispatch is unknown or transport is unavailable. No polling/admission/execution/send is authorized by that result. The prior scope/cursor remain pinned.
- The continuous host emits `transport_wait`; the launcher schedules its next ordinary read-only poll. A valid full proof matching the original scope is still necessary to resume. Positive active paths, integrity errors, changed window/config/gate and other hard failures are not converted into authorization.
- Read-only pre-admission revision races may wait. In-flight execution/send failures are still terminal; this change does not add execution retries or uncertain-send retries.

## Verification

New behavioral tests first failed (two self mention cases and unavailable revalidation). Corrected reader suite passed. Full suites: Core 616 passed/10 skipped; OpenClaw 161 passed; Llm 546 passed/42 skipped. Build: zero warnings/errors. A single isolated current-Jev probe classified `@Filomena tá aí` as `direto`, without generation, Graphiti or transport. Launcher idle phase branch was exercised for start, interaction, wait and abort.

## Rollout

Separate build: `state/mentions-reconnect-build-20260924`. Existing launcher/config/capability files have `*.before-mentions-reconnect` backups in the private operation directory. Revalidated same epoch/config/gate/registry/retained-config; only receiver instance revision changed after reconnect. Updated corresponding host revision and hashed capability pin together.

Same operational project and `runtime-natural-v1` checkpoint/cursor are reused; no prompt, FSM, model, personality, memory, Boundary or native Gateway code changed. No artificial group message was sent. Already processed silent event is not replayed.

Rollback requires idle-stop, restore the prior binary launcher/config as a consistent set, and revalidate current transport proof before resuming. Keep the newest journal/checkpoint/cursor; do not roll them back or retry uncertain outbound.
