import test from 'node:test';
import assert from 'node:assert/strict';
import * as model from '../../src/Filomena.App/Playground/flow-model.mjs';

test('execution wiring is separate from data and survives save/reload',()=>{
 const graph=model.initialGraph();
 model.connect(graph,'prompt','output');
 const data=structuredClone(graph.edges);
 model.connectExecution(graph,'input','prompt');
 model.connectExecution(graph,'prompt','output');
 assert.deepEqual(graph.edges,data);
 assert.deepEqual(graph.controlEdges,[
  {source:'input',sourcePort:'then',target:'prompt',targetPort:'exec'},
  {source:'prompt',sourcePort:'then',target:'output',targetPort:'exec'}
 ]);
 assert.deepEqual(model.openDraft(model.saveDraft(graph)),graph);
 model.removeNode(graph,'prompt');
 assert.deepEqual(graph.edges,[]);assert.deepEqual(graph.controlEdges,[]);
});

test('Branch has two independent execution outcomes and send can continue',()=>{
 const graph=model.initialGraph();graph.nodes.push(
  {id:'branch',kind:'branch',settings:{operator:'eq'},x:200,y:300},
  {id:'send',kind:'send',x:500,y:300});
 model.connectExecution(graph,'input','branch');
 model.connectExecution(graph,'branch','prompt','true');
 model.connectExecution(graph,'branch','output','false');
 model.connectExecution(graph,'branch','send','true');
 model.connectExecution(graph,'send','output');
 assert.equal(graph.controlEdges.find(edge=>edge.source==='branch'&&edge.sourcePort==='true').target,'send');
 assert.equal(graph.controlEdges.find(edge=>edge.source==='branch'&&edge.sourcePort==='false').target,'output');
 assert.equal(graph.controlEdges.length,4);
 assert.deepEqual(model.openDraft(model.saveDraft(graph)),graph);
});

test('execution ports cannot connect to data or to an entry, and Exit cannot continue',()=>{
 const graph=model.initialGraph();const before=model.saveDraft(graph);
 for(const args of [['input','prompt','message'],['input','prompt','then','message'],['prompt','input'],['output','prompt']])
  assert.throws(()=>model.connectExecution(graph,...args));
 assert.throws(()=>model.connect(graph,'input','prompt','then','message'));
 assert.equal(model.saveDraft(graph),before);
 const bad=JSON.parse(before);bad.graph.controlEdges=[{source:'input',sourcePort:'message',target:'prompt',targetPort:'exec'}];
 assert.throws(()=>model.openDraft(JSON.stringify(bad)));
});
