import {typedPorts,serializeTypedGraph,persistedNodeSettings,compatibleTypes} from './flow-contract.mjs';
export const isEntry=node=>['dm','group','input'].includes(node.kind);
export const isTerminal=node=>['output','exit','send'].includes(node.kind);
const portCatalog=Object.freeze({
 dm:{in:{},out:{message:{type:'message',label:'mensagem'}}},
 input:{in:{},out:{message:{type:'message',label:'mensagem'}}},
 group:{in:{},out:{message:{type:'message',label:'mensagem'}}},
 prompt:{in:{message:{type:'text|messages',label:'mensagem'}},out:{message:{type:'text',label:'mensagem'}}},
 send:{in:{message:{type:'text|messages',label:'mensagem'},to:{type:'actor|channel',label:'para'}},out:{}},
 output:{in:{default:{type:'visual',label:'entrada'}},out:{}},
 exit:{in:{default:{type:'visual',label:'entrada'}},out:{}},
 accumulate:{in:{message:{type:'message',label:'mensagem'}},out:{messages:{type:'messages',label:'output'}}},
 history:{in:{},out:{messages:{type:'messages',label:'mensagens'}}},
 memory:{in:{persons:{type:'actor',label:'pessoas'},query:{type:'text',label:'consulta'}},out:{memories:{type:'memory[]',label:'memórias'}}},
 branch:{in:{a:{type:'visual',label:'A'},b:{type:'visual',label:'B'}},out:{true:{type:'visual',label:'Verdadeiro'},false:{type:'visual',label:'Falso'}}}
});
export function nodePorts(node,direction,graph){
 if(graph?.version===3){const result=typedPorts(graph,node,direction);if(result)return result;}
 const ports=portCatalog[node?.kind]?.[direction]??{};
 return Object.entries(ports).map(([id,port])=>({id,...port}));
}
export function executionPorts(node,direction){
 if(node?.kind==='property')return [];
 if(node?.kind==='switch')return direction==='in'?[{id:'exec',label:'Executar'}]:[...(node.settings?.cases??[]).map(c=>({id:c.id,label:c.value})),...(node.settings?.other?[{id:'other',label:'Outro'}]:[])];
 if(!portCatalog[node?.kind])return [];
 if(direction==='in')return isEntry(node)?[]:[{id:'exec',label:'Executar'}];
 if(['exit','output'].includes(node.kind))return [];
 return node.kind==='branch'?[{id:'true',label:'Verdadeiro'},{id:'false',label:'Falso'}]:[{id:'then',label:'Próximo'}];
}
function normalizeControlEdges(graph){
 const edges=graph.controlEdges===undefined?[]:graph.controlEdges;
 if(!Array.isArray(edges)||edges.length>64)throw new Error('Conexões de execução inválidas.');
 const occupied=new Set();
 return edges.map(edge=>{
  const from=graph.nodes.find(node=>node.id===edge?.source),to=graph.nodes.find(node=>node.id===edge?.target);
  if(!from||!to||from.id===to.id||!executionPorts(from,'out').some(port=>port.id===edge.sourcePort)
   ||!executionPorts(to,'in').some(port=>port.id===edge.targetPort))throw new Error('Porta de execução inválida.');
  const key=JSON.stringify([edge.source,edge.sourcePort]);
  if(occupied.has(key))throw new Error('Cada saída de execução aceita um próximo bloco.');
  occupied.add(key);
  return {source:edge.source,sourcePort:edge.sourcePort,target:edge.target,targetPort:edge.targetPort};
 });
}
export function connectExecution(graph,source,target,sourcePort='then',targetPort='exec'){
 const edges=(graph.controlEdges??[]).filter(edge=>edge.source!==source||edge.sourcePort!==sourcePort);
 edges.push({source,sourcePort,target,targetPort});
 if(graph.version===3){normalizeControlEdges({...graph,controlEdges:[edges.at(-1)]});graph.controlEdges=edges;return;}
 graph.controlEdges=normalizeControlEdges({...graph,controlEdges:edges});
}
function port(node,direction,id){
 const definition=portCatalog[node?.kind]?.[direction]?.[id];
 return definition?{id,...definition}:null;
}
function hasTypedPorts(node){
 return [...nodePorts(node,'in'),...nodePorts(node,'out')].some(item=>item.type!=='visual');
}
function compatiblePorts(source,target){
 return source.type==='visual'||target.type==='visual'
  ||source.type.split('|').every(type=>target.type.split('|').includes(type));
}
function inferPorts(from,to){
 const candidates=[];
 for(const source of nodePorts(from,'out'))for(const target of nodePorts(to,'in'))
  if(source.type!=='visual'&&target.type!=='visual'&&compatiblePorts(source,target))candidates.push([source.id,target.id]);
 return candidates.length===1?{sourcePort:candidates[0][0],targetPort:candidates[0][1]}:null;
}
function edgeWithoutPorts(edge){return {source:edge.source,target:edge.target};}
function normalizeEdge(edge,nodes){
 const from=nodes.get(edge?.source),to=nodes.get(edge?.target);
 if(!edge||!from||!to)throw new Error('Conexão inválida no arquivo.');
 const hasSource=Object.hasOwn(edge,'sourcePort'),hasTarget=Object.hasOwn(edge,'targetPort');
 if(hasSource!==hasTarget)throw new Error('Uma conexão tipada precisa das duas portas.');
 if(!hasSource){
  const inferred=inferPorts(from,to);
  return inferred?{source:edge.source,target:edge.target,...inferred}:edgeWithoutPorts(edge);
 }
 if(typeof edge.sourcePort!=='string'||!edge.sourcePort.trim()||typeof edge.targetPort!=='string'||!edge.targetPort.trim())throw new Error('Porta inválida na conexão.');
 const source=port(from,'out',edge.sourcePort),target=port(to,'in',edge.targetPort);
 if(!source||!target)throw new Error('Conector desconhecido na conexão.');
 if(!compatiblePorts(source,target))throw new Error('As portas têm tipos incompatíveis.');
 return {source:edge.source,target:edge.target,sourcePort:source.id,targetPort:target.id};
}
function normalizeEdges(graph){
 const nodes=new Map(graph.nodes.map(node=>[node.id,node])),occupied=new Set(),edges=graph.edges.map(edge=>normalizeEdge(edge,nodes));
 for(const edge of edges){
  if(!edge.targetPort)continue;
  const target=port(nodes.get(edge.target),'in',edge.targetPort);
  if(target?.type!=='visual'){
   const key=`${edge.target}:${edge.targetPort}`;
   if(occupied.has(key))throw new Error('Cada entrada tipada aceita apenas uma conexão.');
   occupied.add(key);
  }
 }
 return edges;
}
export const branchOperators=Object.freeze({eq:'Igual a',ne:'Diferente de',gt:'Maior que',gte:'Maior ou igual a',lt:'Menor que',lte:'Menor ou igual a'});
function branchSettings(settings){
 if(!settings||!Object.hasOwn(branchOperators,settings.operator))throw new Error('Comparação inválida no Branch.');
 return {operator:settings.operator};
}
function exitSettings(settings){
 const result={};
 for(const key of ['targetStateId','mode','stateMapping'])if(settings&&Object.hasOwn(settings,key))result[key]=structuredClone(settings[key]);
 return result;
}
export const accumulatorDefaults=()=>({quietSeconds:3,maxMessages:10,maxWaitSeconds:30});
export function accumulatorSettings(settings){
 const result={};
 for(const key of ['quietSeconds','maxMessages','maxWaitSeconds']){
  const value=settings?.[key];
  if(value!==null&&(!Number.isFinite(value)||value<=0||value>(key==='maxMessages'?10000:86400)||(key==='maxMessages'&&!Number.isInteger(value))))throw new Error('Limite inválido no Acumular.');
  result[key]=value;
 }
 return result;
}
export const historyDefaults=()=>({maxMessages:20,maxAgeMinutes:null,maxCharacters:4000,includeCharacterMessages:false,mode:'before-current-batch'});
export const memoryDefaults=()=>({mode:'hybrid',scope:'person',categories:['limite','apelido','relacao','fato','nao_classificada'],maxItems:8,maxCharacters:1800,activeOnly:true});
export function initialGraph(stateId='state-1') {
 return {version:2,nodes:[
 {id:'input',kind:'dm',x:60,y:150},
  {id:'prompt',kind:'prompt',prompt:'Você é Filomena. Responda de forma natural e direta.',x:370,y:150},
  {id:'output',kind:'exit',settings:{targetStateId:stateId,mode:'switch'},x:680,y:150}
 ],edges:[]};
}

export function saveReloadState(graph,message,selected){
 return JSON.stringify({draft:saveDraft(graph),message,selected});
}

export function readReloadState(text){
 const state=JSON.parse(text),graph=openDraft(state.draft);
 if(typeof state.message!=='string'||state.message.length>16000)throw new Error('Mensagem de rascunho inválida.');
 return {graph,message:state.message,selected:graph.nodes.some(n=>n.id===state.selected)?state.selected:graph.nodes[0].id};
}

export function connect(graph,source,target,sourcePort,targetPort) {
 if(graph.version===3){
  const from=graph.nodes.find(n=>n.id===source),to=graph.nodes.find(n=>n.id===target);
  const a=nodePorts(from,'out',graph).find(p=>p.id===sourcePort),b=nodePorts(to,'in',graph).find(p=>p.id===targetPort);
  if(!a||!b||source===target)throw new Error('Porta inválida.');
  const memoryPersons=to.kind==='memory'&&targetPort==='persons';
  if(memoryPersons&&!['actor','actor?','actor[]','actor?[]'].includes(a.type))throw new Error('Memória recebe Pessoa ou lista de Pessoas opcionais.');
  const flexible=to.kind==='property'||to.kind==='switch'||memoryPersons;
  if(to.kind==='switch'&&!['text','enum'].includes(a.type))throw new Error('Switch recebe Texto ou Enum.');
  if(!flexible&&!compatibleTypes(a,b))throw new Error('As portas têm tipos incompatíveis.');
  if(Object.hasOwn(b,'value'))throw new Error('Remova o valor configurado antes de conectar.');
  graph.edges=graph.edges.filter(e=>e.target!==target||e.targetPort!==targetPort);
  graph.edges.push({source,target,sourcePort,targetPort});return;
 }
 const from=graph.nodes.find(n=>n.id===source),to=graph.nodes.find(n=>n.id===target);
 if(!from||!to||source===target||isTerminal(from)||isEntry(to))throw new Error('Escolha uma saída e depois a entrada de outro bloco.');
 const hasSource=sourcePort!==undefined,hasTarget=targetPort!==undefined;
 let typed;
 if(hasSource!==hasTarget)throw new Error('Uma conexão tipada precisa das duas portas.');
 if(hasSource){
  const sourceDefinition=port(from,'out',sourcePort),targetDefinition=port(to,'in',targetPort);
  if(!sourceDefinition||!targetDefinition)throw new Error('Conector desconhecido na conexão.');
  if(!compatiblePorts(sourceDefinition,targetDefinition))throw new Error('As portas têm tipos incompatíveis.');
  typed={sourcePort:sourceDefinition.id,targetPort:targetDefinition.id};
 }else if(hasTypedPorts(from)&&hasTypedPorts(to)){
  typed=inferPorts(from,to);
  if(!typed)throw new Error('Escolha portas tipadas compatíveis.');
 }
 const edges=normalizeEdges(graph),next={source,target,...(typed??{})};
 if(next.targetPort){
  const targetDefinition=port(to,'in',next.targetPort);
  if(targetDefinition?.type!=='visual')edges.splice(0,edges.length,...edges.filter(edge=>edge.target!==target||edge.targetPort!==next.targetPort));
 }
 edges.push(next);graph.edges=edges;
}

export function removeNode(graph,id) {
 graph.nodes=graph.nodes.filter(n=>n.id!==id);
 graph.edges=graph.edges.filter(e=>e.source!==id&&e.target!==id);
 if(graph.controlEdges)graph.controlEdges=graph.controlEdges.filter(e=>e.source!==id&&e.target!==id);
}

function responseFields(fields){
 if(!Array.isArray(fields)||fields.length>32)throw new Error('Estrutura JSON inválida (máximo de 32 campos).');
 return fields.map(field=>{
  if(!field||typeof field.name!=='string'||field.name.length>80||typeof field.description!=='string'||field.description.length>2000||!['text','number','boolean','enum'].includes(field.type))throw new Error('Campo JSON inválido.');
  if(field.type==='enum'&&(!Array.isArray(field.values)||field.values.length>64||field.values.some(value=>typeof value!=='string'||value.length>200)))throw new Error('Valores do enum inválidos.');
  return {name:field.name,description:field.description,type:field.type,...(field.type==='enum'?{values:[...field.values]}:{})};
 });
}

export function saveDraft(graph) {
 if(graph.version===3)return JSON.stringify({format:'filomena-local-flow',graph:serializeTypedGraph(graph)},null,2);
 // Save design references, never credentials, endpoint configuration, or execution traces.
 return JSON.stringify({format:'filomena-local-flow',graph:{version:2,
   nodes:graph.nodes.map(n=>({id:n.id,kind:n.kind,...(n.kind==='prompt'?{prompt:n.prompt,...(n.responseFields!==undefined?{responseFields:responseFields(n.responseFields)}:{}),...(n.provider!==undefined?{provider:n.provider}:{}),...(n.model!==undefined?{model:n.model}:{})}:{}),...(n.kind==='accumulate'||n.kind==='history'||n.kind==='exit'?{settings:persistedNodeSettings(n)}:{}),...(n.kind==='branch'?{settings:branchSettings(n.settings)}:{}),x:n.x,y:n.y})),
  edges:normalizeEdges(graph),...(graph.controlEdges!==undefined?{controlEdges:normalizeControlEdges(graph)}:{})}},null,2);
}

export function openDraft(text,stateId='state-1') {
 if(text.length>65536)throw new Error('O arquivo excede 64 KB.');
 const document=JSON.parse(text),graph=document?.graph;
 if(document?.format!=='filomena-local-flow'||![1,2,3].includes(graph?.version)||!Array.isArray(graph.nodes)||!Array.isArray(graph.edges)
  ||graph.nodes.length<2||graph.nodes.length>20||graph.edges.length>64)throw new Error('Arquivo de fluxo local inválido ou versão não suportada.');
 // Explicit design reset requested for structured messages. Apply once on old drafts.
 const legacy=graph.version===1;
 if(legacy)graph.version=3;
 const ids=new Set();
 for(const node of graph.nodes){
  if(!node||typeof node.id!=='string'||!node.id.trim()||node.id.length>80||ids.has(node.id)
   ||!['input','dm','group','prompt','send','output','accumulate','history','memory','exit','branch','property','switch'].includes(node.kind)||!Number.isFinite(node.x)||!Number.isFinite(node.y)
   ||node.x<0||node.y<0||node.x>4000||node.y>4000
   ||(node.kind==='prompt'&&(typeof node.prompt!=='string'||node.prompt.length>16000))
   ||(node.provider!==undefined&&(typeof node.provider!=='string'||node.provider.length>120))
   ||(node.model!==undefined&&(typeof node.model!=='string'||node.model.length>256)))throw new Error('Bloco inválido no arquivo.');
  ids.add(node.id);
 }
 if(!graph.nodes.some(isEntry)||graph.nodes.filter(n=>n.kind==='output').length>1)throw new Error('O arquivo deve ter uma entrada.');
 // Legacy local manual-entry drafts now open as simulated DM events.
 for(const node of graph.nodes)if(node.kind==='input')node.kind='dm';
 for(const node of graph.nodes){
  if(node.kind==='output'){node.kind='exit';node.settings={targetStateId:stateId,mode:'switch'};}
 }
 if(graph.version===3)return serializeTypedGraph(graph);
 return JSON.parse(saveDraft({...graph,edges:normalizeEdges(graph)})).graph;
}

// Editor-only project definitions. No active conversation/session state lives here.
export function initialProject(graph=initialGraph()){
 return {version:1,stateScope:'session',initialStateId:'state-1',states:[{id:'state-1',name:'Inicial',graph}]};
}
export function stateGraph(project,id){
 const state=project.states.find(s=>s.id===id);
 if(!state)throw new Error('Estado não encontrado.');
 return state.graph;
}
function stateName(value){
 if(typeof value!=='string'||!value.trim()||value.trim().length>80)throw new Error('Use um nome de estado com 1–80 caracteres.');
 return value.trim();
}
export function addProjectState(project,name){
 if(project.states.length>=16)throw new Error('Limite deste desenho: 16 estados.');
 name=stateName(name);
 let i=1;while(project.states.some(s=>s.id===`state-${i}`))i++;
 const state={id:`state-${i}`,name,graph:initialGraph(`state-${i}`)};project.states.push(state);return state;
}
export function renameProjectState(project,id,name){
 stateGraph(project,id);project.states.find(s=>s.id===id).name=stateName(name);
}
function cleanProject(project){
 if(!project||project.version!==1||project.stateScope!=='session'||!Array.isArray(project.states)||project.states.length<1||project.states.length>16)throw new Error('Projeto de estados inválido.');
 const ids=new Set();
 const states=project.states.map(state=>{
  if(!state||typeof state.id!=='string'||!state.id.trim()||state.id.length>80||ids.has(state.id))throw new Error('Identificador de estado inválido ou duplicado.');
  ids.add(state.id);
  return {id:state.id,name:stateName(state.name),graph:openDraft(JSON.stringify({format:'filomena-local-flow',graph:state.graph}),state.id)};
 });
 const initialStateId=project.initialStateId;
 if(initialStateId!==undefined&&initialStateId!==null&&(typeof initialStateId!=='string'||initialStateId.length>80))throw new Error('Identificador de estado inicial inválido.');
 return {version:1,stateScope:'session',initialStateId,states};
}
export function saveProject(project){
 const text=JSON.stringify({format:'filomena-local-project',project:cleanProject(project)},null,2);
 if(text.length>1048576)throw new Error('O projeto excede 1 MB.');
 return text;
}
export function openProject(text){
 if(text.length>1048576)throw new Error('O projeto excede 1 MB.');
 const document=JSON.parse(text);
 if(document?.format==='filomena-local-flow')return initialProject(openDraft(text));
 if(document?.format!=='filomena-local-project')throw new Error('Formato de projeto não suportado.');
 return cleanProject(document.project);
}
export function saveProjectReloadState(project,selectedStateId,message,selected){
 stateGraph(project,selectedStateId);
 return JSON.stringify({projectDraft:saveProject(project),selectedStateId,message,selected});
}
export function readProjectReloadState(text){
 const state=JSON.parse(text);
 if(!state.projectDraft){
  const old=readReloadState(text);return {project:initialProject(old.graph),selectedStateId:'state-1',message:old.message,selected:old.selected};
 }
 const project=openProject(state.projectDraft),graph=stateGraph(project,state.selectedStateId);
 if(typeof state.message!=='string'||state.message.length>16000)throw new Error('Mensagem de rascunho inválida.');
 return {project,selectedStateId:state.selectedStateId,message:state.message,selected:graph.nodes.some(n=>n.id===state.selected)?state.selected:graph.nodes[0].id};
}
