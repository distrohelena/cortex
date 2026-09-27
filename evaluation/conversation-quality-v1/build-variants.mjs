// Offline candidates only: no runtime, transport, database or provider imports.
import { readFileSync } from 'node:fs';

const definitions = Object.freeze({
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
      matches[0].prompt += '\n\n' + clause;
    }
  }
  return result;
}
