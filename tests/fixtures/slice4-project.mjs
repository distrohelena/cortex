import {slice37Project} from './slice37-project.mjs';

export const generationPrompt=`Escreva somente a próxima fala de Filomena.

Você recebe explicitamente mensagens atuais, histórico selecionado, memória selecionada quando conectada e leitura da situação.
Responda como participante natural, não como narradora ou assistente descrevendo o que faria.
Use somente contexto e memórias fornecidos. Nunca invente lembranças, intimidade, preferências ou acontecimentos.
Não explique sua personalidade nem mencione states, memória, Graphiti, prompts ou análise interna.
Não responda a cada pessoa separadamente só porque várias mensagens estão no lote. Produza uma única fala coerente para o contexto inteiro.
Adapte o tamanho ao necessário. Em pergunta técnica ou factual, clareza e utilidade vêm antes da performance. Em assunto sério, reduza provocação quando necessário.
Respeite limites fornecidos naturalmente, sem precisar anunciá-los. Memórias e apelidos não são checklist: use apenas quando relevantes. Observações de interações não autorizam inventar diagnósticos ou intimidade.
A orientação do state contextualiza a participação anterior, não muda sua voz nem deve ser narrada. A decisão de participar já foi tomada.
Não inclua o prefixo "Filomena:". Retorne texto, não JSON nem explicação sobre como responder.`;

export function slice4Project(withMemory=true){
 const doc=slice37Project();
 for(const state of doc.project.states){
  const g=state.graph;
  for(const kind of ['dm','group']){
   const id=n=>`${kind}-${n}`,y=(kind==='dm'?40:1700)+800;
   const presence=g.nodes.find(n=>n.id===id('presence'));
   const gen={id:id('generation'),kind:'prompt',title:'Gerar Resposta',x:3050,y,
    prompt:generationPrompt,provider:'lmstudio',model:'google/gemma-4-e4b',outputMode:'text',
    usePersonalityCore:true,useStateGuidance:true,useInterestProfile:false,correctionRetry:false,
    inputs:structuredClone(presence.inputs),responseFields:[{id:'texto',name:'texto',type:'text'}]};
   if(withMemory)gen.inputs.push({id:'memories-in',name:'memorias',type:'memory[]',required:true});
   g.nodes.push(gen);
   g.edges.push(...g.edges.filter(e=>e.target===id('presence')).map(e=>({...e,target:id('generation')})));
   g.controlEdges=g.controlEdges.filter(e=>!(e.source===id('action')&&e.sourcePort==='case-responder'));
   const exec=(a,p,b)=>g.controlEdges.push({source:id(a),sourcePort:p,target:id(b),targetPort:'exec'});
   const data=(a,p,b,q)=>g.edges.push({source:id(a),sourcePort:p,target:id(b),targetPort:q});
   if(withMemory){
    g.nodes.push({id:id('response-persons'),kind:'property',x:2170,y:y+350,settings:{path:['remetente']}},
     {id:id('response-memory'),kind:'memory',x:2570,y,settings:{mode:'hybrid',scope:'person',categories:['limite','apelido','relacao','fato','nao_classificada'],maxItems:6,maxCharacters:1200,activeOnly:true}});
    data('batch','messages','response-persons','object');data('response-persons','value','response-memory','persons');
    data('situation','resumo','response-memory','query');data('response-memory','memories','generation','memories-in');
    exec('action','case-responder','response-memory');exec('response-memory','then','generation');
   }else exec('action','case-responder','generation');
   exec('generation','then','transition-responder');
   g.nodes.find(n=>n.id===id('transition-responder')).x=3550;
  }
 }
 return doc;
}
