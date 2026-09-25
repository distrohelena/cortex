import { readFileSync } from "node:fs";
import { isAbsolute } from "node:path";
import type { MessageLifecycleRecorder } from "./message-lifecycle.js";
import { NativeIngestStore } from "./native-ingest-store.js";
import { NativeIngestService, type NativeMemoryBinding } from "./native-ingest.js";
import { registerNativeIngestRoutes } from "./native-ingest-http.js";

type Api = Parameters<typeof registerNativeIngestRoutes>[0] & {
  registerService(service: { id: string; start(): Promise<void>; stop(): void }): void;
};
/** Only deployment environment selects this file; never plugin/project/model input. */
export function composeNativeMemoryIngest(api: Api, recorder: MessageLifecycleRecorder, env: NodeJS.ProcessEnv = process.env): void {
  const file = env.OPENCLAW_NATIVE_MEMORY_INGEST_HOST_FILE;
  if (!file) return;
  if (!isAbsolute(file)) throw new Error("native_memory_host_file_invalid");
  const config = JSON.parse(readFileSync(file, "utf8")) as {
    version: string; storePath: string; capabilityEnv: string; bindings: NativeMemoryBinding[];
  };
  if (!config || Object.keys(config).some(k => !["version", "storePath", "capabilityEnv", "bindings"].includes(k))
    || config.version !== "native-memory-host:v1" || typeof config.storePath !== "string" || !isAbsolute(config.storePath)
    || typeof config.capabilityEnv !== "string" || !/^[A-Z][A-Z0-9_]+$/.test(config.capabilityEnv)
    || !Array.isArray(config.bindings)) throw new Error("native_memory_host_config_invalid");
  const capability = env[config.capabilityEnv];
  if (!capability || Buffer.byteLength(capability) < 32) throw new Error("native_ingest_capability_invalid");
  const store = new NativeIngestStore(config.storePath);
  try {
    const service = new NativeIngestService(store, config.bindings, recorder);
    registerNativeIngestRoutes(api, service, capability);
    api.registerService({ id: "native-memory-ingest", async start() {}, stop: () => store.close() });
  } catch (e) { store.close(); throw e; }
}
