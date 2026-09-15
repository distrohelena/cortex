// Operator artifact preparation only; never arms capture or invokes a model.
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { createHash } from "node:crypto";

const [root, manualFile, journalDirectory] = process.argv.slice(2);
assert(root && manualFile && journalDirectory);
const read = file => JSON.parse(fs.readFileSync(file, "utf8"));
const before = read(path.join(root, "observation-host.before.json"));
const host = read(path.join(root, "observation-host.next.json"));
const binding = read(path.join(root, "native-session-binding.json"));
const preflight = read(path.join(root, "preflight.json"));
const manual = read(manualFile);
assert.equal(before.nativeSession, manual.session.nativeConversationId);
assert.equal(host.accountId, manual.session.accountId);
assert.equal(manual.peerActorId, "whatsapp:" + before.nativeSession);
assert.equal(binding.reverseVerified, true);
const hash = s => createHash("sha256").update(s).digest("hex");
assert.equal(binding.lidRef, hash(host.nativeSession.split("@")[0]));
assert.equal(binding.pnRef, hash(before.nativeSession.split("@")[0]));
assert.equal(binding.accountRef, hash(host.accountId));
assert.equal(preflight.afterIsolation.dispatch.status, "inactive");
assert.equal(preflight.afterIsolation.bootstrap.effectiveSnapshotProof.status, "valid");
assert.equal(preflight.afterIsolation.bootstrap.effectiveSnapshotProof.revision,
  preflight.afterIsolation.bootstrap.isolationGate.approvedEffectiveConfigRevision);
assert(fs.statSync(journalDirectory).isDirectory());
const config = {
  manualConfigFile: path.resolve(manualFile),
  observationEndpoint: "http://127.0.0.1:" + host.port,
  observationTokenEnvironmentVariable: "FILOMENA_NATIVE_OBSERVATION_READER_TOKEN",
  observationSessionRef: preflight.sessionRef,
  nativeConversationId: host.nativeSession,
  approvedRevision: preflight.afterIsolation.dispatch.effectiveConfigRevision,
  journalDirectory: path.resolve(journalDirectory),
  maxInteractions: 5,
};
assert(config.approvedRevision);
fs.writeFileSync(path.join(root, "native-shadow-host.json"), JSON.stringify(config, null, 2), { flag: "wx" });
console.log(JSON.stringify({ hostDocumentPrepared: true, maxInteractions: 5, credentialWritten: false }));
