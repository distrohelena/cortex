export type ConversationKind = "group" | "dm";

export type ConversationIdentity = {
  channel: string;
  conversationJid: string;
  kind: ConversationKind;
};

export type ConversationNamespaceInput = {
  channel?: string;
  conversationId?: string;
  sessionKey?: string;
  kind?: ConversationKind;
  accountId?: string;
};

export function conversationIdentityFromSessionKey(
  sessionKey?: string,
): { channel: string; conversationId: string; kind: ConversationKind } | null;

export function conversationIdentity(input: ConversationNamespaceInput): ConversationIdentity;

export function canonicalConversationNamespace(input: ConversationNamespaceInput): string;

export function canonicalConversationGroupId(input: ConversationNamespaceInput): string;
