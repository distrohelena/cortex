export type ConversationKind = "dm" | "group";

export type NativeConversationRef = {
  channel: string;
  accountId: string;
  nativeId: string;
  kind: ConversationKind;
  threadId?: string;
};

export type ConversationRef = NativeConversationRef & {
  namespace: string;
};

export type ActorKind = "person" | "bot" | "channel" | "unknown";

export type ActorRef = {
  channel: string;
  accountId: string;
  nativeId: string;
  stableId: string;
  displayName?: string;
  username?: string;
  actorKind: ActorKind;
  bindAsPerson: boolean;
  identitySource?: string;
  unresolvedAlternate?: boolean;
  rawNativeId?: string;
};

export type ReplyRef = {
  providerMessageId: string;
  providerMessageIdFull?: string;
  senderNativeId?: string;
  content?: string;
  isQuote?: boolean;
};

export type SafeProvenance = {
  providerUpdateId?: string;
  providerUpdateKind?: string;
  raw?: Readonly<Record<string, unknown>>;
};

export type NormalizedMessageBase = {
  providerLabel: string;
  providerMessageId: string;
  conversation: ConversationRef;
  content: string;
  referenceTime: Date;
  timestampSource: "provider" | "message_sent_observed";
  replyTo?: ReplyRef;
  mentionedActors?: readonly ActorRef[];
  provenance?: SafeProvenance;
};

export type NormalizedInboundMessage = NormalizedMessageBase & {
  direction: "inbound";
  sender?: ActorRef;
  originalTimestamp: number;
};

export type NormalizedOutboundMessage = NormalizedMessageBase & {
  direction: "outbound";
  sender: { role: "filomena" };
  runId?: string;
  structuralAddressee?: ActorRef;
};

export type NormalizedTurnOrigin = {
  runId: string;
  inboundProviderMessageId: string;
  conversation: ConversationRef;
  participant: ActorRef;
};

export type ConversationInput = {
  accountId?: string;
  conversationId?: string;
  kind?: ConversationKind;
  threadId?: string | number;
};

export type ActorInput = {
  accountId?: string;
  nativeId?: string;
  displayName?: string;
  username?: string;
  e164?: string;
  actorKind?: ActorKind;
};

export interface ChannelMemoryAdapter {
  readonly channel: string;
  normalizeConversation(input: ConversationInput): ConversationRef | undefined;
  normalizeActor(input: ActorInput): ActorRef | undefined;
  normalizeInbound(event: Record<string, unknown>, context: Record<string, unknown>): NormalizedInboundMessage | undefined;
  normalizeOutbound(
    event: Record<string, unknown>,
    context: Record<string, unknown>,
    options?: { observedAt?: Date; selfActorIds?: string[] },
  ): NormalizedOutboundMessage | undefined;
  normalizeTurnOrigin(event: Record<string, unknown>): NormalizedTurnOrigin | undefined;
}
