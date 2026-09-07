import {isEntry,isTerminal,nodePorts,executionPorts,connectExecution,branchOperators,accumulatorDefaults,accumulatorSettings,historyDefaults,connect,removeNode,initialProject,stateGraph,addProjectState,saveProject,openProject,saveProjectReloadState,readProjectReloadState} from './flow-model.mjs';
import {setupFlowLayout} from './flow-layout.mjs';
import {createFlowGraphEditor} from './flow-graph/index.js';
import {catalogOptions,chooseProvider,chooseModel} from './flow-catalog.mjs';
import {migrateTypedGraph,syncSwitchCases,typeLabel,typedPorts} from './flow-contract.mjs';
import {renderTypedPanel} from './flow-typed-panel.mjs';
import {memoryDefaults} from './flow-model.mjs';
const $=id=>document.getElementById(id);
let project=initialProject(),selectedStateId=project.initialStateId;
let graph=stateGraph(project,selectedStateId),selected='prompt',busy=false,traces=[],controller=null;
let lastContracts=[],lastCalls=[];
let modelCatalog=null,defaultModel='',catalogError='';
const titles={input:'Mensagem privada',dm:'Mensagem privada',group:'Mensagem de grupo',prompt:'Instrução',accumulate:'Acumular',history:'Histórico da Sessão',exit:'Trocar Estado',branch:'Branch',send:'Enviar mensagem',property:'Obter Propriedade',switch:'Switch'};
const portTypeLabels={text:'texto',message:'Mensagem','message[]':'Mensagem[]',actor:'Pessoa',channel:'Canal','actor|channel':'Pessoa | Canal','text|messages':'Mensagem | Mensagens[]',messages:'Mensagem[]',enum:'Enum',visual:'visual'};
const nodeTitle=node=>node.title||(node.kind==='property'?`Obter Propriedade · ${(node.settings?.path??[]).join('.')}`:node.kind==='prompt'?`Instrução · ${node.id}`:titles[node.kind]);
titles.memory='Consultar Memória';
const tell=text=>$('hint').textContent=text;
const reloadStateKey='filomena-flow-reload';
let restoredDraft=false,projectSaving=false;
function editingStateId(id){return project.states.some(state=>state.id===id)?id:project.states[0].id;}
try{
 const snapshot=sessionStorage.getItem(reloadStateKey);
  if(snapshot){const restored=readProjectReloadState(snapshot);project=restored.project;selectedStateId=editingStateId(restored.selectedStateId);graph=stateGraph(project,selectedStateId);selected=restored.selected;$('message').value=restored.message;sessionStorage.removeItem(reloadStateKey);restoredDraft=true;}
}catch{tell('Não foi possível restaurar o rascunho após a recarga.');}
if(!restoredDraft){
 document.body.inert=true;
 try{
  const response=await fetch('/api/flow/project',{signal:AbortSignal.timeout(5000)});
  if(response.status!==204){
   if(!response.ok)throw new Error('Não foi possível carregar o projeto salvo no backend.');
   project=openProject(await response.text());selectedStateId=editingStateId(project.initialStateId);graph=stateGraph(project,selectedStateId);selected=graph.nodes[0].id;
   tell('Projeto salvo carregado.');
  }
 }catch(error){tell(error.message||'Não foi possível carregar o projeto.');}
 finally{document.body.inert=false;}
}
for(const state of project.states)state.graph=migrateTypedGraph(state.graph);
selectedStateId=editingStateId(selectedStateId);graph=stateGraph(project,selectedStateId);
window.addEventListener('filomena:before-reload',event=>{
 if(busy||projectSaving){event.preventDefault();return;}
 try{sessionStorage.setItem(reloadStateKey,saveProjectReloadState(project,selectedStateId,$('message').value,selected));}
 catch{event.preventDefault();tell('Recarga automática adiada: salve o JSON; não foi possível preservar o rascunho na aba.');}
});

function currentProjectSnapshot(){try{return saveProject(project);}catch{return null;}}
$('save-project').addEventListener('click',async()=>{
 if(projectSaving||busy)return;
 $('project-menu').open=false;projectSaving=true;$('save-project').disabled=true;
 try{
  const snapshot=saveProject(project);
  await api('/api/flow/project',JSON.parse(snapshot),AbortSignal.timeout(10000));
  tell(snapshot===currentProjectSnapshot()?'Projeto salvo no backend.':'Projeto salvo; há alterações posteriores ainda não salvas.');
 }catch(error){tell('Não foi possível salvar: '+error.message);}
 finally{projectSaving=false;$('save-project').disabled=false;}
});
function invalidate(){traces=[];lastContracts=[];lastCalls=[];$('contract-content').textContent='Valide para atualizar o contrato.';$('result').textContent='Fluxo alterado. Execute para atualizar o resultado.';$('status').textContent='Pronto para testar.';}
function wireRender(){editor.redrawWires();}

function renderResponseFields(node){
 const list=$('response-fields');list.replaceChildren();
 const fields=node.kind==='prompt'?(node.responseFields??[]):[];
 $('add-response-field').disabled=busy||node.kind!=='prompt'||fields.length>=32;
 fields.forEach((field,index)=>{
  const row=document.createElement('div');row.className='response-field';
  const control=(caption,element)=>{const label=document.createElement('label');label.append(document.createTextNode(caption),element);row.append(label);element.disabled=busy;return element;};
  const name=control('Nome',document.createElement('input'));name.maxLength=80;name.value=field.name;
  const description=control('Descrição',document.createElement('textarea'));description.rows=2;description.maxLength=2000;description.value=field.description;
  const type=control('Tipo',document.createElement('select'));
  for(const [value,label] of [['text','Texto'],['number','Número'],['boolean','Booleano'],['enum','Enum']]){const option=document.createElement('option');option.value=value;option.textContent=label;type.append(option);}type.value=field.type;
  name.addEventListener('change',()=>{if(busy)return;field.name=name.value;contractsChanged();});
  description.addEventListener('input',()=>{if(busy)return;field.description=description.value;invalidate();});
  type.addEventListener('change',()=>{if(busy)return;field.type=type.value;if(field.type==='enum')field.values??=[];else delete field.values;delete field.min;delete field.max;delete field.maxWords;contractsChanged();});
  if(field.type==='enum'){
   const values=control('Valores (um por linha)',document.createElement('textarea'));values.rows=3;values.maxLength=12864;values.value=(field.values??[]).join('\n');
   values.addEventListener('change',()=>{if(busy)return;field.values=values.value.split(/\r?\n/).filter(Boolean);contractsChanged();});
  }
  for(const [key,label] of (field.type==='number'?[['min','Mínimo'],['max','Máximo']]:field.type==='text'?[['maxWords','Máximo de palavras']]:[])){
   const limit=control(label,document.createElement('input'));limit.type='number';limit.value=field[key]??'';limit.onchange=()=>{if(limit.value==='')delete field[key];else field[key]=Number(limit.value);contractsChanged();};
  }
  const remove=document.createElement('button');remove.type='button';remove.className='danger';remove.textContent='Remover campo';remove.disabled=busy;
  remove.addEventListener('click',()=>{if(busy)return;fields.splice(index,1);contractsChanged();});row.append(remove);list.append(row);
 });
}
$('add-response-field').addEventListener('click',()=>{
 const node=graph.nodes.find(n=>n.id===selected);if(busy||node?.kind!=='prompt')return;
 const fields=node.responseFields??=[];if(fields.length>=32)return;
 let i=1;while(fields.some(field=>field.name===`campo${i}`))i++;
 fields.push({id:crypto.randomUUID(),name:`campo${i}`,description:'',type:'text'});contractsChanged();
});
function contractsChanged(){for(const node of graph.nodes.filter(n=>n.kind==='switch'))syncSwitchCases(graph,node);invalidate();render();}
function exitEnumInput(node){
 const edge=graph.edges.find(edge=>edge.target===node.id&&edge.targetPort==='state');
 const source=edge&&typedPorts(graph,graph.nodes.find(candidate=>candidate.id===edge.source),'out')?.find(port=>port.id===edge.sourcePort);
 return source?.type==='enum'?source:null;
}
function renderExitMapping(node){
 const root=$('exit-state-mapping'),source=exitEnumInput(node);root.replaceChildren();root.hidden=!source;
 if(!source)return;
 const heading=document.createElement('label');heading.textContent='Mapeamento de estado';root.append(heading);
 const mapping=node.settings.stateMapping??={};
 const update=next=>{node.settings={...node.settings,targetStateId:null,mode:'switch',stateMapping:next};invalidate();render();};
 const row=(value,labelText=value,obsolete=false)=>{
  const label=document.createElement('label'),select=document.createElement('select');label.append(document.createTextNode(labelText),select);
  const blank=document.createElement('option');blank.value='';blank.textContent='Selecione';select.append(blank);
  for(const state of project.states){const option=document.createElement('option');option.value=state.id;option.textContent=state.name;select.append(option);}
  const mapped=mapping[value];
  if(mapped&&!project.states.some(state=>state.id===mapped)){const option=document.createElement('option');option.value=mapped;option.textContent=`${mapped} (estado inexistente)`;option.disabled=true;select.append(option);}
  select.value=mapped??'';select.disabled=busy;
  select.onchange=()=>{const next={...mapping};if(select.value)next[value]=select.value;else delete next[value];update(next);};
  root.append(label);
  if(obsolete){const remove=document.createElement('button');remove.type='button';remove.textContent='Remover mapeamento obsoleto';remove.disabled=busy;remove.onclick=()=>{const next={...mapping};delete next[value];update(next);};root.append(remove);}
 };
 const active=new Set(source.values??[]);
 for(const value of active)row(value);
 for(const value of Object.keys(mapping).filter(value=>!active.has(value)))row(value,`${value} (obsoleto)`,true);
}

function inspect(){
 const node=graph.nodes.find(n=>n.id===selected);if(!node)return;
 $('node-model-settings').hidden=node.kind!=='prompt';
 renderTypedPanel($('typed-settings'),graph,node,busy,contractsChanged);
 $('contract-preview').hidden=node.kind!=='prompt';
 const contract=lastContracts.find(n=>n.nodeId===selected),calls=lastCalls.filter(n=>n.nodeId===selected);
 $('contract-content').textContent=contract||calls.length?JSON.stringify({contract,calls},null,2):'Valide para ver o contrato. Simule para ver o payload seguro.';
 renderResponseFields(node);
 $('accumulate-settings').hidden=node.kind!=='accumulate';
 $('exit-settings').hidden=node.kind!=='exit';
 $('branch-settings').hidden=node.kind!=='branch';
 if(node.kind==='branch'){
  const select=$('branch-operator');select.replaceChildren();
  for(const [value,label] of Object.entries(branchOperators)){const option=document.createElement('option');option.value=value;option.textContent=label;select.append(option);}
  select.value=node.settings.operator;select.disabled=busy;
 }
 if(node.kind==='exit'){
   const target=$('exit-target');target.replaceChildren();
   const blank=document.createElement('option');blank.value='';blank.textContent='Selecione';target.append(blank);
   for(const state of project.states){const option=document.createElement('option');option.value=state.id;option.textContent=state.name;target.append(option);}
   target.value=node.settings.targetStateId??'';target.disabled=busy;
   renderExitMapping(node);
   const enumInput=exitEnumInput(node);target.disabled=busy||!!enumInput;
   $('exit-mode').value=node.settings.mode==='carry'?'carry':'switch';$('exit-mode').disabled=busy;
   $('exit-description').textContent=node.settings.mode==='carry'?'Carry é preservado no rascunho, mas não é executável.':enumInput?'Mapeie cada valor do enum para um estado.':'Muda o estado; o próximo fluxo aguarda uma nova mensagem.';
 }
 if(node.kind==='accumulate')for(const key of Object.keys(accumulatorDefaults())){
  $(key+'-enabled').checked=node.settings[key]!==null;$(key+'-enabled').disabled=busy;
  $(key).value=node.settings[key]??'';$(key).disabled=busy||node.settings[key]===null;
 }
 const removable=['prompt','accumulate','history','memory','branch','property','switch'].includes(node.kind)||isTerminal(node)||(isEntry(node)&&graph.nodes.filter(isEntry).length>1);
 $('remove').hidden=!removable;
 $('selected-title').textContent=nodeTitle(node);$('prompt').value=node.prompt??'';$('prompt').disabled=busy||node.kind!=='prompt';
 if(modelCatalog){
  const choices=catalogOptions(modelCatalog,node,defaultModel);
  for(const [id,key] of [['node-provider','providers'],['node-model','models']]){
   const select=$(id);select.replaceChildren();
   for(const choice of choices[key]){const option=document.createElement('option');option.value=choice.value;option.textContent=choice.label;option.disabled=choice.disabled;select.append(option);}
  }
  $('node-provider').value=node.provider??'';$('node-model').value=node.model??'';
 }
 $('node-provider').disabled=busy||node.kind!=='prompt'||!modelCatalog||modelCatalog.providers.length===0;
 $('node-model').disabled=busy||node.kind!=='prompt'||!modelCatalog||![...$('node-model').options].some(o=>!o.disabled);
 $('remove').disabled=busy||!removable;
 $('description').textContent=node.kind==='prompt'?catalogError:'';
 $('description').hidden=!$('description').textContent;
 const inspection=$('inspection');inspection.replaceChildren();const step=traces.find(s=>s.nodeId===selected);
 if(!step){inspection.textContent='Sem resultado neste bloco. Execute o fluxo.';return;}
 const duration=document.createElement('p');duration.textContent=step.status??(step.error?'Falhou':'Concluído');inspection.append(duration);
 for(const [label,value] of [['Entrada',step.input],['Saída',step.output],['Erro',step.error]]){
  if(value==null)continue;const heading=document.createElement('h3'),text=document.createElement('pre');heading.textContent=label;text.textContent=value;inspection.append(heading,text);
 }
}

function render(){
 if(graph.version!==3){const state=project.states.find(s=>s.id===selectedStateId);state.graph=migrateTypedGraph(graph);graph=state.graph;}
  for(const node of graph.nodes.filter(n=>n.kind==='switch'))syncSwitchCases(graph,node);
  $('editing-state').textContent=`Editando estado: ${project.states.find(state=>state.id===selectedStateId)?.name??'sem padrão válido'}`;
 const entrySelect=$('event-entry'),entryId=entrySelect.value;entrySelect.replaceChildren();
 for(const entry of graph.nodes.filter(isEntry)){const option=document.createElement('option');option.value=entry.id;option.textContent=`${nodeTitle(entry)} · ${entry.id}`;entrySelect.append(option);}
 if(graph.nodes.some(n=>isEntry(n)&&n.id===entryId))entrySelect.value=entryId;
 editor.render();inspect();
}

$('prompt').addEventListener('input',()=>{if(busy)return;const node=graph.nodes.find(n=>n.id===selected);node.prompt=$('prompt').value;invalidate();$('inspection').textContent='Instrução alterada. Execute novamente.';});
for(const [field,choose] of [['node-provider',chooseProvider],['node-model',chooseModel]]){
 $(field).addEventListener('change',()=>{
  if(busy||!modelCatalog)return;const node=graph.nodes.find(n=>n.id===selected);if(node?.kind!=='prompt')return;
  try{choose(node,$(field).value,modelCatalog);invalidate();}catch(error){tell(error.message);}
  inspect();
 });
}
$('add').addEventListener('click',()=>{
 if(busy)return;if(graph.nodes.length>=20){tell('Limite: 20 blocos.');return;}
 $('add-menu').open=false;
 if(graph.nodes.filter(n=>n.kind==='prompt').length>=8){tell('Limite deste recorte: 8 instruções.');return;}
 let i=2;while(graph.nodes.some(n=>n.id===`prompt-${i}`))i++;
 const node={id:`prompt-${i}`,kind:'prompt',inputs:[],prompt:'',x:370+(i-2)*35,y:330+(i-2)*25};
 graph.nodes.push(node);selected=node.id;invalidate();render();tell('Nova instrução criada. Conecte-a entre os blocos que desejar.');
});
for(const kind of ['dm','group'])$('add-'+kind).addEventListener('click',()=>{
 if(busy)return;$('add-menu').open=false;
 if(graph.nodes.length>=20){tell('Limite: 20 blocos.');return;}
 let i=1;while(graph.nodes.some(n=>n.id===`${kind}-${i}`))i++;
 const node={id:`${kind}-${i}`,kind,x:60,y:Math.min(3800,150+graph.nodes.filter(isEntry).length*130)};
 graph.nodes.push(node);selected=node.id;invalidate();render();$('event-entry').value=node.id;
 tell('Entrada criada. Conecte a saída ao caminho desejado.');
});
$('event-entry').addEventListener('change',()=>{invalidate();inspect();});
for(const kind of ['property','switch'])$('add-'+kind).addEventListener('click',()=>{
 if(busy||graph.nodes.length>=20)return;$('add-menu').open=false;
 const node={id:crypto.randomUUID(),kind,x:370,y:330,settings:kind==='property'?{path:[]}:{cases:[],other:false}};
 graph.nodes.push(node);selected=node.id;contractsChanged();
});
$('add-branch').addEventListener('click',()=>{
 if(busy)return;$('add-menu').open=false;if(graph.nodes.length>=20){tell('Limite: 20 blocos.');return;}
 let i=1;while(graph.nodes.some(n=>n.id===`branch-${i}`))i++;
 const node={id:`branch-${i}`,kind:'branch',settings:{operator:'eq'},x:370,y:330};graph.nodes.push(node);selected=node.id;invalidate();render();
});
$('branch-operator').addEventListener('change',()=>{
 const node=graph.nodes.find(n=>n.id===selected);if(busy||node?.kind!=='branch')return;
 const operator=$('branch-operator').value;if(!Object.hasOwn(branchOperators,operator))return;
 node.settings={operator};invalidate();render();
});
$('add-exit').addEventListener('click',()=>{
 if(busy)return;$('add-menu').open=false;if(graph.nodes.length>=20){tell('Limite: 20 blocos.');return;}
 let i=1;while(graph.nodes.some(n=>n.id===`exit-${i}`))i++;
 const node={id:`exit-${i}`,kind:'exit',settings:{targetStateId:selectedStateId,mode:'switch'},x:680,y:330};
 graph.nodes.push(node);selected=node.id;invalidate();render();
});
$('exit-target').addEventListener('change',()=>{
  const node=graph.nodes.find(n=>n.id===selected);if(busy||node?.kind!=='exit')return;
  const targetStateId=$('exit-target').value;
  node.settings={...node.settings,targetStateId:targetStateId||null,mode:'switch'};delete node.settings.stateMapping;invalidate();render();
});
$('exit-mode').addEventListener('change',()=>{const node=graph.nodes.find(n=>n.id===selected);if(busy||node?.kind!=='exit')return;node.settings={...node.settings,mode:'switch'};invalidate();render();});
$('add-accumulate').addEventListener('click',()=>{
 if(busy)return;$('add-menu').open=false;if(graph.nodes.length>=20){tell('Limite: 20 blocos.');return;}
 let i=1;while(graph.nodes.some(n=>n.id===`accumulate-${i}`))i++;
 const node={id:`accumulate-${i}`,kind:'accumulate',settings:accumulatorDefaults(),x:370,y:330};
 graph.nodes.push(node);selected=node.id;invalidate();render();
});
$('add-history').addEventListener('click',()=>{
 if(busy)return;$('add-menu').open=false;if(graph.nodes.length>=20){tell('Limite: 20 blocos.');return;}
 let i=1;while(graph.nodes.some(n=>n.id===`history-${i}`))i++;
 const node={id:`history-${i}`,kind:'history',settings:historyDefaults(),x:520,y:330};graph.nodes.push(node);selected=node.id;invalidate();render();
});
$('add-memory').addEventListener('click',()=>{
 if(busy)return;$('add-menu').open=false;if(graph.nodes.length>=20){tell('Limite: 20 blocos.');return;}
 Object.assign(graph,migrateTypedGraph(graph));
 const node={id:crypto.randomUUID(),kind:'memory',settings:memoryDefaults(),x:520,y:330};
 graph.nodes.push(node);selected=node.id;invalidate();render();
});
$('add-send').addEventListener('click',()=>{
 if(busy)return;$('add-menu').open=false;if(graph.nodes.length>=20){tell('Limite: 20 blocos.');return;}
 let id='send',i=2;while(graph.nodes.some(n=>n.id===id))id=`send-${i++}`;
 const node={id,kind:'send',x:680,y:330};graph.nodes.push(node);selected=node.id;invalidate();render();
 tell('Enviar mensagem criado. Conecte Mensagem e Para antes de ativar.');
});
for(const key of Object.keys(accumulatorDefaults()))for(const id of [key,key+'-enabled'])$(id).addEventListener('change',()=>{
 const node=graph.nodes.find(n=>n.id===selected);if(busy||node?.kind!=='accumulate')return;
 const value=$(key+'-enabled').checked?(id.endsWith('-enabled')?accumulatorDefaults()[key]:Number($(key).value)):null;
 try{node.settings=accumulatorSettings({...node.settings,[key]:value});invalidate();}catch(error){tell(error.message);}
 inspect();
});
$('remove').addEventListener('click',()=>{if(busy)return;removeNode(graph,selected);selected='';selected=graph.nodes[0].id;editor.clearConnection();invalidate();render();});
document.addEventListener('keydown',event=>{if(event.key==='Escape'){editor.clearConnection();render();tell('Conexão cancelada.');}});
$('save').addEventListener('click',()=>{
 try{const blob=new Blob([saveProject(project)],{type:'application/json'}),url=URL.createObjectURL(blob),link=document.createElement('a');link.href=url;link.download='filomena-project.json';link.click();setTimeout(()=>URL.revokeObjectURL(url),1000);tell('Projeto exportado com todos os estados. As instruções são texto legível: confira antes de compartilhar.');}catch(error){tell(error.message);}
});
$('open').addEventListener('click',()=>$('file').click());
$('file').addEventListener('change',async()=>{
 try{const file=$('file').files[0];if(!file||busy)return;if(file.size>1048576)throw new Error('O projeto excede 1 MB.');
  const replacement=openProject(await file.text());if(busy)return;project=replacement;selectedStateId=project.initialStateId;graph=stateGraph(project,selectedStateId);selected=graph.nodes[0].id;editor.clearConnection();$('message').value='';invalidate();render();tell('Projeto aberto. Estados e Trocar Estado são apenas desenho; nenhuma sessão foi alterada.');
 }catch(error){tell('Não foi possível abrir: '+error.message);}finally{$('file').value='';}
});
async function api(path,body,signal){
 const response=await fetch(path,{method:'POST',headers:{'Content-Type':'application/json','X-Filomena-Playground':'1'},body:JSON.stringify(body),signal});
 let data;try{data=await response.json();}catch{throw new Error(`O servidor não aceitou o pedido (${response.status}).`);}
 if(!response.ok)throw new Error(data.error||'Falha no servidor.');return data;
}
let contextStateId=null;
function closeStateMenu(){ $('state-context-menu').hidePopover();contextStateId=null; }
function renderStates(){
 closeStateMenu();const list=$('state-list');list.replaceChildren();
 for(const state of project.states){
  const button=document.createElement('button');button.textContent=state.name+(state.id===project.initialStateId?' · padrão':'');button.setAttribute('aria-pressed',String(state.id===selectedStateId));
  button.addEventListener('contextmenu',event=>{
   event.preventDefault();if(busy)return;contextStateId=state.id;
   const menu=$('state-context-menu');menu.showPopover();
   const bounds=button.getBoundingClientRect();
   menu.style.left=Math.max(0,Math.min(event.clientX||bounds.left,window.innerWidth-menu.offsetWidth))+'px';
   menu.style.top=Math.max(0,Math.min(event.clientY||bounds.bottom,window.innerHeight-menu.offsetHeight))+'px';
   $('set-default-state').focus();
  });
  button.addEventListener('click',()=>{if(busy)return;selectEditingState(state.id);renderStates();});list.append(button);
 }
}
function selectEditingState(id){
 const next=stateGraph(project,id);selectedStateId=id;graph=next;selected=graph.nodes[0].id;editor.clearConnection();
 $('message').value='';$('event-entry').value='';invalidate();render();
}
$('show-states').addEventListener('click',()=>{
 if(busy)return;$('project-menu').open=false;showStateScreen(false);renderStates();
 $('states-panel').inert=false;$('states-panel').setAttribute('aria-hidden','false');$('states-panel').classList.add('is-open');$('show-states').setAttribute('aria-expanded','true');$('close-states').focus();
});
function closeStates(){
 closeStateMenu();
 $('project-menu').querySelector('summary').focus();$('states-panel').classList.remove('is-open');$('states-panel').inert=true;$('states-panel').setAttribute('aria-hidden','true');$('show-states').setAttribute('aria-expanded','false');
}
$('close-states').addEventListener('click',closeStates);
function showStateScreen(create){
 closeStateMenu();
 $('states-list-screen').hidden=create;$('states-create-screen').hidden=!create;
 $('states-title').textContent=create?'Criar estado':'Estados';$('states-status').textContent='';
}
$('show-create-state').addEventListener('click',()=>{if(busy)return;$('new-state-name').value='';showStateScreen(true);$('new-state-name').focus();});
$('back-to-states').addEventListener('click',()=>{showStateScreen(false);$('show-create-state').focus();});
$('states-panel').addEventListener('keydown',event=>{if(event.key==='Escape'){event.preventDefault();event.stopPropagation();if(!$('states-create-screen').hidden){showStateScreen(false);$('show-create-state').focus();}else closeStates();}});
$('new-state-name').addEventListener('keydown',event=>{if(event.key==='Enter'){event.preventDefault();$('create-state').click();}});
$('create-state').addEventListener('click',()=>{
 if(busy)return;
 try{const state=addProjectState(project,$('new-state-name').value);selectEditingState(state.id);$('new-state-name').value='';renderStates();showStateScreen(false);$('show-create-state').focus();}catch(error){$('states-status').textContent=error.message;}
});
$('set-default-state').addEventListener('click',()=>{
 if(busy||!project.states.some(state=>state.id===contextStateId))return;
 project.initialStateId=contextStateId;renderStates();
});
const topMenus=[$('project-menu'),$('add-menu')];
for(const menu of topMenus){
 menu.addEventListener('pointerenter',event=>{if(event.pointerType==='mouse'&&topMenus.some(other=>other.open))for(const other of topMenus)other.open=other===menu;});
 menu.addEventListener('toggle',()=>{if(menu.open)for(const other of topMenus)if(other!==menu)other.open=false;});
 menu.addEventListener('keydown',event=>{if(event.key==='Escape'){event.preventDefault();menu.open=false;menu.querySelector('summary').focus();}});
}
document.addEventListener('pointerdown',event=>{for(const menu of topMenus)if(!menu.contains(event.target))menu.open=false;});
const addSubmenus=[...$('add-menu').querySelectorAll('.strip-submenu')];
$('add-menu').addEventListener('toggle',()=>{if(!$('add-menu').open)for(const menu of addSubmenus)menu.open=false;});
for(const menu of addSubmenus){
 menu.addEventListener('pointerenter',event=>{
  if(event.pointerType!=='mouse'||!$('add-menu').open)return;
  for(const other of addSubmenus)other.open=other===menu;
 });
 menu.addEventListener('toggle',()=>{if(menu.open)for(const other of addSubmenus)if(other!==menu)other.open=false;});
 menu.addEventListener('keydown',event=>{if(event.key==='Escape'){event.preventDefault();event.stopPropagation();menu.open=false;menu.querySelector('summary').focus();}});
}
$('add').addEventListener('pointerenter',event=>{if(event.pointerType==='mouse')for(const menu of addSubmenus)menu.open=false;});
function setBusy(value){busy=value;for(const id of ['run','add','add-dm','add-group','add-accumulate','add-history','add-memory','add-branch','add-exit','add-send','show-states','event-entry','open','save','message'])$(id).disabled=value;$('cancel').disabled=!value;render();}
$('cancel').addEventListener('click',()=>controller?.abort());
function typedRequest(){return {graph,entryNodeId:$('event-entry').value,message:{texto:$('message').value,remetente:{nome:'Pessoa de teste',stableId:'fixture:person'},canal:{nome:'Conversa de teste',nativeId:'fixture:conversation'},reply:null}};}
function projectValidationRequest(){return {project:JSON.parse(saveProject(project)).project};}
function projectIssues(issues){return issues.map(issue=>({estado:project.states.find(state=>state.id===issue.stateId)?.name??issue.stateId??'Projeto',entrada:issue.entryId,nodeId:issue.nodeId,porta:issue.port,motivo:issue.reason}));}
$('validate').addEventListener('click',async()=>{
 if(busy)return;
 try{
  const projectValidation=await api('/api/flow/typed/project/validate',projectValidationRequest());
  if(!projectValidation.valid){const issues=projectIssues(projectValidation.issues??[]);$('result').textContent=JSON.stringify(issues,null,2);$('status').textContent='Projeto não executável; corrija os estados, blocos e portas indicados.';lastContracts=[];inspect();return;}
  const data=await api('/api/flow/typed/validate',typedRequest());
 $('result').textContent=JSON.stringify(data.valid?{valid:true}:data.issues,null,2);
 $('status').textContent=data.valid?'Fluxo executável.':'Rascunho salvo é permitido; corrija os erros para executar.';
 lastContracts=data.contracts??[];inspect();
 }catch(error){tell(error.message);}
});
$('run').addEventListener('click',async()=>{
 if(busy)return;
 if(graph.nodes.some(node=>['accumulate','history','memory','exit'].includes(node.kind))){$('status').textContent='Estes nodes exigem a simulação de sessão; use a API de sessões fake.';return;}
 editor.clearConnection();traces=[];controller=new AbortController();setBusy(true);
 try{
  const data=await api('/api/flow/typed/run',{...typedRequest(),responses:JSON.parse($('fake-responses').value)},controller.signal);
 traces=data.steps??[];lastCalls=data.calls??[];$('result').textContent=JSON.stringify(data,null,2);
  $('status').textContent=data.success?'Simulação concluída. Nenhum modelo ou canal real foi chamado.':'Execução interrompida.';
 }catch(error){$('status').textContent=error.name==='AbortError'?'Cancelado.':error.message;}
 finally{controller=null;setBusy(false);}
});
Promise.all([fetch('/api/model').then(r=>r.json()),fetch('/api/model-catalog').then(async r=>{const data=await r.json();if(!r.ok)throw new Error(data.error||'Catálogo indisponível.');return data;})])
 .then(([defaults,catalog])=>{defaultModel=defaults.model;modelCatalog=catalog;inspect();})
 .catch(error=>{catalogError=error.message||'Catálogo indisponível.';for(const id of ['node-provider','node-model'])$(id).options[0].textContent='Catálogo indisponível';tell(catalogError);inspect();});
function describeNode(node){
 const ports=node.kind==='branch'
  ?[{direction:'in',id:'a',type:'visual',label:'A'},{direction:'in',id:'b',type:'visual',label:'B'}]
  :['in','out'].flatMap(direction=>nodePorts(node,direction,graph).map(port=>({...port,direction})));
 let preview;
 if(node.kind==='send')preview='';
 if(node.kind==='accumulate')preview=[node.settings?.quietSeconds!=null&&`${node.settings.quietSeconds}s silêncio`,node.settings?.maxMessages!=null&&`${node.settings.maxMessages} msgs`,node.settings?.maxWaitSeconds!=null&&`max ${node.settings.maxWaitSeconds}s`].filter(Boolean).join(' · ');
 if(node.kind==='history')preview=[`${node.settings?.maxMessages??'–'} msgs`,node.settings?.maxCharacters!=null&&`${node.settings.maxCharacters} car.`,node.settings?.maxAgeMinutes!=null&&`${node.settings.maxAgeMinutes} min`,node.settings?.mode==='through-current-batch'?'inclui lote':'antes do lote'].filter(Boolean).join(' · ');
 if(node.kind==='memory')preview=`${({exact:'Exata',semantic:'Semântica',hybrid:'Híbrida'})[node.settings?.mode]??'–'} · ${node.settings?.maxItems??'–'} itens · ${node.settings?.maxCharacters??'–'} car.`;
 if(node.kind==='exit')preview=node.settings.mode==='carry'?'Carry não suportado':(project.states.find(s=>s.id===node.settings.targetStateId)?.name??'Mapeie ou escolha o estado');
 if(node.kind==='branch')preview=`A ${branchOperators[node.settings.operator].toLowerCase()} B`;
 return {
  title:nodeTitle(node),className:node.kind+(isEntry(node)?' input':''),preview,
  portsClassName:node.kind==='accumulate'?'accumulator-ports':node.kind==='branch'?'branch-ports':'typed-ports',
  ports:ports.map(port=>({...port,typeLabel:portTypeLabels[port.type]??typeLabel(port.type),interactive:port.type!=='visual',
   title:port.type==='visual'?'Protótipo visual':port.type==='message'?'Mensagem: texto, remetente (Pessoa) e canal (Canal).':undefined})),
  executionPorts:['in','out'].flatMap(direction=>executionPorts(node,direction).map(port=>({...port,direction})))
 };
}
const editor=createFlowGraphEditor({
 viewport:$('viewport'),canvas:$('canvas'),nodes:$('nodes'),wires:$('wires'),
 getGraph:()=>graph,describeNode,getSelectedId:()=>selected,isReadonly:()=>busy,
 positionBounds:{min:0,max:4000},
 onSelect:node=>{selected=node.id;inspect();},
 onMove:(node,position)=>Object.assign(node,position),onMoveEnd:()=>invalidate(),
 onConnect:edge=>{
  const connector=edge.kind==='execution'?connectExecution:connect;
  connector(graph,edge.source,edge.target,edge.sourcePort,edge.targetPort);contractsChanged();
 },
 onDisconnect:(edge,kind)=>{const key=kind==='execution'?'controlEdges':'edges';graph[key]=graph[key].filter(e=>e!==edge);invalidate();tell('Conexão removida.');},
 onStatus:tell,
 labels:{chooseOutput:'Escolha primeiro uma porta de saída de dados.',chooseExecution:'Escolha uma saída de execução na titlebar.',
  legacyWire:'Conexão antiga sem portas definidas. Reconecte as portas para executar.',removeWire:'Remover conexão',input:'Entrada',output:'Saída'}
});
render();
setupFlowLayout(wireRender);
