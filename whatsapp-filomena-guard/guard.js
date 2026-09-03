function escapeRegExp(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

function triggerPattern(trigger) {
  return new RegExp(`^${escapeRegExp(trigger)}(?:\\s|$)`, "iu");
}

export function shouldSuppressDispatch(event, context, config) {
  const targetsProtectedGroup =
    event.channel === config.channel &&
    event.isGroup === true &&
    context.conversationId === config.groupId;
  if (!targetsProtectedGroup) return false;
  const body = typeof event.body === "string" ? event.body.trimStart() : "";
  return !triggerPattern(config.trigger).test(body);
}
