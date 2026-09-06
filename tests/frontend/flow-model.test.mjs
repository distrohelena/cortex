import test from 'node:test';
import assert from 'node:assert/strict';
import { initialGraph, connect, removeNode, saveDraft, openDraft, saveReloadState, readReloadState, isTerminal, saveProject, openProject } from '../../src/Filomena.App/Playground/flow-model.mjs';

function typedGraph() {
 return {version:2,nodes:[
  {id:'dm',kind:'dm',x:0,y:0},
  {id:'prompt',kind:'prompt',prompt:'Responda',x:250,y:0},
  {id:'prompt-2',kind:'prompt',prompt:'Revise',x:250,y:200},
  {id:'send',kind:'send',x:520,y:0}
 ],edges:[]};
}

test('Branch comparison survives draft and reload and rejects unknown operators',()=>{
 for(const operator of ['eq','ne','gt','gte','lt','lte']){
  const graph=initialGraph();graph.nodes.push({id:'branch-1',kind:'branch',settings:{operator},x:300,y:300});
  assert.deepEqual(openDraft(saveDraft(graph)),graph);
  assert.deepEqual(readReloadState(saveReloadState(graph,'test','branch-1')).graph,graph);
  const invalid=JSON.parse(saveDraft(graph));invalid.graph.nodes.at(-1).settings.operator='script';
  assert.throws(()=>openDraft(JSON.stringify(invalid)));
 }
});

test('accumulator limits survive export and reload with individually disabled conditions',()=>{
 const graph=initialGraph();
 graph.nodes.push({id:'batch',kind:'accumulate',x:100,y:300,settings:{quietSeconds:2.5,maxMessages:null,maxWaitSeconds:60}});
 assert.deepEqual(openDraft(saveDraft(graph)),graph);
 assert.deepEqual(readReloadState(saveReloadState(graph,'test','batch')).graph,graph);
 for(const settings of [{quietSeconds:-1,maxMessages:10,maxWaitSeconds:30},{quietSeconds:3,maxMessages:1.5,maxWaitSeconds:30},{quietSeconds:3,maxMessages:10,maxWaitSeconds:'forever'}]){
  const draft=JSON.parse(saveDraft(graph));draft.graph.nodes.at(-1).settings=settings;
  assert.throws(()=>openDraft(JSON.stringify(draft)));
 }
});

test('DM and group remain distinct entries and do not implicitly unwrap message objects to text',()=>{
 const graph=initialGraph();graph.nodes[0].kind='dm';
 graph.nodes.push({id:'group',kind:'group',x:60,y:350});
 assert.throws(()=>connect(graph,'group','prompt'),/compatíveis/);
 assert.throws(()=>connect(graph,'input','prompt'),/compatíveis/);
 assert.deepEqual(graph.edges,[]);
 assert.deepEqual(openDraft(saveDraft(graph)),graph);
 const before=saveDraft(graph);
 assert.throws(()=>connect(graph,'prompt','group'));
 assert.equal(saveDraft(graph),before);
});

test('rewiring replaces occupied ports and deleting a node removes its edges', () => {
 const graph=initialGraph();
 graph.nodes.push({id:'p2',kind:'prompt',prompt:'Revise',x:30,y:40});
 connect(graph,'prompt','p2');connect(graph,'p2','output');
 assert.deepEqual(graph.edges,[
  {source:'prompt',target:'p2',sourcePort:'message',targetPort:'message'},
  {source:'p2',target:'output'}
 ]);
 removeNode(graph,'p2');
 assert.deepEqual(graph.edges,[]);
 assert.equal(graph.nodes.length,3);
});
test('save/open preserves prompt positions and edges without run content', () => {
 const graph=initialGraph();graph.nodes[1].x=427;graph.nodes[1].prompt='Be kind';
 const opened=openDraft(saveDraft(graph));assert.deepEqual(opened,graph);
 opened.nodes[1].prompt='Changed';assert.equal(graph.nodes[1].prompt,'Be kind');
});
test('invalid imports and unsafe wiring are rejected without changing existing graph', () => {
 const graph=initialGraph(),before=saveDraft(graph);
 assert.throws(()=>openDraft('{bad'));
 assert.throws(()=>openDraft(JSON.stringify({format:'filomena-local-flow',graph:{version:99,nodes:[],edges:[]}})));
 assert.throws(()=>connect(graph,'output','input'));
 assert.throws(()=>connect(graph,'prompt','prompt'));
 assert.equal(saveDraft(graph),before);
});

test('reload snapshot restores an unfinished draft, selected prompt and test message',()=>{
 const graph=initialGraph();graph.nodes[1].prompt='Custom personality';graph.edges=[];
 const restored=readReloadState(saveReloadState(graph,'Hello','prompt'));
 assert.deepEqual(restored.graph,graph);assert.equal(restored.message,'Hello');assert.equal(restored.selected,'prompt');
 assert.throws(()=>readReloadState('{invalid'));
});

test('provider and model are saved as design values even when unavailable locally',()=>{
 const graph=initialGraph();graph.nodes[1].provider='future-provider';graph.nodes[1].model='my-unloaded-model';
 const restored=openDraft(saveDraft(graph));
 assert.equal(restored.nodes[1].provider,'future-provider');assert.equal(restored.nodes[1].model,'my-unloaded-model');
 assert.deepEqual(readReloadState(saveReloadState(graph,'Hello','prompt')).graph,graph);
 const old=initialGraph();assert.deepEqual(openDraft(saveDraft(old)),old);
});

test('text wires between instructions and send survive project reload',()=>{
 const graph=typedGraph();
 connect(graph,'prompt','send','message','message');
 assert.equal(isTerminal(graph.nodes.find(node=>node.id==='send')),true);
 assert.deepEqual(graph.edges,[
  {source:'prompt',target:'send',sourcePort:'message',targetPort:'message'}
 ]);

 connect(graph,'prompt-2','send','message','message');
 assert.deepEqual(graph.edges,[
  {source:'prompt-2',target:'send',sourcePort:'message',targetPort:'message'}
 ]);
 const project={version:1,stateScope:'session',initialStateId:'state-1',states:[{id:'state-1',name:'Inicial',graph}]};
 assert.deepEqual(openProject(saveProject(project)),project);
});

test('removed sender port is rejected without changing the graph',()=>{
 const graph=typedGraph();
 const before=JSON.stringify(graph);
 assert.throws(()=>connect(graph,'dm','prompt','from','message'),/Conector desconhecido/);
 assert.equal(JSON.stringify(graph),before);
});

test('whole message cannot be used as a Person or Channel recipient',()=>{
 const graph=typedGraph();graph.nodes.push({id:'group',kind:'group',x:0,y:300});
 connect(graph,'prompt','send','message','message');
 assert.throws(()=>connect(graph,'dm','send','message','to'),/tipos incompatíveis/);
 assert.throws(()=>connect(graph,'group','send','message','to'),/tipos incompatíveis/);
 assert.deepEqual(graph.edges.filter(edge=>edge.targetPort==='to'),[]);
 assert.equal(graph.edges.filter(edge=>edge.targetPort==='message').length,1);
 assert.deepEqual(openDraft(saveDraft(graph)),graph);
 assert.deepEqual(readReloadState(saveReloadState(graph,'test','send')).graph,graph);
 const before=saveDraft(graph);
 assert.throws(()=>connect(graph,'group','send','message','to'),/tipos incompatíveis/);
 assert.equal(saveDraft(graph),before);
});

for(const target of ['prompt','send'])test(`${target} content still accepts text OR message list`,()=>{
 const graph=typedGraph();
 graph.nodes.push({id:'batch',kind:'accumulate',x:0,y:300,settings:{quietSeconds:2,maxMessages:10,maxWaitSeconds:30}});
 connect(graph,'prompt-2',target,'message','message');
 connect(graph,'batch',target,'messages','message');
 assert.deepEqual(graph.edges.filter(edge=>edge.target===target&&edge.targetPort==='message'),[
  {source:'batch',sourcePort:'messages',target,targetPort:'message'}
 ]);
 assert.deepEqual(openDraft(saveDraft(graph)),graph);
 assert.deepEqual(readReloadState(saveReloadState(graph,'test',target)).graph,graph);
 const before=saveDraft(graph);
 assert.throws(()=>connect(graph,'dm',target,'message','message'),/tipos incompatíveis/);
 assert.throws(()=>connect(graph,'batch','send','messages','to'),/tipos incompatíveis/);
 assert.equal(saveDraft(graph),before);
 connect(graph,'prompt-2',target,'message','message');
 assert.equal(graph.edges.filter(edge=>edge.target===target&&edge.targetPort==='message').length,1);
 assert.equal(graph.edges.find(edge=>edge.target===target&&edge.targetPort==='message').source,'prompt-2');
});

test('legacy wires remain intact for explicit typed validation',()=>{
 const document={format:'filomena-local-flow',graph:{version:1,nodes:[
  {id:'dm',kind:'dm',x:0,y:0},
  {id:'prompt',kind:'prompt',prompt:'Responda',x:250,y:0},
  {id:'send',kind:'send',x:520,y:0},
  {id:'exit',kind:'exit',settings:{targetStateId:'state-1',mode:'switch'},x:520,y:200}
 ],edges:[
  {source:'dm',target:'prompt'},
  {source:'prompt',target:'send'},
  {source:'dm',target:'send'},
  {source:'prompt',target:'exit'}
 ]}};
 const graph=openDraft(JSON.stringify(document));
 assert.deepEqual(graph.edges,document.graph.edges);
});
