// Explicit host/operator-only recovery. Read native metadata, never transcript text.
// Run only after normal provisioning. Original configs and native records remain unchanged.
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
const [window, source] = process.argv.slice(2);
assert(window && source && path.isAbsolute(window) && path.isAbsolute(source));
const read = (root, name) => JSON.parse(fs.readFileSync(path.join(root, name), 'utf8'));
const host = read(window, 'observation-host.next.json');
const native = read(window, 'native-live-observation.json');
const live = read(window, 'native-live-host.json');
const opened = read(source, 'open.private.json');
const after = opened.cursor;
assert.equal(after.version, 'inbound-observation:v1');
assert.equal(after.sessionRef, native.observationSessionRef);
assert(Number.isSafeInteger(after.sequence) && after.sequence >= 0);
async function get(operation, args = {}) {
  const url = new URL(`http://127.0.0.1:${host.port}/inbound-observation/v1/${operation}`);
  url.searchParams.set('session', native.observationSessionRef);
  for (const [key, value] of Object.entries(args)) url.searchParams.set(key, typeof value === 'string' ? value : JSON.stringify(value));
  const response = await fetch(url, { headers: {Authorization: 'Bearer ' + host.readerToken}, redirect: 'error' });
  assert(response.ok, 'native_read_failed');
  return response.json();
}
const status = await get('status');
assert.equal(status.dispatch.status, 'inactive');
assert.equal(status.dispatch.effectiveConfigRevision, native.approvedRevision);
assert.equal(status.bootstrap.effectiveSnapshotProof.status, 'valid');
assert.equal(status.captureState, 'armed');
const batch = await get('poll', {revision: native.approvedRevision, cursor: after});
assert.equal(batch.status, 'healthy');
assert.equal(batch.epochClosed, true);
assert.equal(batch.nextCursor.epoch, after.epoch);
assert.equal(batch.nextCursor.sessionRef, after.sessionRef);
assert(batch.events.length > 0 && batch.events.length <= 20);
assert.equal(batch.nextCursor.sequence - after.sequence, batch.events.length);
for (const [index, event] of batch.events.entries()) {
  assert.equal(event.sequence, after.sequence + index + 1);
  assert.equal(event.sessionRef, after.sessionRef);
  assert.equal(event.journalEpoch, after.epoch);
  assert(event.nativeMessageId);
}
native.recovery = {after, throughSequence: batch.nextCursor.sequence};
// This operator command is authorized for ONE pending message / ONE real reply.
assert.equal(batch.events.length, 1, 'single_recovery_authorization_only');
assert.equal(batch.events[0].fromMe, false, 'peer_message_required');
const originalEvidence = fs.readFileSync(path.resolve(window, live.capabilityEvidenceFile));
assert.equal(createHash('sha256').update(originalEvidence).digest('hex').toUpperCase(), live.capabilityEvidenceSha256);
const proof = JSON.parse(originalEvidence);
assert.equal(proof.maxInteractions, live.maxInteractions);
native.maxInteractions = live.maxInteractions = proof.maxInteractions = 1;
const narrowedEvidence = Buffer.from(JSON.stringify(proof, null, 2));
live.capabilityEvidenceFile = path.join(window, 'recovery-capability-evidence.json');
live.capabilityEvidenceSha256 = createHash('sha256').update(narrowedEvidence).digest('hex').toUpperCase();
fs.writeFileSync(live.capabilityEvidenceFile, narrowedEvidence, {flag: 'wx'});
live.nativeConfigFile = path.join(window, 'native-recovery.json');
fs.writeFileSync(live.nativeConfigFile, JSON.stringify(native, null, 2), {flag: 'wx'});
fs.writeFileSync(path.join(window, 'live-recovery.json'), JSON.stringify(live, null, 2), {flag: 'wx'});
console.log(JSON.stringify({recoveryPrepared: true, nativeRecords: batch.events.length,
  after: after.sequence, through: batch.nextCursor.sequence, sourceEpochClosed: true, maxInteractions: 1, sendCalls: 0}));
