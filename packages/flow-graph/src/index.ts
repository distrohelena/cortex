import {attachCamera,screenToWorld,type Point} from './camera.js';
export {attachCamera,screenToWorld,zoomAt} from './camera.js';
export type {Point,Camera} from './camera.js';
export interface GraphNode {id:string;kind:string;x:number;y:number}
export interface GraphEdge {source:string;target:string;sourcePort?:string;targetPort?:string}
export interface GraphDocument<N extends GraphNode=GraphNode> {nodes:N[];edges:GraphEdge[];controlEdges?:GraphEdge[]}
export type WireKind='data'|'execution';
export interface Connection extends GraphEdge {kind:WireKind;sourcePort:string;targetPort:string}
export interface PortView {id:string;direction:'in'|'out';label:string;type?:string;typeLabel?:string;title?:string;interactive?:boolean}
export interface NodeView {title:string;className?:string;portsClassName?:string;preview?:string;ports:PortView[];executionPorts:PortView[]}
export interface EditorOptions<N extends GraphNode> {
 viewport:HTMLElement;canvas:HTMLElement;nodes:HTMLElement;wires:SVGElement;
 getGraph:()=>GraphDocument<N>;describeNode:(node:N)=>NodeView;
 getSelectedId?:()=>string;isReadonly?:()=>boolean;
 onSelect?:(node:N)=>void;onConnect?:(edge:Connection)=>void;
 onDisconnect?:(edge:GraphEdge,kind:WireKind)=>void;
 onMove?:(node:N,position:Point)=>void;onMoveEnd?:(node:N)=>void;onStatus?:(text:string)=>void;
 positionBounds?:{min:number;max:number};
 labels?:Partial<{chooseOutput:string;chooseExecution:string;legacyWire:string;removeWire:string;input:string;output:string}>;
}
export function createFlowGraphEditor<N extends GraphNode>(options:EditorOptions<N>){
 const {viewport,canvas,nodes,wires}=options,doc=viewport.ownerDocument;
 const labels={chooseOutput:'Choose a data output first.',chooseExecution:'Choose an execution output first.',legacyWire:'Reconnect this legacy wire to explicit ports.',removeWire:'Remove connection',input:'Input',output:'Output',...options.labels};
 const cards=new Map<string,HTMLElement>();
 const executionIcons=new Map<HTMLElement,SVGPathElement>();
 let pending:{nodeId:string;portId:string;kind:WireKind}|null=null,destroyed=false,dragCleanup:(()=>void)|null=null;
 const readonly=()=>destroyed||options.isReadonly?.()===true;
 const status=(text:string)=>options.onStatus?.(text);
 viewport.classList.add('fg-viewport');canvas.classList.add('fg-canvas');nodes.classList.add('fg-nodes');wires.classList.add('fg-wires');
 const camera=attachCamera(viewport,state=>{
  for(const [key,value] of Object.entries({'--camera-x':state.x+'px','--camera-y':state.y+'px','--camera-zoom':String(state.zoom),'--grid-step':22*state.zoom+'px','--half-step':11*state.zoom+'px'}))viewport.style.setProperty(key,value);
 });
 function findPort(card:HTMLElement,direction:string,id:string,kind:WireKind){
  return Array.from(card.querySelectorAll<HTMLElement>('button.port')).find(button=>button.dataset.direction===direction&&button.dataset.portId===id&&button.dataset.wireKind===kind);
 }
 function redrawWires(){
  if(destroyed)return;wires.replaceChildren();const graph=options.getGraph();
  for(const icon of executionIcons.values())icon.setAttribute('fill','none');
  for(const {edge,kind} of [...graph.edges.map(edge=>({edge,kind:'data' as const})),...(graph.controlEdges??[]).map(edge=>({edge,kind:'execution' as const}))]){
   const from=cards.get(edge.source),to=cards.get(edge.target);if(!from||!to)continue;
   let sourcePort=edge.sourcePort,targetPort=edge.targetPort;
   if(!sourcePort||!targetPort){
    const outputs=from.querySelectorAll<HTMLElement>('button.port'),inputs=to.querySelectorAll<HTMLElement>('button.port');
    const out=Array.from(outputs).filter(p=>p.dataset.direction==='out'&&p.dataset.wireKind===kind),ins=Array.from(inputs).filter(p=>p.dataset.direction==='in'&&p.dataset.wireKind===kind);
    if(out.length===1&&ins.length===1){sourcePort=out[0].dataset.portId;targetPort=ins[0].dataset.portId;}
   }
   const foundA=sourcePort?findPort(from,'out',sourcePort,kind):null,foundB=targetPort?findPort(to,'in',targetPort,kind):null;
   const aPort=foundA??from,bPort=foundB??to;
   if(kind==='execution'&&sourcePort&&targetPort){
    executionIcons.get(aPort)?.setAttribute('fill','currentColor');
    executionIcons.get(bPort)?.setAttribute('fill','currentColor');
   }
   const path=doc.createElementNS('http://www.w3.org/2000/svg','path');if(kind==='execution')path.classList.add('execution-wire');
   if(!foundA||!foundB){path.setAttribute('stroke-dasharray','6 4');const title=doc.createElementNS('http://www.w3.org/2000/svg','title');title.textContent=labels.legacyWire;path.append(title);}
   const bounds=canvas.getBoundingClientRect(),a=aPort.getBoundingClientRect(),b=bPort.getBoundingClientRect(),transform={x:bounds.left,y:bounds.top,zoom:camera.get().zoom};
   const start=screenToWorld(transform,{x:a.right,y:a.top+a.height/2}),end=screenToWorld(transform,{x:b.left,y:b.top+b.height/2});
   const x1=start.x-5,x2=end.x+5,curve=Math.max(55,Math.abs(x2-x1)/2);
   path.setAttribute('d',`M ${x1} ${start.y} C ${x1+curve} ${start.y},${x2-curve} ${end.y},${x2} ${end.y}`);
   path.setAttribute('tabindex','0');path.setAttribute('role','button');path.setAttribute('aria-label',`${labels.removeWire} ${edge.source} → ${edge.target}`);
   const remove=()=>{if(readonly())return;options.onDisconnect?.(edge,kind);redrawWires();};
   path.addEventListener('click',remove);path.addEventListener('keydown',event=>{if(['Enter','Delete','Backspace'].includes(event.key)){event.preventDefault();remove();}});wires.append(path);
  }
 }
 function portButton(node:N,spec:PortView,kind:WireKind,multiple=false){
  const button=doc.createElement('button');button.className=`port ${spec.direction} ${kind==='execution'?'exec-port':spec.type+'-port'}`;
  button.dataset.direction=spec.direction;button.dataset.portId=spec.id;button.dataset.wireKind=kind;button.disabled=readonly();
  const caption=spec.typeLabel??spec.type??'';
  if(kind==='execution'){
   if(multiple){const label=doc.createElement('span');label.textContent=spec.label;button.append(label);}
   const icon=doc.createElementNS('http://www.w3.org/2000/svg','svg');
   for(const [key,value] of Object.entries({viewBox:'0 0 14 14',width:'14',height:'14','aria-hidden':'true',focusable:'false','pointer-events':'none'}))icon.setAttribute(key,value);
   const triangle=doc.createElementNS('http://www.w3.org/2000/svg','path');
   for(const [key,value] of Object.entries({d:'M 3 2 L 11 7 L 3 12 Z',fill:'none',stroke:'currentColor','stroke-width':'1.5','stroke-linejoin':'round'}))triangle.setAttribute(key,value);
   icon.append(triangle);button.append(icon);
   executionIcons.set(button,triangle);
  }else button.textContent=spec.direction==='in'?`● ${spec.label} (${caption})`:`${spec.label} (${caption}) ●`;
  button.title=spec.title??spec.label;button.setAttribute('aria-label',`${spec.direction==='in'?labels.input:labels.output} ${spec.label}${kind==='execution'?'':` (${caption})`}`);
  button.classList.toggle('active',pending?.kind===kind&&pending.nodeId===node.id&&pending.portId===spec.id);
  button.addEventListener('click',event=>{
   event.stopPropagation();if(readonly())return;
   if(spec.interactive===false){pending=null;status(spec.title??'');render();return;}
   if(spec.direction==='out'){pending={nodeId:node.id,portId:spec.id,kind};render();return;}
   if(pending?.kind!==kind){status(kind==='execution'?labels.chooseExecution:labels.chooseOutput);return;}
   try{options.onConnect?.({kind,source:pending.nodeId,sourcePort:pending.portId,target:node.id,targetPort:spec.id});pending=null;render();}catch(error){status(error instanceof Error?error.message:String(error));}
  });return button;
 }
 function render(){
  if(destroyed)return;dragCleanup?.();nodes.replaceChildren();cards.clear();executionIcons.clear();const graph=options.getGraph();
  canvas.style.width=Math.max(1100,...graph.nodes.map(node=>node.x+270))+'px';canvas.style.height=Math.max(450,...graph.nodes.map(node=>node.y+210))+'px';
  for(const node of graph.nodes){
   const view=options.describeNode(node),card=doc.createElement('article');card.className=`node ${view.className??''}${node.id===options.getSelectedId?.()?' selected':''}`;card.style.left=node.x+'px';card.style.top=node.y+'px';
   const heading=doc.createElement('header');heading.className='node-titlebar';const title=doc.createElement('span');title.className='node-title';title.textContent=view.title;title.tabIndex=0;title.setAttribute('role','button');
   const controls=(direction:'in'|'out')=>{const group=doc.createElement('span');group.className=`execution-ports ${direction}`;const ports=view.executionPorts.filter(p=>p.direction===direction);for(const port of ports)group.append(portButton(node,port,'execution',ports.length>1));return group;};
   heading.append(controls('in'),title,controls('out'));card.append(heading);
   const ports=doc.createElement('div');ports.className=`ports ${view.portsClassName??'typed-ports'}`;for(const port of view.ports)ports.append(portButton(node,port,'data'));card.append(ports);
   if(view.preview!==undefined){const preview=doc.createElement('div');preview.className='preview';preview.textContent=view.preview;card.append(preview);preview.addEventListener('click',()=>select());}
   const select=()=>{options.onSelect?.(node);for(const [id,element] of cards)element.classList.toggle('selected',id===node.id);};
   heading.addEventListener('click',select);title.addEventListener('keydown',event=>{if(event.key==='Enter'){event.preventDefault();select();}});
   card.addEventListener('pointerdown',event=>{
    if(readonly()||event.button!==0||(event.target as Element).closest('button.port'))return;
    event.preventDefault();select();const x=node.x,y=node.y,startX=event.clientX,startY=event.clientY,zoom=camera.get().zoom;card.setPointerCapture(event.pointerId);
    const bounds=options.positionBounds??{min:-Infinity,max:Infinity};
    const move=(e:PointerEvent)=>{if(e.pointerId!==event.pointerId||readonly())return;
     const position={x:Math.max(bounds.min,Math.min(bounds.max,x+(e.clientX-startX)/zoom)),y:Math.max(bounds.min,Math.min(bounds.max,y+(e.clientY-startY)/zoom))};
     options.onMove?.(node,position);card.style.left=position.x+'px';card.style.top=position.y+'px';redrawWires();};
    const cleanup=()=>{card.removeEventListener('pointermove',move);for(const type of ['pointerup','pointercancel','lostpointercapture'])card.removeEventListener(type,end as EventListener);dragCleanup=null;if(card.hasPointerCapture(event.pointerId))card.releasePointerCapture(event.pointerId);};
    const end=(e:PointerEvent)=>{if(e.pointerId!==event.pointerId)return;cleanup();options.onMoveEnd?.(node);render();};
    dragCleanup=cleanup;card.addEventListener('pointermove',move);for(const type of ['pointerup','pointercancel','lostpointercapture'])card.addEventListener(type,end as EventListener);
   });nodes.append(card);cards.set(node.id,card);
  }redrawWires();
 }
 const resize=()=>redrawWires();doc.defaultView?.addEventListener('resize',resize);
 return {render,redrawWires,clearConnection(){pending=null;},getCamera:()=>camera.get(),destroy(){if(destroyed)return;destroyed=true;dragCleanup?.();camera.destroy();doc.defaultView?.removeEventListener('resize',resize);nodes.replaceChildren();wires.replaceChildren();cards.clear();executionIcons.clear();pending=null;}};
}
