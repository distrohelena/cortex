import { expect, test } from "vitest";
import { createMessageLifecycleRecorder, buildInboundCapture } from "./message-lifecycle.js";
import { getChannelAdapter } from "./channel-adapters/registry.js";
import type { GraphitiClient, GraphitiEpisode } from "./graphiti.js";
import type { SpiceDbClient } from "./spicedb.js";

function fixture() {
  const message = getChannelAdapter("whatsapp")!.normalizeInbound({ messageId: "test-1", content: "Prefiro café.",
    timestamp: 1790000000000, senderId: "15550000001@s.whatsapp.net" }, { accountId: "test", conversationId: "123@g.us" })!;
  const capture = buildInboundCapture(message);
  const state = { permission: true, authOutage: false, fragmentFailure: false, adds: 0, fragments: 0, episodes: [] as GraphitiEpisode[], submissions: [] as Parameters<GraphitiClient["addEpisode"]>[0][] };
  const recorder = createMessageLifecycleRecorder({
    graphiti: { async addEpisode(p: Parameters<GraphitiClient["addEpisode"]>[0]) { state.adds++; state.submissions.push(p); return { episode_uuid: capture.episodeUuid, resolvedUuid: Promise.resolve(capture.episodeUuid) }; },
      async getEpisodes() { return state.episodes; } } as unknown as GraphitiClient,
    spicedb: { async checkPermission() { if (state.authOutage) throw new Error("offline"); return state.permission; },
      async writeRelationships() { if (state.fragmentFailure) throw new Error("fragment write failed"); state.fragments++; return "test-token"; },
    } as unknown as SpiceDbClient,
    subject: { type: "agent", id: "synthetic-agent" }, extractionInstructions: "unchanged", logger: { info() {}, warn() {} },
    getLastWriteToken: () => undefined, setLastWriteToken() {},
  });
  const episode: GraphitiEpisode = { uuid: capture.episodeUuid, name: capture.episodeName, content: capture.episodeBody,
    group_id: capture.conversationNamespace, source_description: "OpenClaw inbound WhatsApp message", created_at: "2026-09-25T00:00:00Z" };
  return { message, capture, recorder, state, episode };
}
test("queued UUID is not persistence evidence", async () => {
  const f = fixture(); expect((await f.recorder.submitInbound(f.message)).state).toBe("queued");
  expect(await f.recorder.confirmInbound(f.capture)).toBe("pending");
  expect(f.state.fragments).toBe(0);
});
test.each(["denied", "outage"])("%s authorization prevents backend submission", async kind => {
  const f = fixture(); f.state.permission = false; f.state.authOutage = kind === "outage";
  await expect(f.recorder.submitInbound(f.message)).rejects.toThrow(); expect(f.state.adds).toBe(0);
});
test("observed exact episode with zero facts can be persisted after authorization completes", async () => {
  const f = fixture(); f.state.episodes = [f.episode];
  expect(await f.recorder.confirmInbound(f.capture)).toBe("persisted"); expect(f.state.fragments).toBe(1);
});

test("old episode outside latest 100 is still confirmed without resubmitting extraction", async () => {
  const f = fixture();
  const old = Array.from({ length: 100 }, (_, i) => ({ ...f.episode, uuid: `other-${i}` }));
  // Real MCP contract: max_episodes limits the returned latest episodes.
  const get = async (_group: string, count: number) => [...old, f.episode].slice(0, count);
  // Replace only the external read, not the recorder/permission/provenance logic.
  const recorder = createMessageLifecycleRecorder({
    graphiti: { getEpisodes: get } as unknown as GraphitiClient,
    spicedb: { async checkPermission() { return true; }, async writeRelationships() { return "token"; } } as unknown as SpiceDbClient,
    subject: { type: "agent", id: "synthetic-agent" }, extractionInstructions: "unchanged", logger: { info() {}, warn() {} },
    getLastWriteToken: () => undefined, setLastWriteToken() {},
  });
  expect(await recorder.confirmInbound(f.capture)).toBe("persisted");
});
test.each(["namespace", "content", "fragment", "revoked"])("%s failure cannot be called persisted", async kind => {
  const f = fixture(); f.state.episodes = [f.episode];
  if (kind === "namespace") f.episode.group_id = "foreign";
  if (kind === "content") f.episode.content = "different content";
  if (kind === "fragment") f.state.fragmentFailure = true;
  if (kind === "revoked") f.state.permission = false;
  await expect(f.recorder.confirmInbound(f.capture)).rejects.toThrow();
});

test.each([
  ["preference", "Eu prefiro café."], ["joke", "Agora com 1000 cérebros."],
  ["identity claim", "Eu sou o Batman."], ["third party", "Celso disse que gosta de chá."],
  ["correction", "Corrigindo: eu prefiro chá."], ["banal", "Boa tarde."],
])("%s preserves source attribution without claiming an extracted fact", async (_kind, text) => {
  const f = fixture(); await f.recorder.submitInbound({ ...f.message, content: text });
  const submitted = f.state.submissions[0];
  expect(submitted.extraction_body).toBe(text);
  expect(submitted.bound_entities).toHaveLength(1);
  expect(submitted.bound_entities![0]).toMatchObject({ role: "speaker", name: "whatsapp:15550000001@s.whatsapp.net", entity_type: "Person" });
  expect(JSON.parse(submitted.episode_body)).toMatchObject({ text, sender: { stable_id: "whatsapp:15550000001@s.whatsapp.net" } });
  expect(submitted.custom_extraction_instructions).toBe("unchanged");
  expect(await f.recorder.confirmInbound(f.capture)).toBe("pending");
});
