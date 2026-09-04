# Graphiti memory infrastructure verification

Verified on 2026-09-03 against OpenClaw 2026.8.1.

## Result

The active Filomena runtime now uses a persistent FalkorDB volume mounted at
the database's real data directory, one deterministic WhatsApp conversation
namespace, and conversation-bound memory tools and automatic recall. Existing
graphs and rollback artifacts were preserved.

The real WhatsApp group JID `5511981042361-1589823314@g.us` maps to:

```text
whatsapp-group-5511981042361-1589823314-g-us
```

## 1. FalkorDB persistence

Preflight found the live dump at `/var/lib/falkordb/data/dump.rdb`; the former
volume was mounted at `/data`. Before replacing anything, the dump was saved to:

```text
C:\Users\Helena\.openclaw\backups\falkordb\20260903-224949\dump.rdb
SHA256 3D49B6323147293D1999F472FF9FDEF8EFF17AA0B7C945163A2F0375689FEA97
```

At migration time the backup hash matched the dump inside the old container.
The old `docker-falkordb-1` container remains stopped rather than deleted.

The canonical Compose file is `runtime/docker-compose.yml`. It mounts the
external volume `filomena_falkordb_data` at
`/var/lib/falkordb/data`. The active mount was inspected as:

```text
healthy volume|filomena_falkordb_data|/var/lib/falkordb/data
```

Persistence was tested after `BGSAVE` reported `rdb_last_bgsave_status:ok`:

```powershell
docker compose -f runtime/docker-compose.yml up -d --force-recreate --no-deps falkordb
docker exec filomena-falkordb redis-cli GRAPH.QUERY whatsapp-group-5511981042361-1589823314-g-us `
  "MATCH (e:Episodic {uuid: '03372eb4-1fca-4d21-902e-c7e255a466cd'}) RETURN e.uuid, e.group_id"
```

After recreation, FalkorDB was healthy and returned the same episode:

```text
03372eb4-1fca-4d21-902e-c7e255a466cd
whatsapp-group-5511981042361-1589823314-g-us
```

The migrated canonical graph contains 164 nodes, 254 relationships, and 26
episodes. The original `wa-chat` graph was retained as a rollback copy.

The old workspace runtime path is now a junction to the repository source:

```text
C:\Users\Helena\.openclaw\workspace\graphiti-runtime
  -> C:\dev\filomena\runtime
```

Its former contents remain at
`C:\Users\Helena\.openclaw\workspace\graphiti-runtime.legacy-20260903-2252`.

## 2. Canonical conversation namespace

`conversation-namespace.mjs` is the shared implementation used by the live
plugin and WhatsApp importer. It derives the ID from the normalized channel and
native conversation JID; no filename, display name, session lifetime, or global
`whatsapp` fallback participates in persistent identity.

The installed OpenClaw configuration has:

```text
defaultGroupId = whatsapp-group-5511981042361-1589823314-g-us
```

The importer now requires `--jid` or `--group-id`. This dry run proved that the
same real JID selects the live namespace:

```powershell
node scripts/import-whatsapp.mjs C:\Users\Helena\.openclaw\workspace\whatsapp-exports `
  --jid 5511981042361-1589823314@g.us --dry-run
```

```text
chat.txt: 4977 messages, 19 episode(s),
group_id=whatsapp-group-5511981042361-1589823314-g-us
```

The importer's four identity tests pass, including a test proving that renaming
the export does not change the namespace.

## 3. Conversation isolation

The live plugin resolves the current namespace from trusted OpenClaw tool/hook
context. `memory_recall`, automatic recall, and persistent `memory_store` no
longer enumerate every group available to `agent:main`. Automatic recall may
also query only the current ephemeral session namespace.

The Graphiti/FalkorDB adapter required one additional fix: FalkorDB stores each
Graphiti group as a separate graph database, but upstream routing cloned the
driver only when two or more group IDs were supplied. The vendored decorator
now routes even one explicit group to its own graph, and `get_episodes` uses the
routed Graphiti API.

Live A/B verification:

```powershell
node scripts/verify-graphiti-isolation.mjs `
  whatsapp-group-5511981042361-1589823314-g-us `
  whatsapp-group-5511999999999-0000000000-g-us
```

```text
group A: ... -> 26 episode(s), expected text found
group B: ... -> 0 episode(s), expected text absent
```

The expected text is `Zerei Shadow of Mordor`. Focused plugin tests also prove:

- recall in group A never sends group B to Graphiti;
- recall in group B never sends group A to Graphiti;
- persistent `memory_store` in the real group sends only the canonical group ID
  and never `whatsapp`.

## 4. Passive capture design

Passive capture was investigated but deliberately not enabled in this round.
The smallest reliable hook is OpenClaw's `message_received`, emitted once for an
accepted inbound turn independently of reply dispatch and before
`before_dispatch`. Therefore the WhatsApp guard may suppress a Filomena reply
without suppressing memory observation.

The proposed implementation stores one episode per provider message, preserving
the provider message ID, conversation JID, sender identity, timestamp, reply
metadata, and original text. A deterministic identity derived from channel,
conversation JID, and provider message ID prevents duplicate sliding-window
episodes. When enabled, it must replace the current `agent_end` batch capture.

See `docs/openclaw-passive-memory-hook.md` for the inspected OpenClaw files,
event contract, and exact minimal design.

## Verification commands

```powershell
# Plugin behavior
npm.cmd test
.\node_modules\.bin\tsc.cmd --noEmit --pretty false

# Importer identity
node --test scripts/import-whatsapp.test.mjs

# FalkorDB single-group routing regression
python -m pytest --confcutdir=tests tests/test_decorators.py -q

# Runtime configuration
docker compose -f runtime/docker-compose.yml config --quiet
openclaw.cmd config validate
```

Results:

```text
Plugin:   8 files passed, 1 skipped; 146 tests passed, 13 skipped
TypeScript typecheck: passed
Importer: 4 passed
FalkorDB routing: 2 passed
Compose validation: passed
OpenClaw config: valid (duplicate global/local plugin warning remains)
filomena-falkordb: healthy
filomena-graphiti-mcp: healthy
```

The OpenClaw gateway was not started during this work.

## Relevant source changes

- `runtime/docker-compose.yml`: canonical runtime and correct FalkorDB mount.
- `runtime/config.yaml`: disables an accidental global persistent default.
- `plugins/openclaw-memory-graphiti/conversation-namespace.mjs`: shared
  deterministic conversation ID.
- `plugins/openclaw-memory-graphiti/index.ts`: conversation-bound store and
  recall, with no authorized-group fan-out.
- `plugins/openclaw-memory-graphiti/authorization.ts`: targeted authorization.
- `scripts/import-whatsapp.mjs`: explicit JID/group target.
- `vendor/graphiti/graphiti_core/decorators.py`: single-group FalkorDB routing.
- `vendor/graphiti/mcp_server/src/graphiti_mcp_server.py`: routed episode reads.
- `scripts/verify-graphiti-isolation.mjs`: executable live A/B proof.
- `docs/openclaw-passive-memory-hook.md`: passive-capture proposal.
