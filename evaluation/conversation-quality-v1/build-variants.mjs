// Offline candidates only: no runtime, transport, database or provider imports.
import { readFileSync } from 'node:fs';
import { createHash } from 'node:crypto';

const definitions = Object.freeze({
  'group-situation-grounded-v2': { file: 'group-situation-grounded-v2.txt', targets: ['group-situation'], sourceHash: 'f79203b99d65283596afe310579f59cf2a0c7ae4579923427dd77fd7facbd39a' },
  'group-situation-grounded-v1': { file: 'group-situation-grounded-v1.txt', targets: ['group-situation'] },
  'group-unforced-voice-v4': { file: 'group-unforced-voice-v4.txt', targets: ['group-generation', 'group-regeneration'] },
  'group-unforced-voice-v3': { file: 'group-unforced-voice-v3.txt', targets: ['group-generation', 'group-regeneration'], consolidate: true },
  'group-unforced-voice-v2': { file: 'group-unforced-voice-v2.txt', targets: ['group-generation', 'group-regeneration'] },
  'group-unforced-voice-v1': { file: 'group-unforced-voice-v1.txt', targets: ['group-generation', 'group-regeneration'] },
  'group-self-reference-v1': { file: 'group-self-reference-v1.txt', targets: ['group-generation', 'group-regeneration'] },
  'context-v1': { file: 'context-v1.txt', targets: ['dm-situation', 'group-situation'] },
  'participation-v1': { file: 'participation-v1.txt', targets: ['group-presence'] },
  'repetition-v1': { file: 'repetition-v1.txt', targets: ['dm-generation', 'group-generation', 'dm-regeneration', 'group-regeneration'] },
  'voice-v1': { file: 'voice-v1.txt', targets: ['dm-generation', 'group-generation', 'dm-regeneration', 'group-regeneration'] },
});

export function createVariant(document, variantId) {
  if (!Object.hasOwn(definitions, variantId)) throw new Error('Unknown variant');
  const definition = definitions[variantId];
  if (document?.format !== 'filomena-local-project' || !Array.isArray(document.project?.states) || !document.project.states.length)
    throw new Error('Invalid local project');
  const clause = readFileSync(new URL(`./clauses/${definition.file}`, import.meta.url), 'utf8').trim();
  const result = structuredClone(document);
  for (const state of result.project.states) {
    if (!Array.isArray(state.graph?.nodes)) throw new Error('Invalid graph');
    for (const id of definition.targets) {
      const matches = state.graph.nodes.filter(node => node.id === id);
      if (matches.length !== 1 || matches[0].kind !== 'prompt' || typeof matches[0].prompt !== 'string')
        throw new Error('Target absent, ambiguous or not a prompt');
      if (matches[0].prompt.includes(clause)) throw new Error('Variant already applied');
      if (definition.sourceHash) {
        if (createHash('sha256').update(matches[0].prompt).digest('hex') !== definition.sourceHash)
          throw new Error('Unapproved situation source');
        matches[0].prompt = clause;
      } else if (definition.consolidate) {
        const natural = readFileSync(new URL('../../docs/natural-conversation-clause-v1.txt', import.meta.url), 'utf8').trim();
        const voice = readFileSync(new URL('./clauses/voice-v1.txt', import.meta.url), 'utf8').trim();
        if (matches[0].prompt.split(natural).length !== 2 || matches[0].prompt.split('\n\n' + voice).length !== 2)
          throw new Error('Original style clauses must occur exactly once');
        matches[0].prompt = matches[0].prompt.replace(natural, clause).replace('\n\n' + voice, '');
      } else matches[0].prompt += '\n\n' + clause;
    }
  }
  return result;
}
