export interface Point {x:number;y:number}
export interface Camera extends Point {zoom:number}
export function screenToWorld(camera:Camera,point:Point):Point {return {x:(point.x-camera.x)/camera.zoom,y:(point.y-camera.y)/camera.zoom};}
export function zoomAt(camera:Camera,cursor:Point,requestedZoom:number):Camera {
 const zoom=Math.max(.25,Math.min(3,requestedZoom)),world=screenToWorld(camera,cursor);
 return {x:cursor.x-world.x*zoom,y:cursor.y-world.y*zoom,zoom};
}
export function attachCamera(viewport:HTMLElement,onChange:(state:Camera)=>void){
 let state:Camera={x:0,y:0,zoom:1},drag:{id:number;x:number;y:number;initial:Camera}|null=null,editingPointer:number|null=null;
 const cleanup:(()=>void)[]=[];
 const listen=(target:EventTarget|null|undefined,type:string,handler:EventListener,options?:AddEventListenerOptions)=>{
  if(!target)return;target.addEventListener(type,handler,options);cleanup.push(()=>target.removeEventListener(type,handler,options));
 };
 const update=(next:Camera)=>{state=next;onChange({...state});};
 const finish=()=>{const id=drag?.id;drag=null;viewport.classList.toggle('camera-panning',false);if(id!=null&&viewport.hasPointerCapture(id))viewport.releasePointerCapture(id);};
 listen(viewport,'pointerdown',raw=>{const event=raw as PointerEvent;
  if(event.button===0){editingPointer=event.pointerId;return;}
  if(![1,2].includes(event.button)||drag||editingPointer!==null)return;
  event.preventDefault();event.stopPropagation();drag={id:event.pointerId,x:event.clientX,y:event.clientY,initial:{...state}};
  viewport.setPointerCapture(event.pointerId);viewport.classList.toggle('camera-panning',true);
 },{capture:true});
 listen(viewport,'pointermove',raw=>{const event=raw as PointerEvent;
  if(!drag||drag.id!==event.pointerId)return;event.preventDefault();event.stopPropagation();
  update({...state,x:drag.initial.x+event.clientX-drag.x,y:drag.initial.y+event.clientY-drag.y});
 },{capture:true});
 const end:EventListener=raw=>{const event=raw as PointerEvent;if(editingPointer===event.pointerId)editingPointer=null;if(drag?.id===event.pointerId)finish();};
 for(const type of ['pointerup','pointercancel','lostpointercapture'])listen(viewport,type,end,{capture:true});
 for(const type of ['pointerup','pointercancel'])listen(viewport.ownerDocument?.defaultView,type,end,{capture:true});
 listen(viewport.ownerDocument?.defaultView,'blur',()=>{editingPointer=null;finish();});
 listen(viewport,'contextmenu',event=>event.preventDefault());
 listen(viewport,'auxclick',raw=>{const event=raw as MouseEvent;if([1,2].includes(event.button)){event.preventDefault();event.stopPropagation();}});
 listen(viewport,'wheel',raw=>{const event=raw as WheelEvent;
  event.preventDefault();event.stopPropagation();if(drag||editingPointer!==null)return;
  const bounds=viewport.getBoundingClientRect(),delta=event.deltaY*(event.deltaMode===1?16:event.deltaMode===2?bounds.height:1);
  if(!Number.isFinite(delta))return;
  update(zoomAt(state,{x:event.clientX-bounds.left,y:event.clientY-bounds.top},state.zoom*Math.exp(-delta*.0015)));
 },{passive:false});
 update(state);
 return {get:()=>({...state}),set(next:Camera){if(![next.x,next.y,next.zoom].every(Number.isFinite))throw new Error('Camera coordinates must be finite.');finish();update({...next,zoom:Math.max(.25,Math.min(3,next.zoom))});},destroy(){for(const remove of cleanup.splice(0))remove();editingPointer=null;finish();}};
}
