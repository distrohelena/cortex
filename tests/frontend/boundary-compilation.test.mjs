import test from 'node:test';
import assert from 'node:assert/strict';
import {typedPorts,serializeTypedGraph,inputTypes} from '../../src/Filomena.App/Playground/flow-contract.mjs';
import {connect,executionPorts} from '../../src/Filomena.App/Playground/flow-model.mjs';

test('boundary compiler preserves typed pins and versions without a provider through roundtrip',()=>{
 const interpreter={id:'b',kind:'interpretBoundaries',x:200,y:100,settings:{taxonomyVersion:'v1',grammarVersion:'v1',templatesVersion:'v1'}};
 const graph={version:3,nodes:[{id:'m',kind:'memory',x:100,y:100},interpreter],edges:[],controlEdges:[]};
 connect(graph,'m','b','memories','memories');
 assert.deepEqual(executionPorts(interpreter,'in').map(p=>p.id),['exec']);
 assert.deepEqual(executionPorts(interpreter,'out').map(p=>p.id),['then']);
 assert.equal(typedPorts(graph,interpreter,'out')[0].type,'boundary[]');
 assert.ok(inputTypes.includes('boundary[]'));
 assert.deepEqual(serializeTypedGraph(graph).nodes[1],interpreter);
 const previous=structuredClone(graph.edges);
 interpreter.title='Renomeado';
 assert.deepEqual(serializeTypedGraph(graph).edges,previous);
 assert.throws(()=>connect(graph,'m','b','memories','missing'));
});

test('legacy interpreter configuration is removed without losing wires or stable IDs',()=>{
 const graph={version:3,nodes:[{id:'b',kind:'interpretBoundaries',x:20,y:30,provider:'openai',model:'old-experiment',reasoningEffort:'medium',maxOutputTokens:1024,settings:{taxonomyVersion:'v1',interpreterContractVersion:'v1',templatesVersion:'v1'}}],edges:[{source:'b',sourcePort:'boundaries',target:'consumer',targetPort:'limits'}],controlEdges:[]};
 const saved=serializeTypedGraph(graph);
 assert.deepEqual(saved.nodes[0],{id:'b',kind:'interpretBoundaries',x:20,y:30,settings:{taxonomyVersion:'v1',grammarVersion:'v1',templatesVersion:'v1'}});
 assert.deepEqual(saved.edges,graph.edges);
 assert.equal(graph.nodes[0].model,'old-experiment');
});
