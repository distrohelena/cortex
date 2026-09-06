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
test('removed dynamic port leaves a visible removable dashed wire',()=>{
 const removed=[],s=setup({onDisconnect:e=>removed.push(e)});
 const edge={source:'a',sourcePort:'removed',target:'b',targetPort:'value'};
 s.graph.edges.push(edge);s.editor.render();
 assert.equal(s.wires.children.length,1);
 assert.equal(s.wires.children[0].attrs['stroke-dasharray'],'6 4');
 s.wires.children[0].fire('click');assert.deepEqual(removed,[edge]);s.editor.destroy();
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
