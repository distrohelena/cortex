import { timingSafeEqual } from "node:crypto";
import type { IncomingMessage, ServerResponse } from "node:http";
import type { NativeIngestService, NativeIngestRequestV1 } from "./native-ingest.js";

type Route = { path: string; auth: "gateway"; match: "exact"; handler(req: IncomingMessage, res: ServerResponse): Promise<void> };
const root = "/plugins/memory-graphiti/native-ingest/v1";
function object(value: unknown, keys: string[]): Record<string, unknown> {
  if (!value || typeof value !== "object" || Array.isArray(value)
    || Object.keys(value).some(k => !keys.includes(k))) throw new Error("invalid_schema");
  return value as Record<string, unknown>;
}
export function decodeNativeIngestRequest(value: unknown): NativeIngestRequestV1 {
  const r = object(value, ["version", "bindingRef", "message"]);
  const m = object(r.message, ["direction", "providerLabel", "providerMessageId", "conversation", "content", "referenceTime",
    "originalTimestamp", "timestampSource", "sender", "replyTo", "provenance"]);
  const c = object(m.conversation, ["channel", "accountId", "nativeId", "kind", "namespace", "threadId"]);
  const a = object(m.sender, ["channel", "accountId", "nativeId", "stableId", "displayName", "actorKind", "bindAsPerson",
    "identitySource", "unresolvedAlternate", "rawNativeId"]);
  for (const [obj, keys] of [[r, ["version", "bindingRef"]], [m, ["providerLabel", "providerMessageId", "content", "referenceTime"]],
    [c, ["channel", "accountId", "nativeId", "kind", "namespace"]], [a, ["channel", "accountId", "nativeId", "stableId"]]] as const)
    for (const k of keys) if (typeof obj[k] !== "string" || !(obj[k] as string).trim()) throw new Error("invalid_schema");
  if (m.replyTo !== undefined) {
    const reply = object(m.replyTo, ["providerMessageId", "providerMessageIdFull", "senderNativeId", "content", "isQuote"]);
    if (typeof reply.providerMessageId !== "string" || !reply.providerMessageId) throw new Error("invalid_schema");
    for (const [k, v] of Object.entries(reply)) if (typeof v !== (k === "isQuote" ? "boolean" : "string")) throw new Error("invalid_schema");
  }
  // Native provenance has no arbitrary metadata bag; text cannot carry authority or secrets into logs.
  if (m.provenance !== undefined) {
    const p = object(m.provenance, ["providerUpdateId", "providerUpdateKind"]);
    if (Object.values(p).some(v => typeof v !== "string")) throw new Error("invalid_schema");
  }
  for (const k of ["displayName", "identitySource", "rawNativeId"])
    if (a[k] !== undefined && typeof a[k] !== "string") throw new Error("invalid_schema");
  if (c.threadId !== undefined && typeof c.threadId !== "string") throw new Error("invalid_schema");
  return { ...r, message: { ...m, referenceTime: new Date(m.referenceTime as string) } } as NativeIngestRequestV1;
}

export function registerNativeIngestRoutes(api: { registerHttpRoute(route: Route): void }, service: NativeIngestService, capability: string,
  recoveryStatus?: (bindingRef: string) => unknown): void {
  if (typeof capability !== "string" || Buffer.byteLength(capability) < 32) throw new Error("native_ingest_capability_invalid");
  const secret = Buffer.from(capability);
  for (const path of [root, root + "/status", ...(recoveryStatus ? [root + "/recovery"] : [])]) api.registerHttpRoute({ path, auth: "gateway", match: "exact",
    async handler(req, res) {
      const write = (code: number, body: unknown) => { res.writeHead(code, { "content-type": "application/json", "cache-control": "no-store" }); res.end(JSON.stringify(body)); };
      const token = req.headers["x-native-memory-capability"];
      if (typeof token !== "string" || Buffer.byteLength(token) !== secret.length || !timingSafeEqual(Buffer.from(token), secret)) {
        write(403, { diagnostic: "host_capability_required" }); return;
      }
      try {
        const url = new URL(req.url!, "http://localhost");
        if (url.pathname !== path) { write(404, {}); return; }
        if (path.endsWith("/recovery")) {
          if (req.method !== "GET") { write(405, {}); return; }
          const binding = url.searchParams.getAll("bindingRef");
          if (binding.length !== 1 || [...url.searchParams.keys()].length !== 1) throw new Error("invalid_schema");
          write(200, recoveryStatus!(binding[0])); return;
        }
        if (path.endsWith("/status")) {
          if (req.method !== "GET" && req.method !== "POST") { write(405, {}); return; }
          const binding = url.searchParams.getAll("bindingRef"), ref = url.searchParams.getAll("requestRef");
          if (binding.length !== 1 || ref.length !== 1 || [...url.searchParams.keys()].length !== 2) throw new Error("invalid_schema");
          // GET is strictly read-only. Explicit POST performs backend/auth reconciliation.
          write(200, await (req.method === "GET" ? service.getStatus(binding[0], ref[0]) : service.reconcile(binding[0], ref[0])));
          return;
        }
        if (req.method !== "POST") { write(405, {}); return; }
        if (url.search) throw new Error("invalid_schema");
        const chunks: Buffer[] = []; let bytes = 0;
        for await (const chunk of req) {
          const buffer = Buffer.from(chunk); bytes += buffer.length;
          if (bytes > 65536) { write(413, { diagnostic: "request_too_large" }); return; }
          chunks.push(buffer);
        }
        write(200, await service.submit(decodeNativeIngestRequest(JSON.parse(Buffer.concat(chunks).toString("utf8")))));
      } catch { if (!res.headersSent) write(400, { diagnostic: "native_ingest_request_rejected" }); }
    },
  });
}
