import { DatabaseSync } from "node:sqlite";
import { createHash } from "node:crypto";
import type { NormalizedInboundMessage } from "./channel-adapters/types.js";
import type { GraphitiFailureCategory } from "./graphiti.js";

/** Bounded, sanitized evidence. No exception text, request payload or endpoint. */
export type NativeIngestFailure = {
  category: GraphitiFailureCategory | "authorization_unavailable_or_denied" | "unclassified";
  httpStatus?: number;
};

export type NativeIngestStatusV1 = {
  version: "native-memory-ingest:v1";
  requestRef: string;
  episodeUuid: string;
  state: "pending" | "queued" | "persisted" | "rejected" | "failed";
  diagnostic?: string;
  submissionFailure?: NativeIngestFailure;
  confirmationFailure?: NativeIngestFailure;
  retryDisposition: "safe" | "reconcile" | "none";
};
export type StoredIngest = {
  bindingRef: string; digest: string; message: NormalizedInboundMessage; receipt: NativeIngestStatusV1;
};
export function ingestDigest(value: unknown): string {
  const canonical = (v: unknown): unknown => v instanceof Date ? v.toISOString()
    : Array.isArray(v) ? v.map(canonical)
    : v && typeof v === "object" ? Object.fromEntries(Object.entries(v).filter(([, x]) => x !== undefined)
      .sort(([a], [b]) => a.localeCompare(b, "en")).map(([k, x]) => [k, canonical(x)])) : v;
  return createHash("sha256").update(JSON.stringify(canonical(value))).digest("hex");
}

/** Private host store. SQLite owns cross-process transactions and crash recovery. */
export class NativeIngestStore {
  private db: DatabaseSync;
  private closed = false;
  constructor(file: string) {
    this.db = new DatabaseSync(file);
    try {
      this.db.exec("PRAGMA synchronous=FULL; PRAGMA busy_timeout=0;");
      const version = this.db.prepare("PRAGMA user_version").get()?.user_version;
      if (version !== 0 && version !== 1) throw new Error("native_ingest_store_version");
      this.db.exec(`CREATE TABLE IF NOT EXISTS native_ingest (
        ref TEXT PRIMARY KEY, episode TEXT NOT NULL UNIQUE, binding TEXT NOT NULL,
        digest TEXT NOT NULL, payload TEXT NOT NULL, receipt TEXT NOT NULL, claimed INTEGER NOT NULL DEFAULT 0);
        PRAGMA user_version=1;`);
      if (this.db.prepare("PRAGMA quick_check").get()?.quick_check !== "ok") throw new Error("native_ingest_store_corrupt");
    } catch (e) { this.db.close(); throw e; }
  }
  stage(value: StoredIngest): StoredIngest {
    this.db.prepare("INSERT OR IGNORE INTO native_ingest(ref,episode,binding,digest,payload,receipt) VALUES(?,?,?,?,?,?)")
      .run(value.receipt.requestRef, value.receipt.episodeUuid, value.bindingRef, value.digest,
        JSON.stringify(value.message), JSON.stringify(value.receipt));
    const existing = this.get(value.receipt.requestRef);
    if (!existing || existing.digest !== value.digest || existing.bindingRef !== value.bindingRef)
      throw new Error("native_ingest_identity_conflict");
    return existing;
  }
  get(ref: string): StoredIngest | undefined {
    const row = this.db.prepare("SELECT * FROM native_ingest WHERE ref=?").get(ref);
    if (!row) return undefined;
    const message = JSON.parse(String(row.payload)) as NormalizedInboundMessage;
    message.referenceTime = new Date(message.referenceTime);
    const receipt = JSON.parse(String(row.receipt)) as NativeIngestStatusV1;
    if (ingestDigest(message) !== row.digest || receipt.requestRef !== row.ref || receipt.episodeUuid !== row.episode
      || receipt.version !== "native-memory-ingest:v1") throw new Error("native_ingest_store_corrupt");
    return { bindingRef: String(row.binding), digest: String(row.digest), message, receipt };
  }
  claim(ref: string): boolean {
    const receipt = this.get(ref)!.receipt;
    return this.db.prepare("UPDATE native_ingest SET claimed=1, receipt=? WHERE ref=? AND json_extract(receipt,'$.retryDisposition')='safe'")
      .run(JSON.stringify({ ...receipt, retryDisposition: "reconcile" }), ref).changes === 1;
  }
  save(receipt: NativeIngestStatusV1): void {
    this.db.prepare(`UPDATE native_ingest SET receipt=? WHERE ref=? AND episode=?
      AND NOT (json_extract(receipt,'$.state')='persisted' AND ? IN ('pending','queued'))`)
      .run(JSON.stringify(receipt), receipt.requestRef, receipt.episodeUuid, receipt.state);
  }
  close(): void { if (!this.closed) { this.closed = true; this.db.close(); } }
}
