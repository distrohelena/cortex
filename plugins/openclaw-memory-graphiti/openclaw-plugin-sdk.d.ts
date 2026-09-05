declare module "openclaw/plugin-sdk/plugin-entry" {
  import type { Command } from "commander";

  export type OpenClawPluginToolContext = {
    messageChannel?: string;
    nativeChannelId?: string;
    sessionKey?: string;
    sessionId?: string;
  };

  type Logger = {
    info(message: string): void;
    warn(message: string): void;
    error(message: string): void;
    debug(message: string): void;
  };

  type AgentContext = {
    channel?: string;
    messageProvider?: string;
    chatId?: string;
    channelId?: string;
    senderId?: string;
    sessionKey?: string;
    sessionId?: string;
    channelContext?: unknown;
  };

  type HookMap = {
    before_prompt_build: [{ prompt: string; messages: unknown[] }, AgentContext];
    reply_dispatch: [Record<string, unknown>, Record<string, unknown>];
    message_received: [
      {
        from?: string;
        content?: string;
        timestamp?: number;
        messageId?: string;
        senderId?: string;
        replyToId?: string;
        replyToIdFull?: string;
        replyToBody?: string;
        replyToSender?: string;
        replyToIsQuote?: boolean;
        providerUpdate?: Record<string, unknown>;
        metadata?: Record<string, unknown>;
        sessionKey?: string;
      },
      { channelId?: string; accountId?: string; conversationId?: string; messageId?: string; senderId?: string; sessionKey?: string },
    ];
    message_sent: [
      { to?: string; content?: string; success?: boolean; messageId?: string; runId?: string },
      { channelId?: string; conversationId?: string },
    ];
  };

  export type OpenClawPluginApi = {
    pluginConfig: unknown;
    logger: Logger;
    registerTool(
      factory: (context: OpenClawPluginToolContext) => {
        execute(toolCallId: string, params: unknown): Promise<unknown>;
        [key: string]: unknown;
      },
      options: { name: string },
    ): void;
    registerCli(
      registrar: (context: { program: Command }) => void,
      options: { commands: string[] },
    ): void;
    registerService(service: { id: string; start(): Promise<void>; stop(): void }): void;
    on<K extends keyof HookMap>(
      name: K,
      handler: (...args: HookMap[K]) => unknown | Promise<unknown>,
    ): void;
  };
}
