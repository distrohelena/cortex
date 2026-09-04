import { afterAll, beforeAll, describe, expect, test } from "vitest";

import { GraphitiClient, type GraphitiFact } from "./graphiti.js";
import { buildOutboundCapture } from "./outbound-capture.js";
import { TurnCorrelationRegistry } from "./turn-correlation.js";

const live = process.env.OPENCLAW_LIVE_TEST === "1" ? describe : describe.skip;
const endpoint = process.env.GRAPHITI_ENDPOINT ?? "http://localhost:8000";
const groupId = "filomena-social-memory-contract-v1";
const groupJid = "120363099999999999@g.us";

live("social memory MCP + FalkorDB contract", () => {
  const client = new GraphitiClient(endpoint);

  beforeAll(async () => {
    expect(await client.healthCheck()).toBe(true);
    await client.clearGraph([groupId]);
  });

  afterAll(async () => {
    await client.clearGraph([groupId]);
    await client.close();
  });

  test("persists one outbound episode, canonical actors, and set-like address evidence", async () => {
    const correlations = new TurnCorrelationRegistry();
    const correlation = correlations.observeReplyDispatch({
      groupId,
      event: {
        runId: "integration-run-1",
        ctx: {
          Provider: "whatsapp",
          OriginatingTo: groupJid,
          MessageSidFull: "integration-inbound-1",
          SenderId: "5511999999999@s.whatsapp.net",
          SenderName: "Gabriel",
        },
      },
    });
    const capture = buildOutboundCapture({
      event: {
        to: groupJid,
        content: "Filomena: Professor, resposta de integração.",
        success: true,
        messageId: "integration-outbound-1",
        runId: "integration-run-1",
      },
      context: { channelId: "whatsapp", conversationId: groupJid },
      groupId,
      observedAt: new Date("2026-09-03T18:30:00.000Z"),
      correlation,
    });
    expect(capture).toBeDefined();

    const request = {
      name: capture!.episodeName,
      episode_body: capture!.episodeBody,
      extraction_body: capture!.extractionBody,
      source: "json",
      source_description: "Phase 3 outbound integration",
      group_id: groupId,
      episode_uuid: capture!.episodeUuid,
      reference_time: capture!.referenceTime,
      bound_entities: capture!.boundEntities,
      custom_extraction_instructions: "Extract only concrete observed facts.",
    };
    await Promise.all([client.addEpisode(request), client.addEpisode(request)]);

    await client.recordAddressTerm({
      group_id: groupId,
      source_uuid: capture!.boundEntities[0].uuid,
      target_uuid: capture!.boundEntities[1].uuid,
      term: "Professor",
      normalized_term: "professor",
      run_id: "integration-run-1",
      episode_uuid: capture!.episodeUuid,
      reference_time: capture!.referenceTime,
    });
    await client.recordAddressTerm({
      group_id: groupId,
      source_uuid: capture!.boundEntities[0].uuid,
      target_uuid: capture!.boundEntities[1].uuid,
      term: "Professor",
      normalized_term: "professor",
      run_id: "integration-run-1",
      episode_uuid: capture!.episodeUuid,
      reference_time: capture!.referenceTime,
    });
    await client.recordAddressTerm({
      group_id: groupId,
      source_uuid: capture!.boundEntities[0].uuid,
      target_uuid: capture!.boundEntities[1].uuid,
      term: "Professor",
      normalized_term: "professor",
      run_id: "integration-run-2",
      episode_uuid: capture!.episodeUuid,
      reference_time: capture!.referenceTime,
    });

    let episodes = await client.getEpisodes(groupId, 20);
    let neighborhood: GraphitiFact[] = [];
    for (let attempt = 0; attempt < 75; attempt++) {
      if (episodes.some((episode) => episode.uuid === capture!.episodeUuid)
        && neighborhood.some((fact) => fact.name === "USED_ADDRESS_TERM")) break;
      await new Promise((resolve) => setTimeout(resolve, 2000));
      episodes = await client.getEpisodes(groupId, 20);
      try {
        neighborhood = await client.getEntityNeighborhood({
          group_id: groupId,
          entity_uuid: capture!.boundEntities[1].uuid,
          counterpart_uuid: capture!.boundEntities[0].uuid,
          active_only: true,
          limit: 10,
        });
      } catch (error) {
        if (!(error instanceof Error) || !error.message.includes("not found")) throw error;
      }
    }

    expect(episodes.filter((episode) => episode.uuid === capture!.episodeUuid)).toHaveLength(1);
    const evidence = neighborhood.find((fact) => fact.name === "USED_ADDRESS_TERM");
    expect(evidence?.source_node).toMatchObject({
      uuid: capture!.boundEntities[0].uuid,
      canonical: true,
      stable_id: "agent:filomena",
    });
    expect(evidence?.target_node).toMatchObject({
      uuid: capture!.boundEntities[1].uuid,
      canonical: true,
    });
    expect(evidence?.attributes?.evidence_run_ids).toEqual([
      "integration-run-1",
      "integration-run-2",
    ]);
  }, 180_000);
});
