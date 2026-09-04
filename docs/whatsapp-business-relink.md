# WhatsApp Business relink runbook

OpenClaw 2026.8.1 connects WhatsApp through Baileys/WhatsApp Web linked devices. A WhatsApp Business app account uses the same QR login flow; there is no separate Business provider mode.

## Current state and affected files

- OpenClaw config: `C:\Users\Helena\.openclaw\openclaw.json`
- Current default WhatsApp auth directory: `C:\Users\Helena\.openclaw\credentials\whatsapp\default`
- Main credential records: `creds.json` and `creds.json.bak`
- The same directory contains device, identity, LID mapping, pre-key, sender-key, and session JSON files used by Baileys.
- Guard reply-ID state: `C:\Users\Helena\.openclaw\state\whatsapp-filomena-guard.json`
- Graphiti/FalkorDB data is separate and is not removed or renamed by relinking.

An offline config backup already exists at:

`C:\Users\Helena\.openclaw\openclaw.json.pre-business-20260904`

## Relink procedure

Run this only while Helena is present to scan the QR code. The gateway should remain stopped until login and the self-identity config update are complete.

```powershell
openclaw.cmd gateway stop

$waAuth = 'C:\Users\Helena\.openclaw\credentials\whatsapp\default'
$waBackup = 'C:\Users\Helena\.openclaw\credentials\whatsapp\default.pre-business-20260904'
if (Test-Path -LiteralPath $waBackup) { throw "Backup already exists: $waBackup" }
Copy-Item -LiteralPath $waAuth -Destination $waBackup -Recurse

openclaw.cmd channels logout --channel whatsapp --account default
openclaw.cmd channels login --channel whatsapp --account default --verbose
```

On the Business phone, open **WhatsApp Business → Linked devices → Link a device** and scan the QR shown by OpenClaw. Do not delete the dated backup after login.

Then inspect without starting the gateway:

```powershell
openclaw.cmd channels status
openclaw.cmd config validate
```

Before the first gateway start, set `plugins.entries.openclaw-memory-graphiti.config.whatsapp.selfActorIds` to the new Business account's canonical JID, for example `5511XXXXXXXX@s.whatsapp.net`. This makes self-chat detection deterministic. The value is metadata only and does not participate in conversation namespaces.

After the Business account has been added to the existing group, its group JID remains `5511981042361-1589823314@g.us`, so the Graphiti namespace remains `whatsapp-group-5511981042361-1589823314-g-us`.

Stop here. Starting the gateway and running inbound/outbound canaries are a separate step.

