// Editor descriptors only. C# FlowType and executable validation are authoritative.
export const inputTypes=['text','number','boolean','enum','message','actor','channel','memory'].flatMap(t=>[t,t+'?',t+'[]',t+'?[]',t+'[]?']);
export const memoryCategories=['limite','apelido','relacao','fato','nao_classificada'];
export const typeNames={text:'Texto',number:'Número',boolean:'Booleano',enum:'Enum',message:'Mensagem',actor:'Pessoa',channel:'Canal'};
export const typeLabel=type=>type.endsWith('[]')?`${typeLabel(type.slice(0,-2))}[]`:type.endsWith('?')?`${typeLabel(type.slice(0,-1))}?`:typeNames[type]??type;
export function compatibleTypes(source,target){
 const a=source.type,b=target.type;
 if(a.endsWith('?'))return b.endsWith('?')&&compatibleTypes({...source,type:a.slice(0,-1)},{...target,type:b.slice(0,-1)});
 if(b.endsWith('?'))return compatibleTypes(source,{...target,type:b.slice(0,-1)});
 if(a.endsWith('[]')||b.endsWith('[]'))return a.endsWith('[]')&&b.endsWith('[]')&&compatibleTypes({...source,type:a.slice(0,-2)},{...target,type:b.slice(0,-2)});
 if(a==='enum'&&b==='text')return true;
 if(a==='enum'&&b==='enum'&&target.acceptsAnyEnum)return true;
 return a===b&&(a!=='enum'||(source.values??[]).every(v=>(target.values??[]).includes(v)));
}
typeNames.memory='Memória';
const objects={message:{texto:'text',remetente:'actor?',canal:'channel',reply:'reply?'},actor:{nome:'text',stableId:'text'},channel:{nome:'text',nativeId:'text'},reply:{id:'text'},memory:{conteudo:'text',categoria:'enum'}};
export function propertyPaths(type,prefix=[]){
 while(type.endsWith('[]')||type.endsWith('?'))type=type.endsWith('[]')?type.slice(0,-2):type.slice(0,-1);
 return Object.entries(objects[type]??{}).flatMap(([name,child])=>[[...prefix,name],...propertyPaths(child,[...prefix,name])]);
}
export function propertyType(type,path){
 if(!path.length)return type;
 if(type.endsWith('[]'))return propertyType(type.slice(0,-2),path)+'[]';
 if(type.endsWith('?')){const t=propertyType(type.slice(0,-1),path);return t.endsWith('?')?t:t+'?';}
 const object=Object.hasOwn(objects,type)?objects[type]:null;
 const child=object&&Object.hasOwn(object,path[0])?object[path[0]]:null;return child?propertyType(child,path.slice(1)):'invalid';
}
export function migrateTypedGraph(value){
 const graph=structuredClone(value);graph.version=3;
 for(const node of graph.nodes){
  if(node.kind==='prompt'){
   node.inputs??=[{id:'message',name:'mensagem',type:'text',required:true}];
   for(const [index,input] of node.inputs.entries())input.id??=`input-${index+1}`;
   for(const [index,field] of (node.responseFields??[]).entries())field.id??=`output-${index+1}`;
  }
 }
 return graph;
}
export function typedPorts(graph,node,direction,seen=new Set()){
 if(!node||seen.has(node.id))return [];seen=new Set(seen).add(node.id);
 const incoming=id=>{const e=graph.edges.find(e=>e.target===node.id&&e.targetPort===id);return e?typedPorts(graph,graph.nodes.find(n=>n.id===e.source),'out',seen)?.find(p=>p.id===e.sourcePort):null;};
 if(node.kind==='prompt')return (direction==='in'?node.inputs??[]:node.responseFields?.length?node.responseFields:[{id:'message',name:'mensagem',type:'text'}]).map(f=>({...f,label:f.name}));
 if(node.kind==='property'){
  const source=incoming('object');return direction==='in'?[{id:'object',label:'objeto',type:source?.type??'object'}]:[{id:'value',label:(node.settings?.path??[]).join('.')||'valor',type:source?propertyType(source.type,node.settings?.path??[]):'invalid',...(source?.type.startsWith('memory')&&node.settings?.path?.[0]==='categoria'?{values:memoryCategories}:{})}];
 }
 if(node.kind==='switch')return direction==='in'?[{id:'value',label:'valor',type:incoming('value')?.type??'text'}]:[];
 if(['dm','group','input'].includes(node.kind))return direction==='out'?[{id:'message',label:'mensagem',type:'message'}]:[];
 if(node.kind==='accumulate')return direction==='in'?[{id:'message',label:'mensagem',type:'message'}]:[{id:'messages',label:'mensagens',type:'message[]'}];
 if(node.kind==='history')return direction==='out'?[{id:'messages',label:'mensagens',type:'message[]'}]:[];
 if(node.kind==='memory')return direction==='out'?[{id:'memories',label:'memórias',type:'memory[]'}]:[
  {id:'persons',label:'pessoas',type:incoming('persons')?.type??'actor'},
  {id:'query',label:'consulta',type:'text',...(node.settings?.queryText!==undefined?{value:node.settings.queryText}:{})}];
 if(node.kind==='exit')return direction==='in'?[{id:'state',label:'estado',type:'enum',acceptsAnyEnum:true}]:[];
 return null;
}
export function syncSwitchCases(graph,node){
 const edge=graph.edges.find(e=>e.target===node.id&&e.targetPort==='value');
 const source=edge&&typedPorts(graph,graph.nodes.find(n=>n.id===edge.source),'out')?.find(p=>p.id===edge.sourcePort);
 if(source?.type!=='enum')return;
 node.settings??={cases:[],other:false};
 const old=node.settings.cases??[];
 const retired=node.settings.retiredCases??[];
 node.settings.cases=[...new Set(source.values??[])].map(value=>old.find(c=>c.value===value)??retired.find(c=>c.value===value)??{id:crypto.randomUUID(),value});
 const inactive=[...retired,...old.filter(c=>!(source.values??[]).includes(c.value))].filter(c=>!(source.values??[]).includes(c.value));
 if(inactive.length||node.settings.retiredCases)node.settings.retiredCases=inactive.filter((c,i)=>inactive.findIndex(x=>x.id===c.id)===i);
 // Wires from removed cases deliberately remain in the design for validation.
}
export function persistedNodeSettings(node){
 const keys=node.kind==='accumulate'?['quietSeconds','maxMessages','maxWaitSeconds']
  :node.kind==='history'?['maxMessages','maxAgeMinutes','maxCharacters','includeCharacterMessages','mode']
  :node.kind==='memory'?['mode','scope','categories','maxItems','maxCharacters','activeOnly','queryText']
  :node.kind==='exit'?['targetStateId','mode','stateMapping']
  :node.kind==='branch'?['operator']
  :node.kind==='property'?['path']
  :node.kind==='switch'?['cases','retiredCases','other']:[];
 return Object.fromEntries(keys.filter(key=>node.settings?.[key]!==undefined).map(key=>[key,structuredClone(node.settings[key])]));
}
export function serializeTypedGraph(graph){
 const clean=migrateTypedGraph(graph);
 for(const node of clean.nodes)for(const key of ['inputs','responseFields'])if(node[key]!==undefined){
  if(!Array.isArray(node[key])||node[key].length>32)throw new Error('Contrato inválido: máximo de 32 campos.');
  node[key]=node[key].map(f=>{
   if(!f||typeof f.name!=='string'||typeof f.type!=='string'||f.name.length>80||f.type.length>128)throw new Error('Formato de campo inválido.');
   if(f.values!==undefined&&(!Array.isArray(f.values)||f.values.length>64||f.values.some(v=>typeof v!=='string'||v.length>200)))throw new Error('Formato de enum inválido.');
   if(f.value!==undefined&&f.value!==null&&typeof f.value==='object')throw new Error('Valor configurado deve ser escalar.');
   return Object.fromEntries(['id','name','description','type','values','required','value','min','max','maxWords'].filter(k=>f[k]!==undefined).map(k=>[k,f[k]]));
  });
 }
 for(const node of clean.nodes)for(const key of ['usePersonalityCore','useStateGuidance'])if(node[key]!==undefined&&typeof node[key]!=='boolean')throw new Error('Referência de instrução deve ser booleana.');
 clean.nodes=clean.nodes.map(n=>Object.fromEntries(['id','kind','x','y','prompt','provider','model','inputs','responseFields','correctionRetry','title','usePersonalityCore','useStateGuidance'].filter(k=>n[k]!==undefined).map(k=>[k,n[k]]).concat(Object.keys(persistedNodeSettings(n)).length?[['settings',persistedNodeSettings(n)]]:[])));
 return {version:3,nodes:clean.nodes,edges:structuredClone(clean.edges),...(clean.controlEdges?{controlEdges:structuredClone(clean.controlEdges)}:{})};
}
