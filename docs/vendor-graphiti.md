# Vendored Graphiti provenance

`vendor/graphiti/` is an imported Graphiti snapshot whose commit history is
retained as the second-parent history of the import merge.

## Imported history

- Vendor source/base commit: `aa68b38fcdee835c6119a450f728ae3ee54a2fca`
- Final staging adaptation commit: `537a348614c2547c300a425ddbaa50c79b38a834`
- Import merge commit: `1cc37dd4dfd4d5dc1a93e25525991c5a70eee219`
  - First parent: `5a2c6123c2f842241381812908f660b80603f50d`
  - Second parent: `537a348614c2547c300a425ddbaa50c79b38a834`

The base commit already includes inherited Contextable fork divergence and
commits. That divergence was intentionally preserved as vendor history. It is
not claimed as part of the final five-file adaptation described below.

## Final adaptation

The final staging adaptation changes exactly these repository-relative paths:

- `graphiti_core/driver/falkordb/operations/search_ops.py`
- `graphiti_core/driver/falkordb_driver.py`
- `mcp_server/docker/Dockerfile.standalone`
- `mcp_server/pyproject.toml`
- `mcp_server/src/services/factories.py`

## Navigating vendor history

The import added the `vendor/graphiti/` prefix while joining unrelated
histories. Path history from the prefixed working tree therefore does not
transparently continue into the original unprefixed Graphiti paths. Inspect the
import merge's second parent (the staging adaptation commit) instead:

```sh
git show --stat 1cc37dd4dfd4d5dc1a93e25525991c5a70eee219^2
git log --oneline --decorate 1cc37dd4dfd4d5dc1a93e25525991c5a70eee219^2
git show 1cc37dd4dfd4d5dc1a93e25525991c5a70eee219^2 -- graphiti_core/driver/falkordb_driver.py
git log --follow 1cc37dd4dfd4d5dc1a93e25525991c5a70eee219^2 -- graphiti_core/driver/falkordb_driver.py
git diff 1cc37dd4dfd4d5dc1a93e25525991c5a70eee219^2^ 1cc37dd4dfd4d5dc1a93e25525991c5a70eee219^2 -- graphiti_core/driver/falkordb_driver.py
```

Use the same unprefixed path form for other files in the second-parent
history.

## Vendor boundary policy

- Everything under `vendor/graphiti/` is vendored upstream material. Nested
  `AGENTS.md`, `CLAUDE.md`, workflows, manifests, lockfiles, and tests do not
  define policy for the root project.
- Root-wide automation must exclude `vendor/graphiti/` unless the automation
  is explicitly testing or updating the vendored Graphiti snapshot.
- Vendor updates must be prepared through a staging repository and imported
  with exact tree contents and unsquashed history preserved. Do not directly
  rewrite the vendored tree during unrelated root-project work.
