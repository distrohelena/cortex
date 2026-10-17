import {attachCamera,screenToWorld,type Point,type Camera} from './camera.js';
export {attachCamera,screenToWorld,zoomAt} from './camera.js';
export type {Point,Camera} from './camera.js';
export interface GraphNode {id:string;kind:string;x:number;y:number}
export interface GraphEdge {source:string;target:string;sourcePort?:string;targetPort?:string}
export interface GraphDocument<N extends GraphNode=GraphNode> {nodes:N[];edges:GraphEdge[];controlEdges?:GraphEdge[]}
export type WireKind='data'|'execution';
export interface Connection extends GraphEdge {kind:WireKind;sourcePort:string;targetPort:string}
export interface PortView {id:string;direction:'in'|'out';label:string;type?:string;typeLabel?:string;schema?:unknown;required?:boolean;hasDefault?:boolean;defaultValue?:unknown;editableDefault?:boolean;unresolved?:boolean;title?:string;interactive?:boolean;diagnostic?:string}
export type NodeSemanticKind='event'|'operation'|'pure'|'note'|'unavailable';
export interface NodeView {title:string;semantic?:NodeSemanticKind;semanticLabel?:string;effect?:string;badge?:string;className?:string;portsClassName?:string;preview?:string;diagnostics?:Array<{port?:string;reason?:string}>;wireDiagnostics?:Array<{source:string;sourcePort:string;target:string;targetPort:string;port:string;reason:string}>;navigation?:string;fields?:Array<{name:string;label:string;required?:boolean;multiline?:boolean;schema?:unknown}>;ports:PortView[];executionPorts:PortView[]}
export interface EditorOptions<N extends GraphNode> {
 viewport:HTMLElement;canvas:HTMLElement;nodes:HTMLElement;wires:SVGElement;
 getGraph:()=>GraphDocument<N>;describeNode:(node:N)=>NodeView;
 getSelectedId?:()=>string;isReadonly?:()=>boolean;
 onSelect?:(node:N)=>void;onConnect?:(edge:Connection)=>void|Promise<void>;
 onNavigate?:(node:N,reference:string)=>void;
 onDisconnect?:(edge:GraphEdge,kind:WireKind)=>void;
 onMove?:(node:N,position:Point)=>void;onMoveEnd?:(node:N)=>void;onStatus?:(text:string)=>void;
 positionBounds?:{min:number;max:number};
 labels?:Partial<{chooseOutput:string;chooseExecution:string;legacyWire:string;removeWire:string;input:string;output:string;navigate:string;required:string;optional:string;unresolved:string}>;
}
export function createFlowGraphEditor<N extends GraphNode>(options:EditorOptions<N>){
 const {viewport,canvas,nodes,wires}=options,doc=viewport.ownerDocument;
 const labels={chooseOutput:'Choose a data output first.',chooseExecution:'Choose an execution output first.',legacyWire:'Reconnect this legacy wire to explicit ports.',removeWire:'Remove connection',input:'Input',output:'Output',navigate:'Open function',required:'Required',optional:'Optional',unresolved:'Type unresolved',...options.labels};
 const cards=new Map<string,HTMLElement>();
 const executionIcons=new Map<HTMLElement,SVGPathElement>();
 let pending:{nodeId:string;portId:string;kind:WireKind}|null=null,destroyed=false,dragCleanup:(()=>void)|null=null;
 let connectionError:{nodeId:string;portId:string;message:string}|null=null,connectionRequest=0;
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
   const targetNode=graph.nodes.find(node=>node.id===edge.target);
   const invalid=!!targetNode&&(options.describeNode(targetNode).wireDiagnostics??[]).some(issue=>issue.source===edge.source&&issue.sourcePort===edge.sourcePort&&issue.target===edge.target&&issue.targetPort===edge.targetPort);
   if(invalid){path.classList.add('invalid-wire');path.setAttribute('aria-label',`${labels.removeWire} ${edge.source} → ${edge.target}. ${options.describeNode(targetNode!).wireDiagnostics?.find(issue=>issue.source===edge.source&&issue.sourcePort===edge.sourcePort&&issue.target===edge.target&&issue.targetPort===edge.targetPort)?.reason??''}`);}
   if(!foundA||!foundB){path.setAttribute('stroke-dasharray','6 4');const title=doc.createElementNS('http://www.w3.org/2000/svg','title');title.textContent=labels.legacyWire;path.append(title);}else if(invalid){const title=doc.createElementNS('http://www.w3.org/2000/svg','title');title.textContent=options.describeNode(targetNode!).wireDiagnostics?.find(issue=>issue.source===edge.source&&issue.sourcePort===edge.sourcePort&&issue.target===edge.target&&issue.targetPort===edge.targetPort)?.reason??'Invalid connection';path.append(title);}
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
  if(connectionError?.nodeId===node.id&&connectionError.portId===spec.id){button.classList.add('invalid-port');button.setAttribute('aria-invalid','true');button.setAttribute('aria-description',connectionError.message);}
  const caption=spec.typeLabel??schemaLabel(spec.schema)??spec.type??'';
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
   const connection={kind,source:pending.nodeId,sourcePort:pending.portId,target:node.id,targetPort:spec.id};
   const request=++connectionRequest;
   try{const result=options.onConnect?.(connection);pending=null;if(result&&typeof (result as Promise<void>).then==='function')Promise.resolve(result).then(()=>{if(request!==connectionRequest)return;connectionError=null;render();},error=>{if(request!==connectionRequest)return;connectionError={nodeId:node.id,portId:spec.id,message:error instanceof Error?error.message:String(error)};status(connectionError.message);render();});else{connectionError=null;render();}}catch(error){connectionError={nodeId:node.id,portId:spec.id,message:error instanceof Error?error.message:String(error)};status(connectionError.message);render();}
  });return button;
 }
 function render(){
  if(destroyed)return;dragCleanup?.();nodes.replaceChildren();cards.clear();executionIcons.clear();const graph=options.getGraph();
  canvas.style.width=Math.max(1100,...graph.nodes.map(node=>node.x+270))+'px';canvas.style.height=Math.max(450,...graph.nodes.map(node=>node.y+210))+'px';
  for(const node of graph.nodes){
   const view=options.describeNode(node),card=doc.createElement('article');card.className=`node ${view.className??''}${node.id===options.getSelectedId?.()?' selected':''}`;card.dataset.nodeId=node.id;card.style.left=node.x+'px';card.style.top=node.y+'px';
   card.dataset.semantic=view.semantic??(view.className?.includes('unavailable')?'unavailable':'operation');if(view.effect)card.dataset.effect=view.effect;
   const heading=doc.createElement('header');heading.className='node-titlebar';const title=doc.createElement('span');title.className='node-title';title.textContent=view.title;title.tabIndex=0;title.setAttribute('role','button');
   if(view.badge){const badge=doc.createElement('span');badge.className='node-badge';badge.textContent=view.badge;badge.title=`Library: ${view.badge}`;title.append(badge);title.setAttribute('aria-label',`${view.title}, ${view.badge} library`);}
   const controls=(direction:'in'|'out')=>{const group=doc.createElement('span');group.className=`execution-ports ${direction}`;const ports=view.executionPorts.filter(p=>p.direction===direction);for(const port of ports)group.append(portButton(node,port,'execution',ports.length>1));return group;};
   const semantic=doc.createElement('span');semantic.className='node-semantic';semantic.textContent=view.semanticLabel??view.semantic??'Operation';semantic.setAttribute('aria-label',`${view.semanticLabel??view.semantic??'Operation'} node`);
   heading.append(controls('in'),title,controls('out'));const content=doc.createElement('div');content.className='node-content';content.append(semantic);
   card.append(heading);
   if(view.navigation&&options.onNavigate){const action=doc.createElement('div');action.className='node-navigation';const navigate=doc.createElement('button');navigate.type='button';navigate.className='node-navigate';navigate.textContent=labels.navigate;navigate.title=view.navigation;navigate.setAttribute('aria-label',`${labels.navigate}: ${view.title}`);navigate.addEventListener('click',event=>{event.stopPropagation();navigate.focus?.({preventScroll:true});options.onNavigate?.(node,view.navigation!);});action.append(navigate);content.append(action);}
   const ports=doc.createElement('div');ports.className=`ports ${view.portsClassName??'typed-ports'}`;for(const port of view.ports){const button=portButton(node,port,'data');button.classList.toggle('unresolved',port.unresolved===true);button.classList.toggle('invalid-port',!!port.diagnostic);if(port.diagnostic){button.title=port.diagnostic;button.setAttribute('aria-invalid','true');}else button.title=port.unresolved?`${port.label}: ${labels.unresolved}`:port.title??(port.schema?JSON.stringify(port.schema):port.label);button.dataset.required=String(port.required===true);if(port.schema)button.dataset.schema=JSON.stringify(port.schema);const descriptions=[port.required?labels.required:port.required===false?labels.optional:'',port.hasDefault?`Default ${JSON.stringify(port.defaultValue)}`:'',port.diagnostic??''].filter(Boolean);if(descriptions.length)button.setAttribute('aria-description',descriptions.join(', '));ports.append(button);}content.append(ports);
   const localError=connectionError?.nodeId===node.id?connectionError:null;if(localError){const message=doc.createElement('p');message.className='connection-error';message.setAttribute('role','alert');message.textContent=localError.message;content.append(message);}
   if(view.preview!==undefined){const preview=doc.createElement('div');preview.className='preview';preview.textContent=view.preview;content.append(preview);preview.addEventListener('click',()=>select());}
   const issues=[...(view.diagnostics??[]),...(view.wireDiagnostics??[]).map(issue=>({port:issue.port,reason:issue.reason}))];if(issues.length){const diagnostics=doc.createElement('ul');diagnostics.className='node-diagnostics';for(const issue of issues){const item=doc.createElement('li');item.textContent=[issue.port,issue.reason].filter(Boolean).join(': ');diagnostics.append(item);}content.append(diagnostics);}card.append(content);
   const select=()=>{options.onSelect?.(node);for(const [id,element] of cards)element.classList.toggle('selected',id===node.id);};
   heading.addEventListener('click',select);title.addEventListener('keydown',event=>{if(event.key==='Enter'){event.preventDefault();select();}});
   card.addEventListener('pointerdown',event=>{
    if(readonly()||event.button!==0||(event.target as Element).closest('button,input,textarea,select,a,summary,[contenteditable="true"],[role="button"]'))return;
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
 return {render,redrawWires,clearConnection(){pending=null;connectionRequest++;},getCamera:()=>camera.get(),setCamera:(next:Camera)=>camera.set(next),destroy(){if(destroyed)return;destroyed=true;connectionRequest++;dragCleanup?.();camera.destroy();doc.defaultView?.removeEventListener('resize',resize);nodes.replaceChildren();wires.replaceChildren();cards.clear();executionIcons.clear();pending=null;}};
}
function schemaLabel(schema:unknown):string|undefined{
 if(!schema||typeof schema!=='object'||Array.isArray(schema))return;
 const value=schema as Record<string,unknown>,type=value.type;
 if(Object.keys(value).length===0)return 'json';
 if(Array.isArray(type)){const names=type.filter((item):item is string=>typeof item==='string');return names.map(name=>name==='null'?'null':schemaLabel({...value,type:name})??name).join(' | ');}
 if(type==='array')return `array<${schemaLabel(value.items)??'item'}>`;
 if(type==='object'){
  if(value.additionalProperties&&typeof value.additionalProperties==='object'&&!Array.isArray(value.additionalProperties))return `dictionary<${schemaLabel(value.additionalProperties)??'value'}>`;
  const properties=value.properties;
  if(properties&&typeof properties==='object'&&!Array.isArray(properties)){
   const required=new Set(Array.isArray(value.required)?value.required.filter((item):item is string=>typeof item==='string'):[]);
   const fields=Object.entries(properties as Record<string,unknown>).map(([name,field])=>`${name}${required.has(name)?'!':''}: ${schemaLabel(field)??'value'}`);
   return fields.length?`object { ${fields.join(', ')} }`:'object';
  }
 }
 if(type==='integer')return 'integer';
 if(typeof type==='string')return type;
 return;
}
