import { getChannelAdapter } from "./channel-adapters/registry.js";
import { isAbsolute } from "node:path";
import type { ConversationRef, NormalizedInboundMessage } from "./channel-adapters/types.js";
import { buildInboundCapture, NativeIngestAuthorizationError, type MessageCapture } from "./message-lifecycle.js";
import { ingestDigest, NativeIngestStore, type NativeIngestStatusV1, type NativeIngestFailure } from "./native-ingest-store.js";
import { GraphitiRequestError } from "./graphiti.js";

function failureEvidence(error: unknown): NativeIngestFailure {
  if (error instanceof NativeIngestAuthorizationError) return { category: "authorization_unavailable_or_denied" };
  if (error instanceof GraphitiRequestError) {
    return { category: error.category, ...(Number.isInteger(error.httpStatus) && error.httpStatus! >= 100
      && error.httpStatus! <= 599 ? { httpStatus: error.httpStatus } : {}) };
  }
  return { category: "unclassified" };
}

export type NativeMemoryBinding = {
  bindingRef: string; conversation: ConversationRef; notBefore: string; selfActorIds: string[];
  /** Separate deployment authority for recurring history recovery; never supplied by HTTP. */
  historyNotBefore?: string;
  /** Existing account-owned Baileys LID/PN cache. No session keys are read. */
  historyIdentityDirectory?: string;
};
export type NativeIngestRequestV1 = {
  version: "native-memory-ingest:v1"; bindingRef: string; message: NormalizedInboundMessage;
};
export interface NativeIngestRecorder {
  submitInbound(message: NormalizedInboundMessage): Promise<{ state: "queued" }>;
  confirmInbound(capture: MessageCapture): Promise<"pending" | "persisted">;
}

export class NativeIngestService {
  private readonly bindings: Map<string, NativeMemoryBinding>;
  constructor(private store: NativeIngestStore, bindings: NativeMemoryBinding[], private recorder: NativeIngestRecorder) {
    this.bindings = new Map(bindings.map(b => [b.bindingRef, structuredClone(b)]));
    if (this.bindings.size !== bindings.length || !bindings.length) throw new Error("native_ingest_binding_invalid");
    for (const b of this.bindings.values()) {
      const normalized = getChannelAdapter(b.conversation.channel)?.normalizeConversation({ accountId: b.conversation.accountId,
        conversationId: b.conversation.nativeId, kind: b.conversation.kind, threadId: b.conversation.threadId });
      if (!b.bindingRef || !normalized || ingestDigest(normalized) !== ingestDigest(b.conversation)
        || typeof b.notBefore !== "string" || !Number.isFinite(Date.parse(b.notBefore)) || !b.selfActorIds.length
        || (b.historyNotBefore !== undefined && (typeof b.historyNotBefore !== "string" || !Number.isFinite(Date.parse(b.historyNotBefore))
          || Date.parse(b.historyNotBefore) > Date.parse(b.notBefore)))
        || (b.historyIdentityDirectory !== undefined && (typeof b.historyIdentityDirectory !== "string"
          || !isAbsolute(b.historyIdentityDirectory)))) throw new Error("native_ingest_binding_invalid");
    }
  }
  private validate(request: NativeIngestRequestV1, historical = false): NormalizedInboundMessage {
    const b = this.bindings.get(request.bindingRef);
    const m = request.message;
    if (!b || request.version !== "native-memory-ingest:v1" || !m || ingestDigest(m.conversation) !== ingestDigest(b.conversation))
      throw new Error("native_ingest_binding_invalid");
    if (historical && !b.historyNotBefore) throw new Error("native_ingest_history_authority_required");
    const actor = m.sender;
    const canonical = actor && getChannelAdapter(b.conversation.channel)?.normalizeActor({ accountId: b.conversation.accountId, nativeId: actor.nativeId });
    if (m.direction !== "inbound" || m.timestampSource !== "provider" || !actor || !canonical
      || actor.accountId !== b.conversation.accountId || actor.channel !== b.conversation.channel
      || actor.stableId !== canonical.stableId || canonical.unresolvedAlternate || actor.unresolvedAlternate
      || actor.bindAsPerson !== true || actor.actorKind !== "person" || b.selfActorIds.includes(actor.stableId)
      || (b.conversation.kind === "dm" && actor.nativeId !== b.conversation.nativeId))
      throw new Error("native_ingest_actor_invalid");
    if (!(m.referenceTime instanceof Date) || !Number.isFinite(m.referenceTime.getTime())
      || m.referenceTime.getTime() < Date.parse(historical ? b.historyNotBefore! : b.notBefore) || !Number.isFinite(m.originalTimestamp)
      || (m.originalTimestamp < 1e12 ? m.originalTimestamp * 1000 : m.originalTimestamp) !== m.referenceTime.getTime()
      || typeof m.content !== "string" || !m.content.trim() || !m.providerMessageId?.trim()
      || Buffer.byteLength(JSON.stringify(m)) > 60 * 1024) throw new Error("native_ingest_message_invalid");
    return structuredClone(m);
  }
  async submit(request: NativeIngestRequestV1): Promise<NativeIngestStatusV1> {
    return this.submitValidated(this.validate(request), request.bindingRef);
  }
  async submitHistorical(request: NativeIngestRequestV1): Promise<NativeIngestStatusV1> {
    return this.submitValidated(this.validate(request, true), request.bindingRef);
  }
  private async submitValidated(message: NormalizedInboundMessage, bindingRef: string): Promise<NativeIngestStatusV1> {
    const capture = buildInboundCapture(message);
    const requestRef = ingestDigest([message.conversation.accountId, capture.episodeUuid]);
    const entry = this.store.stage({ bindingRef, message, digest: ingestDigest(message),
      receipt: { version: "native-memory-ingest:v1", requestRef, episodeUuid: capture.episodeUuid, state: "pending", retryDisposition: "safe" } });
    if (!this.store.claim(requestRef)) return this.store.get(requestRef)!.receipt;
    let receipt: NativeIngestStatusV1;
    try {
      await this.recorder.submitInbound(message);
      receipt = { ...entry.receipt, state: "queued", diagnostic: undefined, retryDisposition: "reconcile" };
    } catch (e) {
      receipt = { ...entry.receipt, state: "failed",
        submissionFailure: failureEvidence(e),
        diagnostic: e instanceof NativeIngestAuthorizationError ? "authorization_unavailable_or_denied" : "submission_unconfirmed",
        retryDisposition: e instanceof NativeIngestAuthorizationError ? "safe" : "reconcile" };
    }
    this.store.save(receipt);
    return this.store.get(requestRef)!.receipt;
  }
  async getStatus(bindingRef: string, requestRef: string): Promise<NativeIngestStatusV1> {
    const row = this.store.get(requestRef);
    if (!this.bindings.has(bindingRef) || !row || row.bindingRef !== bindingRef) throw new Error("native_ingest_binding_invalid");
    const historical = row.message.referenceTime.getTime() < Date.parse(this.bindings.get(bindingRef)!.notBefore);
    this.validate({ version: "native-memory-ingest:v1", bindingRef, message: row.message }, historical);
    return row.receipt;
  }
  async reconcile(bindingRef: string, requestRef: string): Promise<NativeIngestStatusV1> {
    const receipt = await this.getStatus(bindingRef, requestRef);
    try {
      const state = await this.recorder.confirmInbound(buildInboundCapture(this.store.get(requestRef)!.message));
      if (state === "persisted") {
        const confirmed = { ...receipt, state, diagnostic: undefined, retryDisposition: "none" as const };
        this.store.save(confirmed); return confirmed;
      }
      return receipt;
    } catch (e) {
      const failed = { ...receipt, state: "failed" as const, diagnostic: "confirmation_failed",
        confirmationFailure: failureEvidence(e), retryDisposition: "reconcile" as const };
      this.store.save(failed); return failed;
    }
  }
}
