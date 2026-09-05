# Filomena .NET Phase B

Phase B owns conversation scheduling and turn arbitration in C#. It does not
send WhatsApp, Telegram, or Discord messages.

## Runtime behavior

For every normalized inbound `message_received` event delivered by the local
OpenClaw bridge with a canonical conversation:

1. enqueue it in an isolated per-conversation channel;
2. aggregate a short burst using the configured quiet and maximum windows;
3. build a bounded recent-chat view;
4. ask the already configured LM Studio Gemma model for `Respond`, `Wait`, or
   `Ignore` as structured JSON;
5. validate all returned message and actor references against the supplied
   context;
6. log the decision with the explicit suffix `No response was sent.`

The OpenClaw `transportOnly` guard remains enabled. The OpenClaw agent does not
answer independently, and the C# Phase-B worker has no outbound transport
dependency.

OpenClaw 2026.8.1 MCP `events_wait` observes session-message events rather than
the raw accepted channel event. A `before_dispatch` transport-only claim ends
the turn before that MCP event is published. The memory plugin therefore owns
a deliberately narrow bridge: it normalizes the public `message_received`
hook, POSTs a bounded schema to the C# loopback endpoint, and performs no
arbitration, generation, scheduling, or reply logic. Graphiti capture and the
bridge fail independently.

## Configuration

The default app mode in `src/Filomena.App/appsettings.json` is now `phase-b`.
The same file contains:

- allowlisted DM native IDs;
- Filomena textual aliases used as arbiter signals;
- DM/direct-group/normal-group debounce and max-burst values;
- recent-context limits;
- one `Wait` reevaluation delay;
- LM Studio endpoint `http://localhost:1234/v1/`;
- model `google/gemma-4-e4b`.
- loopback bridge listener `http://127.0.0.1:5147/` and path
  `/v1/openclaw/events/inbound`.

No second arbiter model is configured.

Enable the producer in the OpenClaw memory plugin configuration:

With the Gateway under Helena's control, enable before its next operator-managed
restart (the endpoint and timeout below are defaults):

```powershell
openclaw.cmd config set plugins.entries.openclaw-memory-graphiti.config.eventBridge.enabled true --strict-json
openclaw.cmd config validate
```

```json
"eventBridge": {
  "enabled": true,
  "endpoint": "http://127.0.0.1:5147/v1/openclaw/events/inbound",
  "timeoutMs": 1500
}
```

The endpoint is restricted to loopback HTTP by configuration validation. The
wire payload contains only canonical conversation/actor identity, provider
message ID, timestamp, text, reply metadata, and bounded provenance. Provider
objects, credentials, tokens, cookies, and authorization headers are not
forwarded.

## Run

Keep the user-managed OpenClaw Gateway running, enable the LM Studio local
server with `google/gemma-4-e4b` already loaded, then run:

```powershell
dotnet run --project src\Filomena.App\Filomena.App.csproj --no-build
```

The application should first log:

```text
Filomena inbound event bridge listening at http://127.0.0.1:5147/v1/openclaw/events/inbound
```

The Gateway must be restarted by its operator after enabling or updating the
OpenClaw plugin so it loads the bridge producer.

Send a short burst in one of the admitted conversations. After the configured
quiet window, expect a log similar to:

```text
Phase-B turn decision: conversation=whatsapp-group-... decision=Respond reason=DirectAddress anchor=... target=... messages=2. No response was sent.
```

If LM Studio is unavailable or produces invalid output twice, groups fail
closed to `Ignore`. A clearly addressed allowlisted DM may fall back to
`Respond`; Phase B still only logs that decision.

## Boundaries and known limitations

- Graphiti ingestion remains in the existing OpenClaw plugin; C# does not read
  or write Graphiti yet.
- The MCP client remains available for milestone-A inspection and future tools,
  but Phase B does not use `events_wait` as its inbound source.
- There is no full Filomena personality/generation call yet.
- There is no `messages_send` or native reply call yet.
- A structural reply-to-Filomena signal is consumed only if transport metadata
  explicitly marks `reply_to_filomena=true`; it is never inferred from display
  name, ordering, or timing.
- Pending bursts are intentionally ephemeral and disappear on process restart.
- Bridge delivery is currently best effort: one POST with a bounded timeout.
  Messages sent while Filomena.App is stopped are logged as delivery failures;
  there is no durable replay queue in this phase. Receiver deduplication retains
  the last 10,000 conversation/message keys in memory and resets on restart.
  The listener trusts local processes and accepts JSON only, with a 256 KiB
  body limit. It is not intended to be exposed on a network interface.
- The local LM Studio endpoint was unavailable during the final offline
  verification on 2026-09-04, so a real-model decision remains a separate
  canary after the server is enabled.
