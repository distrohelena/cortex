# Filomena Monorepo Migration Design

**Date:** 2026-09-03

## Objective

Make `C:\dev\filomena` the canonical and active source repository for the
Filomena OpenClaw integration while preserving all existing Git history and
keeping conversations, credentials, runtime state, databases, logs, media, and
backups out of version control.

The migration is organizational. It must not intentionally change Filomena's
WhatsApp trigger, reply, memory capture, or memory recall behavior.

## Repository Layout

The destination will be a single Git repository with this layout:

```text
C:\dev\filomena\
|-- plugins\
|   |-- whatsapp-filomena-guard\
|   `-- openclaw-memory-graphiti\
|-- vendor\
|   `-- graphiti\
|-- runtime\
|   |-- docker-compose.yml
|   |-- config.yaml
|   `-- .env.example
|-- scripts\
|   `-- import-whatsapp.mjs
|-- workspace\
|   |-- AGENTS.md
|   |-- SOUL.md
|   |-- IDENTITY.md
|   `-- USER.md
|-- config\
|   `-- openclaw.example.json
|-- docs\
|-- .gitignore
`-- README.md
```

The live `C:\Users\Helena\.openclaw\openclaw.json` remains outside Git. A
sanitized example will document required settings without real credentials,
phone numbers, group IDs, tokens, or private paths.

## History Preservation

The existing `C:\Users\Helena\.openclaw\workspace` repository will provide
the destination repository's initial history. This preserves the complete
WhatsApp guard and design-document history.

The two nested repositories will be imported without squashing:

1. `openclaw-memory-graphiti` will be imported below
   `plugins/openclaw-memory-graphiti`. Its upstream history and all seven local
   commits ahead of `origin/main` will remain reachable from the monorepo.
2. `graphiti-local` will be imported below `vendor/graphiti`. A temporary clone
   will receive the five current uncommitted local adaptations as one explicit
   migration commit before the subtree import. The source repository will not
   be mutated to manufacture this commit.

The existing guard will move to `plugins/whatsapp-filomena-guard` with
`git mv`. Git's history and rename detection will retain its provenance.

The imported repositories will no longer remain nested Git repositories in the
destination. `C:\dev\filomena` will contain one `.git` directory.

## Included Content

The monorepo will include:

- WhatsApp Filomena guard source, manifests, and tests.
- OpenClaw Graphiti memory plugin source, manifests, tests, and its existing
  design and implementation documents.
- The locally adapted Graphiti source used to build the MCP image.
- Graphiti runtime configuration and Docker Compose definitions.
- The WhatsApp export importer, but not its input files.
- Filomena workspace prompt files.
- Sanitized configuration examples and operational documentation.

Temporary directories and historical compiled WhatsApp bundle backups will not
be treated as source code. They will remain outside the new repository unless
an individual file is required to reproduce an active implementation.

## Excluded and Ignored Content

The repository must ignore and must not commit:

- WhatsApp exports and conversation transcripts, including `chat.txt`.
- WhatsApp credentials, auth state, pairing state, and message-ID state.
- Real OpenClaw configuration files and gateway tokens.
- `.env` files and credentials for SpiceDB or other services.
- FalkorDB, PostgreSQL, SpiceDB, Graphiti, or OpenClaw data directories.
- Generated media, logs, caches, temporary directories, and backups.
- `node_modules`, Python virtual environments, build output, and coverage data.

Before the first destination commit, tracked files will be scanned for obvious
tokens, phone numbers, WhatsApp group IDs, and copied conversation content.

## Active OpenClaw Wiring

After the destination has been assembled and tested, the live OpenClaw config
will be backed up and updated so `plugins.load.paths` points to:

```text
C:\dev\filomena\plugins\openclaw-memory-graphiti
C:\dev\filomena\plugins\whatsapp-filomena-guard
```

The live config will continue to contain environment-specific group IDs,
credentials, model settings, and local data paths. Those values will not be
copied into the repository.

The gateway will not be started by the migration. Helena will start it after
the handoff using her existing command.

## Runtime and Database Persistence

The repository's Docker Compose configuration will use
`vendor/graphiti/mcp_server/docker/Dockerfile.standalone` as the Graphiti MCP
build source.

FalkorDB currently stores `dump.rdb` in `/var/lib/falkordb/data`, while its
named volume is incorrectly mounted at `/data`. The destination runtime will
mount persistent storage at the actual database directory.

Before replacing or recreating a FalkorDB container:

1. Wait until the current `wa-chat` ingestion queue is idle.
2. Request an RDB snapshot and verify that it completed successfully.
3. Copy or restore the snapshot into the correctly mounted named volume.
4. Keep a separate recoverable backup until post-migration recall succeeds.

Database contents remain external runtime data and will never be committed.
Existing containers will not be stopped or recreated merely to assemble the
repository. Runtime cutover will happen only after source and data verification.

## WhatsApp Export Importer

The importer will live at `scripts/import-whatsapp.mjs`. Its input directory
will remain external to the repository and must be supplied explicitly or by a
documented local-only setting.

The migration will preserve current importer behavior. Namespace alignment and
changes to Graphiti capture or recall policy are separate follow-up work and
are not part of this migration.

## Migration Sequence

1. Record source repository heads, working-tree changes, and relevant runtime
   state.
2. Create the destination from the existing workspace Git history without
   deleting the source.
3. Reorganize the tracked guard and documentation inside the destination.
4. Import the Graphiti plugin history as an unsquashed subtree.
5. Preserve the local Graphiti modifications in a temporary clone and import
   that history as an unsquashed subtree.
6. Add runtime files, importer, workspace prompts, sanitized examples,
   `.gitignore`, and README.
7. Scan the destination for secrets and private conversation material.
8. Run component tests and static configuration validation.
9. Preserve the current FalkorDB snapshot and prepare the corrected persistent
   runtime without interrupting an active ingestion queue.
10. Back up and update the live OpenClaw plugin paths.
11. Verify plugin discovery and provide manual gateway/runtime start commands.
12. After Helena starts the services, verify Graphiti health and recall of the
    known Gabriel/Shadow of Mordor fact.

## Verification

Migration acceptance requires:

- A single `.git` directory under `C:\dev\filomena`.
- Existing workspace, Graphiti plugin, and Graphiti upstream/local histories
  visible from the destination repository.
- A clean working tree after migration commits.
- Passing guard and memory-plugin tests.
- Successful TypeScript checks where supported by the existing projects.
- A valid Docker Compose configuration.
- No tracked WhatsApp export, database, credential, token, runtime state, or
  backup files.
- OpenClaw resolving both plugins from `C:\dev\filomena\plugins` after Helena
  restarts the gateway.
- Graphiti returning the known fact that Gabriel completed Shadow of Mordor in
  about 25 hours after runtime cutover.

## Failure Handling and Rollback

The migration is copy-first. Existing source directories and containers remain
intact until acceptance checks pass.

If plugin loading fails, restore the backed-up `openclaw.json` so the old plugin
paths are used. If the corrected FalkorDB runtime fails, retain the old
container and restore the verified RDB snapshot. No original repository or
runtime data will be deleted as part of this migration.

## Explicit Non-Goals

This migration will not redesign Graphiti namespaces, change Filomena's
personality, alter trigger rules, tune retrieval relevance, ingest additional
groups, publish a remote repository, or start the OpenClaw gateway. Those are
separate changes requiring their own design and verification.
