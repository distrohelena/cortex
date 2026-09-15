# Transport provisioning with startup suppression — 2026-09-11

## Result: FAIL before dispatch inspection / arm

Active version verified before deployment: OpenClaw `2026.8.1`, commit
`ea806575e6450e4d1efdfc72c19f04be982a1b9b`. The isolated checkout uses the same
base. The active Graphiti plugin matched the isolated plugin's pre-change base;
only the tested 17-line startup-probe suppression change was applied to it.

## Deployment and first blocker

The core was packaged locally with `npm pack --ignore-scripts`, respecting the
official package excludes. The separate native WhatsApp runtime was built from
the validated isolated source. No source changes were made during provisioning.

The Gateway was stopped through its native CLI (`gateway stop --force`), and
process absence was checked before replacement. Replaced components:

- installed OpenClaw `dist` with the coherent packaged build;
- installed `@openclaw/whatsapp` `dist` with the native producer build;
- active external Graphiti plugin `index.ts` with the tested suppression version;
- Gateway launcher: suppression environment variable and Observation host-file
  environment reference.

The trusted host file was created in a restricted-ACL operational directory.
Its exact DM was obtained from the existing host guard's single canonical DM
allowlist, with account `default` (no account overrides). Credentials were
generated independently and were never printed or placed in project JSON.

New Gateway PID 42932 started at approximately 20:33:40 local time. It progressed
through configuration/authentication/startup logging and eventually opened ports
18789 and 18796. However, bounded authenticated `GET /inbound-observation/v1/status`
requests initially returned connection refused and later timed out at 5 seconds.
The final two status checks timed out. No status body was obtained.

This did NOT establish a healthy/provisioned bootstrap. The operation stopped
at that gate. It is an operational responsiveness failure, not a proven SQLite,
identity, or Dispatch Inspection defect. No heuristic fallback or timeout tuning
was applied. Root cause remains to be diagnosed in isolation.

## Rollback and recovered service

Backup directory (operator-only; contains sensitive configuration, do not share):
`C:/Users/Helena/.config/openclaw-observation-rollback-20260911-v1`.

Backup includes core dist (10,219 files), WhatsApp dist (68 files), original
Graphiti plugin file, launcher files, original OpenClaw config and package
manifests. Original runtime directories were also preserved by rename. The
failed deployed runtime directories and staged package remain available for
diagnosis; nothing was recursively deleted.

The unresponsive experimental Gateway was stopped by its verified PID. Original
core and WhatsApp runtime directories were restored, and the Observation host
environment reference was removed from the launcher. Core/WhatsApp entry hashes
were verified against their pre-deploy backup. The attempted whole-directory hash
scan was interrupted by the rollback's directory rename and is NOT claimed as a
completed full manifest verification.

Only the approved Graphiti suppression plugin change and
`OPENCLAW_GRAPHITI_SUPPRESS_EXTERNAL_STARTUP_PROBES=true` remain active, consistent
with the instruction to retain suppression. Restoring the old Graphiti startup
would have reintroduced the prohibited external probes. The Observation host
file remains protected but is not referenced by the launcher.

The restored service was started through the existing scheduled task. Recovery
checks returned:

```json
{"path":"/healthz","http":200,"result":{"ok":true,"status":"live"}}
{"path":"/readyz","http":200,"ready":true,"failing":[]}
```

These HTTP routes read local liveness/readiness state; they are not Graphiti
probes. Normal startup logged at 20:42:29.325:

```json
{"initialized":true,"externalReadiness":"not_checked","startupProbeSuppressed":true}
```

The active plugin SHA-256 matches the tested isolated file:
`3B33E7652BCDF77D053806C71B5BBA93ED6AB9A93C54EEEAF173A0112FA3B0D4`.
Thus the evidence is not merely absent logs: the deployed code identity, explicit
executed suppression branch, and prior fake-backed tests establish that its
automatic Graphiti/SpiceDB startup checks were skipped. No packet-level audit of
unrelated Gateway activity was performed.

## Gate ledger

| Check | Observed result |
| --- | --- |
| Active version/base | Compatible; same version after rollback |
| Graphiti startup suppression | Active and explicitly reported after recovery |
| Graphiti external readiness | `not_checked`, never asserted healthy |
| Observation bootstrap/control/read status | FAIL: no authenticated status response |
| Startup capture OFF | Not proven by HTTP; no arm was invoked |
| Journal capture evidence | Read-only SQLite query: 0 epochs, 0 records |
| Host exact DM selection | Existing canonical single-DM host allowlist; live binding not proven |
| Self PN/LID/fromMe | Live proof not reached |
| Effective revision | Not obtained |
| Dispatch Inspection / legacy isolation | Not reached; no inactive claim |
| .NET reader deployment/preflight | Not reached |
| Outbound ceiling operational proof | Not reached; no outbound mode was changed |
| Arm / epoch | Not executed / none |
| OpenObservation / cursor / Poll | Not executed / none / not executed |
| Native ID / replay live | Not proven |
| Disarm | Not needed: never armed; process terminated and host reference removed |
| Final Observation state | Not active in restored host; no captured data |
| Graphiti Live Preflight | NOT READY / NOT EXECUTED |
| Shadow Run | NOT AUTHORIZED / NOT EXECUTED |

The additive empty observation tables remain in the native SQLite database.
They were not dropped during rollback; no epoch or message data exists in them.
This is an explicit retained schema artifact, not an armed feature.

## Side-effect accounting and limits

This operation invoked zero `messages_send`, model/Luna/generation/enforcement,
Graphiti/SpiceDB live probes, or agent/conversational execution calls. It created
no canary, no observation Open/Poll, no Shadow WOULD_SEND and no continuous
conversational listener. The restored plugin's automatic startup probes were
explicitly suppressed. Limited post-startup log review found no Graphiti/SpiceDB
failure/schema-write signals; that is not a complete audit of unrelated traffic
in the existing transport lifecycle.

No prompts, FSM, boundaries, namespaces, Graphiti data, SendGuard semantics,
DREAMS or project JSON were changed. No commit or push.

## Next step

Reproduce/profile the experimental build's unresponsive startup/status path in
isolation using an appropriately sanitized operational configuration, compare
with the restored build, and establish responsive native-host proof before
another active deployment. Do not arm or request an inbound canary yet. Even
after that fix, effective dispatch inspection remains a separate required gate;
`transportOnly` and startup probe suppression do not prove isolation.
