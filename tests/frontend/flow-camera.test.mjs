import test from 'node:test';
import assert from 'node:assert/strict';
import { attachCamera, screenToWorld, zoomAt } from '../../src/Filomena.App/Playground/flow-camera.mjs';

class Viewport extends EventTarget {
 classList={toggle:()=>{}};
 ownerDocument={defaultView:new EventTarget()};
 getBoundingClientRect(){return {left:50,top:100,height:600};}
 setPointerCapture(id){this.captured=id;}
 hasPointerCapture(id){return this.captured===id;}
 releasePointerCapture(){this.captured=null;}
 fire(type,fields={}){const event=new Event(type,{cancelable:true});Object.assign(event,fields);this.dispatchEvent(event);return event;}
}

for(const button of [1,2])test(`mouse button ${button} pans without clamping to positive coordinates`,()=>{
 const view=new Viewport(),camera=attachCamera(view,()=>{});
 assert.equal(view.fire('pointerdown',{button,pointerId:1,clientX:200,clientY:200}).defaultPrevented,true);
 view.fire('pointermove',{pointerId:1,clientX:20,clientY:300});
 assert.deepEqual(camera.get(),{x:-180,y:100,zoom:1});
 view.fire('pointerup',{pointerId:1});
 view.fire('pointermove',{pointerId:1,clientX:500,clientY:500});
 assert.deepEqual(camera.get(),{x:-180,y:100,zoom:1});
 assert.equal(view.captured,null);
});

test('left button remains available for editing and wheel does not disrupt a node drag',()=>{
 const view=new Viewport(),camera=attachCamera(view,()=>{});
 assert.equal(view.fire('pointerdown',{button:0,pointerId:1,clientX:100,clientY:200}).defaultPrevented,false);
 view.fire('pointermove',{pointerId:1,clientX:500,clientY:500});
 view.fire('wheel',{deltaY:-100,deltaMode:0,clientX:100,clientY:200});
 assert.deepEqual(camera.get(),{x:0,y:0,zoom:1});
 view.fire('pointerup',{pointerId:1});
 view.fire('wheel',{deltaY:-100,deltaMode:0,clientX:100,clientY:200});
 assert.ok(camera.get().zoom>1);
});

test('zoom holds the world point under the cursor and clamps at safe scales',()=>{
 const original={x:120,y:-50,zoom:1},cursor={x:300,y:200};
 assert.deepEqual(zoomAt(original,cursor,2),{x:-60,y:-300,zoom:2});
 assert.deepEqual(screenToWorld(zoomAt(original,cursor,2),cursor),{x:180,y:250});
 assert.equal(zoomAt(original,cursor,1000).zoom,3);
 assert.equal(zoomAt(original,cursor,0).zoom,.25);
 // Dragging 100 screen pixels at 2x moves only 50 world units.
 assert.equal(screenToWorld({x:0,y:0,zoom:2},{x:100,y:0}).x,50);
});

test('wheel uses viewport-local cursor, not page coordinates',()=>{
 const view=new Viewport(),camera=attachCamera(view,()=>{});
 const event=view.fire('wheel',{deltaY:-200,deltaMode:0,clientX:250,clientY:300});
 assert.equal(event.defaultPrevented,true);
 const point=screenToWorld(camera.get(),{x:200,y:200});
 assert.ok(Math.abs(point.x-200)<1e-9);assert.ok(Math.abs(point.y-200)<1e-9);
});

test('releasing a left click outside the viewport does not leave zoom locked',()=>{
 const view=new Viewport(),camera=attachCamera(view,()=>{});
 view.fire('pointerdown',{button:0,pointerId:5,clientX:100,clientY:100});
 const release=new Event('pointerup');Object.assign(release,{pointerId:5});
 view.ownerDocument.defaultView.dispatchEvent(release);
 view.fire('wheel',{deltaY:-100,deltaMode:0,clientX:100,clientY:100});
 assert.ok(camera.get().zoom>1);
});

test('cancel, lost capture, and blur end panning; native auxiliary actions are suppressed',()=>{
 for(const ending of ['pointercancel','lostpointercapture','blur']){
  const view=new Viewport(),camera=attachCamera(view,()=>{});
  view.fire('pointerdown',{button:2,pointerId:1,clientX:200,clientY:200});
  view.fire('pointermove',{pointerId:2,clientX:999,clientY:999});
  if(ending==='blur')view.ownerDocument.defaultView.dispatchEvent(new Event('blur'));
  else view.fire(ending,{pointerId:1});
  view.fire('pointermove',{pointerId:1,clientX:999,clientY:999});
  assert.deepEqual(camera.get(),{x:0,y:0,zoom:1});
  assert.equal(view.fire('contextmenu').defaultPrevented,true);
  assert.equal(view.fire('auxclick',{button:1}).defaultPrevented,true);
 }
});
