import { describe, test, expect, vi } from "vitest";
import {
  searchAuthorizedMemories,
  searchAuthorizedEpisodeExcerpts,
  searchAuthorizedRecall,
  formatResultsForContext,
  formatDualResults,
  deduplicateSessionResults,
  type SearchResult,
} from "./search.js";
import type { GraphitiClient } from "./graphiti.js";

function mockGraphiti(overrides?: Partial<GraphitiClient>): GraphitiClient {
  return {
    healthCheck: vi.fn().mockResolvedValue(true),
    getStatus: vi.fn().mockResolvedValue({}),
    addEpisode: vi.fn().mockResolvedValue({ episode_uuid: "ep-1" }),
    getEpisodes: vi.fn().mockResolvedValue([]),
    deleteEpisode: vi.fn().mockResolvedValue(undefined),
    searchNodes: vi.fn().mockResolvedValue([]),
    searchFacts: vi.fn().mockResolvedValue([]),
    getEntityEdge: vi.fn().mockResolvedValue({}),
    deleteEntityEdge: vi.fn().mockResolvedValue(undefined),
    clearGraph: vi.fn().mockResolvedValue(undefined),
    close: vi.fn().mockResolvedValue(undefined),
    ...overrides,
  } as unknown as GraphitiClient;
}

describe("searchAuthorizedMemories", () => {
  test("returns empty array when no groupIds provided", async () => {
    const graphiti = mockGraphiti();
    const results = await searchAuthorizedMemories(graphiti, {
      query: "test",
      groupIds: [],
    });
    expect(results).toEqual([]);
  });

  test("searches nodes and facts for each authorized group", async () => {
    const searchNodes = vi.fn().mockResolvedValue([
      { uuid: "n1", name: "Mark", summary: "A person named Mark", group_id: "family", created_at: "2026-01-15T00:00:00Z" },
    ]);
    const searchFacts = vi.fn().mockResolvedValue([
      {
        uuid: "f1",
        fact: "Mark got promoted",
        source_node_name: "Mark",
        target_node_name: "Promotion",
        source_node_uuid: "n1",
        target_node_uuid: "n2",
        group_id: "family",
        created_at: "2026-01-16T00:00:00Z",
      },
    ]);
    const graphiti = mockGraphiti({ searchNodes, searchFacts });

    const results = await searchAuthorizedMemories(graphiti, {
      query: "Mark work",
      groupIds: ["family", "work"],
    });

    // 2 groups x (searchNodes + searchFacts) = 4 calls
    expect(searchNodes).toHaveBeenCalledTimes(2);
    expect(searchFacts).toHaveBeenCalledTimes(2);
    // Deduplicated: n1 appears in both groups but only counted once
    expect(results.length).toBeGreaterThanOrEqual(2);
  });

  test("deduplicates results by UUID", async () => {
    const node = { uuid: "n1", name: "Mark", summary: "A person", group_id: "g1", created_at: "2026-01-15T00:00:00Z" };
    const searchNodes = vi.fn().mockResolvedValue([node]);
    const searchFacts = vi.fn().mockResolvedValue([]);
    const graphiti = mockGraphiti({ searchNodes, searchFacts });

    const results = await searchAuthorizedMemories(graphiti, {
      query: "Mark",
      groupIds: ["g1", "g2", "g3"],
    });

    // Same UUID from 3 groups → deduplicated to 1
    expect(results).toHaveLength(1);
    expect(results[0].uuid).toBe("n1");
  });

  test("sorts by recency (most recent first)", async () => {
    const searchNodes = vi.fn()
      .mockResolvedValueOnce([
        { uuid: "old", name: "Old", summary: "Old node", group_id: "g1", created_at: "2025-01-01T00:00:00Z" },
      ])
      .mockResolvedValueOnce([
        { uuid: "new", name: "New", summary: "New node", group_id: "g2", created_at: "2026-02-01T00:00:00Z" },
      ]);
    const searchFacts = vi.fn().mockResolvedValue([]);
    const graphiti = mockGraphiti({ searchNodes, searchFacts });

    const results = await searchAuthorizedMemories(graphiti, {
      query: "test",
      groupIds: ["g1", "g2"],
    });

    expect(results[0].uuid).toBe("new");
    expect(results[1].uuid).toBe("old");
  });

  test("respects limit parameter", async () => {
    const nodes = Array.from({ length: 20 }, (_, i) => ({
      uuid: `n${i}`,
      name: `Node ${i}`,
      summary: `Summary ${i}`,
      group_id: "g1",
      created_at: `2026-01-${String(i + 1).padStart(2, "0")}T00:00:00Z`,
    }));
    const searchNodes = vi.fn().mockResolvedValue(nodes);
    const searchFacts = vi.fn().mockResolvedValue([]);
    const graphiti = mockGraphiti({ searchNodes, searchFacts });

    const results = await searchAuthorizedMemories(graphiti, {
      query: "test",
      groupIds: ["g1"],
      limit: 5,
    });

    expect(results).toHaveLength(5);
  });

  test("handles partial failures gracefully", async () => {
    const searchNodes = vi.fn()
      .mockResolvedValueOnce([
        { uuid: "n1", name: "OK", summary: "Working result", group_id: "g1", created_at: "2026-01-15T00:00:00Z" },
      ])
      .mockRejectedValueOnce(new Error("Network error"));
    const searchFacts = vi.fn().mockResolvedValue([]);
    const graphiti = mockGraphiti({ searchNodes, searchFacts });

    const results = await searchAuthorizedMemories(graphiti, {
      query: "test",
      groupIds: ["g1", "g2"],
    });

    // Should still return results from the successful group
    expect(results.length).toBeGreaterThanOrEqual(1);
    expect(results[0].uuid).toBe("n1");
  });

  test("can search only nodes or only facts", async () => {
    const searchNodes = vi.fn().mockResolvedValue([]);
    const searchFacts = vi.fn().mockResolvedValue([]);
    const graphiti = mockGraphiti({ searchNodes, searchFacts });

    await searchAuthorizedMemories(graphiti, {
      query: "test",
      groupIds: ["g1"],
      searchNodes: true,
      searchFacts: false,
    });

    expect(searchNodes).toHaveBeenCalledTimes(1);
    expect(searchFacts).not.toHaveBeenCalled();
  });

  test("forwards entityTypes to searchNodes calls", async () => {
    const searchNodes = vi.fn().mockResolvedValue([]);
    const searchFacts = vi.fn().mockResolvedValue([]);
    const graphiti = mockGraphiti({ searchNodes, searchFacts });

    await searchAuthorizedMemories(graphiti, {
      query: "test",
      groupIds: ["g1"],
      entityTypes: ["Preference", "Organization"],
    });

    expect(searchNodes).toHaveBeenCalledWith(
      expect.objectContaining({ entity_types: ["Preference", "Organization"] }),
    );
  });

  test("forwards centerNodeUuid to searchFacts calls", async () => {
    const searchNodes = vi.fn().mockResolvedValue([]);
    const searchFacts = vi.fn().mockResolvedValue([]);
    const graphiti = mockGraphiti({ searchNodes, searchFacts });

    await searchAuthorizedMemories(graphiti, {
      query: "test",
      groupIds: ["g1"],
      centerNodeUuid: "node-uuid-123",
    });

    expect(searchFacts).toHaveBeenCalledWith(
      expect.objectContaining({ center_node_uuid: "node-uuid-123" }),
    );
  });
});

describe("formatResultsForContext", () => {
  test("returns empty string for no results", () => {
    expect(formatResultsForContext([])).toBe("");
  });

  test("formats nodes and facts with type labels", () => {
    const results = [
      { type: "node" as const, uuid: "n1", group_id: "g1", summary: "Mark is a developer", context: "Mark", created_at: "2026-01-15" },
      { type: "fact" as const, uuid: "f1", group_id: "g1", summary: "Mark got promoted", context: "Mark \u2192 Promotion", created_at: "2026-01-16" },
    ];

    const formatted = formatResultsForContext(results);
    expect(formatted).toContain("1. [entity:n1] Mark is a developer (Mark)");
    expect(formatted).toContain("2. [fact:f1] Mark got promoted (Mark \u2192 Promotion)");
  });
});

describe("formatDualResults", () => {
  test("formats long-term results only", () => {
    const longTerm: SearchResult[] = [
      { type: "node", uuid: "n1", group_id: "main", summary: "Mark is a developer", context: "Mark", created_at: "2026-01-15" },
    ];

    const formatted = formatDualResults(longTerm, []);
    expect(formatted).toContain("1. [entity:n1] Mark is a developer (Mark)");
    expect(formatted).not.toContain("Session memories:");
  });

  test("formats session results only", () => {
    const session: SearchResult[] = [
      { type: "fact", uuid: "f1", group_id: "session-s1", summary: "Deadline tomorrow", context: "Mark \u2192 Deadline", created_at: "2026-01-16" },
    ];

    const formatted = formatDualResults([], session);
    expect(formatted).toContain("Session memories:");
    expect(formatted).toContain("1. [fact:f1] Deadline tomorrow");
  });

  test("formats both long-term and session with correct numbering", () => {
    const longTerm: SearchResult[] = [
      { type: "node", uuid: "n1", group_id: "main", summary: "Mark is a developer", context: "Mark", created_at: "2026-01-15" },
      { type: "fact", uuid: "f1", group_id: "main", summary: "Mark got promoted", context: "Mark \u2192 Promotion", created_at: "2026-01-16" },
    ];
    const session: SearchResult[] = [
      { type: "fact", uuid: "f2", group_id: "session-s1", summary: "Deadline tomorrow", context: "Mark \u2192 Deadline", created_at: "2026-02-01" },
    ];

    const formatted = formatDualResults(longTerm, session);
    expect(formatted).toContain("1. [entity:n1] Mark is a developer");
    expect(formatted).toContain("2. [fact:f1] Mark got promoted");
    expect(formatted).toContain("Session memories:");
    expect(formatted).toContain("3. [fact:f2] Deadline tomorrow");
  });

  test("returns empty string when both are empty", () => {
    expect(formatDualResults([], [])).toBe("");
  });
});

describe("deduplicateSessionResults", () => {
  test("removes session results that exist in long-term", () => {
    const longTerm: SearchResult[] = [
      { type: "node", uuid: "n1", group_id: "main", summary: "Mark", context: "Mark", created_at: "2026-01-15" },
    ];
    const session: SearchResult[] = [
      { type: "node", uuid: "n1", group_id: "session-s1", summary: "Mark", context: "Mark", created_at: "2026-01-15" },
      { type: "fact", uuid: "f1", group_id: "session-s1", summary: "New fact", context: "X \u2192 Y", created_at: "2026-01-16" },
    ];

    const deduped = deduplicateSessionResults(longTerm, session);
    expect(deduped).toHaveLength(1);
    expect(deduped[0].uuid).toBe("f1");
  });

  test("returns all session results when no overlap", () => {
    const longTerm: SearchResult[] = [
      { type: "node", uuid: "n1", group_id: "main", summary: "Mark", context: "Mark", created_at: "2026-01-15" },
    ];
    const session: SearchResult[] = [
      { type: "node", uuid: "n2", group_id: "session-s1", summary: "Jane", context: "Jane", created_at: "2026-01-15" },
    ];

    const deduped = deduplicateSessionResults(longTerm, session);
    expect(deduped).toHaveLength(1);
    expect(deduped[0].uuid).toBe("n2");
  });

  test("handles empty inputs", () => {
    expect(deduplicateSessionResults([], [])).toEqual([]);
    expect(deduplicateSessionResults([], [
      { type: "node", uuid: "n1", group_id: "session-s1", summary: "X", context: "X", created_at: "2026-01-01" },
    ])).toHaveLength(1);
  });
});

describe("searchAuthorizedEpisodeExcerpts", () => {
  const episodes = [
    {
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
    },
  ];

  test("finds a bounded raw excerpt when Graphiti extraction missed the event", async () => {
    const getEpisodes = vi.fn().mockResolvedValue(episodes);
    const graphiti = mockGraphiti({ getEpisodes });

    const results = await searchAuthorizedEpisodeExcerpts(graphiti, {
      query: "você lembra quando o Gabriel fechou Shadow of Mordor",
      groupIds: ["wa-chat"],
      limit: 3,
    });

    expect(getEpisodes).toHaveBeenCalledWith("wa-chat", 100);
    expect(results[0]).toMatchObject({
      type: "episode",
      uuid: "ep-wa",
      group_id: "wa-chat",
    });
    expect(results[0].summary).toContain("Zerei Shadow of Mordor");
    expect(results[0].summary.length).toBeLessThanOrEqual(800);
  });

  test("does not return an episode without enough line-level overlap", async () => {
    const graphiti = mockGraphiti({
      getEpisodes: vi.fn().mockResolvedValue(episodes),
    });

    await expect(
      searchAuthorizedEpisodeExcerpts(graphiti, {
        query: "Mariana terminou Resident Evil",
        groupIds: ["wa-chat"],
      }),
    ).resolves.toEqual([]);
  });

  test("only requests supplied groups and keeps matches from a healthy group", async () => {
    const getEpisodes = vi
      .fn()
      .mockRejectedValueOnce(new Error("offline"))
      .mockResolvedValueOnce(episodes);
    const graphiti = mockGraphiti({ getEpisodes });

    const results = await searchAuthorizedEpisodeExcerpts(graphiti, {
      query: "Gabriel Shadow Mordor",
      groupIds: ["broken", "wa-chat"],
    });

    expect(getEpisodes).toHaveBeenNthCalledWith(1, "broken", 100);
    expect(getEpisodes).toHaveBeenNthCalledWith(2, "wa-chat", 100);
    expect(getEpisodes).toHaveBeenCalledTimes(2);
    expect(results).toHaveLength(1);
  });

  test("clamps excerpts to the configured character limit", async () => {
    const longEpisode = {
      ...episodes[0],
      content: `${"antes ".repeat(100)}\nGabriel zerou Shadow of Mordor\n${"depois ".repeat(100)}`,
    };
    const graphiti = mockGraphiti({
      getEpisodes: vi.fn().mockResolvedValue([longEpisode]),
    });

    const results = await searchAuthorizedEpisodeExcerpts(graphiti, {
      query: "Gabriel Shadow Mordor",
      groupIds: ["wa-chat"],
      maxSnippetChars: 120,
    });

    expect(results[0].summary).toContain("Gabriel zerou Shadow of Mordor");
    expect(results[0].summary.length).toBeLessThanOrEqual(120);
  });
});

describe("searchAuthorizedRecall", () => {
  test("prioritizes a matching raw episode over unrelated semantic results", async () => {
    const graphiti = mockGraphiti({
      getEpisodes: vi.fn().mockResolvedValue([
        {
          uuid: "ep-wa",
          name: "whatsapp_wa-chat_0001",
          content: "[4/11/26] Gabriel: Zerei Shadow of Mordor, família",
          source_description: "WhatsApp export",
          group_id: "wa-chat",
          created_at: "2026-04-11T15:37:03Z",
        },
      ]),
      searchNodes: vi.fn().mockResolvedValue([
        {
          uuid: "n1",
          name: "Other",
          summary: "Unrelated semantic result",
          group_id: "wa-chat",
          created_at: "2026-09-01",
        },
      ]),
    });

    const results = await searchAuthorizedRecall(graphiti, {
      query: "Gabriel fechou Shadow of Mordor",
      groupIds: ["wa-chat"],
      limit: 1,
    });

    expect(results).toHaveLength(1);
    expect(results[0].type).toBe("episode");
  });

  test("formats raw history with an episode label and untrusted provenance", () => {
    const formatted = formatResultsForContext([
      {
        type: "episode",
        uuid: "ep-wa",
        group_id: "wa-chat",
        summary: "Gabriel: Zerei Shadow of Mordor",
        context: "untrusted raw historical excerpt from whatsapp_wa-chat_0001",
        created_at: "2026-04-11T15:37:03Z",
      },
    ]);

    expect(formatted).toContain("[episode:ep-wa]");
    expect(formatted).toContain("untrusted raw historical excerpt");
  });
});
