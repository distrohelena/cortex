// Local artifact generation only. Does not load runtime, providers or host config.
import { readFileSync, writeFileSync } from 'node:fs';

const root = new URL('./', import.meta.url);
const read = name => readFileSync(new URL(name, root), 'utf8');
const write = (name, doc) => writeFileSync(new URL(name, root), JSON.stringify(doc, null, 2) + '\n', 'utf8');
const project = JSON.parse(read('manual-group-live-project-v3-natural-v1.json'));
const translations = [
  ['mention', 'menção'],
  ['reply à sua mensagem', 'resposta vinculada à sua mensagem'],
  ['reply formal', 'resposta formalmente vinculada'],
  ['Menção, reply e chamada', 'Menção, resposta vinculada e chamada'],
  ['O input limites', 'A entrada limites'],
  ['a orientação do state', 'a orientação do estado'],
  ['memória, states, prompts ou análise interna', 'memória, estados, instruções ou análise interna'],
  ['GROUNDING DE PESSOAS', 'FUNDAMENTAÇÃO SOBRE PESSOAS'],
  ['REPLY OPCIONAL', 'RESPOSTA VINCULADA OPCIONAL'],
  ['citação interna (reply)', 'citação interna (campo reply)'],
  ['A escolha do reply', 'A escolha da resposta vinculada'],
  ['o validator,', 'o validador,'],
  ['Boundary enforcement: contratos resolvidos pela template versionada.',
    'Aplicação de limites comportamentais: contratos resolvidos pelo modelo de instruções versionado.'],
  ['Comunicação rotineira de status,', 'Comunicação rotineira de situação,'],
];
for (const state of project.project.states)
  for (const node of state.graph.nodes)
    if (typeof node.prompt === 'string')
      for (const [before, after] of translations)
        node.prompt = node.prompt.split(before).join(after);
write('manual-group-live-project-v3-pt-br-v1.json', project);

const clause = read('situation-recipient-clause-v1.txt').trim();
for (const state of project.project.states)
  for (const node of state.graph.nodes)
    if (node.id === 'group-situation' || node.id === 'dm-situation')
      node.prompt += '\n\n' + clause;
write('manual-group-live-project-v3-recipient-v1.json', project);
