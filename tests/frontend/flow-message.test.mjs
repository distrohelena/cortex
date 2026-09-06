import test from 'node:test';
import assert from 'node:assert/strict';
import {initialProject,nodePorts,openProject,saveProject,connect} from '../../src/Filomena.App/Playground/flow-model.mjs';

test('DM and group expose the same single structured message output',()=>{
 for(const kind of ['dm','group']){
  assert.deepEqual(nodePorts({kind},'out'),[{id:'message',type:'message',label:'mensagem'}]);
  assert.deepEqual(nodePorts({kind},'in'),[]);
 }
 assert.deepEqual(initialProject().states[0].graph.edges,[]);
});

test('Acumular accepts only Mensagem from either entry and preserves its output',()=>{
 for(const kind of ['dm','group']){
  const project=initialProject(),graph=project.states[0].graph;
  graph.nodes[0].kind=kind;
  graph.nodes.push({id:'batch',kind:'accumulate',x:300,y:300,settings:{quietSeconds:3,maxMessages:10,maxWaitSeconds:30}});
  connect(graph,'input','batch','message','message');
  assert.deepEqual(nodePorts({kind:'accumulate'},'in'),[{id:'message',type:'message',label:'mensagem'}]);
  assert.deepEqual(nodePorts({kind:'accumulate'},'out'),[{id:'messages',type:'messages',label:'output'}]);
  assert.deepEqual(openProject(saveProject(project)),project);
  const before=saveProject(project);
  assert.throws(()=>connect(graph,'prompt','batch','message','message'),/tipos incompatíveis/);
  for(const port of ['from','group'])assert.throws(()=>connect(graph,'input','batch','message',port),/Conector desconhecido/);
  assert.equal(saveProject(project),before);
 }
});

test('old project preserves even incompatible wires during typed migration',()=>{
 const graph={version:1,nodes:[
  {id:'dm',kind:'dm',x:20,y:30},
  {id:'p',kind:'prompt',prompt:'Meu prompt',x:200,y:40},
  {id:'exit',kind:'exit',settings:{targetStateId:null,mode:'switch'},x:400,y:40}
 ],edges:[{source:'dm',sourcePort:'from',target:'p',targetPort:'message'},{source:'p',target:'exit'}]};
 const document={format:'filomena-local-project',project:{version:1,stateScope:'session',initialStateId:'a',states:[
  {id:'a',name:'Inicial',graph},
  {id:'b',name:'Outro',graph:structuredClone(graph)}
 ]}};
 const migrated=openProject(JSON.stringify(document));
 for(const state of migrated.states){
  assert.deepEqual(state.graph.edges,graph.edges);
  const expected=structuredClone(graph.nodes);expected.find(n=>n.kind==='exit').settings.targetStateId=state.id;
  assert.equal(state.graph.nodes[1].prompt,expected[1].prompt);
  assert.equal(state.graph.nodes[2].settings.targetStateId,state.id);
 }
 // Legacy invalid wires stay editable, never auto-repaired or removed.
 assert.deepEqual(openProject(saveProject(migrated)),migrated);
 assert.equal(document.project.states[0].graph.edges.length,2);
});
