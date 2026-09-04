import type { ReplyRef, SafeProvenance } from "./types.js";

export function nonBlank(value: unknown): string | undefined {
  return typeof value === "string" && value.trim() ? value.trim() : undefined;
}
export function recordValue(value: unknown): Record<string, unknown> | undefined {
  return value && typeof value === "object" && !Array.isArray(value)
    ? value as Record<string, unknown>
    : undefined;
}

export function timestampDate(value: unknown): Date | undefined {
  if (typeof value !== "number" || !Number.isFinite(value) || value <= 0) return undefined;
  const date = new Date(value < 1_000_000_000_000 ? value * 1000 : value);
  return Number.isNaN(date.valueOf()) ? undefined : date;
}

export function normalizedReply(event: Record<string, unknown>, context: Record<string, unknown>): ReplyRef | undefined {
  const providerMessageId = nonBlank(event.replyToId) ?? nonBlank(context.replyToId);
  const providerMessageIdFull = nonBlank(event.replyToIdFull) ?? nonBlank(context.replyToIdFull);
  if (!providerMessageId && !providerMessageIdFull) return undefined;
  return {
    providerMessageId: providerMessageId ?? providerMessageIdFull!,
    ...(providerMessageIdFull ? { providerMessageIdFull } : {}),
    ...(nonBlank(event.replyToSender) ?? nonBlank(context.replyToSender)
      ? { senderNativeId: nonBlank(event.replyToSender) ?? nonBlank(context.replyToSender) }
      : {}),
    ...(typeof event.replyToBody === "string"
      ? { content: event.replyToBody }
      : typeof context.replyToBody === "string" ? { content: context.replyToBody } : {}),
    ...(typeof event.replyToIsQuote === "boolean"
      ? { isQuote: event.replyToIsQuote }
      : typeof context.replyToIsQuote === "boolean" ? { isQuote: context.replyToIsQuote } : {}),
  };
}

/** Project only bounded, non-secret hook fields into durable provenance. */
export function safeProvenance(event: Record<string, unknown>): SafeProvenance | undefined {
  const metadata = recordValue(event.metadata);
  const providerUpdate = recordValue(event.providerUpdate);
  const raw: Record<string, unknown> = {};
  for (const key of ["provider", "surface", "originatingChannel", "originatingTo", "threadId"] as const) {
    const value = metadata?.[key];
    if (typeof value === "string" || typeof value === "number") raw[key] = value;
  }
  const providerUpdateId = nonBlank(providerUpdate?.id);
  const providerUpdateKind = nonBlank(providerUpdate?.kind);
  if (!providerUpdateId && !providerUpdateKind && Object.keys(raw).length === 0) return undefined;
  return {
    ...(providerUpdateId ? { providerUpdateId } : {}),
    ...(providerUpdateKind ? { providerUpdateKind } : {}),
    ...(Object.keys(raw).length > 0 ? { raw } : {}),
  };
}
