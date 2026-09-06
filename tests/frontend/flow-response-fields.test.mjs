import test from 'node:test';
import assert from 'node:assert/strict';
import {initialProject,saveProject,openProject,saveProjectReloadState,readProjectReloadState} from '../../src/Filomena.App/Playground/flow-model.mjs';

test('instruction JSON fields survive project save and reload without replacing the prompt',()=>{
 const project=initialProject(),node=project.states[0].graph.nodes.find(n=>n.kind==='prompt');
 const prompt=node.prompt;
 node.responseFields=[
  {name:'resposta',description:'Texto para enviar',type:'text'},
  {name:'quantidade',description:'Quantidade',type:'number'},
  {name:'enviar',description:'Pode enviar?',type:'boolean'},
  {name:'humor',description:'Tom da resposta',type:'enum',values:['neutro','alegre']}
 ];
 const restored=openProject(saveProject(project)).states[0].graph.nodes.find(n=>n.kind==='prompt');
 assert.deepEqual(restored.responseFields,node.responseFields);assert.equal(restored.prompt,prompt);
 const reload=readProjectReloadState(saveProjectReloadState(project,'state-1','oi',node.id));
 assert.deepEqual(reload.project,project);
 node.responseFields=[];assert.deepEqual(openProject(saveProject(project)),project);
});

test('invalid response field shapes are rejected while old nodes need no schema',()=>{
 const project=initialProject(),node=project.states[0].graph.nodes.find(n=>n.kind==='prompt');
 assert.deepEqual(openProject(saveProject(project)),project);
 for(const fields of [null,{},[{name:'x',description:'',type:'object'}],[{name:42,description:'',type:'text'}],[{name:'x',description:'',type:'enum',values:'a,b'}]]){
  node.responseFields=fields;assert.throws(()=>saveProject(project));
 }
});
