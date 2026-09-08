import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {slice4Project} from '../fixtures/slice4-project.mjs';
import {saveProject,openProject} from '../../src/Filomena.App/Playground/flow-model.mjs';

for(const memory of [true,false])test(`generation fixture roundtrips, explicit memory=${memory}`,()=>{
 const doc=JSON.parse(readFileSync(new URL(`../fixtures/typed-flow-slice4${memory?'':'-no-memory'}.json`,import.meta.url),'utf8'));
 assert.deepEqual(doc,slice4Project(memory));
 assert.deepEqual(JSON.parse(saveProject(openProject(JSON.stringify(doc)))),doc);
 for(const state of doc.project.states){
  assert.ok(state.graph.nodes.length<=32);assert.ok(state.graph.edges.length<=64);
  assert.equal(state.graph.nodes.some(n=>n.kind==='send'),false);
  for(const gen of state.graph.nodes.filter(n=>n.id.endsWith('-generation'))){
   assert.equal(gen.outputMode,'text');assert.equal(gen.usePersonalityCore,true);assert.equal(gen.useInterestProfile,false);
   assert.equal(gen.inputs.some(p=>p.name==='memorias'),memory);
   assert.deepEqual(gen.responseFields,[{id:'texto',name:'texto',type:'text'}]);
  }
 }
});
