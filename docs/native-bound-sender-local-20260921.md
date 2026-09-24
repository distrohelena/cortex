# Native bound sender — local preparation, 2026-09-21

No deployment, real message, model call, Graphiti operation, or personal configuration change.
Brain v3 SHA-256 remains `47D1D4521BCF013E919DDCB3DE5900485DDA7F8BACEFA7437FEDFBDFFC9BC55E`.

## Implemented in the isolated OpenClaw worktree

`extensions/whatsapp/src/inbound/bound-text-send.ts` provides a native text-only operation,
composed by `socket-session.ts` and exposed on the attached listener by `monitor.ts`.
It is now connected to a distinct `messages_send_bound` MCP tool and the versioned
`send.boundText` Gateway branch **in the isolated source/build only**. No active deployment occurred.

- Contract `whatsapp-bound-text:v1`; exact native PN/LID DM and account, no alias/group fallback.
- Caller supplies its durably reserved attempt. Provider ID is derived from contract/account/DM/attempt.
- One native `sock.sendMessage` invocation; no legacy reconnect retry, generic durable delivery queue,
  markdown chunker, composing indicator, or automatic URL preview.
- Text is preserved exactly and must fit both the requested limit and the 4000 UTF-16-unit ceiling.
- A synchronous caller-owned authority fence runs again after readiness, immediately before submission.
- Request fields are copied/validated before awaiting readiness.
- A retained listener cannot adopt a replacement socket or send after shutdown.
- Provider readiness failure blocks. It does not use the legacy advisory lookup's error-to-undefined fallback.
- Timeout is uncertain, never retried. Late acceptance only updates existing echo-suppression bookkeeping.
- Receipt must identify the expected native message, exact DM, and `fromMe=true`; no invented receipt fields.
- The in-memory attempt fence is additional protection only. Durable ownership remains the .NET journal;
  native process restart is not an authorization to replay an uncertain send.

“Single attempt” refers to one application submission/native message ID, not to a claim that the
WhatsApp protocol never retransmits packets or handles a receipt internally.

## Evidence

The monitor composition tests initially failed because `sendBoundText` did not exist on the attached listener.
The readiness regression then reproduced an actual send after a failed readiness query, ending in an
unverified receipt. The controlled path was corrected to propagate that readiness failure before sending.

Focused native regression command:

```powershell
node scripts/run-vitest.mjs extensions/whatsapp/src/inbound/bound-text-send.test.ts extensions/whatsapp/src/monitor-inbox.bound-text.test.ts extensions/whatsapp/src/monitor-inbox.socket-lifecycle.test.ts extensions/whatsapp/src/inbound/send-api.test.ts --reporter=dot
```

Result: **102 passed**, using fake provider sockets. Includes unchanged legacy reconnect behavior,
shutdown, account/DM mismatch, oversize payload, repeated attempt, route/socket race, revocation,
timeout/late acceptance, invalid receipt, and fail-closed readiness.

WhatsApp package typecheck and focused lint passed after fixing one missing-braces lint finding.
`git diff --check` passed. No active installation was built over or restarted.

## MCP / Gateway / .NET connection (explicitly approved local follow-up)

The .NET legacy `OpenClawMcpTransport` and `OpenClawMcpSender` still do not implement
`IBoundOutboundTransport` and cannot enable DM Live. The tested .NET host requires that contract.

The implemented connection is a versioned `messages_send_bound` tool under the existing authenticated
Gateway send authority, passing exact destination and the durable attempt through to the native operation.
A distinct tool is necessary: an old MCP server could otherwise discard new optional arguments and
silently use legacy `messages_send`.

The user explicitly approved this local API/wiring follow-up. A tool review initially rejected the
patch again. Read-only checks established that the isolated worktree is not the installed npm package
or a symlink to it; the subsequent approved patch was accepted. No alternate execution path bypassed
the review, and no installed artifact/configuration was changed.

- MCP resolves the conversation and compares exact account, recipient, channel and session. Threads,
  groups and alias fallback are rejected. Destination/text/attempt are pinned across the await.
- Gateway keeps existing authenticated write authority, account selection, reservation and dedupe.
  The bound branch rejects media/thread/formatting/other legacy options before reaching the listener.
- Native readiness is followed by a fresh synchronous authority fence immediately before the socket call.
- The branch bypasses generic delivery retry, target rediscovery, transcript mirror and plugin send hooks.
- `OpenClawBoundMcpTransport` implements the new contract explicitly. Legacy MCP adapters stay unbound.
  The new adapter requires exact version, attempt, account, recipient, channel and provider receipt;
  uncertain results are errors, never a reason to fall back or retry.
- Host construction remains Disabled. A tool being available does not arm the Filomena host.
- MCP registration is tested through an in-memory MCP client/server; Gateway routing through the real
  handler with a fake native owner. A repeated Gateway attempt reaches the owner once, without legacy
  delivery/mirror calls. Native socket behavior has separate real-composition/fake-provider tests.

Verification of this follow-up:

- MCP/Gateway suites: **177 passed** (37 MCP, 140 Gateway), including the new registered tool/handler.
- Two existing media tests initially failed because `/tmp/...` was compared to its normalized Windows
  path. The fixture now uses `path.resolve`; no media implementation was changed. Both passed afterward.
- Core production typecheck, Gateway test typecheck and focused lint passed.
- .NET solution: **994 passed, 45 live experiments skipped** (Core 551, OpenClaw 88, LLM 355).
- Final native `build-all.mts`: exit 0, 12m54.7s; 63 plugins built, 49 control-plane modules
  verified, all 148 SDK subpaths verified. Existing dependency eval/browser-externalization warnings
  remain; no baseline/budget was relaxed.
- MCP and Gateway test typechecks passed. Formatting and diff checks passed.

No real canary has been sent or authorized by this local wiring. The live App command/window and
deployment still require operational preparation; an implemented adapter alone is not a live-readiness
attestation. There are no model calls, Graphiti operations, capture arms or installed changes here.

Current state: **bound transport connection implemented locally; end-to-end DM Live not yet operated**.
