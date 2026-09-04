import type { GraphitiFact } from "./graphiti.js";
import { establishNickname, type AddressTermObservation } from "./social-evidence.js";

export type SemanticMemory = {
  type: "node" | "fact" | "episode";
  summary: string;
  legacyPersonRisk?: boolean;
};

const BOUNDARY_FACTS = new Set(["REQUESTED_BOUNDARY", "PREFERS_NOT", "DISLIKED_INTERACTION"]);

function timestamp(value: string | null | undefined): number | undefined {
  if (!value) return undefined;
  const parsed = Date.parse(value);
  return Number.isNaN(parsed) ? undefined : parsed;
}

function isActive(fact: GraphitiFact, now: Date): boolean {
  if (fact.expired_at) return false;
  const invalidAt = timestamp(fact.invalid_at);
  if (invalidAt !== undefined && invalidAt <= now.valueOf()) return false;
  const validAt = timestamp(fact.valid_at);
  return validAt === undefined || validAt <= now.valueOf();
}

function endpoint(fact: GraphitiFact, uuid: string) {
  if (fact.source_node_uuid === uuid) return fact.source_node;
  if (fact.target_node_uuid === uuid) return fact.target_node;
  return undefined;
}

function exactCanonicalFact(fact: GraphitiFact, participantUuid: string): boolean {
  const participant = endpoint(fact, participantUuid);
  return participant?.uuid === participantUuid && participant.canonical === true;
}

function relationFact(fact: GraphitiFact, participantUuid: string, filomenaUuid: string): boolean {
  const endpoints = new Set([fact.source_node_uuid, fact.target_node_uuid]);
  return endpoints.size === 2
    && endpoints.has(participantUuid)
    && endpoints.has(filomenaUuid)
    && endpoint(fact, participantUuid)?.canonical === true
    && endpoint(fact, filomenaUuid)?.canonical === true;
}

function establishedNickname(
  facts: GraphitiFact[],
  participantUuid: string,
  filomenaUuid: string,
): string | undefined {
  const observations: AddressTermObservation[] = [];
  for (const fact of facts) {
    if (fact.name !== "USED_ADDRESS_TERM" || !relationFact(fact, participantUuid, filomenaUuid)) continue;
    const term = fact.attributes?.display_term;
    const runIds = fact.attributes?.evidence_run_ids;
    const episodeUuids = fact.attributes?.evidence_episode_uuids;
    if (typeof term !== "string" || !Array.isArray(runIds)) continue;
    for (const [index, runId] of runIds.entries()) {
      if (typeof runId !== "string") continue;
      const episodeUuid = Array.isArray(episodeUuids) && typeof episodeUuids[index] === "string"
        ? episodeUuids[index]
        : fact.uuid;
      observations.push({ term, runId, episodeUuid });
    }
  }
  return establishNickname(observations)?.displayTerm;
}

function appendSection(output: string, title: string, lines: string[], maxChars: number): string {
  if (lines.length === 0) return output;
  let next = output;
  for (const line of lines) {
    const prefix = next ? "\n" : "";
    const header = next.includes(title) ? "" : `${title}\n`;
    const addition = `${prefix}${header}- ${line}`;
    if (next.length + addition.length > maxChars) break;
    next += addition;
  }
  return next;
}

export function buildSocialMemoryContext(input: {
  participantName: string;
  participantUuid: string;
  filomenaUuid: string;
  exactFacts: GraphitiFact[];
  semanticMemories: SemanticMemory[];
  now?: Date;
  maxChars?: number;
}): string {
  const now = input.now ?? new Date();
  const maxChars = input.maxChars ?? 1500;
  const active = input.exactFacts.filter((fact) => isActive(fact, now));
  const seen = new Set<string>();
  const unique = (facts: GraphitiFact[]) => facts.filter((fact) => {
    if (seen.has(fact.uuid)) return false;
    seen.add(fact.uuid);
    return true;
  });

  const boundaries = unique(active.filter((fact) =>
    BOUNDARY_FACTS.has(fact.name ?? "")
    && exactCanonicalFact(fact, input.participantUuid),
  )).map((fact) => fact.fact);
  const nickname = establishedNickname(active, input.participantUuid, input.filomenaUuid);
  const relationships = unique(active.filter((fact) =>
    fact.name !== "USED_ADDRESS_TERM"
    && !BOUNDARY_FACTS.has(fact.name ?? "")
    && relationFact(fact, input.participantUuid, input.filomenaUuid),
  )).map((fact) => fact.fact);
  const participantFacts = unique(active.filter((fact) =>
    fact.name !== "USED_ADDRESS_TERM"
    && !BOUNDARY_FACTS.has(fact.name ?? "")
    && !relationFact(fact, input.participantUuid, input.filomenaUuid)
    && exactCanonicalFact(fact, input.participantUuid),
  )).map((fact) => fact.fact);
  const semantic = input.semanticMemories
    .filter((memory) => !memory.legacyPersonRisk)
    .map((memory) => memory.summary);

  let output = "";
  output = appendSection(output, "[LIMITS]", boundaries, maxChars);
  output = appendSection(
    output,
    `[RELATIONSHIP MEMORY]\n${input.participantName}:`,
    nickname ? [`Established address term: "${nickname}".`] : [],
    maxChars,
  );
  output = appendSection(output, "[RELATIONSHIP FACTS]", relationships, maxChars);
  output = appendSection(output, `[PERSON MEMORY]\n${input.participantName}:`, participantFacts, maxChars);
  output = appendSection(output, "[RELEVANT MEMORY]", semantic, maxChars);
  return output;
}
