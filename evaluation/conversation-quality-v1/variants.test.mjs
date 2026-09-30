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
  ['group-self-reference-v1', ['group-generation', 'group-regeneration']],
  ['group-unforced-voice-v1', ['group-generation', 'group-regeneration']],
  ['group-unforced-voice-v2', ['group-generation', 'group-regeneration']],
  ['group-unforced-voice-v4', ['group-generation', 'group-regeneration']],
  ['group-situation-grounded-v1', ['group-situation']],
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

test('consolidated situation replaces only approved group prompts and rejects changed sources', () => {
  const approved = JSON.parse(readFileSync(new URL('../../docs/manual-group-live-project-v3-conversation-repair-v1.json', import.meta.url), 'utf8'));
  const before = structuredClone(approved);
  const result = createVariant(approved, 'group-situation-grounded-v2');
  let changed = 0;
  for (let s = 0; s < approved.project.states.length; s++) {
    for (let n = 0; n < approved.project.states[s].graph.nodes.length; n++) {
      const original = approved.project.states[s].graph.nodes[n];
      if (original.id !== 'group-situation') continue;
      assert.ok(result.project.states[s].graph.nodes[n].prompt.length < original.prompt.length);
      result.project.states[s].graph.nodes[n].prompt = original.prompt;
      changed++;
    }
  }
  assert.equal(changed, 3);
  assert.deepEqual(result, before);
  assert.deepEqual(approved, before);
  const changedSource = structuredClone(approved);
  changedSource.project.states[1].graph.nodes.find(n => n.id === 'group-situation').prompt += '\nA newly approved requirement.';
  assert.throws(() => createVariant(changedSource, 'group-situation-grounded-v2'), /Unapproved situation source/);
  assert.throws(() => createVariant(createVariant(approved, 'group-situation-grounded-v2'), 'group-situation-grounded-v2'));
});

test('consolidated voice removes only the two exact redundant style clauses', () => {
  const approved = JSON.parse(readFileSync(new URL('../../docs/manual-group-live-project-v3-conversation-repair-v1.json', import.meta.url), 'utf8'));
  const before = structuredClone(approved);
  const result = createVariant(approved, 'group-unforced-voice-v3');
  const natural = readFileSync(new URL('../../docs/natural-conversation-clause-v1.txt', import.meta.url), 'utf8').trim();
  const voice = readFileSync(new URL('./clauses/voice-v1.txt', import.meta.url), 'utf8').trim();
  const replacement = readFileSync(new URL('./clauses/group-unforced-voice-v3.txt', import.meta.url), 'utf8').trim();
  let count = 0;
  for (let s = 0; s < approved.project.states.length; s++) {
    for (let n = 0; n < approved.project.states[s].graph.nodes.length; n++) {
      const original = approved.project.states[s].graph.nodes[n];
      if (!['group-generation', 'group-regeneration'].includes(original.id)) continue;
      const changed = result.project.states[s].graph.nodes[n];
      assert.equal(changed.prompt, original.prompt.replace(natural, replacement).replace('\n\n' + voice, ''));
      assert.ok(changed.prompt.length < original.prompt.length);
      changed.prompt = original.prompt;
      count++;
    }
  }
  assert.equal(count, 6);
  assert.deepEqual(result, before);
  assert.deepEqual(approved, before);
  const malformed = structuredClone(approved);
  malformed.project.states[0].graph.nodes.find(n => n.id === 'group-generation').prompt += '\n\n' + natural;
  assert.throws(() => createVariant(malformed, 'group-unforced-voice-v3'), /exactly once/);
  assert.throws(() => createVariant(createVariant(approved, 'group-unforced-voice-v3'), 'group-unforced-voice-v3'));
});

test('unforced group voice preserves the approved repair and changes only six generation prompts', () => {
  const approved = JSON.parse(readFileSync(new URL('../../docs/manual-group-live-project-v3-conversation-repair-v1.json', import.meta.url), 'utf8'));
  const before = structuredClone(approved);
  const result = createVariant(approved, 'group-unforced-voice-v1');
  const clause = readFileSync(new URL('./clauses/group-unforced-voice-v1.txt', import.meta.url), 'utf8').trim();
  let changed = 0;
  for (let s = 0; s < approved.project.states.length; s++) {
    for (let n = 0; n < approved.project.states[s].graph.nodes.length; n++) {
      const original = approved.project.states[s].graph.nodes[n];
      if (!['group-generation', 'group-regeneration'].includes(original.id)) continue;
      assert.equal(result.project.states[s].graph.nodes[n].prompt, original.prompt + '\n\n' + clause);
      result.project.states[s].graph.nodes[n].prompt = original.prompt;
      changed++;
    }
  }
  assert.equal(changed, 6);
  assert.deepEqual(result, before);
  assert.deepEqual(approved, before);
  assert.throws(() => createVariant(createVariant(approved, 'group-unforced-voice-v1'), 'group-unforced-voice-v1'), /already applied/);
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
