import { shouldRespond } from "../filomena-conversation-policy-core/policy.js";

function normalizeChatId(value) {
  if (typeof value !== "string" && typeof value !== "number") return undefined;
  const raw = String(value).trim().toLowerCase().replace(/^telegram:/, "");
  const match = raw.match(/^(-?\d+)(?::(?:topic|direct-topic):\d+)?$/);
  return match?.[1];
}

export function telegramConversationScope(value) {
  const chatId = normalizeChatId(value);
  return chatId ? `telegram:${chatId}` : undefined;
}

function policyFor(event, context, config) {
  const chatId = normalizeChatId(context.conversationId ?? event.to);
  if (!chatId) return { mode: "disabled" };
  const isGroup = event.isGroup === true || chatId.startsWith("-");
  if (isGroup) return config.respond.groups[chatId] ?? { mode: "disabled" };
  const direct = config.respond.direct;
  if (!Array.isArray(direct.chats) || direct.chats.length === 0) return direct;
  return direct.chats.some((candidate) => normalizeChatId(candidate) === chatId)
    ? direct
    : { mode: "disabled" };
}

export function shouldSuppressTelegramDispatch(event, context, config, registry = { has: () => false }) {
  if (event.channel !== config.channel) return false;
  const scope = telegramConversationScope(context.conversationId ?? event.to);
  const replyIds = [event.replyToId, event.replyToIdFull, context.replyToId, context.replyToIdFull];
  const replyToFilomena = Boolean(scope && replyIds.some((id) => registry.has(scope, id)));
  return !shouldRespond(policyFor(event, context, config).mode, {
    mentioned: event.wasMentioned === true,
    replyToFilomena,
  });
}

export function shouldTrackTelegramOutbound(event, context, config) {
  if (context.channelId !== config.channel || event.success !== true) return false;
  const policy = policyFor({ ...event, channel: config.channel }, context, config);
  return policy.mode !== "disabled" && Boolean(telegramConversationScope(context.conversationId ?? event.to));
}
