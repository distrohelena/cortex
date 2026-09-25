import { DatabaseSync } from "node:sqlite";
import type { NormalizedInboundMessage } from "./channel-adapters/types.js";
import { ingestDigest } from "./native-ingest-store.js";
import type { NativeIngestService, NativeMemoryBinding } from "./native-ingest.js";

export type MemoryHistoryPage = {
  messages: NormalizedInboundMessage[];
  gaps: string[];
  skippedSelf: number;
  unsupported: number;
};
export type MemoryHistorySource = (binding: NativeMemoryBinding) => Promise<MemoryHistoryPage>;

/** Memory-only recovery. It deliberately has no runtime, sender or conversation cursor. */
export class NativeMemoryRecovery {
  private readonly db: DatabaseSync;
  private readonly bindings: NativeMemoryBinding[];
  private running?: Promise<void>;
  private closed = false;
  constructor(file: string, bindings: NativeMemoryBinding[], private readonly writer: NativeIngestService,
    private readonly source: MemoryHistorySource) {
    this.bindings = structuredClone(bindings);
    if (!bindings.length || new Set(bindings.map(b => b.bindingRef)).size !== bindings.length
      || bindings.some(b => b.conversation.kind !== "group" || !b.historyNotBefore))
      throw new Error("history_authority_required");
    this.db = new DatabaseSync(file);
    try {
      this.db.exec(`PRAGMA synchronous=FULL; PRAGMA busy_timeout=0;
        CREATE TABLE IF NOT EXISTS recovery_groups(binding TEXT PRIMARY KEY, authority TEXT NOT NULL, status TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS recovery_items(binding TEXT NOT NULL, ordinal INTEGER NOT NULL, native_id TEXT NOT NULL,
          digest TEXT NOT NULL, payload TEXT NOT NULL, persisted INTEGER NOT NULL DEFAULT 0,
          PRIMARY KEY(binding, native_id), UNIQUE(binding, ordinal));`);
      for (const b of this.bindings) {
        const authority = ingestDigest(b);
        this.db.prepare("INSERT OR IGNORE INTO recovery_groups VALUES(?,?,?)")
          .run(b.bindingRef, authority, JSON.stringify({ coverage: "partial", diagnostic: "not_scanned", gaps: [] }));
        if (this.db.prepare("SELECT authority FROM recovery_groups WHERE binding=?").get(b.bindingRef)?.authority !== authority)
          throw new Error("history_authority_changed");
      }
      if (this.db.prepare("PRAGMA quick_check").get()?.quick_check !== "ok") throw new Error("history_store_corrupt");
    } catch (e) { this.db.close(); throw e; }
  }
  status(bindingRef: string) {
    if (!this.bindings.some(b => b.bindingRef === bindingRef)) throw new Error("history_binding_invalid");
    const count = this.db.prepare(`SELECT COUNT(*) AS discovered, COALESCE(SUM(persisted),0) AS persisted,
      COALESCE(MIN(CASE WHEN persisted=0 THEN ordinal END)-1, MAX(ordinal), 0) AS cursor
      FROM recovery_items WHERE binding=?`).get(bindingRef)!;
    const state = JSON.parse(String(this.db.prepare("SELECT status FROM recovery_groups WHERE binding=?").get(bindingRef)!.status));
    return { ...state, ...count, pending: Number(count.discovered) - Number(count.persisted) };
  }
  runOnce(): Promise<void> {
    if (this.closed) return Promise.reject(new Error("history_store_closed"));
    return this.running ??= this.run().finally(() => { this.running = undefined; });
  }
  private async run(): Promise<void> {
    for (const binding of this.bindings) {
      try {
        const page = await this.source(binding);
        const messages = page.messages;
        for (const m of messages) {
          if (ingestDigest(m.conversation) !== ingestDigest(binding.conversation)) throw new Error("history_binding_invalid");
          if (!m.providerMessageId?.trim() || !(m.referenceTime instanceof Date) || !Number.isFinite(m.referenceTime.getTime()))
            throw new Error("history_message_invalid");
        }
        // Discover every retained identity, including late-arriving old timestamps. The memory
        // checkpoint tracks durable discovery order, not provider time or conversation execution.
        this.db.exec("BEGIN IMMEDIATE");
        try {
          for (const m of [...messages].sort((a, b) => a.referenceTime.getTime() - b.referenceTime.getTime()
            || a.providerMessageId.localeCompare(b.providerMessageId))) {
            if (m.referenceTime.getTime() < Date.parse(binding.historyNotBefore!)) continue;
            const digest = ingestDigest(m);
            const existing = this.db.prepare("SELECT digest FROM recovery_items WHERE binding=? AND native_id=?")
              .get(binding.bindingRef, m.providerMessageId);
            if (existing) { if (existing.digest !== digest) throw new Error("history_identity_conflict"); continue; }
            this.db.prepare(`INSERT INTO recovery_items(binding,ordinal,native_id,digest,payload)
              SELECT ?,COALESCE(MAX(ordinal),0)+1,?,?,? FROM recovery_items WHERE binding=?`)
              .run(binding.bindingRef, m.providerMessageId, digest, JSON.stringify(m), binding.bindingRef);
          }
          this.db.exec("COMMIT");
        } catch (e) { this.db.exec("ROLLBACK"); throw e; }
        const diagnostic = await this.reconcilePending(binding);
        this.saveStatus(binding.bindingRef, { coverage: "partial", gaps: page.gaps,
          skippedSelf: page.skippedSelf, unsupported: page.unsupported, diagnostic });
      } catch (e) {
        const known = new Set(["history_binding_invalid", "history_message_invalid", "history_identity_conflict", "history_store_corrupt",
          "history_integrity_failure", "history_identity_mapping_invalid", "history_scan_limit_reached"]);
        const diagnostic = e instanceof Error && known.has(e.message) ? e.message : "history_source_unavailable";
        // Safe durable work is independent of the source remaining available. Integrity/binding
        // errors instead stop this group, retaining both the evidence and its old checkpoint.
        if (diagnostic === "history_source_unavailable") await this.reconcilePending(binding);
        this.saveStatus(binding.bindingRef, { coverage: "partial", diagnostic });
      }
    }
  }
  private async reconcilePending(binding: NativeMemoryBinding): Promise<string | undefined> {
    let diagnostic: string | undefined;
    const last = Number(this.status(binding.bindingRef).lastVisited ?? 0);
    const rows = this.db.prepare(`SELECT * FROM recovery_items WHERE binding=? AND persisted=0
      ORDER BY CASE WHEN ordinal>? THEN 0 ELSE 1 END, ordinal LIMIT 16`).all(binding.bindingRef, last);
    // Bounded round-robin prevents unresolved early work starving newer arrivals.
    for (const row of rows) {
      const message = JSON.parse(String(row.payload)) as NormalizedInboundMessage;
      message.referenceTime = new Date(message.referenceTime);
      if (ingestDigest(message) !== row.digest) throw new Error("history_store_corrupt");
      const request = { version: "native-memory-ingest:v1" as const, bindingRef: binding.bindingRef, message };
      try {
        let receipt = await this.writer.submitHistorical(request);
        if (receipt.state !== "persisted" && receipt.retryDisposition !== "safe")
          receipt = await this.writer.reconcile(binding.bindingRef, receipt.requestRef);
        if (receipt.state === "persisted") this.db.prepare("UPDATE recovery_items SET persisted=1 WHERE binding=? AND ordinal=?")
          .run(binding.bindingRef, row.ordinal);
        else diagnostic = "ingestion_not_yet_confirmed";
      } catch { diagnostic = "history_ingestion_failed"; }
      this.saveStatus(binding.bindingRef, { lastVisited: row.ordinal });
    }
    return diagnostic;
  }
  private saveStatus(binding: string, state: Record<string, unknown>) {
    const previous = JSON.parse(String(this.db.prepare("SELECT status FROM recovery_groups WHERE binding=?").get(binding)!.status));
    this.db.prepare("UPDATE recovery_groups SET status=? WHERE binding=?").run(JSON.stringify({ ...previous, ...state }), binding);
  }
  async drain(): Promise<void> { if (this.running) await this.running; }
  close(): void {
    if (this.running) throw new Error("history_recovery_still_running");
    if (!this.closed) { this.closed = true; this.db.close(); }
  }
}
