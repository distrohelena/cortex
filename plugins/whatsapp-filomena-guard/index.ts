import type { OpenClawPluginApi } from "openclaw/plugin-sdk/core";

import {
  prefixProtectedOutbound,
  shouldSuppressDispatch,
  targetsProtectedOutbound,
  whatsappConversationScope,
} from "./guard.js";
import { createMessageIdRegistry } from "./state.js";

type GuardConfig = {
  channel: string;
  responsePrefix: string;
  stateFile: string;
  maxTrackedMessageIds: number;
  respond: {
    direct: { mode: "allowlist" | "disabled"; allowFrom: string[] };
    groups: Record<string, {
      trigger: string;
      triggerAliases: string[];
      allowRepliesToFilomena: boolean;
      prefix: boolean;
    }>;
  };
};

function readConfig(api: OpenClawPluginApi): GuardConfig {
  const raw = api.pluginConfig ?? {};
  if (!raw.respond || typeof raw.respond !== "object" || Array.isArray(raw.respond)) {
    throw new Error("whatsapp-filomena-guard: respond must be configured");
  }

  const respondRaw = raw.respond as Record<string, unknown>;
  const directRaw = respondRaw.direct && typeof respondRaw.direct === "object" && !Array.isArray(respondRaw.direct)
    ? respondRaw.direct as Record<string, unknown>
    : {};
  const directMode = directRaw.mode === "disabled" ? "disabled" : "allowlist";
  const allowFrom = Array.isArray(directRaw.allowFrom)
    ? directRaw.allowFrom.filter((value): value is string => typeof value === "string" && Boolean(value.trim()))
    : [];
  const groupsRaw = respondRaw.groups && typeof respondRaw.groups === "object" && !Array.isArray(respondRaw.groups)
    ? respondRaw.groups as Record<string, unknown>
    : {};
  const groups: GuardConfig["respond"]["groups"] = {};
  for (const [rawGroupId, rawPolicy] of Object.entries(groupsRaw)) {
    const groupId = rawGroupId.trim().toLowerCase();
    if (!groupId || !rawPolicy || typeof rawPolicy !== "object" || Array.isArray(rawPolicy)) continue;
    const policy = rawPolicy as Record<string, unknown>;
    const triggerAliases = Array.isArray(policy.triggerAliases)
      ? [...new Set(policy.triggerAliases
          .filter((value): value is string => typeof value === "string")
          .map((value) => value.trim())
          .filter(Boolean))]
      : [];
    groups[groupId] = {
      trigger: typeof policy.trigger === "string" && policy.trigger.trim() ? policy.trigger.trim() : "@Filomena",
      triggerAliases,
      allowRepliesToFilomena: policy.allowRepliesToFilomena !== false,
      prefix: policy.prefix !== false,
    };
  }

  const channel = typeof raw.channel === "string" && raw.channel.trim()
    ? raw.channel.trim()
    : "whatsapp";
  let responsePrefix = "Filomena: ";
  if (raw.responsePrefix !== undefined) {
    if (typeof raw.responsePrefix !== "string" || !raw.responsePrefix.trim()) {
      throw new Error("whatsapp-filomena-guard: responsePrefix must not be blank");
    }
    responsePrefix = raw.responsePrefix;
  }

  let stateFile = api.resolvePath("~/.openclaw/state/whatsapp-filomena-guard.json");
  if (raw.stateFile !== undefined) {
    if (typeof raw.stateFile !== "string" || !raw.stateFile.trim()) {
      throw new Error("whatsapp-filomena-guard: stateFile must not be blank");
    }
    stateFile = api.resolvePath(raw.stateFile.trim());
  }

  let maxTrackedMessageIds = 512;
  if (raw.maxTrackedMessageIds !== undefined) {
    if (
      !Number.isInteger(raw.maxTrackedMessageIds) ||
      Number(raw.maxTrackedMessageIds) < 1 ||
      Number(raw.maxTrackedMessageIds) > 10_000
    ) {
      throw new Error(
        "whatsapp-filomena-guard: maxTrackedMessageIds must be a positive integer no greater than 10000",
      );
    }
    maxTrackedMessageIds = Number(raw.maxTrackedMessageIds);
  }

  return {
    channel,
    responsePrefix,
    stateFile,
    maxTrackedMessageIds,
    respond: { direct: { mode: directMode, allowFrom }, groups },
  };
}

const plugin = {
  id: "whatsapp-filomena-guard",
  name: "WhatsApp Filomena Guard",
  description: "Separates WhatsApp observation from configurable Filomena response policy.",

  register(api: OpenClawPluginApi) {
    const config = readConfig(api);
    const registry = createMessageIdRegistry({
      filePath: config.stateFile,
      maxEntries: config.maxTrackedMessageIds,
      warn: (message: string) => api.logger.warn(message),
    });

    api.on(
      "before_dispatch",
      (event, context) => {
        if (!shouldSuppressDispatch(event, context, config, registry)) return;

        api.logger.debug(
          `whatsapp-filomena-guard: suppressed automatic response in ${String(context.conversationId ?? "unknown conversation")}`,
        );
        return { handled: true };
      },
      { priority: 100 },
    );

    api.on(
      "message_sending",
      (event, context) => {
        const content = prefixProtectedOutbound(event, context, config);
        if (content === event.content) return;
        return { content };
      },
      { priority: -100 },
    );

    api.on("message_sent", (event, context) => {
      if (!event.success || !targetsProtectedOutbound(event, context, config)) return;
      registry.record(event.messageId, whatsappConversationScope(event, context));
    });
  },
};

export default plugin;
