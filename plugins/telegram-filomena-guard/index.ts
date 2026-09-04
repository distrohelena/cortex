import type { OpenClawPluginApi } from "openclaw/plugin-sdk/core";
import { isResponseMode } from "../filomena-conversation-policy-core/policy.js";
import { createScopedMessageIdRegistry } from "../filomena-conversation-policy-core/state.js";
import { shouldSuppressTelegramDispatch, shouldTrackTelegramOutbound, telegramConversationScope } from "./guard.js";

function readConfig(api: OpenClawPluginApi) {
  const raw = (api.pluginConfig ?? {}) as Record<string, unknown>;
  const respond = raw.respond as Record<string, unknown> | undefined;
  if (!respond) throw new Error("telegram-filomena-guard: respond must be configured");
  const directRaw = (respond.direct ?? {}) as Record<string, unknown>;
  const directMode = isResponseMode(directRaw.mode) ? directRaw.mode : "disabled";
  const chats = Array.isArray(directRaw.chats) ? directRaw.chats.filter((value): value is string => typeof value === "string") : [];
  const groups = {} as Record<string, { mode: string }>;
  const groupsRaw = (respond.groups ?? {}) as Record<string, unknown>;
  for (const [chatId, candidate] of Object.entries(groupsRaw)) {
    const mode = candidate && typeof candidate === "object" ? (candidate as Record<string, unknown>).mode : undefined;
    if (isResponseMode(mode)) groups[chatId.replace(/^telegram:/, "").split(":")[0]] = { mode };
  }
  const stateFile = typeof raw.stateFile === "string" && raw.stateFile.trim()
    ? api.resolvePath(raw.stateFile)
    : api.resolvePath("~/.openclaw/state/telegram-filomena-guard.json");
  return {
    channel: "telegram",
    respond: { direct: { mode: directMode, chats }, groups },
    stateFile,
    maxTrackedMessageIds: Number.isInteger(raw.maxTrackedMessageIds) ? Number(raw.maxTrackedMessageIds) : 512,
  };
}

const plugin = {
  id: "telegram-filomena-guard",
  name: "Telegram Filomena Guard",
  description: "Separates Telegram observation from configurable Filomena response policy.",
  register(api: OpenClawPluginApi) {
    const config = readConfig(api);
    const registry = createScopedMessageIdRegistry({
      filePath: config.stateFile,
      maxEntries: config.maxTrackedMessageIds,
      warn: (message) => api.logger.warn(message),
    });
    api.on("before_dispatch", (event, context) => {
      if (!shouldSuppressTelegramDispatch(event, context, config, registry)) return;
      api.logger.debug(`telegram-filomena-guard: suppressed automatic response in ${String(context.conversationId ?? "unknown conversation")}`);
      return { handled: true };
    }, { priority: 100 });
    api.on("message_sent", (event, context) => {
      if (!shouldTrackTelegramOutbound(event, context, config)) return;
      const scope = telegramConversationScope(context.conversationId ?? event.to);
      if (scope) registry.record(scope, event.messageId);
    });
  },
};

export default plugin;
