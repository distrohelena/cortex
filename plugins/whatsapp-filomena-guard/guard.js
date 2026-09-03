function escapeRegExp(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

function triggerPattern(trigger) {
  return new RegExp(`^${escapeRegExp(trigger)}(?:\\s|$)`, "iu");
}

function targetsProtectedInbound(event, context, config) {
  return (
    event.channel === config.channel &&
    event.isGroup === true &&
    context.conversationId === config.groupId
  );
}

function replyTargetsTrackedMessage(event, context, registry) {
  return [event.replyToId, event.replyToIdFull, context.replyToId, context.replyToIdFull]
    .some((value) => registry.has(value));
}

export function targetsProtectedOutbound(event, context, config) {
  if (context.channelId !== config.channel) return false;
  return context.conversationId === config.groupId || event.to === config.groupId;
}

export function prefixProtectedOutbound(event, context, config) {
  const content = typeof event.content === "string" ? event.content : "";
  if (!targetsProtectedOutbound(event, context, config)) return content;
  if (!content || content.startsWith(config.responsePrefix)) return content;
  return `${config.responsePrefix}${content}`;
}

export function shouldSuppressDispatch(
  event,
  context,
  config,
  registry = { has: () => false },
) {
  if (!targetsProtectedInbound(event, context, config)) return false;
  const body = typeof event.body === "string" ? event.body.trimStart() : "";
  if (triggerPattern(config.trigger).test(body)) return false;
  return !replyTargetsTrackedMessage(event, context, registry);
}
