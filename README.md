# Filomena

Local multichannel OpenClaw bot with channel-specific response guards and persistent
Graphiti knowledge-graph memory backed by FalkorDB and authorized by SpiceDB.

## Privacy

Never commit WhatsApp exports, conversations, media, OpenClaw credentials,
gateway tokens, `.env`, databases, logs, state files, or backups. Historical
commits preserve old test identifiers; do not publish this repository without
a separate history-redaction review.

## Components

- `plugins/whatsapp-filomena-guard`: dispatch and reply-label policy.
- `plugins/telegram-filomena-guard`: thin Telegram response-policy adapter.
- `plugins/filomena-conversation-policy-core`: provider-neutral response evaluator and outbound-ID registry.
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
