import { readFileSync } from "node:fs";
import { isAbsolute, resolve } from "node:path";
import type { MessageLifecycleRecorder } from "./message-lifecycle.js";
import { NativeIngestStore } from "./native-ingest-store.js";
import { NativeIngestService, type NativeMemoryBinding } from "./native-ingest.js";
import { registerNativeIngestRoutes } from "./native-ingest-http.js";
import { NativeMemoryRecovery } from "./native-memory-recovery.js";
import { readNativeMemoryHistory } from "./native-memory-history.js";

type Api = Parameters<typeof registerNativeIngestRoutes>[0] & {
  registerService(service: { id: string; start(): Promise<void>; stop(): void | Promise<void> }): void;
};
/** Only deployment environment selects this file; never plugin/project/model input. */
export function composeNativeMemoryIngest(api: Api, recorder: MessageLifecycleRecorder, env: NodeJS.ProcessEnv = process.env): void {
  const file = env.OPENCLAW_NATIVE_MEMORY_INGEST_HOST_FILE;
  if (!file) return;
  if (!isAbsolute(file)) throw new Error("native_memory_host_file_invalid");
  const config = JSON.parse(readFileSync(file, "utf8")) as {
    version: string; storePath: string; capabilityEnv: string; bindings: NativeMemoryBinding[];
    recovery?: { sourceDatabasePath: string; storePath: string; intervalMs: number };
  };
  if (!config || Object.keys(config).some(k => !["version", "storePath", "capabilityEnv", "bindings", "recovery"].includes(k))
    || config.version !== "native-memory-host:v1" || typeof config.storePath !== "string" || !isAbsolute(config.storePath)
    || typeof config.capabilityEnv !== "string" || !/^[A-Z][A-Z0-9_]+$/.test(config.capabilityEnv)
    || !Array.isArray(config.bindings)) throw new Error("native_memory_host_config_invalid");
  const capability = env[config.capabilityEnv];
  if (!capability || Buffer.byteLength(capability) < 32) throw new Error("native_ingest_capability_invalid");
  const r = config.recovery;
  if (r !== undefined && (!r || typeof r !== "object" || Array.isArray(r))) throw new Error("native_memory_recovery_config_invalid");
  if (r && (Object.keys(r).some(k => !["sourceDatabasePath", "storePath", "intervalMs"].includes(k))
    || typeof r.sourceDatabasePath !== "string" || !isAbsolute(r.sourceDatabasePath)
    || typeof r.storePath !== "string" || !isAbsolute(r.storePath)
    || new Set([r.storePath, r.sourceDatabasePath, config.storePath].map(p => resolve(p).toLowerCase())).size !== 3
    || !Number.isSafeInteger(r.intervalMs) || r.intervalMs < 1000)) throw new Error("native_memory_recovery_config_invalid");
  const store = new NativeIngestStore(config.storePath);
  let recovery: NativeMemoryRecovery | undefined;
  try {
    const service = new NativeIngestService(store, config.bindings, recorder);
    if (r) recovery = new NativeMemoryRecovery(r.storePath, config.bindings.filter(b => b.historyNotBefore), service,
      b => readNativeMemoryHistory(r.sourceDatabasePath, b));
    const suppressed = env.OPENCLAW_GRAPHITI_SUPPRESS_EXTERNAL_STARTUP_PROBES === "true";
    let timer: ReturnType<typeof setTimeout> | undefined;
    let started = false, stopped = false, failed = false;
    registerNativeIngestRoutes(api, service, capability, recovery ? binding => ({ ...recovery!.status(binding),
      running: started && !stopped && !suppressed, suppressed, workerFailed: failed }) : undefined);
    const tick = () => {
      if (stopped || suppressed || !recovery) return;
      // Schedule next pass only after this one finishes. Shutdown drains before closing stores.
      void recovery.runOnce().catch(() => { failed = true; }).finally(() => {
        if (!stopped) { timer = setTimeout(tick, r!.intervalMs); timer.unref(); }
      });
    };
    api.registerService({ id: "native-memory-ingest", async start() {
      if (started || stopped) return;
      started = true;
      if (recovery && !suppressed) { timer = setTimeout(tick, 0); timer.unref(); }
    }, async stop() {
      stopped = true;
      if (timer) clearTimeout(timer);
      try { await recovery?.drain(); } finally { recovery?.close(); store.close(); }
    } });
  } catch (e) { recovery?.close(); store.close(); throw e; }
}
