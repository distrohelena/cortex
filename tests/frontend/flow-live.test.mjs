import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { nodePorts,executionPorts,isEntry } from '../../src/Filomena.App/Playground/flow-model.mjs';
import {runInNewContext} from 'node:vm';

const html=readFileSync(new URL('../../src/Filomena.App/Playground/flow.html',import.meta.url),'utf8');
const js=readFileSync(new URL('../../src/Filomena.App/Playground/flow.js',import.meta.url),'utf8');

test('instruction descriptor hides prompt text without removing editable content or ports',()=>{
 const describeNode=runInNewContext(js.slice(js.indexOf('function describeNode(node){'),js.indexOf('const editor=createFlowGraphEditor'))+'\ndescribeNode',{
  nodePorts,executionPorts,isEntry,nodeTitle:()=> 'Instrução',portTypeLabels:{},graph:{version:2},typeLabel:t=>t
 });
 const node={id:'p',kind:'prompt',prompt:'Meu prompt permanece salvo.',x:0,y:0};
 const view=describeNode(node);
 assert.equal(view.preview,undefined);
 assert.equal(node.prompt,'Meu prompt permanece salvo.');
 assert.equal(view.ports.length,2);
 assert.equal(view.executionPorts.length,2);
});

test('ambiguous legacy wires remain visible without inventing a typed port',()=>{
 assert.doesNotMatch(js,/renderableEdgePorts\(edge,from,to\);if\(!ports\)continue/);
 const renderer=readFileSync(new URL('../../packages/flow-graph/dist/index.js',import.meta.url),'utf8');
 assert.match(renderer,/stroke-dasharray/);
 assert.match(js,/Conexão antiga sem portas definidas/);
});
