import test from 'node:test';
import assert from 'node:assert/strict';
import {typedPorts,serializeTypedGraph} from '../../src/Filomena.App/Playground/flow-contract.mjs';
import {slice36Project} from '../fixtures/slice36-project.mjs';
import {saveProject,openProject,connect} from '../../src/Filomena.App/Playground/flow-model.mjs';
import {readFileSync} from 'node:fs';

test('Branch exposes typed interactive inputs and preserves explicit local B',()=>{
 const ai={id:'ai',kind:'prompt',inputs:[],responseFields:[{id:'follow',name:'acompanhar',type:'boolean'}]};
 const branch={id:'branch',kind:'branch',settings:{operator:'eq',b:true}};
 const graph={version:3,nodes:[ai,branch],edges:[{source:'ai',sourcePort:'follow',target:'branch',targetPort:'a'}],controlEdges:[]};
 const ports=typedPorts(graph,branch,'in');
 assert.equal(ports?.find(p=>p.id==='a')?.type,'boolean');
 assert.equal(ports?.find(p=>p.id==='b')?.value,true);
 assert.equal(serializeTypedGraph(graph).nodes[1].settings.b,true);
});

test('Branch accepts matching wired scalar operands, rejects objects and local+wire',()=>{
 const graph={version:3,nodes:[{id:'p',kind:'prompt',responseFields:[{id:'n',name:'n',type:'number'},{id:'m',name:'m',type:'message'}]},
  {id:'b',kind:'branch',settings:{operator:'gt'}}],edges:[],controlEdges:[]};
 connect(graph,'p','b','n','a');connect(graph,'p','b','n','b');
 assert.equal(typedPorts(graph,graph.nodes[1],'in').every(p=>p.type==='number'),true);
 assert.throws(()=>connect(graph,'p','b','m','a'));
 graph.nodes[1].settings.b=true;
 assert.throws(()=>connect(graph,'p','b','n','b'));
});

for(const [suffix,options] of [['',{}],['-single',{singleCall:true}],['-memory',{memory:true}]])test(`slice36 ${suffix} editor roundtrip retains explicit FSM`,()=>{
 const doc=JSON.parse(readFileSync(new URL(`../fixtures/typed-flow-slice36${suffix}.json`,import.meta.url),'utf8'));
 assert.deepEqual(doc,slice36Project(options));
 assert.deepEqual(JSON.parse(saveProject(openProject(JSON.stringify(doc)))),doc);
 for(const state of doc.project.states){
  for(const node of state.graph.nodes){
   if(node.kind==='prompt')assert.equal(node.responseFields.some(f=>f.name==='proximo_estado'),false);
   if(node.kind==='exit')assert.ok(doc.project.states.some(s=>s.id===node.settings.targetStateId));
  }
  assert.equal(state.graph.nodes.some(n=>n.kind==='memory'),Boolean(options.memory));
  assert.equal(state.graph.nodes.some(n=>n.kind==='send'),false);
 }
});
