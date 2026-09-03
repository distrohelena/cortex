# WhatsApp Filomena Identity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Allow implicit replies only to OpenClaw-sent Filomena messages, persist those message IDs across restarts, and prefix protected-group bot replies with `Filomena: `.

**Architecture:** Keep all behavior in the external `whatsapp-filomena-guard` source plugin. A small JSON-backed bounded registry records successful outbound provider message IDs; inbound admission consults that registry, while a group-scoped outbound hook enforces the visible prefix. The official compiled WhatsApp extension remains untouched.

**Tech Stack:** OpenClaw 2026.8.1 typed plugin hooks, TypeScript loaded directly by Node, JavaScript pure modules, Node test runner, JSON configuration.

---

### Task 1: Persistent outbound message-ID registry

**Files:**
- Create: `whatsapp-filomena-guard/state.js`
- Create: `whatsapp-filomena-guard/state.test.ts`
- Modify: `whatsapp-filomena-guard/package.json`

- [ ] **Step 1: Add failing persistence tests**

Create tests using `mkdtempSync(join(tmpdir(), "filomena-state-"))`. Exercise this public interface:

```js
const registry = createMessageIdRegistry({
  filePath,
  maxEntries: 3,
  warn(message) {
    warnings.push(message);
  },
});

registry.record(" bot-1 ");
assert.equal(registry.has("bot-1"), true);
assert.deepEqual(registry.snapshot(), ["bot-1"]);
```

Cover reload from disk, de-duplication that moves a repeated ID to newest position, eviction of the oldest ID, rejection of blank/non-string IDs, missing-file startup, and malformed JSON warning with an empty usable registry.

- [ ] **Step 2: Run tests and verify RED**

Run: `npm.cmd test`

Expected: failure because `state.js` does not exist.

- [ ] **Step 3: Implement the registry**

Implement `state.js` with these exports and behavior:

```js
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname } from "node:path";

export function normalizeMessageId(value) {
  if (typeof value !== "string") return null;
  const normalized = value.trim();
  return normalized || null;
}

export function createMessageIdRegistry({ filePath, maxEntries, warn }) {
  const capacity = Number.isInteger(maxEntries) && maxEntries > 0 ? maxEntries : 512;
  let messageIds = [];

  try {
    const parsed = JSON.parse(readFileSync(filePath, "utf8"));
    const source = Array.isArray(parsed?.messageIds) ? parsed.messageIds : [];
    for (const value of source) {
      const id = normalizeMessageId(value);
      if (!id) continue;
      messageIds = messageIds.filter((candidate) => candidate !== id);
      messageIds.push(id);
    }
    messageIds = messageIds.slice(-capacity);
  } catch (error) {
    if (!(error && typeof error === "object" && "code" in error && error.code === "ENOENT")) {
      warn(`whatsapp-filomena-guard: could not load state: ${String(error)}`);
    }
  }

  function persist() {
    mkdirSync(dirname(filePath), { recursive: true });
    writeFileSync(filePath, `${JSON.stringify({ version: 1, messageIds }, null, 2)}\n`, "utf8");
  }

  return {
    has(value) {
      const id = normalizeMessageId(value);
      return id ? messageIds.includes(id) : false;
    },
    record(value) {
      const id = normalizeMessageId(value);
      if (!id) return false;
      messageIds = messageIds.filter((candidate) => candidate !== id);
      messageIds.push(id);
      messageIds = messageIds.slice(-capacity);
      try {
        persist();
      } catch (error) {
        warn(`whatsapp-filomena-guard: could not persist state: ${String(error)}`);
      }
      return true;
    },
    snapshot() {
      return [...messageIds];
    },
  };
}
```

Update the test script to include `state.test.ts`:

```json
"test": "node --experimental-strip-types --test guard.test.ts state.test.ts index.test.ts"
```

- [ ] **Step 4: Run tests and verify GREEN**

Run: `npm.cmd test`

Expected: all registry tests and the original 12 tests pass.

- [ ] **Step 5: Commit the registry**

```powershell
rtk git add -- whatsapp-filomena-guard/state.js whatsapp-filomena-guard/state.test.ts whatsapp-filomena-guard/package.json
rtk git commit -m "Add persistent Filomena message registry"
```

### Task 2: Pure inbound and outbound policy

**Files:**
- Modify: `whatsapp-filomena-guard/guard.js`
- Modify: `whatsapp-filomena-guard/guard.test.ts`

- [ ] **Step 1: Add failing policy tests**

Add cases proving tracked `replyToId` and `replyToIdFull` are admitted, untracked and blank IDs are suppressed, and IDs exposed only on hook context are supported. Add outbound scope/prefix cases using:

```js
assert.equal(
  shouldSuppressDispatch(
    { channel: "whatsapp", isGroup: true, body: "continua", replyToId: "bot-1" },
    protectedContext,
    config,
    { has: (id) => id === "bot-1" },
  ),
  false,
);

assert.equal(
  prefixProtectedOutbound(
    { to: config.groupId, content: "Olá" },
    { channelId: "whatsapp", conversationId: config.groupId },
    { ...config, responsePrefix: "Filomena: " },
  ),
  "Filomena: Olá",
);
```

Also prove the prefix is not duplicated and that private chats, other groups, other channels, and empty content are untouched.

- [ ] **Step 2: Run tests and verify RED**

Run: `npm.cmd test`

Expected: failures for tracked replies and missing outbound helpers.

- [ ] **Step 3: Implement minimal policy helpers**

Keep explicit-trigger matching unchanged. Add exact, trimmed, case-sensitive provider-ID matching across both event and context reply fields:

```js
function replyTargetsTrackedMessage(event, context, registry) {
  return [event.replyToId, event.replyToIdFull, context.replyToId, context.replyToIdFull]
    .some((value) => registry.has(value));
}

export function shouldSuppressDispatch(event, context, config, registry = { has: () => false }) {
  if (!targetsProtectedInbound(event, context, config)) return false;
  const body = typeof event.body === "string" ? event.body.trimStart() : "";
  if (triggerPattern(config.trigger).test(body)) return false;
  return !replyTargetsTrackedMessage(event, context, registry);
}
```

Add `targetsProtectedOutbound(event, context, config)` using `context.channelId` and exact `context.conversationId`/`event.to` matching. Add `prefixProtectedOutbound` that returns the original content outside the protected group, for empty content, or when the exact prefix is already present; otherwise return the prefixed content.

- [ ] **Step 4: Run tests and verify GREEN**

Run: `npm.cmd test`

Expected: all pure-policy, registry, and existing tests pass.

- [ ] **Step 5: Commit policy behavior**

```powershell
rtk git add -- whatsapp-filomena-guard/guard.js whatsapp-filomena-guard/guard.test.ts
rtk git commit -m "Gate implicit replies by tracked Filomena IDs"
```

### Task 3: Register the three OpenClaw hooks

**Files:**
- Modify: `whatsapp-filomena-guard/index.ts`
- Modify: `whatsapp-filomena-guard/index.test.ts`
- Modify: `whatsapp-filomena-guard/openclaw.plugin.json`

- [ ] **Step 1: Add failing hook-registration tests**

Change the harness to capture handlers by name and supply `resolvePath`. Use a temporary `stateFile`. Assert registrations for:

```ts
assert.deepEqual(
  registrations.map((entry) => entry.name),
  ["before_dispatch", "message_sending", "message_sent"],
);
```

Verify this sequence:

1. An untracked implicit reply is claimed with `{ handled: true }`.
2. A successful protected-group `message_sent` with `messageId: "bot-1"` persists the ID.
3. The same implicit reply is then admitted.
4. `message_sending` returns `{ content: "Filomena: Olá" }` only for the protected group.
5. Failed sends and sends for other targets do not become tracked.
6. Re-registering the plugin with the same state file admits a reply to `bot-1`.

- [ ] **Step 2: Run tests and verify RED**

Run: `npm.cmd test`

Expected: the plugin still registers only `before_dispatch`.

- [ ] **Step 3: Extend configuration and register hooks**

Extend `GuardConfig` and `readConfig`:

```ts
type GuardConfig = {
  channel: string;
  groupId: string;
  trigger: string;
  responsePrefix: string;
  stateFile: string;
  maxTrackedMessageIds: number;
};
```

Defaults are `Filomena: `, `api.resolvePath("~/.openclaw/state/whatsapp-filomena-guard.json")`, and `512`. Reject blank `responsePrefix`/`stateFile` and non-positive/non-integer `maxTrackedMessageIds`.

Create the registry during `register`, pass it to `shouldSuppressDispatch`, and add:

```ts
api.on(
  "message_sending",
  (event, context) => {
    const content = prefixProtectedOutbound(event, context, config);
    return content === event.content ? undefined : { content };
  },
  { priority: -100 },
);

api.on("message_sent", (event, context) => {
  if (!event.success || !targetsProtectedOutbound(event, context, config)) return;
  registry.record(event.messageId);
});
```

Update plugin descriptions and add schema properties:

```json
"responsePrefix": { "type": "string", "default": "Filomena: ", "minLength": 1 },
"stateFile": { "type": "string", "minLength": 1 },
"maxTrackedMessageIds": { "type": "integer", "default": 512, "minimum": 1, "maximum": 10000 }
```

- [ ] **Step 4: Run tests and verify GREEN**

Run: `npm.cmd test`

Expected: every unit and integration test passes.

- [ ] **Step 5: Commit hook integration**

```powershell
rtk git add -- whatsapp-filomena-guard/index.ts whatsapp-filomena-guard/index.test.ts whatsapp-filomena-guard/openclaw.plugin.json
rtk git commit -m "Track and label Filomena WhatsApp replies"
```

### Task 4: Integrate and configure the active OpenClaw installation

**Files:**
- Modify after branch integration: `C:\Users\Helena\.openclaw\openclaw.json`
- Create at runtime: `C:\Users\Helena\.openclaw\state\whatsapp-filomena-guard.json`

- [ ] **Step 1: Verify the feature branch**

Run in `whatsapp-filomena-guard`:

```powershell
npm.cmd test
```

Run in the worktree root:

```powershell
rtk git diff master...HEAD --check
rtk git status --short
```

Expected: all tests pass, diff check is clean, and only intentionally untracked test artifacts are absent.

- [ ] **Step 2: Integrate the branch into master**

Use the finishing-a-development-branch workflow, then fast-forward `master` only after selecting the local integration option. Do not start the gateway.

- [ ] **Step 3: Update active plugin configuration**

Add these exact fields under `plugins.entries.whatsapp-filomena-guard.config`:

```json
"responsePrefix": "Filomena: ",
"stateFile": "C:\\Users\\Helena\\.openclaw\\state\\whatsapp-filomena-guard.json",
"maxTrackedMessageIds": 512
```

Replace the target group prompt with text that distinguishes explicit and verified implicit requests:

```text
The current triggering WhatsApp message is always the primary request. Answer it directly and naturally. If it starts with @Filomena, treat only the text after that leading trigger as the user's request. If it is a verified direct reply to a previous Filomena message and has no @Filomena trigger, treat the entire current message as the user's request. The quoted reply and recent WhatsApp messages are secondary context only: use them to clarify the current request, but never answer, continue, or reinterpret an older message unless the current request explicitly asks you to analyze that context. If the current request asks for historical analysis, or genuinely requires older or broader group context, call memory_recall to search Graphiti before answering. Do not call Graphiti for ordinary requests. Do not guess or claim historical context when no relevant memory is returned.
```

- [ ] **Step 4: Validate without starting the gateway**

Run:

```powershell
openclaw.cmd config validate
openclaw.cmd plugins list
```

Expected: configuration valid and `whatsapp-filomena-guard` discovered/enabled. Do not run `gateway run`, `gateway start`, or `gateway restart`.

- [ ] **Step 5: Report the manual test sequence**

After Helena manually restarts the gateway:

1. Send `@Filomena como vai`; expect a threaded response beginning `Filomena: `.
2. Reply directly to that new Filomena response without a trigger; expect a response.
3. Reply directly to a manually typed Helena message without a trigger; expect silence.
4. Test private self-chat `@ai`; expect unchanged private behavior.

Messages sent before the updated plugin starts are intentionally absent from the registry and do not qualify for implicit replies.
