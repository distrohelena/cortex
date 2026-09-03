/**
 * Parallel Multi-Group Search + Merge/Re-rank
 *
 * Issues parallel search calls to Graphiti (one per authorized group_id),
 * merges results, deduplicates, and re-ranks.
 */

import type {
  GraphitiClient,
  GraphitiEpisode,
  GraphitiNode,
  GraphitiFact,
} from "./graphiti.js";

// ============================================================================
// Types
// ============================================================================

export type SearchResult = {
  type: "node" | "fact" | "episode";
  uuid: string;
  group_id: string;
  summary: string;
  /** Additional context: entity names for facts, node name for nodes */
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

export type SearchOptions = {
  query: string;
  groupIds: string[];
  limit?: number;
  searchNodes?: boolean;
  searchFacts?: boolean;
  entityTypes?: string[];
  centerNodeUuid?: string;
};

// ============================================================================
// Search
// ============================================================================

/**
 * Search across multiple authorized group_ids in parallel.
 * Merges and deduplicates results, returning up to `limit` items.
 */
export async function searchAuthorizedMemories(
  graphiti: GraphitiClient,
  options: SearchOptions,
): Promise<SearchResult[]> {
  const {
    query,
    groupIds,
    limit = 10,
    searchNodes = true,
    searchFacts = true,
    entityTypes,
    centerNodeUuid,
  } = options;

  if (groupIds.length === 0) {
    return [];
  }

  // Fan out parallel searches: for each group_id, search nodes and/or facts
  const promises: Promise<SearchResult[]>[] = [];

  for (const groupId of groupIds) {
    if (searchNodes) {
      promises.push(searchNodesForGroup(graphiti, query, groupId, limit, entityTypes));
    }
    if (searchFacts) {
      promises.push(searchFactsForGroup(graphiti, query, groupId, limit, centerNodeUuid));
    }
  }

  const resultSets = await Promise.allSettled(promises);

  // Collect all successful results
  const allResults: SearchResult[] = [];
  for (const result of resultSets) {
    if (result.status === "fulfilled") {
      allResults.push(...result.value);
    }
    // Silently skip failed group searches — partial results are better than none
  }

  // Deduplicate by UUID
  const seen = new Set<string>();
  const deduped = allResults.filter((r) => {
    if (seen.has(r.uuid)) {
      return false;
    }
    seen.add(r.uuid);
    return true;
  });

  // Sort by recency (most recent first) and trim to limit
  deduped.sort((a, b) => {
    const dateA = new Date(a.created_at).getTime();
    const dateB = new Date(b.created_at).getTime();
    return dateB - dateA;
  });

  return deduped.slice(0, limit);
}

const QUERY_STOP_WORDS = new Set([
  "about",
  "como",
  "com",
  "das",
  "de",
  "dos",
  "ela",
  "ele",
  "eles",
  "essa",
  "esse",
  "esta",
  "foi",
  "lembra",
  "mais",
  "para",
  "por",
  "qual",
  "que",
  "the",
  "uma",
  "voce",
  "quando",
  "with",
]);

function normalizeSearchText(value: string): string {
  return value.normalize("NFD").replace(/\p{Diacritic}/gu, "").toLowerCase();
}

function significantTokens(value: string): string[] {
  const tokens = normalizeSearchText(value).match(/[\p{L}\p{N}]+/gu) ?? [];
  return [...new Set(tokens.filter((token) => token.length >= 3 && !QUERY_STOP_WORDS.has(token)))];
}

function buildEpisodeSnippet(lines: string[], matchIndex: number, maxChars: number): string {
  const selected = [lines[matchIndex].trim()];

  for (let distance = 1; distance <= 2; distance++) {
    const before = lines[matchIndex - distance]?.trim();
    if (before) {
      const candidate = [before, ...selected].join("\n");
      if (candidate.length <= maxChars) selected.unshift(before);
    }

    const after = lines[matchIndex + distance]?.trim();
    if (after) {
      const candidate = [...selected, after].join("\n");
      if (candidate.length <= maxChars) selected.push(after);
    }
  }

  return selected.join("\n").slice(0, maxChars);
}

function episodeMatch(
  episode: GraphitiEpisode,
  queryTokens: string[],
  minimumOverlap: number,
  maxSnippetChars: number,
): { result: SearchResult; score: number } | undefined {
  const lines = episode.content.split(/\r?\n/);
  let bestIndex = -1;
  let bestScore = 0;

  for (let index = 0; index < lines.length; index++) {
    const lineTokens = new Set(significantTokens(lines[index]));
    const score = queryTokens.reduce(
      (matches, token) => matches + (lineTokens.has(token) ? 1 : 0),
      0,
    );
    if (score > bestScore) {
      bestIndex = index;
      bestScore = score;
    }
  }

  if (bestIndex < 0 || bestScore < minimumOverlap) return undefined;

  return {
    score: bestScore,
    result: {
      type: "episode",
      uuid: episode.uuid,
      group_id: episode.group_id,
      summary: buildEpisodeSnippet(lines, bestIndex, maxSnippetChars),
      context: `untrusted raw historical excerpt from ${episode.name}`,
      created_at: episode.created_at,
    },
  };
}

/**
 * Search bounded raw episode excerpts in groups already authorized by SpiceDB.
 * Matching happens per line so unrelated terms elsewhere in a large export do
 * not make the whole episode look relevant.
 */
export async function searchAuthorizedEpisodeExcerpts(
  graphiti: GraphitiClient,
  options: EpisodeSearchOptions,
): Promise<SearchResult[]> {
  const {
    query,
    groupIds,
    limit = 3,
    maxEpisodesPerGroup = 100,
    maxSnippetChars = 800,
  } = options;
  const queryTokens = significantTokens(query);

  if (groupIds.length === 0 || queryTokens.length === 0 || limit <= 0) return [];

  const minimumOverlap = queryTokens.length <= 2 ? 1 : 2;
  const episodeSets = await Promise.allSettled(
    groupIds.map((groupId) => graphiti.getEpisodes(groupId, maxEpisodesPerGroup)),
  );
  const matches: Array<{ result: SearchResult; score: number }> = [];

  for (const episodeSet of episodeSets) {
    if (episodeSet.status !== "fulfilled") continue;
    for (const episode of episodeSet.value) {
      const match = episodeMatch(episode, queryTokens, minimumOverlap, maxSnippetChars);
      if (match) matches.push(match);
    }
  }

  matches.sort((a, b) => {
    if (a.score !== b.score) return b.score - a.score;
    return new Date(b.result.created_at).getTime() - new Date(a.result.created_at).getTime();
  });

  const seen = new Set<string>();
  return matches
    .map(({ result }) => result)
    .filter((result) => {
      const key = `${result.type}:${result.uuid}`;
      if (seen.has(key)) return false;
      seen.add(key);
      return true;
    })
    .slice(0, limit);
}

/** Search semantic graph data and raw episode excerpts through one path. */
export async function searchAuthorizedRecall(
  graphiti: GraphitiClient,
  options: SearchOptions,
): Promise<SearchResult[]> {
  const limit = options.limit ?? 10;
  const [episodeResults, semanticResults] = await Promise.all([
    searchAuthorizedEpisodeExcerpts(graphiti, {
      query: options.query,
      groupIds: options.groupIds,
      limit: Math.min(limit, 3),
    }),
    searchAuthorizedMemories(graphiti, options),
  ]);
  const seen = new Set<string>();

  return [...episodeResults, ...semanticResults]
    .filter((result) => {
      const key = `${result.type}:${result.uuid}`;
      if (seen.has(key)) return false;
      seen.add(key);
      return true;
    })
    .slice(0, limit);
}

// ============================================================================
// Per-group search helpers
// ============================================================================

async function searchNodesForGroup(
  graphiti: GraphitiClient,
  query: string,
  groupId: string,
  limit: number,
  entityTypes?: string[],
): Promise<SearchResult[]> {
  const nodes = await graphiti.searchNodes({ query, group_id: groupId, limit, entity_types: entityTypes });
  return nodes.map(nodeToResult);
}

async function searchFactsForGroup(
  graphiti: GraphitiClient,
  query: string,
  groupId: string,
  limit: number,
  centerNodeUuid?: string,
): Promise<SearchResult[]> {
  const facts = await graphiti.searchFacts({ query, group_id: groupId, limit, center_node_uuid: centerNodeUuid });
  return facts.map(factToResult);
}

function nodeToResult(node: GraphitiNode): SearchResult {
  return {
    type: "node",
    uuid: node.uuid,
    group_id: node.group_id,
    summary: node.summary ?? node.name,
    context: node.name,
    created_at: node.created_at ?? new Date().toISOString(),
  };
}

function factToResult(fact: GraphitiFact): SearchResult {
  // Use node names only — never expose raw node UUIDs in the formatted output.
  // Bare UUIDs in context strings cause LLMs to confuse node IDs with fact/episode IDs.
  const source = fact.source_node_name ?? "?";
  const target = fact.target_node_name ?? "?";
  const context = fact.name ? `${source} -[${fact.name}]→ ${target}` : `${source} → ${target}`;
  return {
    type: "fact",
    uuid: fact.uuid,
    group_id: fact.group_id,
    summary: fact.fact,
    context,
    created_at: fact.created_at,
  };
}

// ============================================================================
// Format for agent context
// ============================================================================

/**
 * Format search results into a text block suitable for injecting into agent context.
 */
export function formatResultsForContext(results: SearchResult[]): string {
  if (results.length === 0) {
    return "";
  }

  return results.map((r, i) => formatResultLine(r, i + 1)).join("\n");
}

/**
 * Format results with session and long-term sections separated.
 * Session group_ids start with "session/".
 */
export function formatDualResults(
  longTermResults: SearchResult[],
  sessionResults: SearchResult[],
): string {
  const parts: string[] = [];
  let idx = 1;

  if (longTermResults.length > 0) {
    for (const r of longTermResults) {
      parts.push(formatResultLine(r, idx++));
    }
  }

  if (sessionResults.length > 0) {
    parts.push("Session memories:");
    for (const r of sessionResults) {
      parts.push(formatResultLine(r, idx++));
    }
  }

  return parts.join("\n");
}

/**
 * Format a single search result line with type-prefixed UUID.
 * e.g. "[fact:da8650cb-...] Eric's birthday is Dec 17th (Eric -[HAS_BIRTHDAY]→ Dec 17th)"
 * The type prefix tells the LLM which deletion method to use.
 */
function formatResultLine(r: SearchResult, idx: number): string {
  const typeLabel = r.type === "node" ? "entity" : r.type;
  return `${idx}. [${typeLabel}:${r.uuid}] ${r.summary} (${r.context})`;
}

/**
 * Deduplicate session results against long-term results (by UUID).
 */
export function deduplicateSessionResults(
  longTermResults: SearchResult[],
  sessionResults: SearchResult[],
): SearchResult[] {
  const longTermIds = new Set(longTermResults.map((r) => r.uuid));
  return sessionResults.filter((r) => !longTermIds.has(r.uuid));
}
