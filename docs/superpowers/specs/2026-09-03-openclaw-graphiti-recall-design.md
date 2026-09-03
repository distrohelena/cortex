# OpenClaw 2026.8 Graphiti Recall Compatibility Design

## Goal

Make Filomena reliably retrieve authorized WhatsApp history from Graphiti on OpenClaw 2026.8.1, including facts that exist only in a stored raw episode, without modifying OpenClaw's compiled WhatsApp bundle or starting the gateway.

## Confirmed failures

The installed OpenClaw rejects the plugin's four agent tools because `openclaw.plugin.json` does not declare `contracts.tools`. It also ignores the retired `before_agent_start` hook and blocks external conversation hooks unless the plugin entry explicitly grants conversation access. Prompt injection has a separate explicit permission.

The imported `wa-chat` episode contains `Gabriel: ... Zerei Shadow of Mordor, família`, but Graphiti did not extract that event into a searchable node or fact. The current recall implementation searches only nodes and facts, so even a successfully registered `memory_recall` tool misses the event.

## Design

### Runtime compatibility

- Declare `memory_recall`, `memory_store`, `memory_forget`, and `memory_status` in manifest `contracts.tools`.
- Replace `before_agent_start` with `before_prompt_build`, preserving `prependContext` behavior and session tracking.
- Set `plugins.entries.openclaw-memory-graphiti.hooks.allowConversationAccess` and `allowPromptInjection` to `true` in the active OpenClaw configuration.

### Authorized raw-episode recall

Add a search helper that receives only group IDs already authorized by SpiceDB. For each authorized group it loads a bounded number of recent Graphiti episodes, normalizes accents/case, tokenizes the query, and scores individual lines by significant token overlap. This line-level scoring avoids matching unrelated words scattered through a large WhatsApp export.

For each matching episode, return one bounded excerpt around its highest-scoring line. Require two matching significant terms for ordinary queries, or one when the query itself has at most two significant terms. Limit retrieval to 100 episodes per group, three excerpt results per scoped search, and 800 characters per excerpt.

Run semantic node/fact search and raw-episode search together. Put exact episode excerpts before semantic results, deduplicate by typed ID, then enforce the caller's result limit. This is important because Graphiti can return semantically unrelated nodes/facts instead of an empty result.

Keep only excerpts tied for the strongest lexical overlap. This prevents a weaker episode containing generic terms such as `Gabriel` and `fechou` from accompanying a stronger result containing `Gabriel`, `Shadow`, and `Mordor`.

Cache each bounded group snapshot for five minutes, share in-flight loads, and warm authorized long-term groups in the background when the plugin service starts. The first SSE JSON-RPC event must be parsed incrementally; neither stream closure nor cancellation acknowledgement may delay a completed response.

### Shared recall path

Both explicit `memory_recall` and automatic `before_prompt_build` recall use the same combined-search function. Output labels raw matches as `[episode:<uuid>]` so the model knows the provenance. Existing authorization boundaries and long-term/session separation remain unchanged.

### Prompt behavior

The injected memory guidance tells the model that raw excerpts are historical evidence, to answer from them when relevant, and to call `memory_recall` before claiming that it cannot remember. It must not claim that it searched memory unless a search or injected result actually occurred.

## Files

- `openclaw.plugin.json`: static tool ownership contract.
- `search.ts`: normalized lexical episode matching and combined recall.
- `search.test.ts`: matching, limits, authorization scope, prioritization, and formatting tests.
- `graphiti.ts`: incremental SSE response parsing.
- `graphiti.test.ts`: regression for long-lived SSE connections.
- `index.ts`: shared combined recall and modern hook name.
- `index.test.ts`: tool and hook integration regressions.
- `C:\Users\Helena\.openclaw\openclaw.json`: external hook permission flags.

## Acceptance criteria

- Runtime plugin inspection reports all four tools and `before_prompt_build`, with no undeclared-tool or unknown-hook diagnostic.
- Automatic recall and explicit `memory_recall` can surface the raw excerpt containing Gabriel and Shadow of Mordor.
- Episode retrieval never queries a group not returned by SpiceDB authorization or the current authorized session scope.
- Injected episode text is bounded and clearly marked as untrusted historical data.
- A long-lived SSE response returns as soon as its first JSON-RPC event arrives.
- Weaker lexical false positives are omitted when a more specific episode match exists.
- Existing unit tests and new regressions pass.
- The active gateway is not started or restarted by this work.
