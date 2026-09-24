# Optional native group replies

The approved group variant adds a nullable `replyTo` to generation and regeneration.
Only real same-session message refs are selectable. Null remains a plain message;
there is no latest-message default or extra model call. Persona, participation,
memory and Boundary rules are unchanged.

The host resolves the opaque ref, binds it to the candidate, persists the native
target in the outbound journal, and checks the receipt. Regeneration replaces the
target, including clearing it with null. Sent/history recovery never resends.
Native contract: `whatsapp-bound-group-reply:v1`; old plain contracts remain closed.

Native resolution uses exact account/group/message cache keys. The socket receives
the retained original as `quoted` after the last synchronous authority fence.
Missing or expired metadata blocks submission; there is no silent plain fallback.
Existing native cache retention remains 10 minutes / 500 entries. This is a
retention limitation, not permission to reconstruct a quote from model text.

## Verification

- .NET: 1,314 passed, 50 skipped; build: zero warnings/errors.
- Native focused receiver/cache/sender: 63 passed.
- MCP + Gateway: 52 passed.
- Core/extensions typechecks and changed-source lint passed.
- Full native build passed. Standalone WhatsApp package required its own supported
  builder; the root build did not update `extensions/whatsapp/dist`.
- Compiled installed-package smoke passed with the actual gateway, receiver and
  quoted sender, fake socket only, one native invocation and rejected duplicate.
- Checkpoint migration: 21 passed, including the actual immutable artifact pair.
  Migration requires exact pins plus structural compatibility, not a hash bypass.
- Review was author self-review, not an independent review.

## Operational record

The earlier active process had stopped before this slice completed on a native
encrypted `MESSAGE_EDIT`. Exact same-session edit-control mapping was tested;
unknown/malformed events remain fail-closed. Old project was resumed while work
continued. No optional reply behavior was enabled during those checks.

For migration, runtime stopped at idle. Old epoch closed at sequence 241 with no
pending record, confirmed read-only from native persisted state. History (205
messages), session state and processed IDs were transferred to a new private
directory; original journals remain untouched. No execution or send occurred in
the transfer. An old observer window reported a stale receiver UUID at cleanup;
its cleanup still confirmed capture OFF and healthy journal.

Private staging/rollback: operator profile `.config/openclaw-observation-ops-20260924-group-reply`.
24,214 copied files were hash-verified; standalone WhatsApp stage was refreshed
after its separate build. Active build and WhatsApp hashes match tested artifacts.
Host config/credentials remain unchanged. Startup and runtime resumption must be
verified before claiming live replies active.

### Resumption verified

Controlled deployment completed; startup proved capture OFF and healthy journal
before explicit rearm. Exact-group isolation remained revision-bound and Dispatch
Inspection returned inactive. A new epoch was opened, and the runtime started
from sequence zero rather than skipping post-arm snapshot messages.

Transport and Graphiti preflights passed. `run_started` confirmed restoration of
205 history messages. Final checks: process alive, `/healthz` 200, `/readyz`
ready=true, explicit native reply capability enabled, two host-owned reply port
bindings, no ABORT. No artificial live message was sent to test replies. Actual
quoted delivery still needs observation from a naturally/manual-triggered reply.

The continuously authorized group runtime remains running, with its observation
window/isolation held open as before; this is not a bounded canary window cleanup.
Private runtime launcher now lives in the new operation directory. Graceful stop
uses its `stop.request`; do not target the superseded launcher's directory.

Rollback uses the existing controlled deployment procedure and the displaced
original artifact directories. Never restore an old inbox/outbox after a new send.
If runtime rollback is needed after sends, reconcile current journals first.
