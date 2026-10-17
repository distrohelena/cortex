import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync,readdirSync} from 'node:fs';
import {createFlowGraphEditor} from '../dist/index.js';

class Element extends EventTarget {
 constructor(tag,doc){super();this.tagName=tag;this.ownerDocument=doc;this.children=[];this.dataset={};this.attrs={};this.style={setProperty(){}};this.className='';this.classList={add:(c)=>this.className+=' '+c,toggle:(c,on)=>{this.className=this.className.split(' ').filter(x=>x!==c).join(' ');if(on)this.className+=' '+c;}};}
 append(...nodes){for(const node of nodes){node.parentElement=this;this.children.push(node);}}
 replaceChildren(...nodes){this.children=[];this.append(...nodes);}
 setAttribute(key,value){this.attrs[key]=value;}
 querySelectorAll(selector){return this.children.flatMap(child=>[...(selector==='button.port'&&child.tagName==='button'&&child.className.split(' ').includes('port')?[child]:[]),...child.querySelectorAll(selector)]);}
 closest(selector){return selector==='button.port'&&this.tagName==='button'?this:this.parentElement?.closest(selector)??null;}
 getBoundingClientRect(){return {left:0,top:0,right:200,bottom:100,width:200,height:100};}
 setPointerCapture(id){this.pointer=id;}hasPointerCapture(id){return this.pointer===id;}releasePointerCapture(){this.pointer=null;}
 fire(type,props={}){const event=new Event(type,{cancelable:true});Object.assign(event,props);this.dispatchEvent(event);return event;}
}
function setup(overrides={}){
 const doc={defaultView:new EventTarget(),createElement(tag){return new Element(tag,this);},createElementNS(_,tag){return this.createElement(tag);}};
 const viewport=doc.createElement('div'),canvas=doc.createElement('div'),nodes=doc.createElement('div'),wires=doc.createElement('svg');
 const graph={nodes:[{id:'a',kind:'source',x:10,y:20},{id:'b',kind:'transform',x:250,y:20}],edges:[],controlEdges:[]};
 const connections=[],moves=[],selections=[];
 const editor=createFlowGraphEditor({viewport,canvas,nodes,wires,getGraph:()=>graph,describeNode:node=>({title:node.kind,
  ports:node.id==='a'?[{id:'value',direction:'out',type:'number',label:'Value'}]:[{id:'value',direction:'in',type:'number',label:'Value'}],
  executionPorts:node.id==='a'?[{id:'then',direction:'out',label:'Next'}]:[{id:'exec',direction:'in',label:'Run'}]}),
  onConnect:connection=>connections.push(connection),onMove:(node,position)=>{Object.assign(node,position);moves.push(position);},onSelect:node=>selections.push(node.id),...overrides});
 editor.render();return {editor,viewport,canvas,nodes,wires,graph,connections,moves,selections};
}

test('host can frame a graph without changing node coordinates or another editor camera',()=>{
 const a=setup(),b=setup();
 a.editor.setCamera({x:25,y:40,zoom:.5});
 assert.deepEqual(a.editor.getCamera(),{x:25,y:40,zoom:.5});
 assert.deepEqual(b.editor.getCamera(),{x:0,y:0,zoom:1});
 assert.deepEqual(a.graph.nodes[0],{id:'a',kind:'source',x:10,y:20});
 assert.throws(()=>a.editor.setCamera({x:0,y:0,zoom:NaN}));
 const card=a.nodes.children[0];card.fire('pointerdown',{button:0,pointerId:1,clientX:10,clientY:20});
 card.fire('pointermove',{pointerId:1,clientX:40,clientY:50});card.fire('pointerup',{pointerId:1});
 assert.deepEqual(a.moves,[{x:70,y:80}]);
 a.editor.destroy();b.editor.destroy();
});
test('optional host badge identifies a node without changing selection or unlabelled nodes',()=>{
 const s=setup({describeNode:node=>({title:node.kind,badge:node.id==='a'?'Video':undefined,ports:[],executionPorts:[]})});
 const title=s.nodes.children[0].children[0].children[1];
 const badge=title.children.find(child=>child.className==='node-badge');
 assert.equal(badge.textContent,'Video');
 assert.equal(title.attrs['aria-label'],'source, Video library');
 assert.equal(s.nodes.children[1].children[0].children[1].children.length,0);
 title.fire('keydown',{key:'Enter'});assert.deepEqual(s.selections,['a']);
 s.editor.destroy();
});
test('removed dynamic port leaves a visible removable dashed wire',()=>{
 const removed=[],s=setup({onDisconnect:e=>removed.push(e)});
 const edge={source:'a',sourcePort:'removed',target:'b',targetPort:'value'};
 s.graph.edges.push(edge);s.editor.render();
 assert.equal(s.wires.children.length,1);
 assert.equal(s.wires.children[0].attrs['stroke-dasharray'],'6 4');
 s.wires.children[0].fire('click');assert.deepEqual(removed,[edge]);s.editor.destroy();
});

test('loaded incompatible wires are visibly diagnosed without being removed',()=>{
 const edge={source:'a',sourcePort:'value',target:'b',targetPort:'value'};
 const s=setup({describeNode:node=>({title:node.kind,ports:node.id==='a'?[{id:'value',direction:'out',type:'string',label:'Value'}]:[{id:'value',direction:'in',type:'object',label:'Value',diagnostic:'Incompatible saved wire.'}],executionPorts:[],wireDiagnostics:node.id==='b'?[{source:'a',sourcePort:'value',target:'b',targetPort:'value',port:'value',reason:'Incompatible saved wire.'}]:[]})});
 s.graph.edges.push(edge);s.editor.render();
 assert.match(s.wires.children[0].className,/invalid-wire/);
 assert.equal(s.nodes.children[1].children[1].children.at(-1).className,'node-diagnostics');
 assert.deepEqual(s.graph.edges,[edge]);s.editor.destroy();
});

test('generic nodes connect data and execution separately without running a workflow',()=>{
 const s=setup(),buttons=()=>s.nodes.querySelectorAll('button.port');
 buttons().find(x=>x.dataset.portId==='value'&&x.dataset.direction==='out').fire('click');
 buttons().find(x=>x.dataset.portId==='exec').fire('click');
 assert.equal(s.connections.length,0);
 buttons().find(x=>x.dataset.portId==='value'&&x.dataset.direction==='in').fire('click');
 buttons().find(x=>x.dataset.portId==='then').fire('click');
 buttons().find(x=>x.dataset.portId==='exec').fire('click');
 assert.deepEqual(s.connections,[{kind:'data',source:'a',sourcePort:'value',target:'b',targetPort:'value'},
  {kind:'execution',source:'a',sourcePort:'then',target:'b',targetPort:'exec'}]);
 assert.deepEqual(s.graph.edges,[]);assert.deepEqual(s.graph.controlEdges,[]);s.editor.destroy();
});

test('execution ports render decorative SVG icons while retaining accessible labels',()=>{
 const s=setup();
 for(const port of s.nodes.querySelectorAll('button.port')){
  const icons=port.children.filter(child=>child.tagName==='svg');
  if(port.dataset.wireKind==='data'){assert.equal(icons.length,0);continue;}
  assert.equal(icons.length,1);
  assert.equal(icons[0].attrs.viewBox,'0 0 14 14');
  assert.equal(icons[0].attrs['aria-hidden'],'true');
  assert.equal(icons[0].attrs['pointer-events'],'none');
  assert.equal(icons[0].children[0].attrs.fill,'none');
  assert.match(port.attrs['aria-label'],/Next|Run/);
  assert.doesNotMatch(port.textContent??'',/▷/);
 }
 s.editor.destroy();
});

test('execution arrows fill only connected ports and clear after disconnecting the last wire',()=>{
 const s=setup({describeNode:node=>({title:node.kind,ports:[],executionPorts:node.id==='a'
  ?[{id:'yes',direction:'out',label:'Yes'},{id:'no',direction:'out',label:'No'}]
  :[{id:'exec',direction:'in',label:'Run'}]}),
  onConnect:({kind,...edge})=>s.graph.controlEdges.push(edge),
  onDisconnect:edge=>{s.graph.controlEdges=s.graph.controlEdges.filter(e=>e!==edge);}});
 const port=id=>s.nodes.querySelectorAll('button.port').find(p=>p.dataset.portId===id);
 const fill=id=>port(id).children.find(c=>c.tagName==='svg').children[0].attrs.fill;
 port('yes').fire('click');assert.equal(fill('yes'),'none');
 port('exec').fire('click');
 assert.equal(fill('yes'),'currentColor');assert.equal(fill('exec'),'currentColor');assert.equal(fill('no'),'none');
 port('no').fire('click');port('exec').fire('click');
 s.wires.children[0].fire('click');
 assert.equal(fill('yes'),'none');assert.equal(fill('no'),'currentColor');assert.equal(fill('exec'),'currentColor');
 s.wires.children[0].fire('click');
 assert.equal(fill('no'),'none');assert.equal(fill('exec'),'none');
 s.graph.edges.push({source:'a',sourcePort:'yes',target:'b',targetPort:'exec'});s.editor.redrawWires();
 assert.equal(fill('yes'),'none');assert.equal(fill('exec'),'none');
 s.editor.destroy();
});

test('wire removal preserves its kind and readonly blocks edits',()=>{
 let locked=false;const removed=[];
 const s=setup({isReadonly:()=>locked,onDisconnect:(edge,kind)=>removed.push({edge,kind})});
 const edge={source:'a',sourcePort:'then',target:'b',targetPort:'exec'};
 s.graph.controlEdges.push(edge);s.editor.render();
 assert.equal(s.wires.children.length,1);
 assert.match(s.wires.children[0].className,/execution-wire/);
 s.wires.children[0].fire('keydown',{key:'Delete'});
 assert.deepEqual(removed,[{edge,kind:'execution'}]);
 locked=true;s.editor.render();
 s.wires.children[0].fire('click');
 for(const port of s.nodes.querySelectorAll('button.port')){assert.equal(port.disabled,true);port.fire('click');}
 s.nodes.children[0].fire('pointerdown',{button:0,pointerId:1,clientX:10,clientY:20});
 assert.equal(removed.length,1);assert.deepEqual(s.connections,[]);assert.deepEqual(s.moves,[]);s.editor.destroy();
});

test('package source has no host imports or Filomena domain types',()=>{
 const directory=new URL('../src/',import.meta.url);
 for(const file of readdirSync(directory).filter(file=>file.endsWith('.ts'))){
  const source=readFileSync(new URL(file,directory),'utf8');
  assert.doesNotMatch(source,/flow-model|flow-catalog|fetch\(|localStorage|sessionStorage|whatsapp|graphiti|lmstudio|targetStateId/i);
  for(const match of source.matchAll(/from\s+['"]([^'"]+)['"]/g))assert.match(match[1],/^\.\/[^/]+\.js$/);
 }
});
test('node drag reports coordinates; destroy releases listeners and does not affect another editor',()=>{
 const a=setup(),b=setup();
 const card=a.nodes.children[0];card.fire('pointerdown',{button:0,pointerId:1,clientX:10,clientY:20});
 card.fire('pointermove',{pointerId:1,clientX:40,clientY:70});card.fire('pointerup',{pointerId:1});
 assert.deepEqual(a.moves,[{x:40,y:70}]);assert.deepEqual(a.selections,['a']);assert.deepEqual(b.moves,[]);
 a.editor.destroy();a.editor.destroy();
 a.viewport.fire('wheel',{deltaY:100,deltaMode:0,clientX:0,clientY:0});assert.equal(a.editor.getCamera().zoom,1);
 b.viewport.fire('wheel',{deltaY:100,deltaMode:0,clientX:0,clientY:0});assert.notEqual(b.editor.getCamera().zoom,1);b.editor.destroy();
});

test('semantic node shape keeps execution mode, structured types, diagnostics and navigation accessible',()=>{
 let navigated=null;const s=setup({describeNode:node=>node.id==='a'
  ?{title:'Pure profile',semantic:'pure',effect:'Pure',ports:[{id:'profile',direction:'out',label:'Profile',schema:{type:'array',items:{type:'object',properties:{name:{type:'string'}}}}}],executionPorts:[],navigation:'profile-graph'}
  :{title:'Unavailable',semantic:'unavailable',diagnostics:[{port:'input',reason:'Node metadata unavailable.'}],ports:[{id:'input',direction:'in',label:'Input',type:'unresolved',unresolved:true}],executionPorts:[]},
  onNavigate:(node,reference)=>{navigated={id:node.id,reference};}});
 const [source,target]=s.nodes.children;assert.equal(source.dataset.semantic,'pure');assert.equal(target.dataset.semantic,'unavailable');
 const output=s.nodes.querySelectorAll('button.port')[0];assert.match(output.textContent,/array<object/);assert.match(output.title,/properties/);
 const before=s.editor.getCamera();const navigate=source.children[1].children.find(child=>child.className==='node-navigation').children[0];navigate.fire('click');
 assert.deepEqual(navigated,{id:'a',reference:'profile-graph'});assert.deepEqual(s.editor.getCamera(),before);
 assert.equal(target.children[1].children.at(-1).className,'node-diagnostics');s.editor.destroy();
});

test('typed dictionaries display their value type in the existing data port label',()=>{
 const s=setup({describeNode:node=>({title:node.kind,ports:node.id==='a'
  ?[{id:'budget',direction:'out',label:'Budget',schema:{type:'object',additionalProperties:{type:'integer'}}}]
  :[],executionPorts:[]})});
 const port=s.nodes.querySelectorAll('button.port').find(item=>item.dataset.portId==='budget');
 assert.match(port.textContent,/dictionary<integer>/);
 assert.match(port.attrs['aria-label'],/dictionary<integer>/);
 s.editor.destroy();
});

test('raw JSON uses a clear type label on the existing data port',()=>{
 const s=setup({describeNode:node=>({title:node.kind,ports:node.id==='a'
  ?[{id:'payload',direction:'out',label:'Payload',type:'json',schema:{}}]
  :[],executionPorts:[]})});
 const port=s.nodes.querySelectorAll('button.port').find(item=>item.dataset.portId==='payload');
 assert.match(port.textContent,/json/);assert.match(port.attrs['aria-label'],/json/);s.editor.destroy();
});

test('failed async connection validation reports the error and leaves graph untouched',async()=>{
 let message='';const s=setup({onConnect:async()=>{throw new Error('Data port types are incompatible.');},onStatus:text=>message=text});
 const buttons=()=>s.nodes.querySelectorAll('button.port');
 buttons().find(port=>port.dataset.direction==='out').fire('click');buttons().find(port=>port.dataset.direction==='in').fire('click');
 await Promise.resolve();assert.equal(message,'Data port types are incompatible.');assert.deepEqual(s.graph.edges,[]);
 assert.equal(s.nodes.children[1].children[1].children.find(child=>child.className==='connection-error').attrs.role,'alert');
 assert.equal(s.nodes.querySelectorAll('button.port').find(port=>port.dataset.direction==='in').attrs['aria-invalid'],'true');s.editor.destroy();
});

test('superseded async connection completion cannot clear a newer port alert',async()=>{
 let settleOld;let calls=0;const s=setup({onConnect:()=>{calls++;if(calls===1)return new Promise(resolve=>{settleOld=resolve;});return Promise.reject(new Error('Newer connection was rejected.'));}});
 const connect=()=>{const buttons=()=>s.nodes.querySelectorAll('button.port');buttons().find(port=>port.dataset.direction==='out').fire('click');buttons().find(port=>port.dataset.direction==='in').fire('click');};
 connect();
 // Switching state/project clears the pending connection while its host callback remains pending.
 s.editor.clearConnection();
 connect();await Promise.resolve();await Promise.resolve();
 const alert=()=>s.nodes.children[1].children[1].children.find(child=>child.className==='connection-error')?.textContent;
 assert.equal(calls,2);await new Promise(resolve=>setImmediate(resolve));assert.equal(alert(),'Newer connection was rejected.');
 settleOld();await Promise.resolve();await Promise.resolve();
 assert.equal(alert(),'Newer connection was rejected.');s.editor.destroy();
});
