import test from 'node:test';
import assert from 'node:assert/strict';
import * as model from '../../src/Filomena.App/Playground/flow-model.mjs';

test('state-change nodes require a destination and new states target themselves',()=>{
 const project=model.initialProject(),second=model.addProjectState(project,'Segundo');
 assert.equal(second.graph.nodes.find(n=>n.kind==='exit').settings.targetStateId,second.id);
 const graph=model.initialGraph();graph.nodes.find(n=>n.kind==='exit').settings.targetStateId=null;
 assert.throws(()=>model.saveDraft(graph));
 model.removeNode(project.states[0].graph,'output');
 assert.deepEqual(model.openProject(model.saveProject(project)),project);
});

test('legacy output becomes an Exit preserving ID, position and wires',()=>{
 const old={version:1,nodes:[{id:'in',kind:'dm',x:10,y:20},{id:'old-result',kind:'output',x:300,y:40}],edges:[{source:'in',target:'old-result'}]};
 const project=model.openProject(JSON.stringify({format:'filomena-local-flow',graph:old}));
 const graph=project.states[0].graph;
 assert.deepEqual(graph.nodes[1],{id:'old-result',kind:'exit',x:300,y:40,settings:{targetStateId:'state-1',mode:'switch'}});
 assert.deepEqual(graph.edges,old.edges);
 assert.equal(model.initialGraph().nodes.at(-1).kind,'exit');
 assert.deepEqual(model.openProject(model.saveProject(project)),project);
});

test('each state owns its graph and renaming preserves Exit references',()=>{
 const project=model.initialProject();
 const first=model.stateGraph(project,'state-1');
 const next=model.addProjectState(project,'Atendimento');
 const second=model.stateGraph(project,next.id);
 second.nodes.find(n=>n.kind==='prompt').prompt='Outra personalidade';
 assert.notEqual(first.nodes.find(n=>n.kind==='prompt').prompt,'Outra personalidade');
 first.nodes.push({id:'exit-1',kind:'exit',x:500,y:300,settings:{targetStateId:next.id,mode:'carry'}});
 model.renameProjectState(project,next.id,'Suporte');
 const saved=model.saveProject(project),restored=model.openProject(saved);
 assert.equal(restored.states[1].name,'Suporte');
 assert.deepEqual(restored,project);
 assert.equal(restored.states[0].graph.nodes.at(-1).settings.targetStateId,next.id);
 assert.equal(restored.stateScope,'session');
 assert.throws(()=>model.stateGraph(project,'missing'));
});

test('legacy single-flow JSON is preserved inside Initial state',()=>{
 const graph=model.initialGraph();graph.nodes[1].prompt='Preserve me';
 const project=model.openProject(model.saveDraft(graph));
 assert.equal(project.states.length,1);
 assert.deepEqual(model.stateGraph(project,project.initialStateId),graph);
});

test('invalid Exit references and invalid project identity cannot be imported',()=>{
 const project=model.initialProject();
 project.states[0].graph.nodes.push({id:'exit',kind:'exit',x:0,y:0,settings:{targetStateId:'state-1',mode:'switch'}});
 const valid=JSON.parse(model.saveProject(project));
 for(const mutate of [
  p=>p.states[0].graph.nodes.at(-1).settings.targetStateId='unknown',
  p=>p.states[0].graph.nodes.at(-1).settings.mode='execute-shell',
  p=>p.initialStateId='unknown',
  p=>p.stateScope='global',
  p=>p.states.push(structuredClone(p.states[0]))
 ]){
  const bad=structuredClone(valid);mutate(bad.project);
  assert.throws(()=>model.openProject(JSON.stringify(bad)));
 }
});

test('Exit is terminal in design and retains both transition modes',()=>{
 for(const mode of ['switch','carry']){
  const graph=model.initialGraph();
  model.removeNode(graph,'output');
  graph.nodes.push({id:'exit',kind:'exit',x:600,y:150,settings:{targetStateId:'state-1',mode}});
  model.connect(graph,'prompt','exit');
  assert.deepEqual(model.openDraft(model.saveDraft(graph)),graph);
  assert.throws(()=>model.connect(graph,'exit','prompt'));
 }
});

test('reload restores all states and editor selection without execution state',()=>{
 const project=model.initialProject();const next=model.addProjectState(project,'Outra');
 project.states[0].graph.nodes[1].prompt='First';
 const restored=model.readProjectReloadState(model.saveProjectReloadState(project,next.id,'Hello','prompt'));
 assert.deepEqual(restored.project,project);
 assert.equal(restored.selectedStateId,next.id);
 assert.equal(restored.message,'Hello');assert.equal(restored.selected,'prompt');
 assert.equal(restored.project.initialStateId,'state-1');
 const legacy=model.readProjectReloadState(model.saveReloadState(model.initialGraph(),'Old message','prompt'));
 assert.equal(legacy.message,'Old message');assert.equal(legacy.selectedStateId,'state-1');
});

test('project export preserves typed send wires alongside visual Branch and Acumular nodes',()=>{
 const project=model.initialProject();
 const graph=model.stateGraph(project,'state-1');
 graph.nodes.push(
  {id:'send',kind:'send',x:680,y:150},
  {id:'branch-1',kind:'branch',settings:{operator:'eq'},x:300,y:300},
  {id:'batch',kind:'accumulate',settings:{quietSeconds:3,maxMessages:null,maxWaitSeconds:30},x:520,y:300}
 );
 model.connect(graph,'prompt','send','message','message');
 const restored=model.openProject(model.saveProject(project));
 assert.deepEqual(restored,project);
});
