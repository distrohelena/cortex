function escapeRegExp(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

function triggerPattern(trigger) {
  return new RegExp(`^${escapeRegExp(trigger)}(?:\\s|$)`, "iu");
}

function nonBlank(value) {
  return typeof value === "string" && value.trim() ? value.trim() : undefined;
}

export function normalizeWhatsAppPeerId(value) {
  const raw = nonBlank(value)?.toLowerCase().replace(/^whatsapp:/, "");
  if (!raw) return undefined;
  if (/^\+?[\d\s().-]+$/.test(raw)) {
    const digits = raw.replace(/\D/g, "");
    return /^\d{7,15}$/.test(digits) ? `${digits}@s.whatsapp.net` : undefined;
  }

  const separator = raw.lastIndexOf("@");
  if (separator <= 0 || separator === raw.length - 1) return undefined;
  const local = raw.slice(0, separator).replace(/:\d+$/, "");
  const rawDomain = raw.slice(separator + 1);
  const domain = rawDomain === "c.us" ? "s.whatsapp.net" : rawDomain;
  return local && domain ? `${local}@${domain}` : undefined;
}

function normalizedConversation(event, context) {
  return normalizeWhatsAppPeerId(context.conversationId ?? event.to);
}

function groupPolicy(event, context, config) {
  const conversation = normalizedConversation(event, context);
  return conversation ? config.respond.groups[conversation] : undefined;
}

function replyTargetsTrackedMessage(event, context, registry) {
  return [event.replyToId, event.replyToIdFull, context.replyToId, context.replyToIdFull]
    .some((value) => registry.has(value));
}

function directPeerIsAllowed(event, context, config) {
  if (config.respond.direct.mode !== "allowlist") return false;
  const allowed = new Set(config.respond.direct.allowFrom.map(normalizeWhatsAppPeerId).filter(Boolean));
  const candidates = [context.conversationId, context.senderId, event.senderId]
    .map(normalizeWhatsAppPeerId)
    .filter(Boolean);
  return candidates.some((candidate) => allowed.has(candidate));
}

export function targetsProtectedOutbound(event, context, config) {
  if (context.channelId !== config.channel) return false;
  const conversation = normalizedConversation(event, context);
  if (!conversation) return false;
  if (conversation.endsWith("@g.us")) return Boolean(config.respond.groups[conversation]);
  return config.respond.direct.mode === "allowlist"
    && config.respond.direct.allowFrom.some((peer) => normalizeWhatsAppPeerId(peer) === conversation);
}

export function prefixProtectedOutbound(event, context, config) {
  const content = typeof event.content === "string" ? event.content : "";
  const policy = context.channelId === config.channel ? groupPolicy(event, context, config) : undefined;
  if (!policy?.prefix) return content;
  if (!content || content.startsWith(config.responsePrefix)) return content;
  return `${config.responsePrefix}${content}`;
}

export function shouldSuppressDispatch(
  event,
  context,
  config,
  registry = { has: () => false },
) {
  if (event.channel !== config.channel) return false;

  const isGroup = event.isGroup === true
    || normalizeWhatsAppPeerId(context.conversationId)?.endsWith("@g.us");
  if (!isGroup) return !directPeerIsAllowed(event, context, config);

  const policy = groupPolicy(event, context, config);
  if (!policy) return true;
  const body = typeof event.body === "string" ? event.body.trimStart() : "";
  if (triggerPattern(policy.trigger).test(body)) return false;
  if (policy.allowRepliesToFilomena && replyTargetsTrackedMessage(event, context, registry)) return false;
  return true;
}
