import test from 'node:test';
import assert from 'node:assert/strict';
import {inputTypes,propertyType,propertyPaths,typedPorts,syncSwitchCases} from '../../src/Filomena.App/Playground/flow-contract.mjs';
import {connect,executionPorts,saveDraft,openDraft,memoryDefaults} from '../../src/Filomena.App/Playground/flow-model.mjs';

test('memory type has only two visual properties and category enum outputs',()=>{
 assert.ok(inputTypes.includes('memory[]'));
 assert.deepEqual(propertyPaths('memory'),[['conteudo'],['categoria']]);
 assert.equal(propertyType('memory[]',['conteudo']),'text[]');
 const graph={version:3,nodes:[{id:'m',kind:'memory',settings:memoryDefaults()},{id:'p',kind:'property',settings:{path:['categoria']}},{id:'s',kind:'switch',settings:{cases:[]}}],edges:[{source:'m',sourcePort:'memories',target:'p',targetPort:'object'}]};
 assert.equal(typedPorts(graph,graph.nodes[1],'out')[0].type,'enum[]');
});

test('nullable projected senders connect to memory without general optional coercion',()=>{
 const graph={version:3,nodes:[{id:'batch',kind:'accumulate'},{id:'p',kind:'property',settings:{path:['remetente']}},{id:'m',kind:'memory',settings:memoryDefaults()}],edges:[]};
 connect(graph,'batch','p','messages','object');
 connect(graph,'p','m','value','persons');
 assert.equal(typedPorts(graph,graph.nodes[2],'in')[0].type,'actor?[]');
 assert.deepEqual(executionPorts(graph.nodes[2],'out').map(p=>p.id),['then']);
 assert.throws(()=>connect(graph,'batch','m','messages','persons'));
});

test('memory draft roundtrip preserves configuration and invalid wires',()=>{
 const graph={version:3,nodes:[{id:'m',kind:'memory',x:1,y:2,settings:{...memoryDefaults(),maxItems:0,queryText:'Persona'}},{id:'p',kind:'prompt',x:3,y:4,prompt:'test',inputs:[{id:'memory-in',name:'memorias',type:'memory[]'}]}],edges:[{source:'m',sourcePort:'removed',target:'p',targetPort:'memory-in'}],controlEdges:[]};
 graph.nodes.push({id:'entry',kind:'dm',x:1,y:2});
 const reopened=openDraft(saveDraft(graph));
 assert.deepEqual(reopened.edges,graph.edges);
 assert.deepEqual(reopened.nodes[0].settings,graph.nodes[0].settings);
});
