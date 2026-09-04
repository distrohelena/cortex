import { describe, expect, test } from "vitest";

import type { GraphitiFact } from "./graphiti.js";
import { buildSocialMemoryContext } from "./social-recall.js";

const PARTICIPANT = "participant-canonical";
const FILOMENA = "filomena-canonical";

function fact(overrides: Partial<GraphitiFact> = {}): GraphitiFact {
  return {
    uuid: "fact-1",
    group_id: "group-a",
    fact: "Gabriel likes concise answers.",
    name: "PREFERS",
    source_node_uuid: PARTICIPANT,
    target_node_uuid: "topic-1",
    source_node: { uuid: PARTICIPANT, canonical: true, stable_id: "whatsapp:x" },
    target_node: { uuid: "topic-1", canonical: false, name: "concise answers" },
    created_at: "2026-09-04T18:00:00Z",
    ...overrides,
  };
}

describe("exact social recall", () => {
  test("prioritizes an explicit active boundary independently of semantic relevance", () => {
    const context = buildSocialMemoryContext({
      participantName: "Gabriel",
      participantUuid: PARTICIPANT,
      filomenaUuid: FILOMENA,
      exactFacts: [fact({ uuid: "boundary", name: "REQUESTED_BOUNDARY", fact: "Gabriel asked Filomena not to joke about his family." })],
      semanticMemories: [{ summary: "Unrelated game fact", type: "fact" }],
    });

    expect(context.indexOf("[LIMITS]")).toBeLessThan(context.indexOf("[RELEVANT MEMORY]"));
    expect(context).toContain("not to joke about his family");
  });

  test("promotes a distinctive repeated term but never a repeated generic vocative", () => {
    const used = (term: string, runs: string[], uuid: string) => fact({
      uuid,
      name: "USED_ADDRESS_TERM",
      fact: `Filomena used ${term}.`,
      source_node_uuid: FILOMENA,
      target_node_uuid: PARTICIPANT,
      source_node: { uuid: FILOMENA, canonical: true, stable_id: "agent:filomena" },
      target_node: { uuid: PARTICIPANT, canonical: true, stable_id: "whatsapp:x" },
      attributes: { display_term: term, normalized_term: term.toLowerCase(), evidence_run_ids: runs, evidence_episode_uuids: runs.map((run) => `ep-${run}`) },
    });
    const context = buildSocialMemoryContext({
      participantName: "Gabriel",
      participantUuid: PARTICIPANT,
      filomenaUuid: FILOMENA,
      exactFacts: [used("amor", ["1", "2"], "amor"), used("Professor", ["1", "2"], "professor")],
      semanticMemories: [],
    });

    expect(context).toContain('Established address term: "Professor"');
    expect(context).not.toContain('Established address term: "amor"');
  });

  test("does not place legacy Person facts in participant-specific sections", () => {
    const context = buildSocialMemoryContext({
      participantName: "Gabriel",
      participantUuid: PARTICIPANT,
      filomenaUuid: FILOMENA,
      exactFacts: [fact({
        uuid: "legacy",
        source_node_uuid: "legacy-person",
        source_node: { uuid: "legacy-person", name: "Gabriel", canonical: false, labels: ["Person"] },
      })],
      semanticMemories: [],
    });

    expect(context).not.toContain("likes concise answers");
  });

  test("omits inactive facts and enforces the hard character budget", () => {
    const context = buildSocialMemoryContext({
      participantName: "Gabriel",
      participantUuid: PARTICIPANT,
      filomenaUuid: FILOMENA,
      exactFacts: [
        fact({ uuid: "inactive", fact: "obsolete", invalid_at: "2026-09-04T17:00:00Z" }),
        ...Array.from({ length: 20 }, (_, index) => fact({ uuid: `f-${index}`, fact: `Fact ${index} ${"x".repeat(80)}` })),
      ],
      semanticMemories: [],
      now: new Date("2026-09-04T18:00:00Z"),
      maxChars: 300,
    });

    expect(context).not.toContain("obsolete");
    expect(context.length).toBeLessThanOrEqual(300);
  });
});
