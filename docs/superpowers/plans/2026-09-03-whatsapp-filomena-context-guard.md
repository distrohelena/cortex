# WhatsApp Filomena Context Guard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore the official WhatsApp bundle and enforce an explicit `@Filomena` group trigger through a separate source plugin while supplying a small native recent-message window and optional Graphiti recall.

**Architecture:** The official WhatsApp channel owns message receipt, bounded ambient history, and normal reply delivery. A focused `before_dispatch` guard plugin suppresses accidental dispatches in one configured group unless the current message starts with the configured trigger; ordinary accepted turns continue through OpenClaw unchanged. Configuration supplies eight recent messages and a group-only instruction that uses Graphiti only when older context is actually needed.

**Tech Stack:** OpenClaw 2026.8.1 plugin SDK, TypeScript entrypoint loaded directly by OpenClaw, JavaScript pure policy module, Node.js 24 built-in test runner, JSON OpenClaw configuration.

---

### Task 1: Create and test the pure trigger policy

**Files:**
- Create: `whatsapp-filomena-guard/package.json`
- Create: `whatsapp-filomena-guard/openclaw.plugin.json`
- Create: `whatsapp-filomena-guard/guard.test.ts`
- Create: `whatsapp-filomena-guard/guard.js`

- [ ] **Step 1: Create minimal plugin metadata**

Create `whatsapp-filomena-guard/package.json`:

```json
{
  "name": "whatsapp-filomena-guard",
  "version": "0.1.0",
  "private": true,
  "type": "module",
  "scripts": {
    "test": "node --experimental-strip-types --test guard.test.ts index.test.ts"
  },
  "openclaw": {
    "extensions": [
      "./index.ts"
    ]
  }
}
```

Create `whatsapp-filomena-guard/openclaw.plugin.json`:

```json
{
  "id": "whatsapp-filomena-guard",
  "name": "WhatsApp Filomena Guard",
  "description": "Requires an explicit leading trigger before dispatching one WhatsApp group to the agent.",
  "configSchema": {
    "type": "object",
    "additionalProperties": false,
    "required": ["groupId"],
    "properties": {
      "channel": {
        "type": "string",
        "default": "whatsapp"
      },
      "groupId": {
        "type": "string",
        "minLength": 1
      },
      "trigger": {
        "type": "string",
        "default": "@Filomena",
        "minLength": 1
      }
    }
  }
}
```

- [ ] **Step 2: Write the failing pure-policy tests**

Create `whatsapp-filomena-guard/guard.test.ts`:

```ts
import assert from "node:assert/strict";
import test from "node:test";

import { shouldSuppressDispatch } from "./guard.js";

const config = {
  channel: "whatsapp",
  groupId: "5511981042361-1589823314@g.us",
  trigger: "@Filomena",
};

function targetEvent(body?: string) {
  return {
    event: { channel: "whatsapp", isGroup: true, body },
    context: { conversationId: config.groupId },
  };
}

test("allows the exact trigger at the beginning", () => {
  const { event, context } = targetEvent("@Filomena oi");
  assert.equal(shouldSuppressDispatch(event, context, config), false);
});

test("allows leading whitespace and case-insensitive trigger text", () => {
  const { event, context } = targetEvent("  @filomena\nme ajuda");
  assert.equal(shouldSuppressDispatch(event, context, config), false);
});

test("suppresses a trigger appearing later in the message", () => {
  const { event, context } = targetEvent("oi @Filomena");
  assert.equal(shouldSuppressDispatch(event, context, config), true);
});

test("suppresses an implicit reply without the trigger", () => {
  const { event, context } = targetEvent("continua a resposta");
  assert.equal(shouldSuppressDispatch(event, context, config), true);
});

test("suppresses missing message bodies in the protected group", () => {
  const { event, context } = targetEvent(undefined);
  assert.equal(shouldSuppressDispatch(event, context, config), true);
});

test("does not accept a longer name with the same prefix", () => {
  const { event, context } = targetEvent("@Filomenaaa oi");
  assert.equal(shouldSuppressDispatch(event, context, config), true);
});

test("leaves direct messages untouched", () => {
  const event = { channel: "whatsapp", isGroup: false, body: "@ai oi" };
  const context = { conversationId: "+5511944558218" };
  assert.equal(shouldSuppressDispatch(event, context, config), false);
});

test("leaves other groups and channels untouched", () => {
  assert.equal(
    shouldSuppressDispatch(
      { channel: "whatsapp", isGroup: true, body: "sem gatilho" },
      { conversationId: "outro@g.us" },
      config,
    ),
    false,
  );
  assert.equal(
    shouldSuppressDispatch(
      { channel: "telegram", isGroup: true, body: "sem gatilho" },
      { conversationId: config.groupId },
      config,
    ),
    false,
  );
});
```

- [ ] **Step 3: Run the test and verify the missing module failure**

Run:

```powershell
node --experimental-strip-types --test .\whatsapp-filomena-guard\guard.test.ts
```

Expected: FAIL with `ERR_MODULE_NOT_FOUND` for `guard.js`.

- [ ] **Step 4: Implement the minimal pure policy**

Create `whatsapp-filomena-guard/guard.js`:

```js
function escapeRegExp(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

function triggerPattern(trigger) {
  return new RegExp(`^${escapeRegExp(trigger)}(?:\\s|$)`, "iu");
}

export function shouldSuppressDispatch(event, context, config) {
  const targetsProtectedGroup =
    event.channel === config.channel &&
    event.isGroup === true &&
    context.conversationId === config.groupId;

  if (!targetsProtectedGroup) return false;

  const body = typeof event.body === "string" ? event.body.trimStart() : "";
  return !triggerPattern(config.trigger).test(body);
}
```

- [ ] **Step 5: Run the pure-policy tests**

Run:

```powershell
node --experimental-strip-types --test .\whatsapp-filomena-guard\guard.test.ts
```

Expected: 8 tests pass, 0 fail.

- [ ] **Step 6: Commit the policy unit**

```powershell
git -c safe.directory=C:/Users/Helena/.openclaw/workspace -C C:\Users\Helena\.openclaw\workspace add -- whatsapp-filomena-guard/package.json whatsapp-filomena-guard/openclaw.plugin.json whatsapp-filomena-guard/guard.js whatsapp-filomena-guard/guard.test.ts
git -c safe.directory=C:/Users/Helena/.openclaw/workspace -C C:\Users\Helena\.openclaw\workspace commit -m "Add Filomena dispatch policy"
```

### Task 2: Register the external OpenClaw guard

**Files:**
- Create: `whatsapp-filomena-guard/index.test.ts`
- Create: `whatsapp-filomena-guard/index.ts`

- [ ] **Step 1: Write the failing registration tests**

Create `whatsapp-filomena-guard/index.test.ts`:

```ts
import assert from "node:assert/strict";
import test from "node:test";

import plugin from "./index.ts";

function registerGuard(pluginConfig: Record<string, unknown>) {
  let hookName = "";
  let handler: ((event: any, context: any) => unknown) | undefined;
  const logs: string[] = [];

  plugin.register({
    pluginConfig,
    logger: {
      debug(message: string) {
        logs.push(message);
      },
      info() {},
      warn() {},
      error() {},
    },
    on(name: string, registeredHandler: typeof handler) {
      hookName = name;
      handler = registeredHandler;
    },
  } as never);

  assert.equal(hookName, "before_dispatch");
  assert.ok(handler);
  return { handler, logs };
}

test("registers a before_dispatch hook that silently claims non-trigger replies", () => {
  const { handler, logs } = registerGuard({
    groupId: "5511981042361-1589823314@g.us",
  });
  const result = handler(
    { channel: "whatsapp", isGroup: true, body: "resposta sem gatilho" },
    { conversationId: "5511981042361-1589823314@g.us" },
  );
  assert.deepEqual(result, { handled: true });
  assert.equal(logs.length, 1);
});

test("lets explicit Filomena messages continue to the model", () => {
  const { handler } = registerGuard({
    groupId: "5511981042361-1589823314@g.us",
    trigger: "@Filomena",
  });
  const result = handler(
    { channel: "whatsapp", isGroup: true, body: "@Filomena oi" },
    { conversationId: "5511981042361-1589823314@g.us" },
  );
  assert.equal(result, undefined);
});

test("rejects missing group configuration during registration", () => {
  assert.throws(() => registerGuard({}), /groupId must be configured/);
});
```

- [ ] **Step 2: Run the registration test and verify the missing entrypoint failure**

Run:

```powershell
node --experimental-strip-types --test .\whatsapp-filomena-guard\index.test.ts
```

Expected: FAIL with `ERR_MODULE_NOT_FOUND` for `index.ts`.

- [ ] **Step 3: Implement the typed plugin entrypoint**

Create `whatsapp-filomena-guard/index.ts`:

```ts
import type { OpenClawPluginApi } from "openclaw/plugin-sdk";

import { shouldSuppressDispatch } from "./guard.js";

type GuardConfig = {
  channel: string;
  groupId: string;
  trigger: string;
};

function readConfig(api: OpenClawPluginApi): GuardConfig {
  const raw = api.pluginConfig ?? {};
  const groupId = typeof raw.groupId === "string" ? raw.groupId.trim() : "";
  if (!groupId) {
    throw new Error("whatsapp-filomena-guard: groupId must be configured");
  }

  const channel = typeof raw.channel === "string" && raw.channel.trim()
    ? raw.channel.trim()
    : "whatsapp";
  const trigger = typeof raw.trigger === "string" && raw.trigger.trim()
    ? raw.trigger.trim()
    : "@Filomena";

  return { channel, groupId, trigger };
}

const plugin = {
  id: "whatsapp-filomena-guard",
  name: "WhatsApp Filomena Guard",
  description: "Requires an explicit leading trigger in one WhatsApp group.",

  register(api: OpenClawPluginApi) {
    const config = readConfig(api);

    api.on(
      "before_dispatch",
      (event, context) => {
        if (!shouldSuppressDispatch(event, context, config)) return;

        api.logger.debug(
          `whatsapp-filomena-guard: suppressed non-trigger dispatch in ${config.groupId}`,
        );
        return { handled: true };
      },
      { priority: 100 },
    );
  },
};

export default plugin;
```

- [ ] **Step 4: Run all plugin tests**

Run:

```powershell
npm.cmd test --prefix .\whatsapp-filomena-guard
```

Expected: 11 tests pass, 0 fail.

- [ ] **Step 5: Commit plugin registration**

```powershell
git -c safe.directory=C:/Users/Helena/.openclaw/workspace -C C:\Users\Helena\.openclaw\workspace add -- whatsapp-filomena-guard/index.ts whatsapp-filomena-guard/index.test.ts
git -c safe.directory=C:/Users/Helena/.openclaw/workspace -C C:\Users\Helena\.openclaw\workspace commit -m "Register external Filomena guard"
```

### Task 3: Restore the official WhatsApp runtime file

**Files:**
- Read: `.tmp-whatsapp-original/package/dist/monitor-CySzv38g.js`
- Backup: `backups/whatsapp-2026.8.1/monitor-CySzv38g.js.modified`
- Restore: `C:\Users\Helena\.openclaw\extensions\whatsapp\dist\monitor-CySzv38g.js`

- [ ] **Step 1: Record the current and official hashes**

Run:

```powershell
Get-FileHash -Algorithm SHA256 -LiteralPath C:\Users\Helena\.openclaw\extensions\whatsapp\dist\monitor-CySzv38g.js
Get-FileHash -Algorithm SHA256 -LiteralPath C:\Users\Helena\.openclaw\workspace\.tmp-whatsapp-original\package\dist\monitor-CySzv38g.js
```

Expected: hashes differ before restoration.

- [ ] **Step 2: Preserve the modified bundle outside the extension directory**

Run:

```powershell
New-Item -ItemType Directory -Path C:\Users\Helena\.openclaw\workspace\backups\whatsapp-2026.8.1 -Force
Copy-Item -LiteralPath C:\Users\Helena\.openclaw\extensions\whatsapp\dist\monitor-CySzv38g.js -Destination C:\Users\Helena\.openclaw\workspace\backups\whatsapp-2026.8.1\monitor-CySzv38g.js.modified
```

Expected: the backup exists and retains the pre-restoration SHA-256 hash.

- [ ] **Step 3: Restore the exact official file**

Use `apply_patch` only if a textual patch is necessary. For this byte-for-byte artifact restoration, copy the known official package file:

```powershell
Copy-Item -LiteralPath C:\Users\Helena\.openclaw\workspace\.tmp-whatsapp-original\package\dist\monitor-CySzv38g.js -Destination C:\Users\Helena\.openclaw\extensions\whatsapp\dist\monitor-CySzv38g.js -Force
```

- [ ] **Step 4: Verify byte identity**

Run:

```powershell
$installed = (Get-FileHash -Algorithm SHA256 -LiteralPath C:\Users\Helena\.openclaw\extensions\whatsapp\dist\monitor-CySzv38g.js).Hash
$official = (Get-FileHash -Algorithm SHA256 -LiteralPath C:\Users\Helena\.openclaw\workspace\.tmp-whatsapp-original\package\dist\monitor-CySzv38g.js).Hash
if ($installed -ne $official) { throw "WhatsApp bundle restoration hash mismatch" }
Write-Output "official bundle restored"
```

Expected: `official bundle restored`.

### Task 4: Configure recent history, group instructions, and plugin loading

**Files:**
- Modify: `C:\Users\Helena\.openclaw\openclaw.json`

- [ ] **Step 1: Back up the active configuration**

Run:

```powershell
Copy-Item -LiteralPath C:\Users\Helena\.openclaw\openclaw.json -Destination C:\Users\Helena\.openclaw\workspace\backups\openclaw.before-filomena-guard.json
```

Expected: backup file exists without changing the active configuration.

- [ ] **Step 2: Add the external plugin entry and load path**

Under `plugins.entries`, add:

```json
"whatsapp-filomena-guard": {
  "enabled": true,
  "config": {
    "channel": "whatsapp",
    "groupId": "5511981042361-1589823314@g.us",
    "trigger": "@Filomena"
  }
}
```

Append this unique entry to `plugins.load.paths` without removing the Graphiti path:

```json
"C:\\Users\\Helena\\.openclaw\\workspace\\whatsapp-filomena-guard"
```

- [ ] **Step 3: Configure bounded recent history and the group prompt**

Under `channels.whatsapp`, add:

```json
"historyLimit": 8
```

Change the target group entry to:

```json
"5511981042361-1589823314@g.us": {
  "requireMention": true,
  "systemPrompt": "Use the recent WhatsApp messages supplied by the channel as immediate conversational context. Answer the request following the leading @Filomena trigger. If the user asks for historical analysis, or older or broader group context is clearly required, call memory_recall to search Graphiti before answering. Do not call Graphiti for ordinary requests. Do not guess or claim historical context when no relevant memory is returned."
}
```

- [ ] **Step 4: Validate the active configuration without starting the gateway**

Run:

```powershell
openclaw.cmd config validate
```

Expected: configuration is valid, with no unknown plugin or channel fields.

- [ ] **Step 5: Verify plugin discovery**

Run:

```powershell
openclaw.cmd plugins inspect whatsapp-filomena-guard
openclaw.cmd plugins doctor
```

Expected: `whatsapp-filomena-guard` is discovered and enabled; doctor reports no issue for it.

### Task 5: Final verification and manual WhatsApp handoff

**Files:**
- Verify: `whatsapp-filomena-guard/*`
- Verify: `C:\Users\Helena\.openclaw\openclaw.json`
- Verify: `C:\Users\Helena\.openclaw\extensions\whatsapp\dist\monitor-CySzv38g.js`

- [ ] **Step 1: Run the complete automated verification**

Run:

```powershell
npm.cmd test --prefix C:\Users\Helena\.openclaw\workspace\whatsapp-filomena-guard
openclaw.cmd config validate
openclaw.cmd plugins inspect whatsapp-filomena-guard
```

Expected: 11 tests pass, configuration validation succeeds, and the plugin is enabled.

- [ ] **Step 2: Recheck bundle integrity**

Run:

```powershell
$installed = (Get-FileHash -Algorithm SHA256 -LiteralPath C:\Users\Helena\.openclaw\extensions\whatsapp\dist\monitor-CySzv38g.js).Hash
$official = (Get-FileHash -Algorithm SHA256 -LiteralPath C:\Users\Helena\.openclaw\workspace\.tmp-whatsapp-original\package\dist\monitor-CySzv38g.js).Hash
if ($installed -ne $official) { throw "WhatsApp bundle is not official" }
Write-Output "bundle verified"
```

Expected: `bundle verified`.

- [ ] **Step 3: Confirm only intended repository files changed**

Run:

```powershell
git -c safe.directory=C:/Users/Helena/.openclaw/workspace -C C:\Users\Helena\.openclaw\workspace status --short
```

Expected: no tracked plugin or plan changes remain uncommitted. Existing unrelated untracked workspace files remain untouched.

- [ ] **Step 4: Hand off manual runtime checks**

Helena starts the gateway herself:

```powershell
openclaw.cmd gateway run --force --verbose
```

Then verify in the target WhatsApp group:

1. Send an ordinary message and reply to an old Filomena response without the trigger; neither produces a model run or response.
2. Send several ordinary messages, then `@Filomena resuma a conversa`; Filomena receives the recent bounded history and replies to the triggering message.
3. Send `@Filomena usando o histórico antigo, quem costuma falar de jogos?`; Filomena may use `memory_recall` for Graphiti context.
4. Send `@ai oi` in the self-chat; direct-message behavior remains unchanged.
