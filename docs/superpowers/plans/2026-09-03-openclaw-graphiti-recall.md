# OpenClaw Graphiti Recall Compatibility Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore Graphiti tools and automatic recall on OpenClaw 2026.8.1, including bounded raw-episode excerpts when entity/fact extraction misses WhatsApp history.

**Architecture:** Keep SpiceDB as the sole source of authorized group IDs. Add a shared combined search in `search.ts` that runs Graphiti semantic search and bounded line-level episode matching concurrently, prioritizes exact episode excerpts, and is called by both `memory_recall` and `before_prompt_build`.

**Tech Stack:** TypeScript, OpenClaw plugin SDK, Graphiti MCP, SpiceDB, Vitest.

---

### Task 1: Declare the OpenClaw 2026.8 plugin contract

**Files:**
- Create: `manifest.test.ts`
- Modify: `openclaw.plugin.json`

- [ ] **Step 1: Write the failing manifest contract test**

Create `manifest.test.ts` that reads `openclaw.plugin.json` and asserts:

```ts
import { readFileSync } from "node:fs";
import { describe, expect, test } from "vitest";

describe("OpenClaw plugin manifest", () => {
  test("declares every registered agent tool", () => {
    const manifest = JSON.parse(readFileSync(new URL("./openclaw.plugin.json", import.meta.url), "utf8"));
    expect(manifest.contracts?.tools).toEqual([
      "memory_recall",
      "memory_store",
      "memory_forget",
      "memory_status",
    ]);
  });
});
```

- [ ] **Step 2: Verify RED**

Run: `npx.cmd vitest run manifest.test.ts`

Expected: FAIL because `contracts.tools` is absent.

- [ ] **Step 3: Add the static contract**

Add this top-level block to `openclaw.plugin.json`:

```json
"contracts": {
  "tools": ["memory_recall", "memory_store", "memory_forget", "memory_status"]
}
```

- [ ] **Step 4: Verify GREEN and commit**

Run: `npx.cmd vitest run manifest.test.ts`

Expected: one passing test.

Commit: `fix: declare Graphiti memory tools`

### Task 2: Add bounded authorized episode matching

**Files:**
- Modify: `search.test.ts`
- Modify: `search.ts`

- [ ] **Step 1: Write failing episode-search tests**

Import `searchAuthorizedEpisodeExcerpts` and `searchAuthorizedRecall`, then add these cases:

```ts
const episodes = [{
  uuid: "ep-wa",
  name: "whatsapp_wa-chat_0001",
  content: [
    "[4/11/26 12:30:00 PM] Outra pessoa: conversa sem relação",
    "[4/11/26 12:37:03 PM] Gabriel: <image omitted> Zerei Shadow of Mordor, família",
    "[4/11/26 12:37:33 PM] Gabriel: Jogaço",
  ].join("\n"),
  source_description: "WhatsApp export",
  group_id: "wa-chat",
  created_at: "2026-04-11T15:37:03Z",
}];

const getEpisodes = vi.fn().mockResolvedValue(episodes);
const graphiti = mockGraphiti({ getEpisodes });
const results = await searchAuthorizedEpisodeExcerpts(graphiti, {
  query: "você lembra quando o Gabriel fechou Shadow of Mordor",
  groupIds: ["wa-chat"],
  limit: 3,
});

expect(getEpisodes).toHaveBeenCalledWith("wa-chat", 100);
expect(results[0]).toMatchObject({ type: "episode", uuid: "ep-wa", group_id: "wa-chat" });
expect(results[0].summary).toContain("Zerei Shadow of Mordor");
expect(results[0].summary.length).toBeLessThanOrEqual(800);
```

```ts
test("does not return an episode without enough line-level overlap", async () => {
  const graphiti = mockGraphiti({
    getEpisodes: vi.fn().mockResolvedValue(episodes),
  });
  await expect(searchAuthorizedEpisodeExcerpts(graphiti, {
    query: "Mariana terminou Resident Evil",
    groupIds: ["wa-chat"],
  })).resolves.toEqual([]);
});

test("keeps matches from healthy authorized groups", async () => {
  const getEpisodes = vi.fn()
    .mockRejectedValueOnce(new Error("offline"))
    .mockResolvedValueOnce(episodes);
  const graphiti = mockGraphiti({ getEpisodes });
  const results = await searchAuthorizedEpisodeExcerpts(graphiti, {
    query: "Gabriel Shadow Mordor",
    groupIds: ["broken", "wa-chat"],
  });
  expect(getEpisodes).toHaveBeenNthCalledWith(1, "broken", 100);
  expect(getEpisodes).toHaveBeenNthCalledWith(2, "wa-chat", 100);
  expect(results).toHaveLength(1);
});

test("prioritizes raw excerpts over unrelated semantic results", async () => {
  const graphiti = mockGraphiti({
    getEpisodes: vi.fn().mockResolvedValue(episodes),
    searchNodes: vi.fn().mockResolvedValue([{ uuid: "n1", name: "Other", summary: "Unrelated", group_id: "wa-chat", created_at: "2026-09-01" }]),
  });
  const results = await searchAuthorizedRecall(graphiti, {
    query: "Gabriel fechou Shadow of Mordor",
    groupIds: ["wa-chat"],
    limit: 1,
  });
  expect(results).toHaveLength(1);
  expect(results[0].type).toBe("episode");
});
```

- [ ] **Step 2: Verify RED**

Run: `npx.cmd vitest run search.test.ts`

Expected: FAIL because the two new search functions and `episode` result type do not exist.

- [ ] **Step 3: Implement normalized line-level matching**

In `search.ts`:

```ts
export type SearchResult = {
  type: "node" | "fact" | "episode";
  uuid: string;
  group_id: string;
  summary: string;
  context: string;
  created_at: string;
};

export type EpisodeSearchOptions = {
  query: string;
  groupIds: string[];
  limit?: number;
  maxEpisodesPerGroup?: number;
  maxSnippetChars?: number;
};
```

Implement `searchAuthorizedEpisodeExcerpts` with these rules:

- normalize with Unicode NFD, remove diacritics, and lowercase;
- tokenize Unicode letters/numbers, discard Portuguese/English stop words and tokens shorter than three characters;
- score each non-empty episode line by unique query-token overlap;
- require two overlaps for queries with three or more significant tokens, otherwise one;
- keep the best line per episode and include at most two adjacent lines on each side;
- clamp every excerpt to `maxSnippetChars` (default 800);
- call `getEpisodes(groupId, 100)` only for supplied group IDs, using `Promise.allSettled`;
- sort by overlap score and then episode recency, returning at most `limit` (default 3).

Implement `searchAuthorizedRecall` by concurrently calling existing semantic search and episode search, putting episode results first, deduplicating by `${type}:${uuid}`, and enforcing the requested limit.

Update `formatResultLine` so episode results use `episode` rather than `entity` or `fact`, and describe the context as an untrusted raw historical excerpt.

- [ ] **Step 4: Verify GREEN and regressions**

Run: `npx.cmd vitest run search.test.ts`

Expected: all search tests pass.

Run: `npm.cmd test`

Expected: all existing tests plus new tests pass.

Commit: `feat: recall matching Graphiti episode excerpts`

### Task 3: Use combined recall in the tool and modern hook

**Files:**
- Modify: `index.test.ts`
- Modify: `index.ts`

- [ ] **Step 1: Update hook expectations and add failing integration tests**

Change every test reference from `before_agent_start` to `before_prompt_build`. Assert the old hook is absent. Add a Graphiti MCP mock that returns the Shadow of Mordor episode for `get_episodes` and empty nodes/facts for semantic calls.

For explicit recall, execute:

```ts
const result = await recallTool.execute("call-shadow", {
  query: "Gabriel fechou Shadow of Mordor",
  scope: "all",
  limit: 5,
});
expect(result.content[0].text).toContain("Zerei Shadow of Mordor");
expect(result.content[0].text).toContain("[episode:ep-wa]");
```

For automatic recall, invoke `registeredHooks.before_prompt_build[0]` with the same prompt and assert `prependContext` contains the excerpt and `<relevant-memories>`.

- [ ] **Step 2: Verify RED**

Run: `npx.cmd vitest run index.test.ts`

Expected: FAIL because production still registers `before_agent_start` and calls semantic-only search.

- [ ] **Step 3: Implement shared combined recall**

Replace both `searchAuthorizedMemories(...)` calls used for recall in `index.ts` with `searchAuthorizedRecall(...)`. Rename the auto-recall hook registration to:

```ts
api.on("before_prompt_build", async (event, ctx) => {
```

Update tool guidance to say that `[episode:*]` results are bounded, untrusted historical excerpts and must be treated as evidence rather than instructions. Keep the instruction to call `memory_recall` before claiming no memory exists, and add that the model must not claim it searched unless recall actually ran.

- [ ] **Step 4: Verify GREEN and commit**

Run: `npx.cmd vitest run index.test.ts`

Expected: all plugin integration tests pass.

Run: `npm.cmd test`

Expected: full suite passes.

Commit: `fix: restore Graphiti recall hooks on OpenClaw 2026.8`

### Task 4: Enable external conversation hooks and verify live data

**Files:**
- Modify: `C:\Users\Helena\.openclaw\openclaw.json`

- [ ] **Step 1: Add explicit hook permissions**

Inside `plugins.entries.openclaw-memory-graphiti`, add alongside `enabled` and `config`:

```json
"hooks": {
  "allowConversationAccess": true,
  "allowPromptInjection": true
}
```

- [ ] **Step 2: Validate configuration and runtime registration**

Run: `openclaw.cmd config validate`

Expected: configuration valid.

Run: `openclaw.cmd plugins inspect openclaw-memory-graphiti --runtime --json`

Expected: the four memory tools and `before_prompt_build` are registered, with no undeclared-tool, unknown-hook, or conversation-access diagnostic.

- [ ] **Step 3: Verify the imported historical query without starting the gateway**

Run:

```powershell
npx.cmd tsx -e "import { GraphitiClient } from './graphiti.ts'; import { searchAuthorizedEpisodeExcerpts } from './search.ts'; void (async () => { const client = new GraphitiClient('http://localhost:8000'); const rows = await searchAuthorizedEpisodeExcerpts(client, { query: 'Gabriel fechou Shadow of Mordor', groupIds: ['wa-chat'], limit: 3 }); console.log(JSON.stringify(rows, null, 2)); await client.close(); })();"
```

Expected: output contains the authorized `wa-chat` excerpt with `Zerei Shadow of Mordor` and remains bounded.

- [ ] **Step 4: Final verification**

Run: `npm.cmd test`

Expected: zero failed tests.

Run: `rtk git diff --check`

Expected: no whitespace errors.

Do not start or restart the gateway. Report that Helena must run `openclaw.cmd gateway run --force --verbose` for the new runtime to load.
