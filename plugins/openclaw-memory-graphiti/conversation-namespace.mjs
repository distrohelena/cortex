const SESSION_CONVERSATION_KINDS = new Set(["group", "dm", "direct", "private"]);

function normalizeSegment(value, label) {
  const normalized = String(value ?? "")
    .normalize("NFKD")
    .replace(/[\u0300-\u036f]/g, "")
    .toLowerCase()
    .replace(/[^a-z0-9_-]+/g, "-")
    .replace(/-+/g, "-")
    .replace(/^[-_]+|[-_]+$/g, "");

  if (!normalized) throw new Error(`Missing ${label} for conversation namespace`);
  return normalized;
}

/**
 * Parse the channel conversation suffix from an OpenClaw canonical session key.
 * Returns null for local/non-channel sessions.
 */
export function conversationIdentityFromSessionKey(sessionKey) {
  if (typeof sessionKey !== "string" || !sessionKey.trim()) return null;

  const parts = sessionKey.split(":");
  const kindIndex = parts.findIndex((part, index) => index >= 2 && SESSION_CONVERSATION_KINDS.has(part.toLowerCase()));
  if (kindIndex < 3 || kindIndex >= parts.length - 1) return null;

  const rawKind = parts[kindIndex].toLowerCase();
  return {
    channel: parts[kindIndex - 1],
    conversationId: parts.slice(kindIndex + 1).join(":"),
    kind: rawKind === "group" ? "group" : "dm",
  };
}

/**
 * Produce the single persistent Graphiti group_id for a channel conversation.
 */
export function canonicalConversationGroupId(input) {
  const fallback = conversationIdentityFromSessionKey(input?.sessionKey);
  const channel = input?.channel ?? fallback?.channel;
  const conversationId = input?.conversationId ?? fallback?.conversationId;

  if (!channel || !conversationId) {
    throw new Error("Missing channel conversation identity for persistent memory");
  }

  const normalizedConversation = String(conversationId).trim().toLowerCase();
  const explicitKind = input?.kind === "group" || input?.kind === "dm" ? input.kind : fallback?.kind;
  const kind = explicitKind ?? (normalizedConversation.endsWith("@g.us") ? "group" : "dm");

  return `${normalizeSegment(channel, "channel")}-${kind}-${normalizeSegment(normalizedConversation, "conversation ID")}`;
}

