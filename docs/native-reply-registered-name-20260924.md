# Native replies and registered participant names

## Cause

NativeObservationReader discarded WhatsApp contextInfo, then the trusted inbound
canonicalizer discarded reply again. Message projection exposed only a reply ID.
The exact Graphiti participant API omitted display_name_current even when present
on the scoped canonical Person. Native sender names were consequently empty.

## Correction

- Preserve stanza ID and quoted text/captions, with sender-supplied quote provenance.
- Reject an explicit foreign conversation reference. Never execute a quoted message.
- Map known quoted actors through reader-local native identity bindings only.
- Preserve optional quote context through canonicalization and model projection;
  native identifiers remain opaque model references.
- Expose display_name_current from the exact scoped entity read. Never use the
  technical node name, display-name matching, or cross-namespace lookup.
- Enrich incoming and known quoted participants before admission, validating
  namespace, canonical UUID, stable actor ID and Person label. Self/media skip
  conversational identity lookup. Backend failure remains failure.
- Handle the observed Baileys GROUP_CHANGE_DESCRIPTION (24) and
  GROUP_CHANGE_SUBJECT (21) stubs as durable non-conversational ignored events.
  Unknown/missing-body events still fail closed.

## Verification

1,290 .NET tests passed, 50 skipped; four Python API tests passed against an
isolated source copy with fake backend. Reply-drop regressions and the API-name
regression failed before correction. Full build ran with the test suite.

Private read-only smoke replayed the original captured quote through the new
adapter/projection and resolved the registered sender name from the real scoped
Graphiti entity: PASS, zero executions/sends. It also validated the protocol
notification that had stopped the old runtime. No Graphiti data was modified.

## Deployment/rollback

Private operational backup: drift-continuous/before-reply-identity. Existing
runtime build retained. Launcher now selects state/reply-identity-build-20260924.
Only two Graphiti API source files were replaced, with originals preserved;
Graphiti API restarted, not Gateway. Project/prompt pin unchanged. Existing
history, processed IDs, inbox/outbox and read cursor were not rewritten.

To roll code back, stop the runtime at idle, restore the backed-up launcher and
two API files, restart the API and validate before resuming. Never restore old
inbox/outbox/cursor snapshots after new sends; those copies are evidence, not an
automatic replay/rollback plan.
