# Filomena .NET Phase B

Phase B owns conversation scheduling and turn arbitration in C#. It does not
send WhatsApp, Telegram, or Discord messages.

## Runtime behavior

For every normalized inbound OpenClaw MCP event with a canonical conversation:

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

No second arbiter model is configured.

## Run

Keep the user-managed OpenClaw Gateway running, enable the LM Studio local
server with `google/gemma-4-e4b` already loaded, then run:

```powershell
dotnet run --project src\Filomena.App\Filomena.App.csproj --no-build
```

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
- There is no full Filomena personality/generation call yet.
- There is no `messages_send` or native reply call yet.
- A structural reply-to-Filomena signal is consumed only if transport metadata
  explicitly marks `reply_to_filomena=true`; it is never inferred from display
  name, ordering, or timing.
- Pending bursts are intentionally ephemeral and disappear on process restart.
- The local LM Studio endpoint was unavailable during the final offline
  verification on 2026-09-04, so a real-model decision remains a separate
  canary after the server is enabled.
