// Offline artifact generation only. No host configuration, providers or transport.
import {readFileSync,writeFileSync} from 'node:fs';
import {createHash} from 'node:crypto';
import {createVariant} from './build-variants.mjs';
const base=JSON.parse(readFileSync(new URL('../../docs/manual-group-live-project-v3-natural-v1.json',import.meta.url),'utf8'));
const candidate=createVariant(base,'group-self-reference-v1');
const read=name=>readFileSync(new URL(name,import.meta.url),'utf8').trim();
const additions={
 'group-situation':read('../../docs/situation-recipient-clause-v1.txt')+'\n\n'+read('./clauses/context-v1.txt'),
 'group-presence':read('./clauses/participation-v1.txt'),
 'group-generation':read('./clauses/voice-v1.txt')+'\n\n'+read('./clauses/repetition-v1.txt'),
 'group-regeneration':read('./clauses/voice-v1.txt')+'\n\n'+read('./clauses/repetition-v1.txt')
};
for(const state of candidate.project.states) for(const [id,clause] of Object.entries(additions)){
 const nodes=state.graph.nodes.filter(n=>n.id===id&&n.kind==='prompt');
 if(nodes.length!==1)throw new Error('ambiguous_group_target');
 nodes[0].prompt+='\n\n'+clause;
}
// Enforce a prompt-only group delta, keeping DM, models, routing and safety unchanged.
const restored=structuredClone(candidate);
for(let i=0;i<base.project.states.length;i++)for(let j=0;j<base.project.states[i].graph.nodes.length;j++){
 const original=base.project.states[i].graph.nodes[j],node=restored.project.states[i].graph.nodes[j];
 if(Object.hasOwn(additions,original.id))node.prompt=original.prompt;
}
if(JSON.stringify(restored)!==JSON.stringify(base))throw new Error('unexpected_non_prompt_change');
const bytes=JSON.stringify(candidate,null,2)+'\n';
writeFileSync(new URL('../../docs/manual-group-live-project-v3-conversation-repair-v1.json',import.meta.url),bytes);
console.log(JSON.stringify({kind:'local_candidate_only',sha256:createHash('sha256').update(bytes).digest('hex'),deploy:false}));
