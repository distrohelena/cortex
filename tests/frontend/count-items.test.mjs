import test from 'node:test';
import assert from 'node:assert/strict';
import {typedPorts,serializeTypedGraph} from '../../src/Filomena.App/Playground/flow-contract.mjs';
import {connect,executionPorts} from '../../src/Filomena.App/Playground/flow-model.mjs';

test('count accepts domain arrays, preserves IDs and rejects optional arrays',()=>{
 for(const type of ['memory[]','message[]','text[]','actor?[]']){
  const graph={version:3,nodes:[{id:'src',kind:'prompt',inputs:[],responseFields:[{id:'items',name:'lista',type}]},{id:'count',kind:'count'}],edges:[],controlEdges:[]};
  connect(graph,'src','count','items','list');
  assert.equal(typedPorts(graph,graph.nodes[1],'out')[0].type,'number');
  assert.deepEqual(executionPorts(graph.nodes[1],'in'),[]);
  assert.deepEqual(executionPorts(graph.nodes[1],'out'),[]);
  assert.equal(serializeTypedGraph(graph).edges[0].targetPort,'list');
  graph.nodes[0].responseFields[0].type=type+'?';
  assert.throws(()=>connect(graph,'src','count','items','list'));
 }
});
