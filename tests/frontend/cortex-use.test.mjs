import test from 'node:test';
import assert from 'node:assert/strict';
import {UseModel} from '../../src/Cortex.Editor/wwwroot/use-model.mjs';
const state=()=>({graph:{version:3,nodes:[{id:'brief',kind:'source',settings:{title:'original'}},{id:'draft',kind:'model'}],edges:[],controlEdges:[]},entryId:'brief',usageFields:{brief:['title']},runState:{state:'idle',nextNodeId:'brief'}});
test('consumer parameters do not alter template and steps never use graph mutation routes',async()=>{
 const calls=[];const m=new UseModel(state(),async(path,method,body)=>{calls.push({path,method,body});return {state:'paused',nextNodeId:'draft',result:{steps:[{nodeId:'brief',status:'completed'}],outputs:{}}};});
 m.edit('brief',{name:'title',required:true,schema:{type:'string'}},'new');await m.advance();
 assert.equal(m.graph.nodes[0].settings.title,'original');assert.equal(calls[0].path,'/api/use/step');assert.deepEqual(calls[0].body.parameters,{brief:{title:'new'}});
 assert.throws(()=>m.edit('brief',{name:'title',schema:{type:'string'}},'changed'));
 await m.advance();assert.deepEqual(calls[1].body.parameters,{});
});
test('locked fields and busy changes are refused; reconnect restores real progress',()=>{
 const s=state();s.runState={state:'running',nextNodeId:'draft'};const m=new UseModel(s,async()=>({}));
 assert.equal(m.busy,true);assert.throws(()=>m.edit('brief',{name:'title',schema:{type:'string'}},'x'));
 const fresh=new UseModel(state(),async()=>({}));assert.throws(()=>fresh.edit('brief',{name:'private',schema:{type:'string'}},'x'));
});
