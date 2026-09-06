// UI-only sizing. No graph data, model state, or persistence is involved.
export function attachSplitter(handle,{axis,reverse=false,read,write}) {
 let drag=null;
 const coordinate=event=>axis==='x'?event.clientX:event.clientY;
 const apply=value=>{
  const bounds=read(),next=Math.max(bounds.min,Math.min(bounds.max,value));
  write(next);
  handle.setAttribute('aria-valuemin',Math.round(bounds.min));
  handle.setAttribute('aria-valuemax',Math.round(bounds.max));
  handle.setAttribute('aria-valuenow',Math.round(next));
 };
 const refresh=()=>{
  const bounds=read();
  handle.setAttribute('aria-valuemin',Math.round(bounds.min));
  handle.setAttribute('aria-valuemax',Math.round(bounds.max));
  handle.setAttribute('aria-valuenow',Math.round(bounds.value));
 };
 handle.addEventListener('pointerdown',event=>{
  if(event.button!==0||drag)return;
  event.preventDefault();drag={id:event.pointerId,start:coordinate(event),value:read().value};
  handle.setPointerCapture(event.pointerId);
 });
 handle.addEventListener('pointermove',event=>{
  if(drag?.id!==event.pointerId)return;
  apply(drag.value+(coordinate(event)-drag.start)*(reverse?-1:1));
 });
 const end=event=>{
  if(drag?.id!==event.pointerId)return;
  drag=null;
  if(handle.hasPointerCapture(event.pointerId))handle.releasePointerCapture(event.pointerId);
 };
 for(const name of ['pointerup','pointercancel','lostpointercapture'])handle.addEventListener(name,end);
 handle.addEventListener('keydown',event=>{
  const bounds=read(),negative=axis==='x'?'ArrowLeft':'ArrowUp',positive=axis==='x'?'ArrowRight':'ArrowDown';
  if(![negative,positive,'Home','End'].includes(event.key))return;
  event.preventDefault();
  apply(event.key==='Home'?bounds.min:event.key==='End'?bounds.max:bounds.value+(event.key===positive?20:-20)*(reverse?-1:1));
 });
 refresh();
 return refresh;
}

export function setupFlowLayout(onResize) {
 const main=document.querySelector('main'),runner=document.querySelector('.runner'),inspector=document.querySelector('aside');
 const panels=runner.querySelectorAll(':scope > .run-panel');
 const set=(name,value)=>{document.documentElement.style.setProperty(name,value);onResize();};
 const refresh=[];
 refresh.push(attachSplitter(document.getElementById('height-divider'),{
  axis:'y',read:()=>({value:main.getBoundingClientRect().height,min:260,max:main.getBoundingClientRect().height+runner.getBoundingClientRect().height-200}),
  write:value=>{
   const total=main.getBoundingClientRect().height+runner.getBoundingClientRect().height;
   set('--editor-track',value+'fr');set('--runner-track',(total-value)+'fr');
  }
 }));
 refresh.push(attachSplitter(document.getElementById('inspector-divider'),{
  axis:'x',reverse:true,read:()=>({value:inspector.getBoundingClientRect().width,min:230,max:Math.max(230,main.clientWidth-330)}),
  write:value=>set('--inspector-width',value+'px')
 }));
 refresh.push(attachSplitter(document.getElementById('io-divider'),{
  axis:'x',read:()=>({value:panels[0].getBoundingClientRect().width,min:180,max:Math.max(180,panels[0].getBoundingClientRect().width+panels[1].getBoundingClientRect().width-180)}),
  write:value=>set('--input-ratio',value/(panels[0].getBoundingClientRect().width+panels[1].getBoundingClientRect().width))
 }));
 window.addEventListener('resize',()=>refresh.forEach(update=>update()));
}
