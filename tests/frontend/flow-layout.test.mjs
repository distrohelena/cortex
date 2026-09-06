import test from 'node:test';
import assert from 'node:assert/strict';
import { attachSplitter, setupFlowLayout } from '../../src/Filomena.App/Playground/flow-layout.mjs';

class Handle extends EventTarget {
 attributes=new Map();
 setAttribute(key,value){this.attributes.set(key,String(value));}
 setPointerCapture(id){this.captured=id;}
 hasPointerCapture(id){return this.captured===id;}
 releasePointerCapture(){this.captured=null;}
 fire(type,fields){const event=new Event(type,{cancelable:true});Object.assign(event,fields);this.dispatchEvent(event);return event;}
}
function fixture(reverse=false){
 const handle=new Handle();let value=400;
 attachSplitter(handle,{axis:'x',reverse,read:()=>({value,min:200,max:700}),write:next=>value=next});
 return {handle,value:()=>value};
}
test('drag adjusts panel size, respects limits and ignores other pointers',()=>{
 const f=fixture();f.handle.fire('pointerdown',{button:0,pointerId:1,clientX:100});
 f.handle.fire('pointermove',{pointerId:2,clientX:600});assert.equal(f.value(),400);
 f.handle.fire('pointermove',{pointerId:1,clientX:180});assert.equal(f.value(),480);
 f.handle.fire('pointermove',{pointerId:1,clientX:1000});assert.equal(f.value(),700);
 f.handle.fire('pointermove',{pointerId:1,clientX:-500});assert.equal(f.value(),200);
});
test('inspector grows when its divider moves left; cancellation ends resizing',()=>{
 const f=fixture(true);f.handle.fire('pointerdown',{button:0,pointerId:3,clientX:100});
 f.handle.fire('pointermove',{pointerId:3,clientX:50});assert.equal(f.value(),450);
 f.handle.fire('pointercancel',{pointerId:3});
 f.handle.fire('pointermove',{pointerId:3,clientX:0});assert.equal(f.value(),450);
 assert.equal(f.handle.captured,null);
});
test('keyboard resizes, clamps and exposes the current accessible value',()=>{
 const f=fixture();f.handle.fire('keydown',{key:'ArrowRight'});assert.equal(f.value(),420);
 f.handle.fire('keydown',{key:'Home'});assert.equal(f.value(),200);
 f.handle.fire('keydown',{key:'ArrowLeft'});assert.equal(f.value(),200);
 f.handle.fire('keydown',{key:'End'});assert.equal(f.value(),700);
 assert.equal(f.handle.attributes.get('aria-valuenow'),'700');
});

test('vertical resizing stores relative viewport tracks rather than fixed pixel heights',t=>{
 const priorDocument=Object.getOwnPropertyDescriptor(globalThis,'document'),priorWindow=Object.getOwnPropertyDescriptor(globalThis,'window');
 t.after(()=>{
  if(priorDocument)Object.defineProperty(globalThis,'document',priorDocument);else delete globalThis.document;
  if(priorWindow)Object.defineProperty(globalThis,'window',priorWindow);else delete globalThis.window;
 });
 const properties=new Map(),handles=new Map(['height-divider','inspector-divider','io-divider'].map(id=>[id,new Handle()]));
 const main={clientWidth:1200,getBoundingClientRect:()=>({height:500})};
 const runner={getBoundingClientRect:()=>({height:250}),querySelectorAll:()=>[0,1].map(()=>({getBoundingClientRect:()=>({width:500})}))};
 const inspector={getBoundingClientRect:()=>({width:310})};
 globalThis.document={querySelector:selector=>({'main':main,'.runner':runner,'aside':inspector}[selector]),getElementById:id=>handles.get(id),documentElement:{style:{setProperty:(key,value)=>properties.set(key,value)}}};
 globalThis.window=new EventTarget();
 setupFlowLayout(()=>{});
 handles.get('height-divider').fire('pointerdown',{button:0,pointerId:1,clientY:500});
 handles.get('height-divider').fire('pointermove',{pointerId:1,clientY:400});
 assert.equal(properties.get('--editor-track'),'400fr');
 assert.equal(properties.get('--runner-track'),'350fr');
 assert.equal(properties.has('--editor-height'),false);
 assert.equal(properties.has('--runner-height'),false);
});
