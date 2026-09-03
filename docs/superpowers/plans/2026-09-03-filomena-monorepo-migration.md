# Filomena Monorepo Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create `C:\dev\filomena` as the single, history-preserving, active Git source for Filomena's OpenClaw plugins, workspace prompts, Graphiti fork, importer, and local runtime without versioning conversations, credentials, or database state.

**Architecture:** Clone the current workspace repository as the monorepo base, then merge the memory plugin and Graphiti repositories as unsquashed prefixed histories. Keep private data outside Git, migrate FalkorDB through a verified RDB snapshot into a correctly mounted external volume, and switch OpenClaw to the new plugin and workspace paths only after source tests pass.

**Tech Stack:** Git, PowerShell, Node.js, TypeScript, OpenClaw 2026.8.1, Docker Compose, Graphiti Core 0.28.1, FalkorDB, SpiceDB, PostgreSQL, LM Studio.

---

## Target File Map

- `C:\dev\filomena\plugins\whatsapp-filomena-guard\`: trigger gating, implicit-reply identity tracking, and `Filomena:` output prefix.
- `C:\dev\filomena\plugins\openclaw-memory-graphiti\`: OpenClaw memory tools, capture/recall hooks, SpiceDB authorization, and tests.
- `C:\dev\filomena\vendor\graphiti\`: full Graphiti upstream history plus local LM Studio/FalkorDB adaptations.
- `C:\dev\filomena\runtime\docker-compose.yml`: local Graphiti, FalkorDB, SpiceDB, and PostgreSQL topology; no gateway service.
- `C:\dev\filomena\runtime\config.yaml`: non-secret Graphiti model, embedder, graph, and entity configuration.
- `C:\dev\filomena\runtime\.env.example`: documented optional environment overrides only.
- `C:\dev\filomena\scripts\import-whatsapp.mjs`: importer code; input remains outside Git.
- `C:\dev\filomena\workspace\`: active OpenClaw workspace prompts; `memory/` remains ignored local state.
- `C:\dev\filomena\config\openclaw.example.json`: sanitized configuration shape with placeholders.
- `C:\dev\filomena\README.md`: setup, start, import, verification, privacy, and rollback instructions.
- `C:\dev\filomena\.gitignore`: privacy and generated-file boundary.

### Task 1: Establish a Reproducible Preflight Baseline

**Files:**
- Read: `C:\Users\Helena\.openclaw\workspace\.git`
- Read: `C:\Users\Helena\.openclaw\workspace\openclaw-memory-graphiti\.git`
- Read: `C:\Users\Helena\.openclaw\workspace\graphiti-local\.git`
- Read: Docker containers and volumes

- [ ] **Step 1: Verify the expected source heads without changing safe-directory globally**

Run:

```powershell
git -c safe.directory=C:/Users/Helena/.openclaw/workspace `
  -C C:\Users\Helena\.openclaw\workspace rev-parse HEAD

git -c safe.directory=C:/Users/Helena/.openclaw/workspace/openclaw-memory-graphiti `
  -C C:\Users\Helena\.openclaw\workspace\openclaw-memory-graphiti rev-parse HEAD

git -c safe.directory=C:/Users/Helena/.openclaw/workspace/graphiti-local `
  -C C:\Users\Helena\.openclaw\workspace\graphiti-local rev-parse HEAD
```

Expected baseline commits:

```text
07f8053  workspace design baseline
412afb4  memory plugin
aa68b38  Graphiti base
```

The workspace HEAD will be newer after this plan is committed. Verify that
`07f8053` is an ancestor of HEAD. If either nested repository has moved
unexpectedly, stop and inspect its log before migrating.

- [ ] **Step 2: Verify the known Graphiti working-tree adaptations**

Run:

```powershell
git -c safe.directory=C:/Users/Helena/.openclaw/workspace/graphiti-local `
  -C C:\Users\Helena\.openclaw\workspace\graphiti-local status --short
```

Expected modified files:

```text
graphiti_core/driver/falkordb/operations/search_ops.py
graphiti_core/driver/falkordb_driver.py
mcp_server/docker/Dockerfile.standalone
mcp_server/pyproject.toml
mcp_server/src/services/factories.py
```

- [ ] **Step 3: Confirm the destination is still absent**

Run:

```powershell
Test-Path -LiteralPath C:\dev\filomena
```

Expected: `False`. If it is `True`, stop; do not merge into an unknown directory.

- [ ] **Step 4: Confirm all 19 imported WhatsApp episodes finished semantic processing**

Run:

```powershell
$queueLog = docker logs --since 2026-09-03T22:41:30Z openclaw-graphiti-mcp 2>&1
($queueLog | Select-String 'Successfully processed episode None for group wa-chat').Count
```

Expected: `19`. If the value is lower, leave Graphiti and LM Studio running and wait; do not snapshot or cut over the database.

- [ ] **Step 5: Verify the known fact before migration**

Run:

```powershell
openclaw.cmd graphiti-mem search "Gabriel Zerei Shadow of Mordor" --scope long-term --limit 10
```

Expected output includes:

```text
Gabriel completou o jogo Shadow of Mordor em cerca de 25 horas.
```

### Task 2: Create the Monorepo Base and Move the Guard

**Files:**
- Create: `C:\dev\filomena\.git`
- Move in destination: `whatsapp-filomena-guard\` to `plugins\whatsapp-filomena-guard\`
- Preserve: `docs\superpowers\`

- [ ] **Step 1: Clone the workspace history without hard-linking object files**

Run:

```powershell
git clone --no-hardlinks C:\Users\Helena\.openclaw\workspace C:\dev\filomena
```

Expected: clone succeeds and `git -C C:\dev\filomena log -1 --oneline` shows the migration design commit.

- [ ] **Step 2: Verify the destination contains exactly one repository root**

Run:

```powershell
Get-Item -LiteralPath C:\dev\filomena\.git
git -c safe.directory=C:/dev/filomena -C C:\dev\filomena status --short --branch
```

Expected: branch `master`, clean working tree.

- [ ] **Step 3: Move the guard into the plugins boundary**

Run:

```powershell
New-Item -ItemType Directory -Path C:\dev\filomena\plugins
git -c safe.directory=C:/dev/filomena -C C:\dev\filomena mv `
  whatsapp-filomena-guard plugins/whatsapp-filomena-guard
```

Expected: `git status --short` reports only renames below `plugins/whatsapp-filomena-guard`.

- [ ] **Step 4: Run the guard tests from the new location**

Run:

```powershell
npm test --prefix C:\dev\filomena\plugins\whatsapp-filomena-guard
```

Expected: all guard, state, and registration tests pass.

- [ ] **Step 5: Commit the structural move**

Run:

```powershell
git -c safe.directory=C:/dev/filomena -C C:\dev\filomena add -- plugins/whatsapp-filomena-guard
git -c safe.directory=C:/dev/filomena -C C:\dev\filomena commit -m "Reorganize Filomena guard under plugins"
```

### Task 3: Import the Memory Plugin With Full History

**Files:**
- Create: `C:\dev\filomena\plugins\openclaw-memory-graphiti\`
- Preserve source: `C:\Users\Helena\.openclaw\workspace\openclaw-memory-graphiti\`

- [ ] **Step 1: Fetch the local memory-plugin history into the monorepo**

Run:

```powershell
git -c safe.directory=C:/dev/filomena -C C:\dev\filomena remote add memory-plugin `
  C:\Users\Helena\.openclaw\workspace\openclaw-memory-graphiti
git -c safe.directory=C:/dev/filomena -C C:\dev\filomena fetch memory-plugin main
```

Expected: `memory-plugin/main` resolves to `412afb4` or a deliberate descendant.

- [ ] **Step 2: Begin an unsquashed unrelated-history merge**

Run:

```powershell
git -c safe.directory=C:/dev/filomena -C C:\dev\filomena merge `
  -s ours --no-commit --allow-unrelated-histories memory-plugin/main
```

Expected: Git reports that the automatic merge stopped before committing.

- [ ] **Step 3: Materialize the plugin tree beneath its prefix**

Run:

```powershell
git -c safe.directory=C:/dev/filomena -C C:\dev\filomena read-tree `
  --prefix=plugins/openclaw-memory-graphiti/ -u memory-plugin/main
```

Expected: the complete plugin appears under `plugins/openclaw-memory-graphiti` and no nested `.git` directory exists there.

- [ ] **Step 4: Commit the history-preserving merge and remove the temporary remote**

Run:

```powershell
git -c safe.directory=C:/dev/filomena -C C:\dev\filomena commit `
  -m "Import Graphiti memory plugin history"
git -c safe.directory=C:/dev/filomena -C C:\dev\filomena remote remove memory-plugin
```

- [ ] **Step 5: Prove the plugin history is reachable**

Run:

```powershell
git -c safe.directory=C:/dev/filomena -C C:\dev\filomena merge-base --is-ancestor 412afb4 HEAD
$LASTEXITCODE
```

Expected: `0`.

### Task 4: Preserve and Import the Adapted Graphiti History

**Files:**
- Create temporary repository: `C:\dev\filomena-graphiti-import\`
- Create: `C:\dev\filomena\vendor\graphiti\`
- Copy exactly five modified files from the source Graphiti working tree

- [ ] **Step 1: Confirm the temporary repository path is unused**

Run:

```powershell
Test-Path -LiteralPath C:\dev\filomena-graphiti-import
```

Expected: `False`. If `True`, stop and inspect it; do not delete it automatically.

- [ ] **Step 2: Clone the Graphiti repository into the temporary staging repository**

Run:

```powershell
git clone --no-hardlinks `
  C:\Users\Helena\.openclaw\workspace\graphiti-local `
  C:\dev\filomena-graphiti-import
```

Expected: clean clone at base commit `aa68b38`.

- [ ] **Step 3: Overlay only the five known local adaptations**

Run:

```powershell
$sourceGraphiti = 'C:\Users\Helena\.openclaw\workspace\graphiti-local'
$stagedGraphiti = 'C:\dev\filomena-graphiti-import'
$adaptedFiles = @(
  'graphiti_core\driver\falkordb\operations\search_ops.py',
  'graphiti_core\driver\falkordb_driver.py',
  'mcp_server\docker\Dockerfile.standalone',
  'mcp_server\pyproject.toml',
  'mcp_server\src\services\factories.py'
)
foreach ($relativePath in $adaptedFiles) {
  Copy-Item -LiteralPath (Join-Path $sourceGraphiti $relativePath) `
    -Destination (Join-Path $stagedGraphiti $relativePath)
}
```

Expected: staging-repository status reports exactly those five modified files.

- [ ] **Step 4: Commit the local adaptations in the staging repository**

Run:

```powershell
git -c safe.directory=C:/dev/filomena-graphiti-import `
  -C C:\dev\filomena-graphiti-import add -- `
  graphiti_core/driver/falkordb/operations/search_ops.py `
  graphiti_core/driver/falkordb_driver.py `
  mcp_server/docker/Dockerfile.standalone `
  mcp_server/pyproject.toml `
  mcp_server/src/services/factories.py

git -c safe.directory=C:/dev/filomena-graphiti-import `
  -C C:\dev\filomena-graphiti-import commit `
  -m "Adapt Graphiti for local LM Studio and FalkorDB"
```

- [ ] **Step 5: Import the staging repository as unsquashed history**

Run:

```powershell
git -c safe.directory=C:/dev/filomena -C C:\dev\filomena remote add graphiti-vendor `
  C:\dev\filomena-graphiti-import
git -c safe.directory=C:/dev/filomena -C C:\dev\filomena fetch graphiti-vendor main
git -c safe.directory=C:/dev/filomena -C C:\dev\filomena merge `
  -s ours --no-commit --allow-unrelated-histories graphiti-vendor/main
git -c safe.directory=C:/dev/filomena -C C:\dev\filomena read-tree `
  --prefix=vendor/graphiti/ -u graphiti-vendor/main
git -c safe.directory=C:/dev/filomena -C C:\dev\filomena commit `
  -m "Import adapted Graphiti history"
git -c safe.directory=C:/dev/filomena -C C:\dev\filomena remote remove graphiti-vendor
```

- [ ] **Step 6: Prove the upstream history and adaptation commit are reachable**

Run:

```powershell
git -c safe.directory=C:/dev/filomena -C C:\dev\filomena merge-base --is-ancestor aa68b38 HEAD
$LASTEXITCODE
git -c safe.directory=C:/dev/filomena -C C:\dev\filomena log `
  --oneline --all --grep="Adapt Graphiti for local LM Studio and FalkorDB"
```

Expected: exit code `0` and one adaptation commit.

### Task 5: Add Owned Runtime, Importer, and Workspace Files

**Files:**
- Create: `C:\dev\filomena\runtime\config.yaml`
- Create: `C:\dev\filomena\scripts\import-whatsapp.mjs`
- Create: `C:\dev\filomena\workspace\AGENTS.md`
- Create: `C:\dev\filomena\workspace\SOUL.md`
- Create: `C:\dev\filomena\workspace\IDENTITY.md`
- Create: `C:\dev\filomena\workspace\USER.md`
- Keep untracked: `C:\dev\filomena\workspace\memory\`

- [ ] **Step 1: Create the target directories**

Run:

```powershell
New-Item -ItemType Directory -Path C:\dev\filomena\runtime
New-Item -ItemType Directory -Path C:\dev\filomena\scripts
New-Item -ItemType Directory -Path C:\dev\filomena\workspace
New-Item -ItemType Directory -Path C:\dev\filomena\config
```

- [ ] **Step 2: Copy the non-secret runtime and importer sources**

Run:

```powershell
Copy-Item -LiteralPath `
  C:\Users\Helena\.openclaw\workspace\graphiti-runtime\config.yaml `
  -Destination C:\dev\filomena\runtime\config.yaml

Copy-Item -LiteralPath `
  C:\Users\Helena\.openclaw\workspace\whatsapp-exports\import-whatsapp.mjs `
  -Destination C:\dev\filomena\scripts\import-whatsapp.mjs
```

- [ ] **Step 3: Copy the active workspace prompt sources**

Run:

```powershell
$promptFiles = 'AGENTS.md','SOUL.md','IDENTITY.md','USER.md'
foreach ($name in $promptFiles) {
  Copy-Item -LiteralPath (Join-Path 'C:\Users\Helena\.openclaw\workspace' $name) `
    -Destination (Join-Path 'C:\dev\filomena\workspace' $name)
}
```

- [ ] **Step 4: Preserve existing file-memory state locally but outside Git**

Run only if the source directory exists:

```powershell
if (Test-Path -LiteralPath C:\Users\Helena\.openclaw\workspace\memory) {
  Copy-Item -LiteralPath C:\Users\Helena\.openclaw\workspace\memory `
    -Destination C:\dev\filomena\workspace\memory -Recurse
}
```

Expected: memory files are locally present but will be ignored in Task 6.

### Task 6: Establish the Privacy Boundary and Sanitize the Current Tree

**Files:**
- Modify: `C:\dev\filomena\.gitignore`
- Modify: current guard tests and historical design documents containing live identifiers

- [ ] **Step 1: Replace `.gitignore` with the repository privacy policy**

Use `apply_patch` to make `C:\dev\filomena\.gitignore` exactly:

```gitignore
# Local secrets and environment
.env
.env.*
!.env.example
config/openclaw.json
config/*.local.*

# Private conversations and imported media
whatsapp-exports/
data/
chat.txt
*.chat.txt
workspace/media/

# Runtime state and databases
workspace/memory/
workspace/*.jsonl
state/
logs/
backups/
*.rdb
*.aof
*.sqlite
*.sqlite3

# Dependencies and generated files
**/node_modules/
**/.venv/
**/__pycache__/
**/.pytest_cache/
**/coverage/
**/dist/
**/build/

# Local work areas
.worktrees/
.tmp-*/
```

- [ ] **Step 2: Replace current-tree live identifiers with fictional test IDs**

In the destination only, use fictional values for every live identifier found:

```text
WhatsApp group JID -> 120363012345678901@g.us
personal/group phone-shaped ID -> +15551234567 or 15551234567
```

Apply the replacement only to tracked guard tests and design/plan documents found by:

```powershell
rg -l -P "55[0-9]{10,14}(?:-[0-9]{6,16})?@g\.us|[+]55[0-9]{10,11}|55[0-9]{10,11}" `
  C:\dev\filomena\plugins\whatsapp-filomena-guard `
  C:\dev\filomena\docs
```

Use `apply_patch` for the edits, then run the same `rg` command. Expected: no output.

Full history is intentionally preserved, so old commits remain capable of containing those identifiers. Do not publish this repository without a separately approved history-redaction operation.

- [ ] **Step 3: Verify that private source directories are ignored**

Run:

```powershell
git -c safe.directory=C:/dev/filomena -C C:\dev\filomena check-ignore -v `
  whatsapp-exports/chat.txt workspace/memory/example.md backups/example.json data/example.txt
```

Expected: every path is matched by `.gitignore`.

- [ ] **Step 4: Commit privacy boundaries and copied source files**

Run:

```powershell
git -c safe.directory=C:/dev/filomena -C C:\dev\filomena add -- `
  .gitignore runtime/config.yaml scripts/import-whatsapp.mjs workspace docs `
  plugins/whatsapp-filomena-guard
git -c safe.directory=C:/dev/filomena -C C:\dev\filomena commit `
  -m "Add Filomena runtime sources and privacy boundaries"
```

### Task 7: Define the Reproducible Local Runtime

**Files:**
- Create: `C:\dev\filomena\runtime\docker-compose.yml`
- Create: `C:\dev\filomena\runtime\.env.example`

- [ ] **Step 1: Create the full local Docker Compose file**

Use `apply_patch` to create `runtime/docker-compose.yml` with:

```yaml
name: filomena

services:
  postgres:
    image: postgres:16-alpine
    container_name: filomena-postgres
    restart: unless-stopped
    environment:
      POSTGRES_USER: spicedb
      POSTGRES_PASSWORD: ${SPICEDB_DB_PASSWORD:-spicedb_dev}
      POSTGRES_DB: spicedb
    ports:
      - "127.0.0.1:${POSTGRES_PORT:-5432}:5432"
    volumes:
      - postgres-data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U spicedb"]
      interval: 5s
      timeout: 3s
      retries: 10

  spicedb-migrate:
    image: authzed/spicedb:latest
    command:
      - migrate
      - head
      - --datastore-engine=postgres
      - --datastore-conn-uri=postgres://spicedb:${SPICEDB_DB_PASSWORD:-spicedb_dev}@postgres:5432/spicedb?sslmode=disable
    depends_on:
      postgres:
        condition: service_healthy

  spicedb:
    image: authzed/spicedb:latest
    container_name: filomena-spicedb
    restart: unless-stopped
    command:
      - serve
      - --grpc-preshared-key=${SPICEDB_TOKEN:-dev_token}
      - --datastore-engine=postgres
      - --datastore-conn-uri=postgres://spicedb:${SPICEDB_DB_PASSWORD:-spicedb_dev}@postgres:5432/spicedb?sslmode=disable
      - --http-enabled=true
    ports:
      - "127.0.0.1:${SPICEDB_GRPC_PORT:-50051}:50051"
      - "127.0.0.1:${SPICEDB_HTTP_PORT:-8443}:8443"
      - "127.0.0.1:${SPICEDB_METRICS_PORT:-9090}:9090"
    depends_on:
      postgres:
        condition: service_healthy
      spicedb-migrate:
        condition: service_completed_successfully
    healthcheck:
      test: ["CMD", "grpc_health_probe", "-addr=localhost:50051"]
      interval: 5s
      timeout: 3s
      retries: 10

  falkordb:
    image: falkordb/falkordb:latest
    container_name: filomena-falkordb
    restart: unless-stopped
    ports:
      - "127.0.0.1:${FALKORDB_PORT:-6380}:6379"
      - "127.0.0.1:${FALKORDB_UI_PORT:-3001}:3000"
    volumes:
      - falkordb-data:/var/lib/falkordb/data
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 5s
      timeout: 3s
      retries: 10

  graphiti-mcp:
    build:
      context: ../vendor/graphiti
      dockerfile: mcp_server/docker/Dockerfile.standalone
      args:
        GRAPHITI_CORE_VERSION: "0.28.1"
    image: filomena-graphiti-mcp:local
    container_name: filomena-graphiti-mcp
    restart: unless-stopped
    ports:
      - "127.0.0.1:${GRAPHITI_PORT:-8000}:8000"
    extra_hosts:
      - "host.docker.internal:host-gateway"
    environment:
      CONFIG_PATH: /app/mcp/config/local.yaml
      FALKORDB_URI: redis://falkordb:6379
      FALKORDB_DATABASE: default_db
      LLM_PROVIDER: openai_generic
      LLM_MODEL: google/gemma-4-e4b
      LLM_MAX_TOKENS: "4096"
      LLM_API_URL: http://host.docker.internal:1234/v1
      OPENAI_API_KEY: lm-studio
      EMBEDDER_PROVIDER: openai
      EMBEDDER_MODEL: text-embedding-nomic-embed-text-v2-moe
      EMBEDDER_DIMENSIONS: "768"
      EMBEDDER_API_URL: http://host.docker.internal:1234/v1
      EMBEDDER_API_KEY: lm-studio
      RERANKER_PROVIDER: none
      SEMAPHORE_LIMIT: "1"
    volumes:
      - ./config.yaml:/app/mcp/config/local.yaml:ro
    command: ["uv", "run", "--no-sync", "main.py", "--config", "/app/mcp/config/local.yaml"]
    depends_on:
      falkordb:
        condition: service_healthy
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8000/health"]
      interval: 10s
      timeout: 5s
      retries: 12
      start_period: 30s

volumes:
  postgres-data:
    external: true
    name: docker_postgres-data
  falkordb-data:
    external: true
    name: filomena_falkordb_data
```

- [ ] **Step 2: Create the environment example**

Use `apply_patch` to create `runtime/.env.example` with:

```dotenv
SPICEDB_TOKEN=replace-with-a-local-secret
SPICEDB_DB_PASSWORD=replace-with-the-existing-postgres-password
POSTGRES_PORT=5432
SPICEDB_GRPC_PORT=50051
SPICEDB_HTTP_PORT=8443
SPICEDB_METRICS_PORT=9090
FALKORDB_PORT=6380
FALKORDB_UI_PORT=3001
GRAPHITI_PORT=8000
```

- [ ] **Step 3: Validate the compose model without starting containers**

Run:

```powershell
docker compose -f C:\dev\filomena\runtime\docker-compose.yml config --quiet
```

Expected: exit code `0` and no services started.

- [ ] **Step 4: Build the Graphiti image while the old runtime remains active**

Run:

```powershell
docker compose -f C:\dev\filomena\runtime\docker-compose.yml build graphiti-mcp
```

Expected: image `filomena-graphiti-mcp:local` builds successfully. This step does not bind ports or stop the old runtime.

- [ ] **Step 5: Commit the runtime definition**

Run:

```powershell
git -c safe.directory=C:/dev/filomena -C C:\dev\filomena add -- runtime
git -c safe.directory=C:/dev/filomena -C C:\dev\filomena commit `
  -m "Define persistent local Filomena runtime"
```

### Task 8: Add Sanitized Configuration and Operator Documentation

**Files:**
- Create: `C:\dev\filomena\config\openclaw.example.json`
- Create: `C:\dev\filomena\README.md`

- [ ] **Step 1: Create a valid sanitized OpenClaw configuration example**

Use `apply_patch` to create `config/openclaw.example.json` with:

```json
{
  "plugins": {
    "entries": {
      "whatsapp-filomena-guard": {
        "enabled": true,
        "config": {
          "channel": "whatsapp",
          "groupId": "<WHATSAPP_GROUP_JID>",
          "trigger": "@Filomena",
          "responsePrefix": "Filomena: ",
          "stateFile": "<OPENCLAW_STATE_DIR>\\whatsapp-filomena-guard.json",
          "maxTrackedMessageIds": 512
        }
      },
      "openclaw-memory-graphiti": {
        "enabled": true,
        "hooks": {
          "allowConversationAccess": true,
          "allowPromptInjection": true
        },
        "config": {
          "spicedb": {
            "endpoint": "localhost:50051",
            "token": "<SPICEDB_TOKEN>",
            "insecure": true
          },
          "graphiti": {
            "endpoint": "http://localhost:8000",
            "defaultGroupId": "whatsapp"
          },
          "subjectType": "agent",
          "subjectId": "main",
          "autoCapture": true,
          "autoRecall": true,
          "maxCaptureMessages": 10
        }
      }
    },
    "load": {
      "paths": [
        "C:\\dev\\filomena\\plugins\\openclaw-memory-graphiti",
        "C:\\dev\\filomena\\plugins\\whatsapp-filomena-guard"
      ]
    },
    "slots": {
      "memory": "openclaw-memory-graphiti"
    }
  },
  "agents": {
    "defaults": {
      "workspace": "C:\\dev\\filomena\\workspace",
      "model": {
        "primary": "lmstudio/google/gemma-4-e4b"
      }
    }
  }
}
```

- [ ] **Step 2: Create the operator README**

Use `apply_patch` to create `README.md` with these complete sections:

```markdown
# Filomena

Local OpenClaw WhatsApp bot with a fixed trigger/identity guard and persistent
Graphiti knowledge-graph memory backed by FalkorDB and authorized by SpiceDB.

## Privacy

Never commit WhatsApp exports, conversations, media, OpenClaw credentials,
gateway tokens, `.env`, databases, logs, state files, or backups. Historical
commits preserve old test identifiers; do not publish this repository without
a separate history-redaction review.

## Components

- `plugins/whatsapp-filomena-guard`: dispatch and reply-label policy.
- `plugins/openclaw-memory-graphiti`: OpenClaw Graphiti integration.
- `vendor/graphiti`: locally adapted Graphiti source.
- `runtime`: Docker Compose and Graphiti model configuration.
- `scripts/import-whatsapp.mjs`: imports an explicitly supplied export folder.
- `workspace`: active OpenClaw prompt workspace; local memory files are ignored.

## Prerequisites

- Docker Desktop
- Node.js
- OpenClaw 2026.8.1 or compatible
- LM Studio on `localhost:1234` with `google/gemma-4-e4b` and
  `text-embedding-nomic-embed-text-v2-moe` available

## Start infrastructure

From `C:\dev\filomena\runtime`:

```powershell
docker compose up -d
docker compose ps
```

The OpenClaw gateway is intentionally not part of Compose. Start it manually:

```powershell
openclaw.cmd gateway run --force --verbose
```

## Import a WhatsApp export

Keep exports outside this repository and pass the directory explicitly:

```powershell
node C:\dev\filomena\scripts\import-whatsapp.mjs `
  C:\Users\Helena\.openclaw\workspace\whatsapp-exports
```

## Verify memory

```powershell
openclaw.cmd graphiti-mem status
openclaw.cmd graphiti-mem search "Gabriel Zerei Shadow of Mordor" `
  --scope long-term --limit 10
```

## Rollback

Restore the pre-migration OpenClaw config backup, stop the `filomena` Compose
project, and start the retained pre-migration containers. Do not delete either
FalkorDB volume until recall has been verified after a separate restart.
```

- [ ] **Step 3: Validate that the example is JSON**

Run:

```powershell
Get-Content -Raw C:\dev\filomena\config\openclaw.example.json | ConvertFrom-Json | Out-Null
```

Expected: no output and exit code `0`.

- [ ] **Step 4: Commit documentation and configuration examples**

Run:

```powershell
git -c safe.directory=C:/dev/filomena -C C:\dev\filomena add -- README.md config/openclaw.example.json
git -c safe.directory=C:/dev/filomena -C C:\dev\filomena commit `
  -m "Document Filomena local deployment"
```

### Task 9: Test the Monorepo and Scan the Current Tree

**Files:**
- Test: `plugins\whatsapp-filomena-guard\*.test.ts`
- Test: `plugins\openclaw-memory-graphiti\*.test.ts`
- Inspect: all tracked files

- [ ] **Step 1: Run guard tests**

Run:

```powershell
npm test --prefix C:\dev\filomena\plugins\whatsapp-filomena-guard
```

Expected: all tests pass.

- [ ] **Step 2: Install memory-plugin dependencies without creating another lockfile**

Run:

```powershell
npm install --no-package-lock --ignore-scripts `
  --prefix C:\dev\filomena\plugins\openclaw-memory-graphiti
```

Expected: dependencies installed under ignored `node_modules`; no tracked files change.

- [ ] **Step 3: Run memory-plugin tests**

Run:

```powershell
npm test --prefix C:\dev\filomena\plugins\openclaw-memory-graphiti
```

Expected: all unit tests pass. Live E2E tests are not run at this stage.

- [ ] **Step 4: Check formatting errors and unexpected nested repositories**

Run:

```powershell
git -c safe.directory=C:/dev/filomena -C C:\dev\filomena diff --check
Get-ChildItem -LiteralPath C:\dev\filomena -Directory -Recurse -Force `
  -Filter .git | Select-Object -ExpandProperty FullName
```

Expected: no diff errors; only `C:\dev\filomena\.git` exists.

- [ ] **Step 5: Scan tracked current content for forbidden private material**

Run:

```powershell
git -c safe.directory=C:/dev/filomena -C C:\dev\filomena grep -n -I `
  -E "55[0-9]{10,14}(-[0-9]{6,16})?@g\.us|[+]55[0-9]{10,11}|chat\.txt:|\[4/11/26," -- .
```

Expected: no output. Do not print or commit any real gateway or SpiceDB token while scanning.

- [ ] **Step 6: Verify all three histories and a clean tree**

Run:

```powershell
git -c safe.directory=C:/dev/filomena -C C:\dev\filomena merge-base --is-ancestor 07f8053 HEAD
git -c safe.directory=C:/dev/filomena -C C:\dev\filomena merge-base --is-ancestor 412afb4 HEAD
git -c safe.directory=C:/dev/filomena -C C:\dev\filomena merge-base --is-ancestor aa68b38 HEAD
git -c safe.directory=C:/dev/filomena -C C:\dev\filomena status --short --branch
```

Expected: all ancestor checks exit `0`; tree is clean except ignored dependency/runtime files.

### Task 10: Snapshot and Cut Over FalkorDB Safely

**Files:**
- Create untracked backup: `C:\Users\Helena\.openclaw\backups\filomena-migration-2026-09-03\falkordb-dump.rdb`
- Create external Docker volume: `filomena_falkordb_data`
- Preserve stopped old containers for rollback

- [ ] **Step 1: Reconfirm the Graphiti queue is idle immediately before snapshot**

Run:

```powershell
$queueLog = docker logs --since 2026-09-03T22:41:30Z openclaw-graphiti-mcp 2>&1
($queueLog | Select-String 'Successfully processed episode None for group wa-chat').Count
```

Expected: `19`. Stop if lower.

- [ ] **Step 2: Request and verify a fresh FalkorDB snapshot**

Run:

```powershell
docker exec docker-falkordb-1 redis-cli BGSAVE
docker exec docker-falkordb-1 redis-cli INFO persistence
```

Expected: `Background saving started`, followed by `rdb_bgsave_in_progress:0` and `rdb_last_bgsave_status:ok`. Poll `INFO persistence` until those values are present.

- [ ] **Step 3: Copy the snapshot to a recoverable location and hash it**

Run:

```powershell
New-Item -ItemType Directory -Force -Path `
  C:\Users\Helena\.openclaw\backups\filomena-migration-2026-09-03
docker cp `
  docker-falkordb-1:/var/lib/falkordb/data/dump.rdb `
  C:\Users\Helena\.openclaw\backups\filomena-migration-2026-09-03\falkordb-dump.rdb
Get-FileHash -Algorithm SHA256 `
  C:\Users\Helena\.openclaw\backups\filomena-migration-2026-09-03\falkordb-dump.rdb
```

Expected: non-empty file and a recorded SHA-256 hash.

- [ ] **Step 4: Create a new correctly mounted volume and populate it**

Run:

```powershell
docker volume create filomena_falkordb_data
docker run --rm --entrypoint sh `
  -v filomena_falkordb_data:/target `
  -v C:\Users\Helena\.openclaw\backups\filomena-migration-2026-09-03:/backup:ro `
  falkordb/falkordb:latest `
  -lc "cp /backup/falkordb-dump.rdb /target/dump.rdb && chmod 644 /target/dump.rdb"
```

Expected: command exits `0`. Do not overwrite or remove `docker_falkordb_data`.

- [ ] **Step 5: Stop, but do not delete, the old runtime containers**

Run:

```powershell
docker stop openclaw-graphiti-mcp docker-spicedb-1 docker-postgres-1 docker-falkordb-1
```

Expected: all four containers report stopped and remain visible under `docker ps -a`.

- [ ] **Step 6: Start the new infrastructure without starting OpenClaw**

Run:

```powershell
docker compose -f C:\dev\filomena\runtime\docker-compose.yml up -d
docker compose -f C:\dev\filomena\runtime\docker-compose.yml ps
```

Expected: PostgreSQL, SpiceDB, FalkorDB, and Graphiti become healthy. There is no gateway service.

- [ ] **Step 7: Verify graph and authorization state survived**

Run:

```powershell
docker exec filomena-falkordb redis-cli GRAPH.LIST
openclaw.cmd graphiti-mem status
openclaw.cmd graphiti-mem groups
openclaw.cmd graphiti-mem search "Gabriel Zerei Shadow of Mordor" --scope long-term --limit 10
```

Expected:

- `wa-chat` is listed as a graph.
- Graphiti and SpiceDB report connected.
- `wa-chat` remains authorized for `agent:main`.
- The Shadow of Mordor fact is returned.

- [ ] **Step 8: Roll back immediately if verification fails**

Run only on failure:

```powershell
docker compose -f C:\dev\filomena\runtime\docker-compose.yml stop
docker start docker-postgres-1 docker-spicedb-1 docker-falkordb-1 openclaw-graphiti-mcp
```

Expected: old services regain their previous ports. Keep both volumes and the backup snapshot intact for diagnosis.

### Task 11: Switch OpenClaw to the Active Monorepo Sources

**Files:**
- Back up: `C:\Users\Helena\.openclaw\openclaw.json`
- Modify: `C:\Users\Helena\.openclaw\openclaw.json`
- Do not start: OpenClaw gateway

- [ ] **Step 1: Back up the live OpenClaw configuration**

Run:

```powershell
Copy-Item -LiteralPath C:\Users\Helena\.openclaw\openclaw.json `
  -Destination C:\Users\Helena\.openclaw\backups\openclaw.before-filomena-monorepo.json
```

Expected: backup exists and is outside Git.

- [ ] **Step 2: Point OpenClaw at the monorepo plugins**

Run:

```powershell
openclaw.cmd config set plugins.load.paths `
  '["C:\\dev\\filomena\\plugins\\openclaw-memory-graphiti","C:\\dev\\filomena\\plugins\\whatsapp-filomena-guard"]' `
  --strict-json
```

Expected: validated write succeeds.

- [ ] **Step 3: Make the monorepo workspace active**

Run:

```powershell
openclaw.cmd config set agents.defaults.workspace `
  'C:\dev\filomena\workspace'
```

Expected: validated write succeeds.

- [ ] **Step 4: Validate configuration without starting the gateway**

Run:

```powershell
openclaw.cmd config validate
openclaw.cmd config get plugins.load.paths
openclaw.cmd config get agents.defaults.workspace
```

Expected: validation passes and all paths resolve under `C:\dev\filomena`.

- [ ] **Step 5: Verify plugin registration through a non-gateway CLI command**

Run:

```powershell
openclaw.cmd graphiti-mem status
```

Expected: plugin registers from the new path and both Graphiti and SpiceDB report connected.

- [ ] **Step 6: Do not start the gateway; hand off the existing start command**

Helena runs:

```powershell
openclaw.cmd gateway run --force --verbose
```

- [ ] **Step 7: After Helena starts it, verify the live WhatsApp session**

Run:

```powershell
openclaw.cmd channels status --probe
openclaw.cmd logs --plain --local-time --limit 200
```

Expected:

- WhatsApp is linked and connected.
- Logs show `openclaw-memory-graphiti: initialized (graphiti: OK, spicedb: OK)`.
- Logs contain no plugin-load error from the old workspace paths.

- [ ] **Step 8: Perform the final user-visible recall test**

Send in the configured WhatsApp group:

```text
@Filomena você lembra quando o Gabriel fechou Shadow of Mordor?
```

Expected: Filomena answers the current message and reports the recovered fact that Gabriel completed the game in about 25 hours. She must not claim that absent memories would necessarily have been stored.

- [ ] **Step 9: Confirm the destination tree remains clean**

Run:

```powershell
git -c safe.directory=C:/dev/filomena -C C:\dev\filomena status --short --branch
```

Expected: clean working tree. Runtime data and workspace memory remain ignored.

## Completion Boundary

Do not delete the original workspace repositories, old stopped containers,
`docker_falkordb_data`, the staging clone, or the RDB backup during this plan.
Cleanup is a separate, explicitly approved operation after at least one full
restart has demonstrated that memory persists.
