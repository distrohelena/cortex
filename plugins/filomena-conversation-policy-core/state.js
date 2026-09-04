import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname } from "node:path";

function normalized(value) {
  if (typeof value !== "string" && typeof value !== "number") return null;
  const result = String(value).trim();
  return result || null;
}

export function createScopedMessageIdRegistry({ filePath, maxEntries = 512, warn }) {
  const capacity = Number.isInteger(maxEntries) && maxEntries > 0 ? maxEntries : 512;
  const reportWarning = typeof warn === "function" ? warn : () => {};
  let entries = [];
  let legacyIds = [];

  try {
    const parsed = JSON.parse(readFileSync(filePath, "utf8"));
    legacyIds = (Array.isArray(parsed?.messageIds) ? parsed.messageIds : []).map(normalized).filter(Boolean);
    for (const candidate of Array.isArray(parsed?.entries) ? parsed.entries : []) {
      const scope = normalized(candidate?.scope);
      const id = normalized(candidate?.id);
      if (!scope || !id) continue;
      entries = entries.filter((entry) => entry.scope !== scope || entry.id !== id);
      entries.push({ scope, id });
    }
    entries = entries.slice(-capacity);
    legacyIds = [...new Set(legacyIds)].slice(-capacity);
  } catch (error) {
    const missing = error && typeof error === "object" && "code" in error && error.code === "ENOENT";
    if (!missing) reportWarning(`filomena-conversation-policy-core: could not load state: ${String(error)}`);
  }

  function persist() {
    mkdirSync(dirname(filePath), { recursive: true });
    writeFileSync(filePath, `${JSON.stringify({ version: 2, entries, messageIds: legacyIds }, null, 2)}\n`, "utf8");
  }

  return {
    has(scopeValue, idValue) {
      const scope = normalized(scopeValue);
      const id = normalized(idValue);
      if (!scope || !id) return false;
      return entries.some((entry) => entry.scope === scope && entry.id === id) || legacyIds.includes(id);
    },
    record(scopeValue, idValue) {
      const scope = normalized(scopeValue);
      const id = normalized(idValue);
      if (!scope || !id) return false;
      entries = entries.filter((entry) => entry.scope !== scope || entry.id !== id);
      entries.push({ scope, id });
      entries = entries.slice(-capacity);
      try { persist(); } catch (error) {
        reportWarning(`filomena-conversation-policy-core: could not persist state: ${String(error)}`);
      }
      return true;
    },
    snapshot() { return entries.map((entry) => ({ ...entry })); },
  };
}
