import test from 'node:test';
import assert from 'node:assert/strict';
import {EditorModel} from '../../src/Cortex.Editor/wwwroot/editor-model.mjs';

const graph=()=>({version:3,custom:{keep:true},nodes:[{id:'a',kind:'missing',settings:{text:'<img src=x onerror=alert(1)>'},x:10,y:20}],edges:[],controlEdges:[]});
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

test('optional settings stay omitted when adding a node or clearing a field',()=>{
 const model=new EditorModel({graph:{version:3,nodes:[],edges:[],controlEdges:[]},catalog:[],descriptions:{}},async()=>({}));
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
