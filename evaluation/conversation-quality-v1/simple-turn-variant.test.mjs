import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createSimpleTurnVariant } from './simple-turn-variant.mjs';
const source=JSON.parse(readFileSync(new URL('./group-luna-summary-v1.project.json',import.meta.url),'utf8'));
const clause='VOZ DE CONVERSA ENTRE ADULTOS\n\nOffline transformation fixture.';
const targets=['group-generation','group-regeneration'];

test('changes exactly one voice block on six group nodes without modifying source or other fields',()=>{
 const before=structuredClone(source),candidate=createSimpleTurnVariant(source,clause);let count=0;
 for(let s=0;s<3;s++)for(let n=0;n<source.project.states[s].graph.nodes.length;n++){
  const a=source.project.states[s].graph.nodes[n],b=candidate.project.states[s].graph.nodes[n];
  if(!targets.includes(a.id))continue;
  const start=a.prompt.indexOf('VOZ DE CONVERSA ENTRE ADULTOS'),end=a.prompt.indexOf('CONTINUIDADE SEM RECICLAR A PRÓPRIA FALA',start);
  assert.equal(b.prompt,a.prompt.slice(0,start)+clause+'\n\n'+a.prompt.slice(end));
  b.prompt=a.prompt;count++;
 }
 assert.equal(count,6);assert.deepEqual(candidate,before);assert.deepEqual(source,before);
});
for(const mutation of ['missing-node','duplicate-node','missing-heading','duplicate-heading','missing-end','wrong-kind','already-replaced'])test(`fails closed for ${mutation}`,()=>{
 const input=structuredClone(source),nodes=input.project.states[0].graph.nodes;
 const node=nodes.find(n=>n.id==='group-generation');
 if(mutation==='missing-node')nodes.splice(nodes.indexOf(node),1);
 if(mutation==='duplicate-node')nodes.push(structuredClone(node));
 if(mutation==='missing-heading')node.prompt=node.prompt.replace('VOZ DE CONVERSA ENTRE ADULTOS','MISSING');
 if(mutation==='duplicate-heading')node.prompt+='\nVOZ DE CONVERSA ENTRE ADULTOS';
 if(mutation==='missing-end')node.prompt=node.prompt.replace('CONTINUIDADE SEM RECICLAR A PRÓPRIA FALA','MISSING');
 if(mutation==='wrong-kind')node.kind='tool';
 if(mutation==='already-replaced'){
  const start=node.prompt.indexOf('VOZ DE CONVERSA ENTRE ADULTOS'),end=node.prompt.indexOf('CONTINUIDADE SEM RECICLAR A PRÓPRIA FALA',start);
  node.prompt=node.prompt.slice(0,start)+clause+'\n\n'+node.prompt.slice(end);
 }
 assert.throws(()=>createSimpleTurnVariant(input,clause),/Unreviewed|Already/);
});
test('rejects an invalid replacement rather than erasing required prompt boundaries',()=>{
 assert.throws(()=>createSimpleTurnVariant(source,''),/replacement/);
 assert.throws(()=>createSimpleTurnVariant(source,'Wrong header'),/replacement/);
});
