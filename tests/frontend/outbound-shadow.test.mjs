import test from 'node:test';
import assert from 'node:assert/strict';
import {typedPorts,serializeTypedGraph} from '../../src/Filomena.App/Playground/flow-contract.mjs';
import {nodePorts,executionPorts} from '../../src/Filomena.App/Playground/flow-model.mjs';

test('Enviar has only a text input, no destination or deployment configuration',()=>{
 const node={id:'s',kind:'send',x:100,y:200};
 const graph={version:3,nodes:[node],edges:[],controlEdges:[]};
 assert.deepEqual(typedPorts(graph,node,'in'),[{id:'message',label:'mensagem',type:'text'}]);
 assert.deepEqual(nodePorts(node,'in').map(p=>p.id),['message']);
 assert.deepEqual(typedPorts(graph,node,'out').map(p=>p.id),['status','diagnostico']);
 assert.deepEqual(executionPorts(node,'in').map(p=>p.id),['exec']);
 assert.deepEqual(serializeTypedGraph(graph).nodes[0],node);
 for(const extra of [{target:'someone'},{settings:{mode:'enabled'}},{recipient:'someone'},{executionProfile:'normal'}]){
  assert.throws(()=>serializeTypedGraph({...graph,nodes:[{...node,...extra}]}));
 }
});
