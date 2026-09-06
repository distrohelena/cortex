import test from 'node:test';
import assert from 'node:assert/strict';
import {migrateTypedGraph,typedPorts,syncSwitchCases,propertyType,compatibleTypes} from '../../src/Filomena.App/Playground/flow-contract.mjs';
import {openProject,saveProject,initialProject} from '../../src/Filomena.App/Playground/flow-model.mjs';
test('migration preserves wires, rename preserves IDs, type edits do not delete wires',()=>{
 const project=initialProject(),original=project.states[0].graph;
 const graph=migrateTypedGraph(original);project.states[0].graph=graph;
 const p=graph.nodes.find(n=>n.kind==='prompt');p.inputs=[{id:'opaque-in',name:'texto',type:'text',required:true}];
 p.responseFields=[{id:'opaque-out',name:'acao',description:'',type:'enum',values:['a','b']}];
 const sw={id:'s',kind:'switch',x:20,y:20,settings:{cases:[],other:false}};graph.nodes.push(sw);
 graph.edges.push({source:p.id,sourcePort:'opaque-out',target:'s',targetPort:'value'});syncSwitchCases(graph,sw);
 const id=sw.settings.cases[0].id;graph.controlEdges=[{source:'s',sourcePort:id,target:p.id,targetPort:'exec'}];
 const before=structuredClone(graph.edges);p.responseFields[0].name='decisao';p.inputs[0].name='renomeado';
 assert.equal(typedPorts(graph,p,'out')[0].id,'opaque-out');assert.deepEqual(graph.edges,before);
 p.responseFields[0].type='number';assert.deepEqual(graph.edges,before);
 p.responseFields[0].type='enum';syncSwitchCases(graph,sw);assert.equal(sw.settings.cases[0].id,id);
 p.responseFields[0].values=['b'];syncSwitchCases(graph,sw);assert.equal(graph.controlEdges[0].sourcePort,id);
 assert.deepEqual(openProject(saveProject(project)),project);
 p.responseFields[0].values=['a','b'];syncSwitchCases(graph,sw);assert.equal(sw.settings.cases[0].id,id);
 assert.deepEqual(openProject(saveProject(project)),project);
});
test('UI compatibility preserves optional, list and enum contracts without coercion',()=>{
 const t=type=>({type});
 assert.equal(compatibleTypes(t('text'),t('enum')),false);
 assert.equal(compatibleTypes(t('actor?'),t('actor')),false);
 assert.equal(compatibleTypes(t('actor'),t('actor?')),true);
 assert.equal(compatibleTypes(t('actor?[]'),t('actor[]')),false);
 assert.equal(compatibleTypes(t('message'),t('text')),false);
 assert.equal(compatibleTypes({type:'enum',values:['a']},{type:'enum',values:['a','b']}),true);
 assert.equal(compatibleTypes({type:'enum',values:['c']},{type:'enum',values:['a','b']}),false);
});
test('property descriptors project lists and preserve optionality',()=>{
 assert.equal(propertyType('message',['texto']),'text');assert.equal(propertyType('message',['remetente']),'actor?');
 assert.equal(propertyType('message[]',['remetente']),'actor?[]');assert.equal(propertyType('message',['canal']),'channel');
 assert.equal(propertyType('message',['__proto__','texto']),'invalid');
});
