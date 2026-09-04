import type { ChannelMemoryAdapter } from "./types.js";
import { telegramMemoryAdapter } from "./telegram.js";
import { whatsappMemoryAdapter } from "./whatsapp.js";

const adapters = new Map<string, ChannelMemoryAdapter>([
  [telegramMemoryAdapter.channel, telegramMemoryAdapter],
  [whatsappMemoryAdapter.channel, whatsappMemoryAdapter],
]);

export function getChannelAdapter(channel: string | undefined): ChannelMemoryAdapter | undefined {
  return channel ? adapters.get(channel.trim().toLowerCase()) : undefined;
}
export function supportedMemoryChannels(): string[] {
  return [...adapters.keys()].sort();
}
