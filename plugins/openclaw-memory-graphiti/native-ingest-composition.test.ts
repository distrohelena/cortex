import { test, expect } from "vitest";
import { createServer } from "node:http";
import { once } from "node:events";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { resolve, join } from "node:path";
import { spawn } from "node:child_process";
import { NativeIngestService } from "./native-ingest.js";
import { NativeIngestStore } from "./native-ingest-store.js";
import { registerNativeIngestRoutes } from "./native-ingest-http.js";
import { createMessageLifecycleRecorder } from "./message-lifecycle.js";
import { getChannelAdapter } from "./channel-adapters/registry.js";
import type { GraphitiClient, GraphitiEpisode } from "./graphiti.js";
import type { SpiceDbClient } from "./spicedb.js";

// Explicit local integration opt-in: normal unit tests don't launch a nested test process.
test.skipIf(process.env.NATIVE_MEMORY_CROSS_LANGUAGE_TEST !== "1")("real .NET outbox -> plugin recorder -> confirmed episode -> real .NET reader", async () => {
  const dir = mkdtempSync(join(tmpdir(), "native-memory-composition-"));
  const store = new NativeIngestStore(join(dir, "receipts.sqlite"));
  const episodes: GraphitiEpisode[] = []; const facts: unknown[] = []; let adds = 0;
  const recorder = createMessageLifecycleRecorder({
    graphiti: {
      async addEpisode(p: Parameters<GraphitiClient["addEpisode"]>[0]) {
        adds++;
        episodes.push({ uuid: p.episode_uuid!, name: p.name, content: p.episode_body, group_id: p.group_id!,
          source_description: p.source_description!, created_at: "2026-09-25T00:00:00Z" });
        const speaker = p.bound_entities![0];
        // Controlled extraction fixture, NOT a claim of model selection quality.
        facts.push({ uuid: "synthetic-fact", group_id: p.group_id, fact: p.extraction_body, name: "LIKES",
          source_node_uuid: speaker.uuid, target_node_uuid: "coffee", episodes: [p.episode_uuid],
          source_node: { uuid: speaker.uuid, labels: ["Person"], stable_id: speaker.name, canonical: true },
          target_node: { uuid: "coffee", labels: ["Entity"], canonical: false } });
        return { episode_uuid: p.episode_uuid!, resolvedUuid: Promise.resolve(p.episode_uuid!) };
      }, async getEpisodes() { return episodes; },
    } as unknown as GraphitiClient,
    spicedb: { async checkPermission() { return true; }, async writeRelationships() { return "synthetic-token"; } } as unknown as SpiceDbClient,
    subject: { type: "agent", id: "synthetic-agent" }, extractionInstructions: "fixture", logger: { info() {}, warn() {} },
    getLastWriteToken: () => undefined, setLastWriteToken() {},
  });
  const conversation = getChannelAdapter("whatsapp")!.normalizeConversation({ accountId: "test-account", conversationId: "12345@g.us" })!;
  const service = new NativeIngestService(store, [{ bindingRef: "test-binding", conversation,
    notBefore: "2026-01-01T00:00:00Z", selfActorIds: ["whatsapp:15550000002@s.whatsapp.net"] }], recorder);
  const routes: Parameters<Parameters<typeof registerNativeIngestRoutes>[0]["registerHttpRoute"]>[0][] = [];
  registerNativeIngestRoutes({ registerHttpRoute(r) { routes.push(r); } }, service, "synthetic-host-capability-32-bytes-minimum");
  const server = createServer((req, res) => {
    const path = new URL(req.url!, "http://localhost").pathname;
    if (path === "/test-facts") { res.writeHead(200, { "content-type": "application/json" }).end(JSON.stringify({ facts })); return; }
    if (req.headers.authorization !== "Bearer synthetic-gateway") { res.writeHead(401).end(); return; }
    const route = routes.find(r => r.path === path);
    if (!route) { res.writeHead(404).end(); return; }
    void route.handler(req, res);
  }).listen(0, "127.0.0.1");
  try {
    await once(server, "listening");
    const env = Object.fromEntries(Object.entries(process.env).filter(([k]) => !/^(FILOMENA_|OPENAI_API_KEY$|TYPESAFE_API_KEY$|JEV_API_KEY$)/.test(k)));
    env.NATIVE_MEMORY_TEST_ENDPOINT = `http://127.0.0.1:${(server.address() as { port: number }).port}/plugins/memory-graphiti/native-ingest/v1`;
    const child = spawn("dotnet", ["test", "tests/Filomena.Llm.Tests/Filomena.Llm.Tests.csproj", "-c", "NativeMemory", "--no-build", "--no-restore",
      "--filter", "FullyQualifiedName~NativeMemoryCompositionTests.Real_plugin_round_trip", "--verbosity", "normal"],
      { cwd: resolve("../.."), env, windowsHide: true });
    let output = ""; child.stdout.on("data", b => { output += b; }); child.stderr.on("data", b => { output += b; });
    const [code] = await once(child, "exit");
    expect(code, output).toBe(0);
    expect(output).toMatch(/Passed:\s+1/);
    expect(adds).toBe(1);
    expect(JSON.parse(episodes[0].content)).toMatchObject({ account_id: "test-account", provider_message_id: "synthetic-1",
      sender: { stable_id: "whatsapp:15550000001@s.whatsapp.net" } });
  } finally { await new Promise<void>(done => server.close(() => done())); store.close(); rmSync(dir, { recursive: true }); }
});
