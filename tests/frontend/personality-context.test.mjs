import test from 'node:test';
import assert from 'node:assert/strict';
import {initialProject,saveProject,openProject} from '../../src/Filomena.App/Playground/flow-model.mjs';
import {migrateTypedGraph} from '../../src/Filomena.App/Playground/flow-contract.mjs';
import {readFileSync} from 'node:fs';
import {slice35Project} from '../fixtures/slice35-project.mjs';

test('personality, guidance and explicit instruction references roundtrip without runtime state',()=>{
 const project=initialProject();project.personalityCore='Core imutável';project.interestProfile='Programação e criação';
 const state=project.states[0];state.participationGuidance='Observar primeiro';state.graph=migrateTypedGraph(state.graph);
 const instruction=state.graph.nodes.find(n=>n.kind==='prompt');instruction.usePersonalityCore=false;instruction.useStateGuidance=true;instruction.useInterestProfile=true;
 const loaded=openProject(saveProject(project));
 assert.equal(loaded.personalityCore,project.personalityCore);
 assert.equal(loaded.interestProfile,project.interestProfile);
 assert.equal(loaded.states[0].graph.nodes.find(n=>n.id===instruction.id).useInterestProfile,true);
 assert.equal(loaded.states[0].participationGuidance,state.participationGuidance);
 assert.equal(loaded.states[0].graph.nodes.find(n=>n.id===instruction.id).useStateGuidance,true);
 assert.equal(loaded.states[0].graph.nodes.find(n=>n.id===instruction.id).usePersonalityCore,false);
 assert.equal('currentStateId' in loaded,false);
});

test('old projects do not acquire implicit personality references',()=>{
 const loaded=openProject(saveProject(initialProject()));
 assert.equal('personalityCore' in loaded,false);
 assert.equal('interestProfile' in loaded,false);
 assert.equal('participationGuidance' in loaded.states[0],false);
});

for(const memory of [false,true])test(`three-state experimental fixture roundtrips with memory=${memory}`,()=>{
 const filename=`../fixtures/typed-flow-slice35${memory?'-memory':''}.json`;
 const document=JSON.parse(readFileSync(new URL(filename,import.meta.url),'utf8'));
 assert.deepEqual(document,slice35Project(memory));
 assert.deepEqual(JSON.parse(saveProject(openProject(JSON.stringify(document)))),document);
 for(const state of document.project.states){
  assert.equal(state.graph.nodes.length,memory?22:18);
  assert.equal(state.graph.nodes.filter(n=>n.kind==='prompt').length,4);
  assert.equal(state.graph.nodes.some(n=>n.kind==='send'),false);
 }
});
