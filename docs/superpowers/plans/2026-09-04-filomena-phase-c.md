# Phase C: standalone generation and MCP send

**Approved goal:** consume Respond in C#, generate with the existing Gemma, send
standalone via the installed messages_send tool. No native quote (phase D), no
Graphiti migration/recall (phase E), no gateway operations or live test sends.

**Design:** retain bridge inbound and optional phase-b dry run. Add an optional
turn executor awaited inside each conversation processor, keeping other chats
independent. Snapshot bounded context; remember only confirmed outbound turns.
Keep one lazy, long-lived MCP connection, reconnect on a subsequent turn after
failure, never retry an uncertain send. Restrict outbound by explicit namespaces.
Use a minimal configurable system prompt, not workspace/DREAMS.md.

## Implementation order

1. Add failing tests for MCP messages_send schema, receipt validation and no
   replay; implement provider-neutral sender and persistent MCP connection.
2. Add failing tests for free-text generation with the same configured model;
   implement generation separately from arbitration JSON.
3. Add failing tests for Respond-only execution, allowlist, blank/silent output,
   failure isolation, context boundaries and confirmed outbound history.
4. Integrate optional executor into per-conversation scheduling; test messages
   received during generation and independence of another conversation.
5. Wire phase-c in Generic Host and configuration; preserve phase-b dry run.
6. Run dotnet test Filomena.slnx, dotnet build, dotnet format verification and
   git diff --check. Commit only scoped files on main, never DREAMS.md or push.

## Acceptance / limits

- One MCP send attempt per successful generated turn; no blank/silent sends.
- Unknown namespaces are observation-only. Three initial configured namespaces.
- Ignore/Wait never call full generation; failures leave conversation runnable.
- Same channel model/endpoint as arbitration; no model load API or second model.
- Correct opaque session routing, no identity derived from display/session text.
- Confirmation requires provider messageId; unknown/timeout is not success.
- Bridge and turn state are ephemeral; no guarantee across restart or provider
  chunks. No replay/recovery queue introduced in phase C.
- Real canary requires Helena to run App and Gateway and send a new inbound.
