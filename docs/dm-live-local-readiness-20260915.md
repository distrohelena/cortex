# DM Live — local preparation, not enabled

Date: 2026-09-15. No deployment, live send, model call or Graphiti call in this preparation.

## Frozen baseline

The five-message WhatsApp Shadow run is documented separately in
`whatsapp-shadow-five-20260915.md`. It does not prove real-send continuity.
Brain v3 remains unchanged (SHA256
`47D1D4521BCF013E919DDCB3DE5900485DDA7F8BACEFA7437FEDFBDFFC9BC55E`).

## Implemented and tested locally

- `SendReceipt` preserves observed channel, account, recipient and gateway run ID.
- Missing metadata remains null. Nothing is inferred from an opaque session route.
- Malformed metadata is an uncertain outcome and throws; the adapter never retries.
- These fields are evidence only, not authorization or delivery/read confirmation.
- Seven new fake-MCP regressions failed before the implementation and pass after it.
- An existing invalid-actor test used newline-sensitive string replacement and could
  accidentally submit the valid fixture on Windows. It now changes JSON fields
  structurally and asserts that the invalid identity is actually present. No
  receiver behavior was changed.

Verification: OpenClaw tests 66 passed; Core tests 501 passed, 10 live experiments
skipped; application and dependencies compiled during the OpenClaw test build;
targeted `git diff --check` passed.

## Remaining implementation gates before real send

1. **Durable attempt ownership.** The current Shadow outbound host has no sender.
   Reserve a unique real attempt durably before external effect; crash/pending or
   ambiguous failure must become indeterminate and require reconciliation, never
   automatic retry. Do not promote old shadow records into send attempts.
2. **Pre-send destination and physical-send contract.** The MCP API takes a session
   key and text. Its bridge currently generates a new idempotency key per call.
   Receipt metadata after sending cannot prevent a wrong destination. Validate an
   exact host-owned account/DM route before dispatch and establish a stable attempt
   identity, one physical message, and no hidden queue/retry/chunking behavior.
3. **Confirmed speech commit.** The current typed send node has only disabled,
   shadowed, blocked and failed paths. It suppresses target-state application after
   a send attempt. Add a separately tested confirmed-sent path: append speech once,
   then apply the existing transition once; uncertain outcomes do neither. Ensure
   a following queued batch sees confirmed predecessor speech without importing
   future inbound, repinning the project, or treating WOULD_SEND as speech.

Use the existing controlled-outbound design as the implementation baseline; this
document does not authorize a parallel sender or changes to brain rules. Any live
operational graph/pin must explicitly preserve the frozen brain configuration.

## Required local acceptance cases

Confirmed receipt; wrong/missing route; timeout after possible acceptance;
cancellation after dispatch; crash after reservation; duplicate invocation;
restart with pending attempt; two queued turns; receipt before local commit crash;
Shadow producing neither speech nor applied transition. Verify exact session,
self suppression and legacy isolation throughout. Audit native message-sent hooks
before live activation so Graphiti read-only remains true.

## Operational boundary / rollback

No active files or settings were changed. Current work is local source/test changes
only. Do not restore/reset the entire dirty worktree to undo this slice: revert
only its receipt metadata additions and fixture edit if necessary.

DM Live remains **NOT READY**. Real sending requires the remaining gates, explicit
host arming, an exact-DM operational window and separate user authorization.
Shadow remains incapable of enabling outbound. No canary or send is requested now.

Update, same day: the three implementation gates above now have a tested local slice
(fake transports only); see `dm-live-local-preparation-20260915.md` for what is proven
locally and what remains before any real send.
