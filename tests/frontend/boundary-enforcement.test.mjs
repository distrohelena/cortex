import test from 'node:test';
import assert from 'node:assert/strict';
import {typedPorts,serializeTypedGraph} from '../../src/Filomena.App/Playground/flow-contract.mjs';

test('enforcement profile derives stable outputs and roundtrips versions without results',()=>{
 const node={id:'v',kind:'prompt',x:10,y:20,title:'Qualquer título',executionProfile:'boundary_enforcement_v1',semanticContractVersion:'boundary-semantic-features:v1',boundaryEvaluatorVersion:'no_teasing_about_topic:evaluator-v1',boundaryTemplateVersion:'no_teasing_about_topic:v1',inputs:[],responseFields:[]};
 const graph={version:3,nodes:[node],edges:[{source:'v',sourcePort:'resultado',target:'s',targetPort:'value'}],controlEdges:[]};
 assert.deepEqual(typedPorts(graph,node,'out').map(p=>p.id),['resultado','diagnostico','explicacao']);
 assert.deepEqual(serializeTypedGraph(graph).nodes[0],node);
 node.title='Renomeado';node.semanticResults={ato:'neutro'};
 const saved=serializeTypedGraph(graph);
 assert.equal(saved.nodes[0].semanticResults,undefined);
 assert.deepEqual(saved.edges,graph.edges);
 assert.equal(typedPorts(graph,node,'out')[0].type,'enum');
 node.semanticContractVersion='no_teasing_about_topic:semantic-v2';node.boundaryEvaluatorVersion='no_teasing_about_topic:evaluator-v2';
 const updated=serializeTypedGraph(graph);
 assert.equal(updated.nodes[0].semanticContractVersion,'no_teasing_about_topic:semantic-v2');
 assert.equal(updated.nodes[0].boundaryEvaluatorVersion,'no_teasing_about_topic:evaluator-v2');
 assert.deepEqual(updated.edges,saved.edges);
 assert.equal(saved.nodes[0].semanticContractVersion,'boundary-semantic-features:v1');
 assert.deepEqual(typedPorts(graph,node,'out').map(p=>p.id),['resultado','diagnostico','explicacao']);
});

test('multi detector composition versions roundtrip without execution artifacts',()=>{
 const node={id:'v',kind:'prompt',inputs:[],enforcementStrategy:'multiple_detectors_v1',complementaryContractVersion:'no_teasing_about_topic:implicit-depreciation-v1',aggregationPolicyVersion:'any_block_all_allow:v1'};
 const saved=serializeTypedGraph({version:3,nodes:[node],edges:[],controlEdges:[]});
 assert.deepEqual(saved.nodes[0],node);
});
