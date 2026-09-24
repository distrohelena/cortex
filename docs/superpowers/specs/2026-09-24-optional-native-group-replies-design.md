# Optional native group replies

## Intent and scope

Filomena can optionally quote a real message in the current group when that
clarifies what she is answering. Plain messages remain valid. Do not force the
latest message as the target. Preserve existing personality, participation,
Boundary enforcement, name/received-reply fixes and no-retry send guarantees.
No artificial live sends or edits to previously delivered messages.

## Why this crosses a contract boundary

The existing .NET BoundSendRequest, MCP messages_send_bound schema, gateway
expectation and WhatsApp bound sender carry text only. The native sender submits
`{ messageId }`, without Baileys `quoted`. This is missing capability, not merely
a prompt omission. Adding it must preserve durable send provenance.

## Selected design

1. Give actual batch/history messages execution-local opaque references derived
   from their trusted provider IDs and exact session. Do not make arbitrary quote
   text or model-supplied IDs into selectable native messages.
2. Generation and regeneration return text plus an optional reply reference in
   their existing structured output. No extra model call or decision subsystem.
   Explain that reply is optional and useful for disambiguation, not mandatory.
3. Resolve the chosen reference exclusively through the execution's trusted
   same-account/same-group message map. Reject foreign, absent, fabricated or
   stale targets before send reservation. Null means plain message.
4. Bind the target to the generated candidate. Regeneration replaces both;
   Boundary enforcement continues to approve the exact text candidate. Persist
   the selected native target with the outbound reservation and confirmed history
   so restart/reconciliation does not lose reply metadata or generate a resend.
5. Add an explicit versioned group-reply native contract, leaving current text
   contracts unchanged. Carry only the trusted target reference through .NET,
   MCP and Gateway, never an arbitrary model-created WAMessage.
6. Native owner resolves the original retained message by exact account/group/ID
   using its existing retained observation/message data. Verify scope and source
   before the final authorization fence; submit the original message as `quoted`.
   Target unavailable or integrity mismatch is a failure, not a silent fallback
   to an unrelated/plain response. Do not add transcript heuristics or a new
   message store. If existing retained data cannot prove the target, block that
   target rather than widening authority.
7. Receipt must attest the selected reply target under the new contract. Existing
   single-message/no-hidden-retry behavior stays unchanged; uncertain send is
   still reconciled explicitly and never retried automatically.

## Implementation areas

- Filomena: message projection, typed generation output, OutboundExecution,
  durable outbound journal/history, OpenClawBoundMcpTransport.
- OpenClaw isolated checkout: src/mcp/channel-tools.ts, bound-send bridge/schema,
  src/gateway/server-methods/send-bound-text.ts, and
  extensions/whatsapp/src/inbound/bound-text-send.ts plus owner composition.
- New operational project revision only for reply output/instructions. Preserve
  existing context using reviewed idle checkpoint migration if required.

## Verification

- Optional null: same plain text send, no quote.
- Select an earlier actual message, not automatically the newest one.
- Native IDs never appear in model input or model-controlled authority.
- Unknown/foreign account/group/ref cannot reach native submission.
- Regeneration cannot retain an obsolete candidate's target accidentally.
- Shadow records intent but never calls transport.
- Real bound sender receives exactly one original quoted message under the
  final authority/socket fence. No duplicate call or automatic retry.
- Journal/restart/history retain the selected target, without re-execution.
- Existing received-reply, canonical-name, grouping, self suppression and send
  regressions stay green.

## Deployment

No active changes during design/local implementation. Native sender deployment
requires a controlled Gateway window with current account/group isolation,
capture OFF at restart, startup Graphiti probes suppressed and preserved rollback.
Only resume the existing group runtime after capabilities and revision proof
validate the new contract. Preserve queue/history/cursors; never restore an old
outbound journal after subsequent sends. Test live only through naturally/manual
sent messages in the already-authorized group, not synthetic sends.

## Non-goals

No auto-reply-to-last rule, new participation router, extra model/checker,
Graphiti writes, general-purpose message fetching or additional group authority.
