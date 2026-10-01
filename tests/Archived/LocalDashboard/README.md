# Superseded local dashboard test drafts

These two source files are preserved, not passing tests or implemented features.
They belong to the incomplete 2026-09-05 local settings dashboard. The flow-engine
specification explicitly superseded that dashboard and records that its components
were never delivered:

- `docs/superpowers/specs/2026-09-05-filomena-flow-engine-design.md`, sections 1-2.
- `docs/superpowers/plans/2026-09-05-filomena-local-dashboard.md`, tasks 1 and 6.

On 2026-10-01, the Cortex acceptance review moved these drafts from the transport
test project. Their `.cs.txt` extension deliberately prevents compiling references
to nonexistent `Filomena.App.Settings` and `Filomena.App.Frontend` implementations.
No assertions were weakened, replacement implementations invented, or dashboard
coverage claimed. Reassess reuse only if the corresponding product work resumes.
All other transport tests remain in the executable test project.
