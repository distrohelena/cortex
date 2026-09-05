import type { NormalizedInboundMessage } from "./channel-adapters/types.js";

export type InboundEventBridgeOptions = {
  endpoint: string;
  timeoutMs: number;
};

type Fetch = typeof globalThis.fetch;

export class InboundEventBridge {
  constructor(
    private readonly options: InboundEventBridgeOptions,
    private readonly fetchImpl: Fetch = globalThis.fetch,
  ) {}

  async forward(message: NormalizedInboundMessage, sessionKey?: string): Promise<void> {
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), this.options.timeoutMs);
    try {
      const response = await this.fetchImpl(this.options.endpoint, {
        method: "POST",
        redirect: "error",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({
          schemaVersion: 1,
          ...(sessionKey?.trim() ? { sessionKey: sessionKey.trim() } : {}),
          providerMessageId: message.providerMessageId,
          content: message.content,
          timestamp: message.referenceTime.toISOString(),
          timestampSource: message.timestampSource,
          conversation: message.conversation,
          ...(message.sender ? {
            sender: {
              channel: message.sender.channel,
              accountId: message.sender.accountId,
              nativeId: message.sender.nativeId,
              stableId: message.sender.stableId,
              ...(message.sender.displayName ? { displayName: message.sender.displayName } : {}),
              ...(message.sender.rawNativeId ? { rawNativeId: message.sender.rawNativeId } : {}),
            },
          } : {}),
          ...(message.replyTo ? { replyTo: message.replyTo } : {}),
          provenance: {
            source: "openclaw_message_received",
            ...(message.provenance?.providerUpdateId
              ? { providerUpdateId: message.provenance.providerUpdateId }
              : {}),
            ...(message.provenance?.providerUpdateKind
              ? { providerUpdateKind: message.provenance.providerUpdateKind }
              : {}),
          },
        }),
        signal: controller.signal,
      });
      if (!response.ok) {
        throw new Error(`Filomena inbound event bridge returned HTTP ${response.status}`);
      }
    } finally {
      clearTimeout(timeout);
    }
  }
}
