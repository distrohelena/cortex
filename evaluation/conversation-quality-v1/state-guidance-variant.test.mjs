import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createStateGuidanceDiagnostic } from './state-guidance-variant.mjs';

const baseline = JSON.parse(readFileSync(new URL('./group-luna-summary-v1.project.json', import.meta.url), 'utf8'));
const targets = new Set(['group-generation', 'group-regeneration']);

test('diagnostic removes only group generation/regeneration state guidance in all three states', () => {
  const before = structuredClone(baseline);
  const candidate = createStateGuidanceDiagnostic(baseline);
  let changed = 0;
  for (const state of candidate.project.states) {
    for (const node of state.graph.nodes) {
      if (!targets.has(node.id)) continue;
      assert.equal(node.useStateGuidance, false);
      node.useStateGuidance = true;
      changed++;
    }
  }
  assert.equal(changed, 6);
  assert.deepEqual(candidate, before, 'Everything except six explicit flags must remain unchanged');
  assert.deepEqual(baseline, before, 'Source must not be mutated');
});

for (const kind of ['missing', 'duplicate', 'already-disabled', 'wrong-kind', 'extra-state']) {
  test(`rejects unreviewed source shape: ${kind}`, () => {
    const source = structuredClone(baseline);
    const nodes = source.project.states[0].graph.nodes;
    const node = nodes.find(n => n.id === 'group-generation');
    if (kind === 'missing') nodes.splice(nodes.indexOf(node), 1);
    if (kind === 'duplicate') nodes.push(structuredClone(node));
    if (kind === 'already-disabled') node.useStateGuidance = false;
    if (kind === 'wrong-kind') node.kind = 'tool';
    if (kind === 'extra-state') source.project.states.push(structuredClone(source.project.states[0]));
    assert.throws(() => createStateGuidanceDiagnostic(source), /Unreviewed/);
  });
}
