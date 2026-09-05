import { describe, expect, test, vi } from "vitest";
import { InboundEventBridge } from "./event-bridge.js";
import { whatsappMemoryAdapter } from "./channel-adapters/whatsapp.js";

function inboundMessage() {
  const message = whatsappMemoryAdapter.normalizeInbound(
    {
      content: "Oiii flooo",
      timestamp: 1_788_571_873,
      messageId: "3EB0C49C242B01294DB555",
      senderId: "5511944558218:7@c.us",
      metadata: {
        senderName: "Helena Assis",
        senderE164: "+5511944558218",
        provider: "whatsapp",
      },
      replyToId: "3EB0ANCHOR",
    },
    {
      channelId: "whatsapp",
      accountId: "default",
      conversationId: "5511944558218@s.whatsapp.net",
      sessionKey: "agent:main:whatsapp:default:direct:+5511944558218",
    },
  );
  if (!message) throw new Error("fixture did not normalize");
  return message;
}

describe("InboundEventBridge", () => {
  test("posts one bounded canonical event without provider secrets", async () => {
    const fetch = vi.fn(async () => new Response(null, { status: 202 }));
    const bridge = new InboundEventBridge(
      {
        endpoint: "http://127.0.0.1:5147/v1/openclaw/events/inbound",
        timeoutMs: 500,
      },
      fetch,
    );

    await bridge.forward(
      inboundMessage(),
      "agent:main:whatsapp:default:direct:+5511944558218",
    );

    expect(fetch).toHaveBeenCalledTimes(1);
    const [url, init] = fetch.mock.calls[0];
    expect(url).toBe("http://127.0.0.1:5147/v1/openclaw/events/inbound");
    expect(init?.method).toBe("POST");
    const body = JSON.parse(String(init?.body));
    expect(body).toMatchObject({
      schemaVersion: 1,
      sessionKey: "agent:main:whatsapp:default:direct:+5511944558218",
      providerMessageId: "3EB0C49C242B01294DB555",
      content: "Oiii flooo",
      conversation: {
        channel: "whatsapp",
        accountId: "default",
        nativeId: "5511944558218@s.whatsapp.net",
        namespace: "whatsapp-dm-5511944558218-s-whatsapp-net",
        kind: "dm",
      },
      sender: {
        nativeId: "5511944558218@s.whatsapp.net",
        stableId: "whatsapp:5511944558218@s.whatsapp.net",
        displayName: "Helena Assis",
        rawNativeId: "5511944558218:7@c.us",
      },
      replyTo: { providerMessageId: "3EB0ANCHOR" },
      timestamp: "2026-09-05T01:31:13.000Z",
    });
    expect(JSON.stringify(body)).not.toMatch(/token|authorization|cookie/i);
  });

  test("throws on non-success so the hook can log delivery failure", async () => {
    const bridge = new InboundEventBridge(
      { endpoint: "http://127.0.0.1:5147/v1/openclaw/events/inbound", timeoutMs: 500 },
      async () => new Response(null, { status: 503 }),
    );

    await expect(bridge.forward(inboundMessage(), "session-key"))
      .rejects.toThrow(/503/);
  });
});
