import type { OpenClawPluginApi } from "openclaw/plugin-sdk/core";

import {
  prefixProtectedOutbound,
  shouldSuppressDispatch,
  targetsProtectedOutbound,
} from "./guard.js";
import { createMessageIdRegistry } from "./state.js";

type GuardConfig = {
  channel: string;
  groupId: string;
  trigger: string;
  responsePrefix: string;
  stateFile: string;
  maxTrackedMessageIds: number;
};

function readConfig(api: OpenClawPluginApi): GuardConfig {
  const raw = api.pluginConfig ?? {};
  const groupId = typeof raw.groupId === "string" ? raw.groupId.trim() : "";
  if (!groupId) {
    throw new Error("whatsapp-filomena-guard: groupId must be configured");
  }

  const channel = typeof raw.channel === "string" && raw.channel.trim()
    ? raw.channel.trim()
    : "whatsapp";
  const trigger = typeof raw.trigger === "string" && raw.trigger.trim()
    ? raw.trigger.trim()
    : "@Filomena";

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
    groupId,
    trigger,
    responsePrefix,
    stateFile,
    maxTrackedMessageIds,
  };
}

const plugin = {
  id: "whatsapp-filomena-guard",
  name: "WhatsApp Filomena Guard",
  description: "Gates and labels Filomena replies in one WhatsApp group.",

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
          `whatsapp-filomena-guard: suppressed non-trigger dispatch in ${config.groupId}`,
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
      registry.record(event.messageId);
    });
  },
};

export default plugin;
