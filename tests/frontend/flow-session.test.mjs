import test from 'node:test';
import assert from 'node:assert/strict';
import {compatibleTypes,migrateTypedGraph,typedPorts} from '../../src/Filomena.App/Playground/flow-contract.mjs';
import {connect,connectExecution,executionPorts,initialGraph,initialProject,nodePorts,openProject,saveDraft,saveProject} from '../../src/Filomena.App/Playground/flow-model.mjs';
import {renderTypedPanel} from '../../src/Filomena.App/Playground/flow-typed-panel.mjs';

class Element {
 constructor(tag){this.tag=tag;this.children=[];}
 append(...children){this.children.push(...children);}
 replaceChildren(...children){this.children=children;}
 all(){return [this,...this.children.flatMap(node=>node.all())];}
}
const documentFake={createElement:tag=>new Element(tag),createTextNode:text=>Object.assign(new Element('#text'),{textContent:text})};
const panelInput=(root,label)=>root.all().find(node=>node.tag==='label'&&node.children[0]?.textContent===label)?.children[1];

test('session nodes expose stable typed and execution ports',()=>{
 const graph=migrateTypedGraph(initialProject().states[0].graph);
 const [entry,prompt]=graph.nodes;
 const accumulate={id:'batch',kind:'accumulate',x:300,y:300,settings:{quietSeconds:null,maxMessages:4,maxWaitSeconds:null}};
 const history={id:'history',kind:'history',x:500,y:300,settings:{maxMessages:12,maxAgeMinutes:null,maxCharacters:1200,includeCharacterMessages:false,mode:'before-current-batch'}};
 const exit={id:'exit-typed',kind:'exit',x:700,y:300,settings:{mode:'switch',targetStateId:null,stateMapping:{idle:'state-1',active:'state-2'}}};
 prompt.responseFields=[{id:'state-choice',name:'estado',description:'',type:'enum',values:['idle','active']}];
 graph.nodes.push(accumulate,history,exit);

 assert.deepEqual(nodePorts(accumulate,'in',graph).map(p=>[p.id,p.type]),[['message','message']]);
 assert.deepEqual(nodePorts(accumulate,'out',graph).map(p=>[p.id,p.type]),[['messages','message[]']]);
 assert.deepEqual(typedPorts(graph,history,'out').map(p=>[p.id,p.type]),[['messages','message[]']]);
 assert.deepEqual(typedPorts(graph,exit,'in').map(p=>[p.id,p.type]),[['state','enum']]);
 assert.deepEqual(executionPorts(accumulate,'in').map(p=>p.id),['exec']);
 assert.deepEqual(executionPorts(accumulate,'out').map(p=>p.id),['then']);
 assert.deepEqual(executionPorts(history,'in').map(p=>p.id),['exec']);
 assert.deepEqual(executionPorts(history,'out').map(p=>p.id),['then']);
 assert.deepEqual(executionPorts(exit,'out'),[]);

 connect(graph,entry.id,accumulate.id,'message','message');
 connect(graph,prompt.id,exit.id,'state-choice','state');
 connectExecution(graph,entry.id,accumulate.id);
 connectExecution(graph,accumulate.id,history.id);
 connectExecution(graph,history.id,exit.id);
 assert.equal(graph.controlEdges.length,3);
});

test('only Exit state inputs accept an enum without declared cases',()=>{
 const source={type:'enum',values:['idle']};
 assert.equal(compatibleTypes(source,{type:'enum'}),false);
 assert.equal(compatibleTypes(source,{type:'enum',acceptsAnyEnum:true}),true);
});

test('session message arrays connect from accumulator and history to typed instruction inputs',()=>{
 const graph=migrateTypedGraph(initialProject().states[0].graph);
 const prompt=graph.nodes.find(node=>node.kind==='prompt');
 prompt.inputs=[
  {id:'batch',name:'lote',type:'message[]',required:true},
  {id:'history',name:'histórico',type:'message[]',required:true}
 ];
 graph.nodes.push(
  {id:'batch-node',kind:'accumulate',x:300,y:300,settings:{quietSeconds:2,maxMessages:null,maxWaitSeconds:30}},
  {id:'history-node',kind:'history',x:500,y:300,settings:{maxMessages:20,maxAgeMinutes:null,maxCharacters:4000,includeCharacterMessages:false,mode:'before-current-batch'}}
 );

 connect(graph,'batch-node',prompt.id,'messages','batch');
 connect(graph,'history-node',prompt.id,'messages','history');
 assert.deepEqual(graph.edges.filter(edge=>edge.target===prompt.id),[
  {source:'batch-node',target:prompt.id,sourcePort:'messages',targetPort:'batch'},
  {source:'history-node',target:prompt.id,sourcePort:'messages',targetPort:'history'}
 ]);
});

test('project drafts preserve disabled session limits and incomplete transitions',()=>{
 const project=initialProject();
 project.initialStateId='missing-default';
 const graph=migrateTypedGraph(project.states[0].graph);
 graph.nodes.push(
  {id:'batch',kind:'accumulate',x:300,y:300,settings:{quietSeconds:null,maxMessages:null,maxWaitSeconds:null}},
  {id:'history',kind:'history',x:500,y:300,settings:{maxMessages:null,maxAgeMinutes:null,maxCharacters:0,includeCharacterMessages:false,mode:'through-current-batch'}},
  {id:'legacy-carry',kind:'exit',x:700,y:300,settings:{mode:'carry',targetStateId:null,stateMapping:{idle:'missing-state'}}}
 );
 project.states[0].graph=graph;

 const restored=openProject(saveProject(project));
 assert.equal(restored.initialStateId,'missing-default');
 assert.deepEqual(restored.states[0].graph.nodes.find(n=>n.id==='batch').settings,{quietSeconds:null,maxMessages:null,maxWaitSeconds:null});
 assert.deepEqual(restored.states[0].graph.nodes.find(n=>n.id==='history').settings,{maxMessages:null,maxAgeMinutes:null,maxCharacters:0,includeCharacterMessages:false,mode:'through-current-batch'});
 assert.deepEqual(restored.states[0].graph.nodes.find(n=>n.id==='legacy-carry').settings,{mode:'carry',targetStateId:null,stateMapping:{idle:'missing-state'}});
});

test('project draft default IDs remain scalar while incomplete values round-trip',()=>{
 for(const initialStateId of [undefined,null,'','missing-state']){
  const project=initialProject();project.initialStateId=initialStateId;
  assert.equal(openProject(saveProject(project)).initialStateId,initialStateId);
 }
 for(const initialStateId of [1,true,{},[],'x'.repeat(81)]){
  const project=initialProject();project.initialStateId=initialStateId;
  assert.throws(()=>saveProject(project));
 }
});

test('session settings exclude runtime values in legacy and typed graph drafts',()=>{
 const settings={quietSeconds:null,maxMessages:4,maxWaitSeconds:30,runtime:{batchId:'secret'}};
 const history={maxMessages:20,maxAgeMinutes:null,maxCharacters:4000,includeCharacterMessages:false,mode:'before-current-batch',runtime:{watermark:4}};
 const v2=initialGraph();v2.nodes.push({id:'batch',kind:'accumulate',x:300,y:300,settings},{id:'history',kind:'history',x:500,y:300,settings:history});
 const v3=migrateTypedGraph(v2);
 for(const graph of [v2,v3]){
  const saved=JSON.parse(saveDraft(graph)).graph;
  assert.deepEqual(saved.nodes.find(node=>node.id==='batch').settings,{quietSeconds:null,maxMessages:4,maxWaitSeconds:30});
  assert.deepEqual(saved.nodes.find(node=>node.id==='history').settings,{maxMessages:20,maxAgeMinutes:null,maxCharacters:4000,includeCharacterMessages:false,mode:'before-current-batch'});
 }
});

test('history panel edits bounded snapshot settings without runtime fields',()=>{
 const previous=globalThis.document;globalThis.document=documentFake;
 try{
  const graph=migrateTypedGraph(initialProject().states[0].graph);
  const history={id:'history',kind:'history',x:500,y:300,settings:{maxMessages:20,maxAgeMinutes:null,maxCharacters:4000,includeCharacterMessages:false,mode:'before-current-batch'}};
  graph.nodes.push(history);
  const root=new Element('root');let changed=0;
  renderTypedPanel(root,graph,history,false,()=>changed++);
  const maxMessages=panelInput(root,'Máximo de mensagens');
  maxMessages.value='8';maxMessages.onchange();
  panelInput(root,'Incluir mensagens da personagem').checked=true;
  panelInput(root,'Incluir mensagens da personagem').onchange();
  panelInput(root,'Janela').value='through-current-batch';panelInput(root,'Janela').onchange();
  assert.deepEqual(history.settings,{maxMessages:8,maxAgeMinutes:null,maxCharacters:4000,includeCharacterMessages:true,mode:'through-current-batch'});
  assert.equal(changed,3);
 }finally{globalThis.document=previous;}
});
