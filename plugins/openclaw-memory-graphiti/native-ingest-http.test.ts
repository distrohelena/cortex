import { createServer, type IncomingMessage, type ServerResponse } from "node:http";
import { once } from "node:events";
import { expect, test } from "vitest";
import { registerNativeIngestRoutes } from "./native-ingest-http.js";
import type { NativeIngestService } from "./native-ingest.js";

async function fixture() {
  const routes: { path: string; auth: string; handler(req: IncomingMessage, res: ServerResponse): unknown }[] = [];
  let submits = 0;
  registerNativeIngestRoutes({ registerHttpRoute: r => routes.push(r) }, {
    async submit() { submits++; return { state: "queued" }; },
    async getStatus() { return { state: "queued" }; }, async reconcile() { return { state: "persisted" }; },
  } as unknown as NativeIngestService, "synthetic-host-capability-32-bytes-minimum");
  const server = createServer((req, res) => {
    const route = routes.find(r => r.path === new URL(req.url!, "http://localhost").pathname);
    if (!route) { res.writeHead(404).end(); return; } void route.handler(req, res);
  }).listen(0, "127.0.0.1");
  await once(server, "listening");
  const url = `http://127.0.0.1:${(server.address() as { port: number }).port}/plugins/memory-graphiti/native-ingest/v1`;
  return { routes, url, submits: () => submits, close: () => new Promise<void>((resolve) => server.close(() => resolve())) };
}
test.each([undefined, "wrong"])("rejects missing or invalid host capability before any ingestion", async capability => {
  const f = await fixture(); try {
    const r = await fetch(f.url, { method: "POST", headers: capability ? { "x-native-memory-capability": capability } : {}, body: "malformed" });
    expect(r.status).toBe(403); expect(f.submits()).toBe(0);
    expect(f.routes.every(r => r.auth === "gateway")).toBe(true);
  } finally { await f.close(); }
});
test.each(["{", JSON.stringify({ message: {} }), "x".repeat(65537)])("rejects malformed or oversized body", async body => {
  const f = await fixture(); try {
    const r = await fetch(f.url, { method: "POST", headers: { "x-native-memory-capability": "synthetic-host-capability-32-bytes-minimum" }, body });
    expect([400, 413]).toContain(r.status); expect(f.submits()).toBe(0);
  } finally { await f.close(); }
});
test("status is authenticated and does not submit or reconcile implicitly", async () => {
  const f = await fixture(); try {
    const r = await fetch(f.url + "/status?bindingRef=test&requestRef=abc", { headers: { "x-native-memory-capability": "synthetic-host-capability-32-bytes-minimum" } });
    expect(await r.json()).toEqual({ state: "queued" }); expect(f.submits()).toBe(0);
  } finally { await f.close(); }
});
test("weak capability cannot register routes", () => {
  expect(() => registerNativeIngestRoutes({ registerHttpRoute() { throw new Error("registered"); } }, {} as NativeIngestService, "short"))
    .toThrow("capability_invalid");
});
