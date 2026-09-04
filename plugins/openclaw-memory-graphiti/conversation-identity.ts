import type { ConversationRef, NativeConversationRef } from "./channel-adapters/types.js";

function normalizedSegment(value: string, label: string): string {
  const normalized = value
    .normalize("NFKD")
    .replace(/[\u0300-\u036f]/g, "")
    .toLowerCase()
    .replace(/[^a-z0-9_-]+/g, "-")
    .replace(/-+/g, "-")
    .replace(/^[-_]+|[-_]+$/g, "");
  if (!normalized) throw new Error(`Missing ${label} for conversation namespace`);
  return normalized;
}
export function legacyNamespaceSegment(value: string): string {
  return normalizedSegment(value, "conversation ID");
}

export function buildConversationRef(
  native: NativeConversationRef,
  encodedNativeId: string,
): ConversationRef {
  return {
    ...native,
    namespace: `${normalizedSegment(native.channel, "channel")}-${native.kind}-${encodedNativeId}`,
  };
}
