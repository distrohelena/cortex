import { createScopedMessageIdRegistry } from "../filomena-conversation-policy-core/state.js";

export function normalizeMessageId(value) {
  if (typeof value !== "string") return null;
  const normalized = value.trim();
  return normalized || null;
}

/** Phase 1 facade over the common scoped outbound-ID registry. */
export function createMessageIdRegistry(options) {
  const scope = "whatsapp:legacy";
  const common = createScopedMessageIdRegistry(options);
  return {
    has(value, conversationScope = scope) {
      const id = normalizeMessageId(value);
      return id ? common.has(conversationScope ?? scope, id) : false;
    },
    record(value, conversationScope = scope) {
      const id = normalizeMessageId(value);
      return id ? common.record(conversationScope ?? scope, id) : false;
    },
    snapshot() {
      return common.snapshot().map((entry) => entry.id);
    },
  };
}
