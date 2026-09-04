# Telegram canary for Filomena

This setup keeps memory conversation-scoped. A Telegram DM, a Telegram group,
and every WhatsApp conversation use different Graphiti namespaces. Telegram
forum topics share the base chat's social namespace; `threadId` remains routing
and provenance metadata.

## Current lifecycle

- OpenClaw `message_received` -> Telegram adapter -> common memory recorder.
- OpenClaw `message_sent` -> Telegram adapter -> common memory recorder.
- `telegram-filomena-guard` decides whether model dispatch is allowed. It does
  not own or suppress passive observation.
- DMs bind the numeric peer structurally as addressee.
- Group outbound messages are saved without addressee because OpenClaw 2026.8.1
  does not reliably carry `runId` through `message_sent`. No session/time/order
  fallback is used, so person-specific outbound evidence is not promoted there.

Namespaces are deterministic:

```text
telegram-dm-pos-8734062810
telegram-group-neg-1001234567890
```

`accountId`, display names, usernames, topic IDs and session keys do not alter
these identifiers.

## Create the bot

1. In Telegram, open the verified `@BotFather` account.
2. Run `/newbot`, choose the display name and username, and copy the bot token.
3. Run `/setjoingroups` and enable group joins.
4. Run `/setprivacy`, select the bot and choose **Disable** so passive group
   observation is possible. Making the bot an administrator is the alternative.
5. After changing privacy mode, remove and re-add the bot in each test group.

Telegram does not use `openclaw channels login telegram`. The token is supplied
through OpenClaw configuration, `tokenFile`, or `TELEGRAM_BOT_TOKEN` for the
default account. Prefer a secret environment variable or protected token file;
never place it in this repository.

## Canary configuration

Replace the IDs below with numeric Telegram IDs. Group/supergroup IDs are
negative (usually `-100...`); user IDs are positive.

```json5
{
  messages: {
    inbound: { byChannel: { telegram: 0 } },
  },
  channels: {
    telegram: {
      enabled: true,
      dmPolicy: "allowlist",
      allowFrom: ["<HELENA_TELEGRAM_USER_ID>"],
      groupPolicy: "allowlist",
      groups: {
        "<TELEGRAM_TEST_GROUP_ID>": {
          groupPolicy: "open",
          requireMention: false,
        },
      },
    },
  },
  plugins: {
    entries: {
      "telegram-filomena-guard": {
        enabled: true,
        config: {
          respond: {
            direct: {
              mode: "allow",
              chats: ["<HELENA_TELEGRAM_USER_ID>"],
            },
            groups: {
              "<TELEGRAM_TEST_GROUP_ID>": {
                mode: "mention-or-reply",
              },
            },
          },
          stateFile: "<OPENCLAW_STATE_DIR>\\telegram-filomena-guard.json",
          maxTrackedMessageIds: 512,
        },
      },
    },
    load: {
      paths: [
        "C:\\dev\\filomena\\plugins\\telegram-filomena-guard",
      ],
    },
  },
}
```

`requireMention: false` is intentional: OpenClaw must admit each group message
far enough for `message_received` to observe it. The guard then claims unrelated
messages in `before_dispatch` before a model run. The zero Telegram inbound
debounce preserves one native message per episode.

## Bring-up and checks

1. Set `TELEGRAM_BOT_TOKEN` in the shell that will launch OpenClaw, or configure
   an equivalent protected `tokenFile`.
2. Validate configuration with `openclaw.cmd config validate`.
3. Start the gateway manually with
   `openclaw.cmd gateway run --force --verbose`.
4. DM the bot. If using pairing instead of the allowlist shown above, run
   `openclaw.cmd pairing list telegram`, then
   `openclaw.cmd pairing approve telegram <CODE>`.
5. Add the bot to the explicitly configured test group.
6. Send one unmentioned message: it should create memory but no response.
7. Send a native `@bot_username` mention: it should create one inbound episode,
   one actual response, and one outbound episode.
8. Confirm namespaces with Graphiti tooling/logs before enabling more chats.

Useful discovery commands from the installed OpenClaw documentation are
`openclaw.cmd logs --follow` and `/whoami@<bot_username>`. Bot API `getUpdates`
also exposes `from.id` and `chat.id`, but only one long poller may use a token at
a time; do not run it while the gateway is polling.
