export type ConversationKind = "group" | "dm";

export type ConversationIdentity = {
  channel: string;
  conversationId: string;
  kind: ConversationKind;
};

export type ConversationNamespaceInput = {
  channel?: string;
  conversationId?: string;
  sessionKey?: string;
  kind?: ConversationKind;
};

export function conversationIdentityFromSessionKey(
  sessionKey?: string,
): ConversationIdentity | null;

export function canonicalConversationGroupId(input: ConversationNamespaceInput): string;
