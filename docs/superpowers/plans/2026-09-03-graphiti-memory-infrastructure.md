# Graphiti memory infrastructure implementation plan

> Execute in order. Do not start or restart the OpenClaw gateway; Helena owns
> gateway startup. Docker service recreation is in scope.

## Task 1: Establish clean baseline

Run the memory plugin and importer tests before editing:

```powershell
cd C:\Users\Helena\filomena-main\plugins\openclaw-memory-graphiti
npm test
npm run typecheck
```

Record current Docker mounts, RDB status, graph list, and a stable episode from
`wa-chat`. No mutation occurs in this task.

## Task 2: Add canonical namespace helper

Files:

- Add `plugins/openclaw-memory-graphiti/conversation-namespace.mjs`
- Add `plugins/openclaw-memory-graphiti/conversation-namespace.d.mts`
- Add `plugins/openclaw-memory-graphiti/conversation-namespace.test.ts`

Write failing tests for the real WhatsApp JID, direct-message JIDs, session-key
fallback, invalid/missing context, and deterministic output. Implement one pure
helper shared by runtime and importer.

Expected canonical result:

```text
whatsapp-group-5511981042361-1589823314-g-us
```

## Task 3: Require explicit importer identity

Files:

- Modify `scripts/import-whatsapp.mjs`
- Add `scripts/import-whatsapp.test.mjs`

Refactor argument parsing into exported functions guarded from automatic main
execution. Test that `--jid` and `--group-id` resolve correctly and that no
identity derives from `chat.txt`. A missing target must exit with an actionable
error. Preserve dry-run support.

## Task 4: Bind memory tools to the active conversation

Files:

- Modify `plugins/openclaw-memory-graphiti/index.ts`
- Modify `plugins/openclaw-memory-graphiti/authorization.ts`
- Modify `plugins/openclaw-memory-graphiti/index.test.ts`
- Modify `plugins/openclaw-memory-graphiti/authorization.test.ts`

Add a targeted `canAccessGroup` permission check. Convert `memory_recall` and
`memory_store` to tool factories using trusted OpenClaw tool context. Remove the
module-global session state.

Tests must prove:

- group A recall sends only A to Graphiti;
- group B recall sends only B and cannot return A;
- `memory_store` in the real group writes the canonical group;
- no call writes to `whatsapp`;
- missing conversation context fails closed;
- optional session scope only adds the current ephemeral session group.

## Task 5: Restrict automatic recall and transitional capture

Files:

- Modify `plugins/openclaw-memory-graphiti/index.ts`
- Modify `plugins/openclaw-memory-graphiti/index.test.ts`

Resolve the current conversation from `before_prompt_build` hook context and
search only its canonical persistent group plus the current ephemeral session
group. Stop startup enumeration/warming of all authorized groups. Route the
existing transitional `agent_end` capture to the canonical group and never to
the global default. Do not implement passive ingestion in this task.

Add hook tests that spy on every Graphiti search call and assert no foreign
group ID is present.

## Task 6: Document and validate passive hook contract

Files:

- Add `docs/openclaw-passive-memory-hook.md`
- Add or extend plugin tests with a payload-shape fixture

Document the verified OpenClaw 2026.8.1 ordering and fields for
`message_received`. Specify one inbound message per episode, deterministic
identity, sender/timestamp preservation, and replacement of `agent_end` when
activated. Do not enable ingestion in this round.

## Task 7: Remove global runtime defaults

Files:

- Modify `runtime/config.yaml`
- Modify `config/openclaw.example.json`
- Modify the active untracked `C:\Users\Helena\.openclaw\openclaw.json`

Replace the Graphiti MCP fallback `whatsapp` group with a clearly unused
sentinel. Runtime tools must always pass an explicit canonical group. Remove
startup behavior that creates default-group membership.

## Task 8: Back up and repair FalkorDB persistence

Files:

- Modify `runtime/docker-compose.yml` if verification finds drift
- Modify `.gitignore` for local backup/runtime artifacts if needed

Operational sequence:

1. Run and verify `BGSAVE` in `docker-falkordb-1`.
2. Record SHA-256 and copy `/var/lib/falkordb/data/dump.rdb` to a timestamped
   directory under `C:\Users\Helena\.openclaw\backups\falkordb`.
3. Create/seed `filomena_falkordb_data` without removing the source container.
4. Stop the old conflicting FalkorDB container only after seed verification.
5. Start `filomena-falkordb` from the canonical compose.
6. Query the pre-recorded episode.
7. Force-recreate `filomena-falkordb` and query it again.

If any post-start query fails, stop the new container and restore service from
the untouched old container/dump.

## Task 9: Create the canonical graph without deleting legacy graphs

Use `GRAPH.COPY wa-chat <canonical-id>`, then update `group_id` properties on
nodes and relationships in the copy only. Verify node, relationship, and
episode counts match `wa-chat`, and verify the Gabriel/Shadow of Mordor episode
is visible through the canonical group. Keep `wa-chat` and the legacy session
graph untouched.

Authorize `agent:main` only as needed for the canonical conversation group.

## Task 10: Replace compatibility path with junction

Rename the old runtime directory to a timestamped recoverable backup, create a
junction from
`C:\Users\Helena\.openclaw\workspace\graphiti-runtime` to
`C:\dev\filomena\runtime`, and verify both paths resolve to the same compose
and config files.

## Task 11: Acceptance verification

Run:

```powershell
cd C:\Users\Helena\filomena-main\plugins\openclaw-memory-graphiti
npm test
npm run typecheck
node ..\..\scripts\import-whatsapp.mjs <exports> --dry-run --jid <real-jid>
docker compose -f C:\dev\filomena\runtime\docker-compose.yml config
```

Execute live Graphiti/FalkorDB probes for persistence, canonical graph access,
import targeting, A/B isolation, and context-bound store. Capture commands and
their relevant outputs in `docs/verification/graphiti-memory-infrastructure.md`.

## Task 12: Commit and fast-forward the active checkout

Commit all source, tests, documentation, and versioned configuration on
`main`. Fetch the local implementation repository from `C:\dev\filomena` and
fast-forward its `main`. Confirm both checkouts report the same commit and a
clean status. Do not push remotely.

