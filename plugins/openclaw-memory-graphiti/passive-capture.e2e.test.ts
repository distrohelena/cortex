import { afterAll, beforeAll, describe, expect, test } from "vitest";

import { GraphitiClient } from "./graphiti.js";

const live = process.env.OPENCLAW_LIVE_TEST === "1" ? describe : describe.skip;
const endpoint = process.env.GRAPHITI_ENDPOINT ?? "http://localhost:8000";
const groupId = "filomena-passive-mcp-contract-v1";
const episodeUuid = "30000000-0000-5000-8000-000000000001";
const participantUuid = "40000000-0000-5000-8000-000000000001";

live("passive capture MCP contract", () => {
  const client = new GraphitiClient(endpoint);

  beforeAll(async () => {
    expect(await client.healthCheck()).toBe(true);
    await client.clearGraph([groupId]);
  });

  afterAll(async () => {
    await client.clearGraph([groupId]);
    await client.close();
  });

  test("preserves deterministic episode identity, timestamp, and bound sender across retry", async () => {
    const request = {
      name: `inbound_${episodeUuid}`,
      episode_body: JSON.stringify({
        conversation_jid: "5511981042361-1589823314@g.us",
        sender_jid: "5511999999999@s.whatsapp.net",
        provider_message_id: "provider-contract-1",
        text: "eu gosto de Shadow of Mordor",
      }),
      extraction_body: "eu gosto de Shadow of Mordor",
      source: "json",
      source_description: "passive MCP contract test",
      group_id: groupId,
      episode_uuid: episodeUuid,
      reference_time: "2026-09-04T10:34:56.000Z",
      bound_entities: [{
        uuid: participantUuid,
        role: "speaker" as const,
        name: "whatsapp:5511999999999@s.whatsapp.net",
        entity_type: "Person",
        labels: ["Person"],
        attributes: {
          stable_id: "whatsapp:5511999999999@s.whatsapp.net",
          display_name_current: "Gabriel",
          aliases: ["Gabriel"],
        },
      }],
    };

    await Promise.all([client.addEpisode(request), client.addEpisode(request)]);

    let episodes = [] as Awaited<ReturnType<GraphitiClient["getEpisodes"]>>;
    for (let attempt = 0; attempt < 60; attempt++) {
      episodes = await client.getEpisodes(groupId, 20);
      if (episodes.some((episode) => episode.uuid === episodeUuid)) break;
      await new Promise((resolve) => setTimeout(resolve, 2000));
    }

    const matching = episodes.filter((episode) => episode.uuid === episodeUuid);
    expect(matching).toHaveLength(1);
    expect(matching[0].content).toContain("provider-contract-1");

    const people = await client.searchNodes({
      query: "whatsapp:5511999999999@s.whatsapp.net",
      group_id: groupId,
      entity_types: ["Person"],
      limit: 10,
    });
    expect(people.filter((person) => person.uuid === participantUuid)).toHaveLength(1);
  }, 150_000);
});
