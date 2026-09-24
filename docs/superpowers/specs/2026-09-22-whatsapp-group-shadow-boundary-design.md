# WhatsApp Group Shadow Boundary — design

## Outcome and scope

Prove one host-approved WhatsApp group can feed the existing Filomena v3 session runtime and produce `WOULD_SEND` without any transport send. This is not authorization for a live group reply. DM behavior, the v3 brain, Graphiti data/semantics and the current DM live route remain unchanged. No group is selected by project JSON, a prompt or a model.

## Existing boundary

The typed flow compiler already has a group entry and the identity layer derives a distinct WhatsApp group namespace. The operational observation path is DM-only: native capture rejects `@g.us`, status reports `kind=dm`, the .NET reader maps records to `Dm`, `ShadowSessionBinding.Accepts` requires `Dm`, the native isolation gate is exact-DM only, and `ShadowOutboundHost.GuardAsync` rejects non-DM before even producing a shadow journal result. The older MCP bridge's group mapper is not the durable observation source and cannot substitute for it.

## Design choice

Add an enumerated **group shadow scope** alongside, not inside, the DM-approved scope. Reuse the journal's durable admission and reader mechanics; add a versioned group identity envelope containing exact account, `@g.us` conversation, authenticated participant JID, native message ID, `fromMe` and canonical self identity. Keep `inbound-observation:v1` strict for DM; a group-capable contract must advertise and validate its own version rather than silently relaxing V1. Missing/ambiguous participant identity fails closed before runtime admission.

Host configuration binds one exact group and a bounded set of trusted participant identities for the initial pilot. No display-name matching. The native group isolation proof must cover normal ingress, durable replay, restart recovery, imperative dispatch and command/plugin paths for that account/group, tied to the same effective-config revision as raw dispatch evidence. Any uncovered or stale path leaves inspection `active` or `unknown`; capture may not be armed for the pilot. Raw path evidence is retained alongside effective exclusion. Group isolation must not affect the tested DM or other groups.

The .NET reader maps a verified group record to `SessionEntryKind.Group`, checks account/group/actor/self/cursor/epoch exactly, and resolves each actor to a canonical participant in the group's namespace. The runtime uses the existing group graph, batching, FSM, memory/Boundary lookup and v3 models without prompt or rule changes. Group-specific Graphiti preflight is read-only and must prove namespace, participant binding, canary isolation and empty-versus-outage before the first shadow execution.

The outbound host may record `WOULD_SEND` for an allowlisted exact group **only in `Shadow` mode** with ordinary provenance and enforcement checks. `Enabled` remains categorically blocked for groups; no bound group transport sender is constructed. A hypothetical response is not inserted into sent history and does not trigger a spoken-state transition.

## Pilot and failure behavior

Start with a local isolated fake/native-contract test, then (only after separate operational readiness) an exact-group live observation pilot. Open produces context/cursor only; a later poll offers one or a few real inbound messages once. Reject wrong group, wrong account, untrusted participant, self/echo, stale revision, gaps, duplicate execution, legacy dispatch eligibility, Graphiti outage and any send attempt. Disarm capture and isolation after the bounded window. No automatic canary or message send.

## Verification deferred while GPU is busy

Do not run tests, builds, local model calls, Graphiti, services or live probes during the current GPU-use window. Once available, use test-first cycles for: native group identity and wrong/missing participant; exact-group isolation including replay/recovery and unrelated chats; version/reader mapping and cursor/dedupe; participant-scoped memory provenance; group SHADOW journal result and categorical Enabled rejection. Then run focused regressions, full relevant suites, typecheck/build/lint and a compiled smoke before considering any live pilot. No production-code change is claimed by this design document.
