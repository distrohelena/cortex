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

function normalizeWhatsAppConversationJid(value, kind) {
  let normalized = String(value).trim().toLowerCase().replace(/^whatsapp:/, "");
  if (kind === "group") return normalized;

  if (/^\+?[\d\s().-]+$/.test(normalized)) {
    const digits = normalized.replace(/\D/g, "");
    if (/^\d{7,15}$/.test(digits)) return `${digits}@s.whatsapp.net`;
  }

  const separator = normalized.lastIndexOf("@");
  if (separator <= 0 || separator === normalized.length - 1) return normalized;
  const local = normalized.slice(0, separator).replace(/:\d+$/, "");
  const domain = normalized.slice(separator + 1) === "c.us"
    ? "s.whatsapp.net"
    : normalized.slice(separator + 1);
  return `${local}@${domain}`;
}

/** Resolve a typed conversation from the native provider identity. */
export function conversationIdentity(input) {
  const fallback = conversationIdentityFromSessionKey(input?.sessionKey);
  const channel = String(input?.channel ?? fallback?.channel ?? "").trim().toLowerCase();
  const rawConversation = input?.conversationId ?? fallback?.conversationId;
  if (!channel || !rawConversation) {
    throw new Error("Missing channel conversation identity for persistent memory");
  }

  const rawNormalized = String(rawConversation).trim().toLowerCase().replace(/^whatsapp:/, "");
  const explicitKind = input?.kind === "group" || input?.kind === "dm"
    ? input.kind
    : input?.conversationId === undefined
      ? fallback?.kind
      : undefined;
  const kind = explicitKind ?? (rawNormalized.endsWith("@g.us") ? "group" : "dm");
  const conversationJid = channel === "whatsapp"
    ? normalizeWhatsAppConversationJid(rawNormalized, kind)
    : rawNormalized;
  if (!conversationJid) throw new Error("Missing conversation JID for persistent memory");

  return { channel, conversationJid, kind };
}

/**
 * Produce the single persistent Graphiti group_id for a channel conversation.
 */
export function canonicalConversationNamespace(input) {
  const identity = conversationIdentity(input);
  return `${normalizeSegment(identity.channel, "channel")}-${identity.kind}-${normalizeSegment(identity.conversationJid, "conversation ID")}`;
}

/** @deprecated Compatibility alias for callers that mirror Graphiti's group_id name. */
export function canonicalConversationGroupId(input) {
  return canonicalConversationNamespace(input);
}
