import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { parseLiveStatus,nodePorts,executionPorts,isEntry } from '../../src/Filomena.App/Playground/flow-model.mjs';
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

test('live status preserves the exact available active target contract',()=>{
 assert.deepEqual(parseLiveStatus({available:true,active:false,target:{channel:'whatsapp',accountId:'default',nativeId:'5511999999999'}}),{
  available:true,active:false,target:{channel:'whatsapp',accountId:'default',nativeId:'5511999999999'}
 });
 assert.deepEqual(parseLiveStatus({available:false,active:false,target:null}),{available:false,active:false,target:null});
});

test('live status rejects malformed targets instead of enabling controls',()=>{
 for(const status of [
  null,
  {available:'yes',active:false,target:null},
  {available:true,active:false,target:{channel:'whatsapp',accountId:'default'}},
  {available:true,active:true,target:null}
 ])assert.throws(()=>parseLiveStatus(status));
});

test('editor exposes the minimal send node and conditional live controls contract',()=>{
 assert.match(html,/id="add-send"[^>]*>Enviar mensagem</);
 assert.match(html,/id="live-controls"[^>]*hidden/);
 assert.match(html,/id="live-activate"[^>]*>Ativar na DM</);
 assert.match(html,/id="live-deactivate"[^>]*>Desativar</);
 assert.match(js,/let id='send'/);
 assert.match(js,/liveBusy/);
 assert.match(js,/live-deactivate'\)\.disabled=liveBusy;/);
 assert.match(js,/async function reconcileLiveStatus/);
 assert.match(js,/Estado não confirmado/);
 assert.match(js,/snapshot não confirmado; reaplique/);
 assert.match(js,/\/api\/flow\/typed\/run/);
 assert.match(js,/\/api\/flow\/live/);
 assert.match(js,/\/api\/flow\/live\/activate/);
 assert.match(js,/\/api\/flow\/live\/deactivate/);
 assert.match(js,/JSON\.parse\(saveProject\(project\)\)/);
 assert.match(js,/X-Filomena-Playground':'1'/);
});

test('ambiguous legacy wires remain visible without inventing a typed port',()=>{
 assert.doesNotMatch(js,/renderableEdgePorts\(edge,from,to\);if\(!ports\)continue/);
 const renderer=readFileSync(new URL('../../packages/flow-graph/dist/index.js',import.meta.url),'utf8');
 assert.match(renderer,/stroke-dasharray/);
 assert.match(js,/Conexão antiga sem portas definidas/);
});
