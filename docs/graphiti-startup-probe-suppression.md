# Graphiti startup probe suppression — isolated provisioning slice

## Implemented scope

Only the isolated `plugins/openclaw-memory-graphiti` source changes. The active
plugin, Gateway, personal configuration and OpenClaw installation are untouched.
This external plugin belongs to the Filomena repository, not the native OpenClaw
repository. No deploy, restart, capture arm or live preflight was performed.

The host/deployment may supply the process environment variable
`OPENCLAW_GRAPHITI_SUPPRESS_EXTERNAL_STARTUP_PROBES=true` before plugin
registration. Unset or exactly `false` retains the existing startup behavior.
Any other value fails registration with `startup_probe_policy_invalid` before
client initialization. No personal environment variable was set in this slice.

The value is pinned at registration, not read from project JSON, pluginConfig,
service-start arguments, tools, prompts or memory. PluginConfig rejects the new
option as an unknown key. Late environment changes do not alter the registered
service. As with other host environment controls, this is not a sandbox against
native code already running with the host's OS privileges. The operator must
keep process startup/environment management outside project/model authority.

## Suppressed startup

Local initialization and tool/hook/service registration remain intact. The
service's automatic external startup block is skipped, including:

- Graphiti `healthCheck()`;
- SpiceDB `readSchema()`;
- conditional initial SpiceDB `writeSchema()`.

The existing logger emits a structured `startup_status` diagnostic:

```json
{"initialized":true,"externalReadiness":"not_checked","startupProbeSuppressed":true}
```

This is an explicit log diagnostic, not a new endpoint or a health certificate.
Host service initialization must not be interpreted as external readiness.
There is no `healthy` result manufactured for the suppressed branch.

## Boundaries retained

This option is NOT a network kill switch and is NOT a live-execution permission.
Explicit `memory_status`, recall, authorization, namespace resolution, capture
hooks and error handling retain their existing behavior. For example, an explicit
status check still executes its checks and reports an outage as unreachable.
No outage is translated into an empty memory by this change.

Before a future Shadow Run, the separate Graphiti Live Preflight remains mandatory:
reachability, namespace, participant binding, boundaries, social memory, canaries
and empty-versus-outage. `not_checked` does not satisfy that gate. This slice
does not add another Graphiti evaluator or replace the existing runtime preflight.

Future provisioning must install the reviewed external plugin revision as well
as the native Observation build. Then the operator may set the option in the
trusted launch environment for that provisioning operation. Removing the option
and re-registering/restarting restores normal startup probes. Neither action was
performed here. Dispatch isolation is still a separate blocker/gate; suppressing
probes does not make opaque handlers `inactive`.

## Verification

TDD: the new suppression test first failed because startup still called the
Graphiti health endpoint (mocked); malformed host input also failed its test.
After the implementation:

- Unit suite: 231 passed, 13 skipped, zero failures (19 files passed, 1 skipped).
- Existing plugin typecheck: passed (`tsc --noEmit`).
- Focused behavior covers all startup RPC suppression, truthful `not_checked`,
  default/false behavior including schema initialization, registration-time pin,
  rejected plugin-config authority, malformed host input, failed ordinary checks
  and explicit status checks after suppressed startup.
- All backend operations in these tests use fakes/mocks. No real Graphiti,
  SpiceDB, model, outbound, agent dispatch or continuous listener was invoked.

No active startup/deploy integration claim is made. No commit or push.
