import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {slice37Project,reasons,interestProfile} from '../fixtures/slice37-project.mjs';
import {slice36Project} from '../fixtures/slice36-project.mjs';
import {saveProject,openProject} from '../../src/Filomena.App/Playground/flow-model.mjs';

test('slice37 preserves FSM wires and roundtrips explicit administrative interests',()=>{
 const doc=JSON.parse(readFileSync(new URL('../fixtures/typed-flow-slice37.json',import.meta.url),'utf8'));
 assert.deepEqual(doc,slice37Project());
 assert.deepEqual(JSON.parse(saveProject(openProject(JSON.stringify(doc)))),doc);
 const previous=slice36Project();
 assert.equal(doc.project.interestProfile,interestProfile);
 for(const [index,state] of doc.project.states.entries()){
  assert.deepEqual(state.graph.edges,previous.project.states[index].graph.edges);
  assert.deepEqual(state.graph.controlEdges,previous.project.states[index].graph.controlEdges);
  assert.equal(state.graph.edges.some(e=>e.sourcePort==='motivo'),false);
  assert.equal(state.graph.nodes.some(n=>n.kind==='memory'||n.kind==='send'),false);
  for(const n of state.graph.nodes.filter(n=>n.kind==='prompt')){
   const presence=n.id.endsWith('-presence');
   assert.equal(n.useInterestProfile,presence);assert.equal(n.useStateGuidance,presence);assert.equal(n.usePersonalityCore,false);
   if(presence){
    const reason=n.responseFields.find(f=>f.id==='motivo');
    assert.equal(reason.type,'enum');assert.deepEqual(reason.values,Object.keys(reasons));assert.equal('maxWords' in reason,false);
    assert.equal(n.responseFields.some(f=>f.id==='proximo_estado'),false);
   }
  }
 }
});
