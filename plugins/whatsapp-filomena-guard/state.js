import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname } from "node:path";

export function normalizeMessageId(value) {
  if (typeof value !== "string") return null;
  const normalized = value.trim();
  return normalized || null;
}

export function createMessageIdRegistry({ filePath, maxEntries, warn }) {
  const capacity = Number.isInteger(maxEntries) && maxEntries > 0 ? maxEntries : 512;
  const reportWarning = typeof warn === "function" ? warn : () => {};
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
    const isMissingFile =
      error && typeof error === "object" && "code" in error && error.code === "ENOENT";
    if (!isMissingFile) {
      reportWarning(`whatsapp-filomena-guard: could not load state: ${String(error)}`);
    }
  }

  function persist() {
    mkdirSync(dirname(filePath), { recursive: true });
    const state = { version: 1, messageIds };
    writeFileSync(filePath, `${JSON.stringify(state, null, 2)}\n`, "utf8");
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
        reportWarning(`whatsapp-filomena-guard: could not persist state: ${String(error)}`);
      }
      return true;
    },

    snapshot() {
      return [...messageIds];
    },
  };
}
