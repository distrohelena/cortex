import test from 'node:test';
import assert from 'node:assert/strict';
import {EditorModel} from '../../src/Cortex.Editor/wwwroot/editor-model.mjs';

const graph=()=>({version:4,custom:{keep:true},nodes:[{id:'a',kind:'missing',settings:{text:'<img src=x onerror=alert(1)>'},x:10,y:20}],edges:[],controlEdges:[]});
test('unknown nodes settings and graph metadata survive save',async()=>{
 const original=graph(),calls=[];
 const model=new EditorModel({graph:original,catalog:[],descriptions:{},entryId:'a'},async(path,method,body)=>{calls.push({path,method,body});return {saved:true};});
 await model.save();
 assert.deepEqual(calls[0].body.graph,original);
 assert.equal(calls.at(-1).path,'/api/graph/save');
});
test('busy state prevents changing nodes and settings',async()=>{
 const model=new EditorModel({graph:graph(),catalog:[],descriptions:{},busy:true},async()=>({}));
 assert.throws(()=>model.editSettings('a',{text:'changed'}));
 await assert.rejects(()=>model.saveSettings({plugins:[]}));
 assert.equal(model.graph.nodes[0].settings.text,'<img src=x onerror=alert(1)>');
});
test('failed validation never starts a run',async()=>{
 const calls=[];
 const model=new EditorModel({graph:graph(),catalog:[],descriptions:{}},async(path)=>{calls.push(path);return path==='/api/validate'?{success:false,issues:[{reason:'plugin unavailable'}]}:{};});
 assert.equal(await model.run(),false);
 assert.ok(!calls.includes('/api/run'));
 assert.equal(model.busy,false);
 assert.equal(model.issues[0].reason,'plugin unavailable');
});
test('saving plugin state marks restart without changing active catalog',async()=>{
 const catalog=[{kind:'literal'}];
 const model=new EditorModel({graph:graph(),catalog,descriptions:{}},async()=>({pendingRestart:true}));
 await model.saveSettings({plugins:[{id:'plugin',enabled:false}]});
 assert.equal(model.pendingRestart,true);
 assert.deepEqual(model.catalog,catalog);
});
test('cancel uses server status and releases busy state',async()=>{
 const model=new EditorModel({graph:graph(),catalog:[],descriptions:{},busy:true},async()=>({state:'cancelled',result:null,error:'cancelled'}));
 await model.cancel();
 assert.equal(model.busy,false);
 assert.equal(model.runState.state,'cancelled');
});
test('result preview preserves literal text and source values',()=>{
 const text='<img src=x onerror=alert(1)>';
 const model=new EditorModel({graph:graph(),catalog:[],descriptions:{}},async()=>({}));
 model.acceptRun({state:'completed',result:{success:true,steps:[{nodeId:'a',status:'completed'}],outputs:{a:{value:{schema:{type:'string'},value:text}}}}});
 assert.equal(model.preview('a'),text);
 assert.equal(model.runState.result.outputs.a.value.value,text);
});

test('inspection context restores completed outputs and validation issues without rerunning',async()=>{
 const text='rendered output survives';
 const caller=new EditorModel({graph:graph(),catalog:[],descriptions:{}},async()=>{throw new Error('must not rerun');});
 caller.acceptRun({state:'completed',result:{success:true,steps:[{nodeId:'a',status:'completed'}],outputs:{a:{summary:{value:{schema:{type:'string'},value:text}}}}}});
 caller.issues=[{nodeId:'a',reason:'previous validation warning'}];
 const context=caller.captureInspectionContext();
 const returned=new EditorModel({graph:graph(),catalog:[],descriptions:{}},async()=>{throw new Error('must not rerun');});
 returned.restoreInspectionContext(context);
 assert.equal(returned.runState.state,'completed');
 assert.equal(returned.preview('a'),text);
 assert.equal(returned.issues[0].reason,'previous validation warning');
 assert.deepEqual(returned.runState.result.steps,[{nodeId:'a',status:'completed'}]);
});

test('optional settings stay omitted when adding a node or clearing a field',()=>{
 const model=new EditorModel({graph:{version:4,nodes:[],edges:[],controlEdges:[]},catalog:[],descriptions:{}},async()=>({}));
 const required={name:'outputDirectory',required:true,schema:{type:'string'}};
 const optional={name:'editPlanFile',required:false,schema:{type:'string'}};
 const node=model.addNode({kind:'renderCuts',fields:[required,optional]});
 assert.deepEqual(node.settings,{outputDirectory:''});
 model.editField(node.id,required,'C:/output');
 model.editField(node.id,optional,'C:/plan.json');
 assert.equal(node.settings.editPlanFile,'C:/plan.json');
 model.editField(node.id,optional,'');
 assert.deepEqual(node.settings,{outputDirectory:'C:/output'});
});

test('incompatible connection leaves the document untouched',async()=>{
 const original=graph(),model=new EditorModel({graph:original,catalog:[],descriptions:{}},async(path)=>{assert.equal(path,'/api/graph/validate-connection');throw new Error('Data port types are incompatible.');});
 const before=structuredClone(model.graph);
 await assert.rejects(()=>model.connect({kind:'data',source:'a',sourcePort:'value',target:'b',targetPort:'value'}),/incompatible/);
 assert.deepEqual(model.graph,before);assert.equal(model.dirty,false);
});

test('dynamic config refreshes resolved node descriptions',async()=>{
 const model=new EditorModel({graph:graph(),catalog:[],descriptions:{}},async(path,method,body)=>{
  assert.equal(path,'/api/graph/describe');assert.equal(body.graph.nodes[0].settings.limit,7);
  return {a:{title:'Configured',semantic:'operation',ports:[{id:'result',type:'number',direction:'out'}],executionPorts:[]}};
 });
 await model.editField('a',{name:'limit',required:true,schema:{type:'number'}},7);
 assert.equal(model.descriptions.a.title,'Configured');assert.equal(model.descriptions.a.ports[0].type,'number');
});

test('host input defaults are stored as settings and can be removed',()=>{
 const model=new EditorModel({graph:graph(),catalog:[],descriptions:{a:{ports:[{id:'limit',editableDefault:true}]}}},async()=>({}));
 model.setInputDefault('a','limit',5);assert.deepEqual(model.graph.nodes[0].settings.inputValues,{limit:5});
 model.setInputDefault('a','limit',undefined,false);assert.equal(model.graph.nodes[0].settings.inputValues,undefined);
});

test('structured dictionary defaults stay native JSON objects in editor settings',()=>{
 const value={'model-A':8192,'model-B':4096};
 const model=new EditorModel({graph:graph(),catalog:[],descriptions:{a:{ports:[{id:'budget',editableDefault:true,schema:{type:'object',additionalProperties:{type:'integer'}}}]}}},async()=>({}));
 model.setInputDefault('a','budget',value);
 assert.deepEqual(model.graph.nodes[0].settings.inputValues.budget,value);
 assert.equal(typeof model.graph.nodes[0].settings.inputValues.budget,'object');
});

test('unsupported builtin and connected inputs reject synthetic defaults',()=>{
 const original=graph();original.nodes[0].kind='branch';original.edges=[{source:'x',sourcePort:'value',target:'a',targetPort:'b'}];
 const model=new EditorModel({graph:original,catalog:[],descriptions:{a:{ports:[{id:'b',editableDefault:false}]}}},async()=>({}));
 assert.throws(()=>model.setInputDefault('a','b','changed'),/does not support/i);
 model.descriptions.a.ports[0].editableDefault=true;
 assert.throws(()=>model.setInputDefault('a','b','changed'),/disconnect/i);
 assert.equal(model.graph.nodes[0].settings.inputValues,undefined);
});

test('read-only graphs allow inspection but reject document edits',()=>{
 const model=new EditorModel({graph:graph(),catalog:[],descriptions:{},useMode:true},async()=>({}));
 assert.throws(()=>model.editSettings('a',{text:'changed'}),/read-only/);
 assert.throws(()=>model.removeNode('a'),/read-only/);
 assert.equal(model.graph.nodes.length,1);
});

test('stale description failures are ignored while current failures remain visible',async()=>{
 const replies=[];let updates=0;
 const model=new EditorModel({graph:graph(),catalog:[],descriptions:{a:{title:'Old',ports:[]}}},()=>new Promise((resolve,reject)=>replies.push({resolve,reject})));
 model.onDescriptionsUpdated=()=>updates++;
 const old=model.refreshDescriptions(),current=model.refreshDescriptions();
 replies[0].reject(new Error('obsolete failure'));
 replies[1].reject(new Error('describe unavailable'));
 await Promise.all([old,current]);
 assert.equal(model.error,'describe unavailable');
 assert.equal(model.descriptionsStale,true);
 assert.equal(updates,1);
});

test('connection validation cannot apply to a changed or busy graph',async()=>{
 let resolve;const api=path=>path==='/api/graph/validate-connection'?new Promise(done=>resolve=done):Promise.resolve({});
 const model=new EditorModel({graph:graph(),catalog:[],descriptions:{}},api);
 const pending=model.connect({kind:'data',source:'a',sourcePort:'value',target:'b',targetPort:'value'});
 model.removeNode('a');
 resolve({success:true});
 await assert.rejects(pending,/graph changed/i);
 assert.deepEqual(model.graph.edges,[]);
 const second=new EditorModel({graph:graph(),catalog:[],descriptions:{}},api);
 const pendingRun=second.connect({kind:'data',source:'a',sourcePort:'value',target:'b',targetPort:'value'});
 second.busy=true;resolve({success:true});
 await assert.rejects(pendingRun,/run to finish/i);
 assert.deepEqual(second.graph.edges,[]);
});
