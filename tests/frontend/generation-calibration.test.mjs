import test from 'node:test';
import assert from 'node:assert/strict';
import {slice4Project} from '../fixtures/slice4-project.mjs';
import {slice41Project} from '../fixtures/slice41-project.mjs';
import {saveProject,openProject} from '../../src/Filomena.App/Playground/flow-model.mjs';

test('boundary and social inputs are separate without changing FSM or perception',()=>{
 const previous=slice4Project(),next=slice41Project();
 assert.deepEqual(JSON.parse(saveProject(openProject(JSON.stringify(next)))),next);
 assert.equal(next.project.interestProfile,previous.project.interestProfile);
 for(const [i,s] of next.project.states.entries()){
  assert.equal(s.participationGuidance,previous.project.states[i].participationGuidance);
  for(const kind of ['dm','group']){
   const gen=s.graph.nodes.find(n=>n.id===kind+'-generation');
   assert.ok(gen.inputs.some(p=>p.name==='limites'));
   assert.ok(gen.inputs.some(p=>p.name==='memorias_sociais'));
   assert.ok(!gen.inputs.some(p=>p.name==='memorias'));
   const boundary=s.graph.nodes.find(n=>n.id===kind+'-response-boundaries');
   assert.deepEqual(boundary.settings.categories,['limite']);
   assert.equal(boundary.settings.mode,'exact');
   assert.equal(boundary.settings.maxCharacters,700);
   assert.equal(boundary.settings.maxItems,4);
   const social=s.graph.nodes.find(n=>n.id===kind+'-response-memory');
   assert.deepEqual(social.settings.categories,['apelido','relacao','fato']);
   for(const suffix of ['presence','situation','batch','action','transition-responder']){
    const node=s.graph.nodes.find(n=>n.id===kind+'-'+suffix),old=previous.project.states[i].graph.nodes.find(n=>n.id===node.id);
    assert.deepEqual({...node,x:0,y:0},{...old,x:0,y:0});
   }
  }
 }
});
