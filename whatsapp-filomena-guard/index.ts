import type { OpenClawPluginApi } from "openclaw/plugin-sdk/core";

import { shouldSuppressDispatch } from "./guard.js";

type GuardConfig = {
  channel: string;
  groupId: string;
  trigger: string;
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

  return { channel, groupId, trigger };
}

const plugin = {
  id: "whatsapp-filomena-guard",
  name: "WhatsApp Filomena Guard",
  description: "Requires an explicit leading trigger in one WhatsApp group.",

  register(api: OpenClawPluginApi) {
    const config = readConfig(api);

    api.on(
      "before_dispatch",
      (event, context) => {
        if (!shouldSuppressDispatch(event, context, config)) return;

        api.logger.debug(
          `whatsapp-filomena-guard: suppressed non-trigger dispatch in ${config.groupId}`,
        );
        return { handled: true };
      },
      { priority: 100 },
    );
  },
};

export default plugin;
