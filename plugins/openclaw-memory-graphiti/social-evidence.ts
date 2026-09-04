export type AddressTermObservation = {
  term: string;
  runId: string;
  episodeUuid: string;
};

export type AddressTermRequest = {
  requestedTerm: string;
  episodeUuid: string;
};

export type EstablishedNickname = {
  displayTerm: string;
  normalizedTerm: string;
  evidenceRunIds: string[];
  evidenceEpisodeUuids: string[];
};

const GENERIC_ADDRESS_TERMS = new Set([
  "amiga",
  "amigo",
  "amigue",
  "amor",
  "bonitinha",
  "bonitinho",
  "darling",
  "dear",
  "honey",
  "meu bem",
  "minha querida",
  "meu querido",
  "querida",
  "querido",
  "sweetheart",
]);

/** Conservative syntax-only detector: `Filomena: <vocative>, ...`. */
export function extractAddressTerm(content: string): string | undefined {
  const match = content.match(/^\s*Filomena:\s*([^,:;!?—–\n]{1,48})\s*[,;!—–]\s*\S/iu);
  if (!match) return undefined;
  const term = match[1].trim().replace(/\s+/g, " ");
  const words = term.split(" ");
  if (words.length > 3 || !words.every((word) => /^[\p{L}\p{N}][\p{L}\p{N}'’-]*$/u.test(word))) {
    return undefined;
  }
  const normalized = normalizeAddressTerm(term);
  if (["oi", "ola", "olá", "ei", "hey"].includes(normalized)) return undefined;
  return term;
}

export function normalizeAddressTerm(value: string): string {
  return value
    .normalize("NFD")
    .replace(/\p{Diacritic}/gu, "")
    .trim()
    .replace(/\s+/g, " ")
    .toLocaleLowerCase("pt-BR");
}

export function establishNickname(
  observations: AddressTermObservation[],
  request?: AddressTermRequest,
): EstablishedNickname | undefined {
  const grouped = new Map<string, AddressTermObservation[]>();
  for (const observation of observations) {
    const normalized = normalizeAddressTerm(observation.term);
    if (!normalized || !observation.runId.trim() || !observation.episodeUuid.trim()) continue;
    const group = grouped.get(normalized) ?? [];
    group.push(observation);
    grouped.set(normalized, group);
  }

  const requested = request ? normalizeAddressTerm(request.requestedTerm) : undefined;
  for (const normalized of [...grouped.keys()].sort()) {
    const evidence = grouped.get(normalized)!;
    const runIds = [...new Set(evidence.map((item) => item.runId))].sort();
    const explicitlyRequested = requested === normalized && Boolean(request?.episodeUuid.trim());
    if (GENERIC_ADDRESS_TERMS.has(normalized) && !explicitlyRequested) continue;
    if (runIds.length < 2 && !explicitlyRequested) continue;

    return {
      displayTerm: evidence[0].term,
      normalizedTerm: normalized,
      evidenceRunIds: runIds,
      evidenceEpisodeUuids: [
        ...new Set([
          ...evidence.map((item) => item.episodeUuid),
          ...(explicitlyRequested ? [request!.episodeUuid] : []),
        ]),
      ].sort(),
    };
  }
  return undefined;
}
