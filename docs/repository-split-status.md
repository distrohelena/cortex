# Cortex / Filo split status

Source checkpoint: `557eab0e073452b9287c7b819c6455ac6258dd33`.
Approved native execution: 2026-10-01. No agents, push or live operation.

## Code boundary

The source manifest enumerates all 1,427 checkpoint files: 388 Cortex,
1,036 Filo and three shared repository metadata files. Filo has its own Git
directory, no object alternates and no remote. The full common history remains.

Cortex owns six production projects, four infrastructure test projects and the
reusable editor. Filo owns App/Llm and application tests. Thirteen mixed test
files moved: two Core, six OpenClaw and five Evaluation. Shared test fixtures
remain test-only Cortex dependencies. No runtime dependency points to Filo.

All 53 compatibility hash values are retained, assigned an owner and checked at
their current path. Frozen artifacts use byte-preserving Git attributes.
No prompt, model, memory, identity or delivery semantics were intentionally changed.

## Local acceptance

- Cortex: 1,511 .NET passes, 10 live skips.
- Filo: 887 .NET passes, 49 live skips.
- Combined: the same 2,398 passing cases as the original checkpoint.
- Cortex architecture: 10 passes; editor: seven passes.
- Filo architecture/frontend/tool tests: 163 passes.
- PostgreSQL, local-socket/database lifecycle acceptance and live services remain
  excluded. Offline results do not prove deployment.

## Operational boundary

The original folder has NOT been renamed. The active launcher and supervisor
refer to scripts beneath `C:/dev/filomena`; the published application itself is
outside the repository. Original Filo-owned files remain on disk, removed only
from the Cortex index and locally excluded. DREAMS remains private and untouched.

Required next live operation: stop/drain the known supervisor in a controlled
window, migrate its script/root references to the Filo checkout, rename the
original folder to Cortex, verify launch inputs and restore operation. This is
not implicitly authorized by a source split. No alias or junction hides old paths.

Until that operation, builds use `-p:CortexRoot=C:/dev/filomena`. The default
is sibling `../cortex`; absence fails with `CORTEX_ROOT_INVALID`.

Recovery is through the preserved source commit, ownership manifest and unchanged
operational files, not a destructive reset. No private state was copied to Filo.
