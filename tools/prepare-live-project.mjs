// Mechanical, local artifact derivation. Never edits the frozen source or host config.
import { readFile, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { resolve } from 'node:path';

const source = resolve('docs/manual-shadow-project-v3.json');
const target = resolve('docs/manual-live-project-v3-continuity.json');
const bytes = await readFile(source);
const hash = value => createHash('sha256').update(value).digest('hex').toUpperCase();
if (hash(bytes) !== '47D1D4521BCF013E919DDCB3DE5900485DDA7F8BACEFA7437FEDFBDFFC9BC55E')
  throw new Error('Frozen project pin mismatch');
const document = JSON.parse(bytes);
const sources = new Set(['dm-transition-responder-shadow-send', 'dm-accepted-1-shadow-send', 'dm-accepted-2-shadow-send']);
let changed = 0;
for (const state of document.project.states) {
  const history = state.graph.nodes.find(n => n.id === 'dm-history');
  if (history?.settings.maxAgeMinutes !== 15 || history.settings.maxMessages !== 20 || history.settings.maxCharacters !== 5000)
    throw new Error('Unexpected DM history policy');
  history.settings.maxAgeMinutes = null;
  for (const edge of state.graph.controlEdges) {
    if (!sources.has(edge.source)) continue;
    if (edge.sourcePort !== 'shadowed' || edge.targetPort !== 'exec') throw new Error('Unexpected send edge');
    edge.sourcePort = 'sent';
    changed++;
  }
}
if (changed !== 9) throw new Error('Unexpected edge count');
const result = JSON.stringify(document, null, 2) + '\n';
await writeFile(target, result, { flag: 'wx' });
console.log(JSON.stringify({ changedEdges: changed, projectSha256: hash(result), sourceUnchanged: true }));
