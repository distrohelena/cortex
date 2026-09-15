import test from 'node:test';
import assert from 'node:assert/strict';
import {slice42Project} from '../fixtures/slice42-project.mjs';
import {saveProject,openProject} from '../../src/Filomena.App/Playground/flow-model.mjs';
test('full gate draft roundtrips including provider settings and explicit terminal block',()=>{
 const fixture=slice42Project();
 assert.deepEqual(JSON.parse(saveProject(openProject(JSON.stringify(fixture)))),fixture);
 for(const state of fixture.project.states)for(const kind of ['dm','group']){
  const graph=state.graph;
  assert.equal(graph.controlEdges.filter(e=>e.source===kind+'-validation-2'&&e.sourcePort==='block').length,0);
  assert.equal(graph.nodes.find(n=>n.id===kind+'-validator-1').reasoningEffort,'medium');
 }
});
