import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createVariant } from './build-variants.mjs';

const baseline = JSON.parse(readFileSync(new URL('../../docs/manual-group-live-project-v3-recipient-v1.json', import.meta.url), 'utf8'));
const variants = [
  ['context-v1', ['dm-situation', 'group-situation']],
  ['participation-v1', ['group-presence']],
  ['repetition-v1', ['dm-generation', 'group-generation', 'dm-regeneration', 'group-regeneration']],
  ['voice-v1', ['dm-generation', 'group-generation', 'dm-regeneration', 'group-regeneration']],
];

for (const [id, targetIds] of variants) {
  test(`${id} changes only intended node prompts in every state, without mutating baseline`, () => {
    const before = JSON.stringify(baseline);
    const result = createVariant(baseline, id);
    assert.equal(JSON.stringify(baseline), before);
    assert.notStrictEqual(result, baseline);
    let changed = 0;
    for (let s = 0; s < baseline.project.states.length; s++) {
      const originalNodes = baseline.project.states[s].graph.nodes;
      const nodes = result.project.states[s].graph.nodes;
      for (let n = 0; n < originalNodes.length; n++) {
        if (targetIds.includes(originalNodes[n].id)) {
          assert.ok(nodes[n].prompt.startsWith(originalNodes[n].prompt + '\n\n'));
          assert.ok(nodes[n].prompt.length > originalNodes[n].prompt.length + 80);
          nodes[n].prompt = originalNodes[n].prompt;
          changed++;
        }
      }
    }
    assert.equal(changed, targetIds.length * 3);
    assert.deepEqual(result, baseline); // edges, FSM, models, schemas, history and personality unchanged
  });
}

test('unknown variant cannot silently produce an unchanged candidate', () => {
  assert.throws(() => createVariant(baseline, 'typo'), /Unknown variant/);
});

test('missing target in one state fails rather than producing a partially modified candidate', () => {
  const input = structuredClone(baseline);
  input.project.states[1].graph.nodes = input.project.states[1].graph.nodes.filter(n => n.id !== 'group-presence');
  assert.throws(() => createVariant(input, 'participation-v1'), /Target/);
});

test('duplicate target fails rather than modifying an ambiguous graph', () => {
  const input = structuredClone(baseline);
  input.project.states[0].graph.nodes.push(structuredClone(input.project.states[0].graph.nodes.find(n => n.id === 'group-presence')));
  assert.throws(() => createVariant(input, 'participation-v1'), /Target/);
});

test('already applied variant is rejected to avoid duplicate prompt clauses', () => {
  assert.throws(() => createVariant(createVariant(baseline, 'context-v1'), 'context-v1'), /already applied/);
});
