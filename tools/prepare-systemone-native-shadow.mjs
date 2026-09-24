// Operator-only bridge configuration. Read-only status; no arm, poll, MCP or sender.
import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { resolve, dirname, join } from 'node:path';

const [operation, baseFile, profileFile, ...extra] = process.argv.slice(2);
assert(operation && baseFile && profileFile && !extra.length);
const root = resolve(operation);
assert(/^C:\\Users\\Helena\\\.config\\openclaw-observation-ops-[\w-]+$/.test(root));
const read = async file => JSON.parse(await readFile(file, 'utf8'));
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
const host = await read(join(root, 'observation-host.next.json'));
const before = await read(join(root, 'observation-host.before.json'));
const manual = await read(resolve(baseFile));
const profile = await read(resolve(profileFile));
assert.equal(manual.session.channelId, 'whatsapp');
assert.equal(manual.session.accountId, host.accountId);
assert.equal(manual.session.nativeConversationId, before.nativeSession);
assert.equal(manual.peerActorId, 'whatsapp:' + before.nativeSession);
assert.equal(profile.version, 'systemone-participation:v1');
assert.equal(profile.model, 'jev-1.13.0');
manual.projectFile = resolve(dirname(resolve(baseFile)), manual.projectFile);
assert.equal(hash(await readFile(manual.projectFile)).toUpperCase(), manual.expectedProjectSha256);
const sessionRef = hash(JSON.stringify(['observation', JSON.stringify(['whatsapp', hash(host.accountId).slice(0, 24)]), host.nativeSession]));
const endpoint = `http://127.0.0.1:${host.port}`;
const response = await fetch(`${endpoint}/inbound-observation/v1/status?session=${sessionRef}`, {
  headers: { Authorization: 'Bearer ' + host.readerToken }, redirect: 'error', signal: AbortSignal.timeout(10000)
});
assert(response.ok);
const status = await response.json();
assert.equal(status.version, 'inbound-observation:v1');
assert.equal(status.sessionRef, sessionRef);
assert.equal(status.captureState, 'armed');
assert.equal(status.journalHealth, 'healthy');
assert.equal(status.dispatch.status, 'inactive');
const proof = status.bootstrap.effectiveSnapshotProof;
assert.equal(proof.status, 'valid');
assert.equal(proof.version, 'effective-runtime-config:v1');
assert.equal(status.bootstrap.isolationGate.approvedEffectiveConfigRevision, proof.revision);
assert.equal(status.bootstrap.isolationGate.lifecycleState, 'armed');
const revision = status.dispatch.effectiveConfigRevision;
assert(revision && revision === status.bootstrap.effectiveConfigRevision);
assert(status.selfIdentityRef);
const directory = join(root, 'jev-runtime');
await mkdir(directory); // Never overwrite/reuse a previous window.
await mkdir(join(directory, 'journal'));
manual.journalDirectory = join(directory, 'journal');
manual.systemOneParticipation = profile;
const save = (name, value) => writeFile(join(directory, name), JSON.stringify(value, null, 2) + '\n', { flag: 'wx', mode: 0o600 });
await save('manual-shadow.private.json', manual);
await save('native-shadow.json', {
  manualConfigFile: 'manual-shadow.private.json', observationEndpoint: endpoint,
  observationTokenEnvironmentVariable: 'FILOMENA_NATIVE_OBSERVATION_READER_TOKEN',
  observationSessionRef: sessionRef, nativeConversationId: host.nativeSession,
  approvedRevision: revision, journalDirectory: join(directory, 'journal'), maxInteractions: 1
});
console.log(JSON.stringify({ prepared: true, mode: 'shadow', maxInteractions: 1, transportSender: false, projectPinUnchanged: true }));
