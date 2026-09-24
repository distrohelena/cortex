// Operator-only artifact generation. Reads metadata/alias files; never calls services.
// Destination must already be private. No existing file is overwritten.
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { createHash } from "node:crypto";
import { DatabaseSync } from "node:sqlite";

const [baseFile, databaseFile, authDirectory, sessionKey, projectFile, outputDirectory, ...extra] = process.argv.slice(2);
assert(baseFile && databaseFile && authDirectory && sessionKey && projectFile && outputDirectory && !extra.length);
const read = file => JSON.parse(fs.readFileSync(file, "utf8"));
const host = read(baseFile);
const output = path.resolve(outputDirectory);
assert(fs.statSync(output).isDirectory());
const project = path.resolve(projectFile);
assert.equal(createHash("sha256").update(fs.readFileSync(project)).digest("hex").toUpperCase(),
  "A3B066C5B05B3D28E1B429CE0D900C38C8FF82D892429C3D2E42A4E2B15AAF97");
const db = new DatabaseSync(databaseFile, { readOnly: true });
let entry, rows;
try {
  entry = JSON.parse(db.prepare("SELECT entry_json FROM session_nodes WHERE session_key=?").get(sessionKey).entry_json);
  rows = db.prepare("SELECT identity_namespace,actor_id FROM session_participants WHERE session_key=?").all(sessionKey);
} finally { db.close(); }
const group = entry.groupId;
assert(/^\d+(?:-\d+)?@g\.us$/.test(group));
assert.equal(entry.chatType, "group");
assert.equal(entry.delivery.kind, "external");
assert.equal(entry.delivery.route.channel, "whatsapp");
assert.equal(entry.delivery.route.accountId, host.session.accountId);
assert.equal(entry.delivery.context.to, group);
assert.equal(entry.delivery.context.accountId, host.session.accountId);
assert.equal(entry.delivery.origin.chatType, "group");
const namespace = "whatsapp-group-" + group.replace(/[^a-z0-9_-]+/g, "-").replace(/-+/g, "-").replace(/^-|-$/g, "");
const drift = host.graphitiProbes.find(p => p.foreignCanary === "DRIFT42");
const plex = host.graphitiProbes.find(p => p.foreignCanary === "PLEX73");
assert(drift && plex);
assert.equal(drift.foreignNamespace, namespace);
assert.equal(drift.localCanary, "PRIVATE91");
assert.equal(host.selfActorIds.length, 1);
const participants = {};
const peers = new Set();
const clean = value => String(value).replace(/^\+/, "").replace(/@(s\.whatsapp\.net|lid)$/, "");
for (const row of rows) {
  const origin = JSON.parse(row.identity_namespace);
  assert.equal(origin.type, "observation");
  assert.equal(origin.pluginId, "whatsapp");
  assert.equal(origin.accountId, host.session.accountId);
  assert(/^\+\d+$/.test(row.actor_id));
  const pn = clean(row.actor_id);
  const actor = `whatsapp:${pn}@s.whatsapp.net`;
  if (host.selfActorIds.includes(actor)) continue;
  const lid = clean(read(path.join(authDirectory, `lid-mapping-${pn}.json`)));
  assert(/^\d+$/.test(lid));
  assert.equal(clean(read(path.join(authDirectory, `lid-mapping-${lid}_reverse.json`))), pn);
  for (const alias of [`${pn}@s.whatsapp.net`, `${lid}@lid`]) {
    assert(!participants[alias] || participants[alias] === actor);
    participants[alias] = actor;
  }
  peers.add(actor);
}
assert(peers.size > 0 && peers.has(host.peerActorId));
const controls = [
  { foreignNamespace: host.conversationNamespace, foreignCanary: "PRIVATE91" },
  { foreignNamespace: plex.foreignNamespace, foreignCanary: "PLEX73" },
];
host.projectFile = project;
host.projectVersion = 3;
host.expectedProjectSha256 = createHash("sha256").update(fs.readFileSync(project)).digest("hex").toUpperCase();
host.session = { ...host.session, nativeConversationId: group };
host.conversationNamespace = namespace;
host.peerDisplayName = "";
host.journalDirectory = output;
host.outboundBindings = {
  recallNodes: ["group-response-boundaries"], compilerNodes: ["group-compiled-boundaries"],
  candidatePorts: { "group-generation": "texto", "group-regeneration": "texto" },
  enforcementNodes: ["group-validator-1", "group-validator-2"], wouldTransitionTo: "participation-engaged-v1",
};
host.graphitiProbes = [...peers].sort().flatMap(participantStableId => controls.map(control => ({
  conversationNamespace: namespace, participantStableId, localCanary: "DRIFT42", ...control,
})));
for (const [name, document] of [["manual.private.json", host], ["participants.private.json", participants]]) {
  fs.writeFileSync(path.join(output, name), JSON.stringify(document, null, 2) + "\n", { flag: "wx", mode: 0o600 });
}
console.log(JSON.stringify({ status: "prepared", participants: peers.size, aliases: Object.keys(participants).length,
  probes: host.graphitiProbes.length, transportAvailable: false, memoryWrites: 0, preflightOnly: true }));
