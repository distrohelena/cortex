import {inputTypes,typeLabel,typedPorts,propertyPaths,syncSwitchCases} from './flow-contract.mjs';

// Host-specific contract UI. The reusable graph library only renders ports/wires.
export function renderTypedPanel(root,graph,node,busy,changed,modelCatalog=null){
 root.replaceChildren();
 const section=()=>{const div=document.createElement('section');div.className='response-field';root.append(div);return div;};
 const field=(parent,title,tag='input')=>{const label=document.createElement('label'),el=document.createElement(tag);label.append(document.createTextNode(title),el);parent.append(label);el.disabled=busy;return el;};
 const button=(parent,text,action)=>{const b=document.createElement('button');b.type='button';b.textContent=text;b.disabled=busy;b.onclick=action;parent.append(b);};
 const select=(parent,title,values,value,action)=>{const el=field(parent,title,'select');for(const [v,t] of values){const o=document.createElement('option');o.value=v;o.textContent=t;el.append(o);}el.value=value;el.onchange=()=>action(el.value);return el;};
 const change=()=>{for(const n of graph.nodes.filter(n=>n.kind==='switch'))syncSwitchCases(graph,n);changed();};
 if(node.kind==='send'){
  const box=section(),preview=document.createElement('p');
  preview.textContent='Enviar · Fase 0. Destino: somente a sessão de origem (DM allowlisted pelo host). Requer candidate aprovada com proveniência da execução. O host inicia disabled; shadow registra WOULD_SEND sem transporte, histórico ou transição por fala. Enabled indisponível. Permissões não são configuráveis pelo projeto.';
  box.append(preview);
 }
 if(node.kind==='interpretBoundaries'){
  const box=section();
  node.settings??={taxonomyVersion:'v1',grammarVersion:'v1',templatesVersion:'v1'};
  for(const [key,title] of [['taxonomyVersion','Taxonomia'],['grammarVersion','Gramática'],['templatesVersion','Templates']]){
   const current=node.settings[key]??'';
   select(box,title,[['v1','v1'],...(current&&current!=='v1'?[[current,current+' (não suportada)']]:[])],current,v=>{node.settings[key]=v;change();});
  }
  const preview=document.createElement('pre');preview.textContent='Carregando policies do compilador…';box.append(preview);
  fetch('/api/boundary-policies').then(async response=>{if(!response.ok)throw new Error();return response.json();}).then(value=>{preview.textContent=JSON.stringify(value,null,2);}).catch(()=>{preview.textContent='Preview indisponível. A compilação exige as versões configuradas.';});
 }
 if(node.kind==='property'){
  const box=section(),type=typedPorts(graph,node,'in')[0].type,paths=propertyPaths(type).map(p=>[p.join('.'),p.join('.')]);
  const current=(node.settings?.path??[]).join('.');
  if(current&&!paths.some(([v])=>v===current))paths.unshift([current,current+' (inválida)']);
  select(box,'Propriedade',[['','Selecione'],...paths],current,v=>{node.settings={path:v?v.split('.'):[]};change();});
 }
 if(node.kind==='switch'){
  const box=section(),edge=graph.edges.find(e=>e.target===node.id&&e.targetPort==='value');
  const source=edge&&typedPorts(graph,graph.nodes.find(n=>n.id===edge.source),'out')?.find(p=>p.id===edge.sourcePort);
  node.settings??={cases:[],other:false};
  for(const c of node.settings.cases){const el=field(box,'Caso');el.value=c.value;el.disabled=busy||source?.type==='enum';el.onchange=()=>{c.value=el.value;change();};if(source?.type!=='enum')button(box,'Remover caso',()=>{node.settings.cases=node.settings.cases.filter(x=>x!==c);change();});}
  if(source?.type!=='enum')button(box,'Adicionar caso',()=>{node.settings.cases.push({id:crypto.randomUUID(),value:''});change();});
  const other=field(box,'Outro');other.type='checkbox';other.checked=!!node.settings.other;other.onchange=()=>{node.settings.other=other.checked;change();};
 }
 if(node.kind==='history'){
  const box=section(),settings=node.settings??={maxMessages:20,maxAgeMinutes:null,maxCharacters:4000,includeCharacterMessages:false,mode:'before-current-batch'};
  const number=(title,key,integer=false)=>{const el=field(box,title);el.type='number';el.min='0';el.step=integer?'1':'any';el.value=settings[key]??'';el.onchange=()=>{settings[key]=el.value===''?null:Number(el.value);change();};};
  number('Máximo de mensagens','maxMessages',true);
  number('Idade máxima (minutos)','maxAgeMinutes');
  number('Máximo de caracteres','maxCharacters',true);
  const characters=field(box,'Incluir mensagens da personagem');characters.type='checkbox';characters.checked=!!settings.includeCharacterMessages;characters.onchange=()=>{settings.includeCharacterMessages=characters.checked;change();};
  select(box,'Janela',[['before-current-batch','Antes do lote atual'],['through-current-batch','Incluindo o lote atual']],settings.mode,v=>{settings.mode=v;change();});
 }
 if(node.kind==='memory'){
  const box=section(),s=node.settings;
  select(box,'Modo',[['exact','Exato'],['semantic','Semântico'],['hybrid','Híbrido']],s.mode,v=>{s.mode=v;change();});
  select(box,'Escopo',[['person','Pessoa nesta conversa'],['conversation','Conversa']],s.scope,v=>{s.scope=v;change();});
  for(const [key,title] of [['maxItems','Máximo de itens'],['maxCharacters','Máximo de caracteres']]){
   const el=field(box,title);el.type='number';el.min='1';el.step='1';el.value=s[key]??'';el.onchange=()=>{s[key]=el.value===''?null:Number(el.value);change();};
  }
  for(const [key,title] of [['limite','Limites'],['apelido','Apelidos'],['relacao','Relações'],['fato','Fatos'],['nao_classificada','Não classificadas']]){
   const el=field(box,title);el.type='checkbox';el.checked=(s.categories??[]).includes(key);el.onchange=()=>{s.categories=el.checked?[...new Set([...(s.categories??[]),key])]:(s.categories??[]).filter(c=>c!==key);change();};
  }
  const active=field(box,'Somente ativas');active.type='checkbox';active.checked=s.activeOnly!==false;active.disabled=true;
  const query=field(box,'Consulta configurada (opcional)','textarea');query.value=s.queryText??'';query.disabled=busy||graph.edges.some(e=>e.target===node.id&&e.targetPort==='query');query.onchange=()=>{if(query.value)s.queryText=query.value;else delete s.queryText;change();};
 }
 if(node.kind!=='prompt')return;
 const profileBox=section();
 select(profileBox,'Perfil de execução',[['normal','Normal'],['boundary_enforcement_v1','Boundary enforcement v1']],node.executionProfile??'normal',v=>{node.executionProfile=v;change();});
 if(node.executionProfile==='boundary_enforcement_v1'){
  const info=document.createElement('p');info.textContent=node.enforcementStrategy==='multiple_detectors_v1'?'Dois detectors independentes em paralelo por Boundary. Qualquer Block reprova; falha técnica aborta sem regenerar.':'Uma classificação por Boundary. O modelo extrai semântica; resultado/diagnóstico/explicação vêm do evaluator. Falhas abortam. Nenhum retry semântico.';profileBox.append(info);
  if(node.enforcementStrategy){for(const [key,title] of [['enforcementStrategy','Enforcement'],['complementaryContractVersion','Detector complementar'],['aggregationPolicyVersion','Aggregation']]){const el=field(profileBox,title);el.value=node[key]??'Não configurado';el.readOnly=true;}}
  for(const [key,title] of [['boundaryTemplateVersion','Boundary template'],['semanticContractVersion','Semantic detector'],['boundaryEvaluatorVersion','Evaluator']]){const el=field(profileBox,title);el.value=node[key]??'Não configurado';el.readOnly=true;}
  const preview=document.createElement('pre');preview.textContent='Carregando contrato semântico…';profileBox.append(preview);
  const readContract=async query=>{const r=await fetch('/api/boundary-enforcement?'+new URLSearchParams(query));if(!r.ok)throw new Error();return r.json();};
  const pinned={semanticVersion:node.semanticContractVersion??'',evaluatorVersion:node.boundaryEvaluatorVersion??'',templateVersion:node.boundaryTemplateVersion??''};
  readContract(pinned).then(contract=>{preview.textContent=JSON.stringify({modelOutputs:contract.modelOutputs,enforcementOutputs:contract.enforcementOutputs},null,2);}).catch(()=>{preview.textContent='Contrato não configurado ou indisponível. Nenhuma versão foi substituída.';});
  readContract({semanticVersion:'no_teasing_about_topic:semantic-v2',evaluatorVersion:'no_teasing_about_topic:evaluator-v2',templateVersion:'no_teasing_about_topic:v1'}).then(contract=>{
   button(profileBox,'Aplicar detector/evaluator v2 (explícito; preserva fios)',()=>{
    for(const key of ['semanticContractVersion','boundaryEvaluatorVersion','boundaryTemplateVersion','prompt'])node[key]=contract[key];
    node.inputs=[['resposta','text'],['limites','boundary[]'],['mensagens','message[]']].map(([name,type])=>({...node.inputs?.find(i=>i.name===name),id:node.inputs?.find(i=>i.name===name)?.id??crypto.randomUUID(),name,type,required:true}));
    node.responseFields=[];node.outputMode='structured';node.usePersonalityCore=false;node.useStateGuidance=false;node.useInterestProfile=false;node.correctionRetry=false;change();
   });
  }).catch(()=>{});
 }
 if(node.provider==='openai'){
  const box=section();
  select(box,'Esforço de raciocínio',[['','Padrão do provider'],['low','Baixo'],['medium','Médio'],['high','Alto']],node.reasoningEffort??'',v=>{if(v)node.reasoningEffort=v;else delete node.reasoningEffort;change();});
  const budget=field(box,'Máximo de tokens de saída');budget.type='number';budget.min='1';budget.step='1';budget.value=node.maxOutputTokens??'';
  budget.onchange=()=>{if(budget.value)node.maxOutputTokens=Number(budget.value);else delete node.maxOutputTokens;change();};
 }
 const heading=document.createElement('h3');heading.textContent='Entradas tipadas';root.append(heading);
 for(const input of node.inputs??[]){
  const box=section(),name=field(box,'Nome');name.value=input.name;name.maxLength=80;name.onchange=()=>{input.name=name.value;change();};
  select(box,'Tipo',inputTypes.map(t=>[t,typeLabel(t)]),input.type,v=>{input.type=v;delete input.value;if(v.startsWith('enum'))input.values??=[];change();});
  if(input.type.startsWith('enum')){const values=field(box,'Valores (um por linha)','textarea');values.value=(input.values??[]).join('\n');values.onchange=()=>{input.values=values.value.split(/\r?\n/).filter(Boolean);change();};}
  const required=field(box,'Obrigatória');required.type='checkbox';required.checked=input.required!==false;required.onchange=()=>{input.required=required.checked;change();};
  if(['text','number','boolean','enum'].includes(input.type)){
   const connected=graph.edges.some(e=>e.target===node.id&&e.targetPort===input.id);
   const configured=field(box,'Valor configurado');configured.type='checkbox';configured.checked=Object.hasOwn(input,'value');configured.disabled=busy||connected;
   configured.onchange=()=>{if(configured.checked)input.value=input.type==='number'?0:input.type==='boolean'?false:'';else delete input.value;change();};
   if(Object.hasOwn(input,'value')){
    if(input.type==='boolean')select(box,'Valor',[['true','Verdadeiro'],['false','Falso']],String(input.value),v=>{input.value=v==='true';change();});
    else if(input.type==='enum')select(box,'Valor',[['','Selecione'],...(input.values??[]).map(v=>[v,v])],input.value,v=>{input.value=v;change();});
    else {const el=field(box,'Valor');el.type=input.type==='number'?'number':'text';el.value=input.value;el.onchange=()=>{input.value=input.type==='number'?Number(el.value):el.value;change();};}
   }
  }
  button(box,'Remover entrada',()=>{node.inputs=node.inputs.filter(x=>x!==input);change();});
 }
 button(root,'Adicionar entrada',()=>{node.inputs??=[];if(node.inputs.length<32)node.inputs.push({id:crypto.randomUUID(),name:'',type:'text',required:true});change();});
 const retry=field(root,'Permitir uma tentativa corretiva');retry.type='checkbox';retry.checked=node.correctionRetry!==false;retry.onchange=()=>{node.correctionRetry=retry.checked;change();};
}
