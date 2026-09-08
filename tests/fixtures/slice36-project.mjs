import {slice35Project,states,situationPrompt} from './slice35-project.mjs';

export const inactiveGuidance=`Filomena está Inativa nesta conversa.

Seu comportamento padrão é observar sem participar e permanecer Inativa.

Não aumente seu nível de atenção apenas porque uma conversa está acontecendo.

Só vale acompanhar de forma especial quando algo concreto capturou seu interesse: ela foi envolvida, surgiu um assunto especialmente relevante para ela, existe continuidade de algo anterior ou apareceu uma oportunidade social realmente promissora.

Se não existe uma razão específica para continuar acompanhando, permaneça Inativa.

Silêncio e desinteresse ativo são comportamentos normais.`;

export const presencePrompt=`Decida apenas se Filomena deve participar agora. Não escreva a resposta dela nem escolha um estado.

Use mensagens, histórico, leitura da situação e somente memórias explicitamente fornecidas. A orientação atual define sua disposição para participar.

calar: não há contribuição natural suficiente agora.
esperar: há envolvimento ou interesse concreto, mas falta completar o pensamento ou surgir a abertura.
responder: existe uma abertura natural e concreta agora.

continuar_acompanhando=true significa que, mesmo sem falar, há uma razão específica para continuar particularmente atenta a este assunto: envolvimento, continuidade ou oportunidade realmente promissora. Não significa que a conversa existe, que há conflito ou que talvez algo interessante apareça. Sem razão concreta, false.

Menções e replies são sinais fortes, não obrigações. Em grupo, não interrompa conversas entre outras pessoas; banalidades e conflitos sem convite não exigem acompanhar. Em DM há expectativa maior de resposta, mas mensagens rápidas podem ser um pensamento incompleto.

Uma restrição sobre COMO falar não proíbe automaticamente participar; não decida o conteúdo de uma futura resposta nesta etapa.

Não invente interesse pessoal, intenção, intimidade ou memória. Motivo: no máximo 12 palavras.`;

export const presenceFields=[
 {id:'acao',name:'acao',type:'enum',values:['calar','esperar','responder']},
 {id:'continuar_acompanhando',name:'continuar_acompanhando',type:'boolean'},
 {id:'motivo',name:'motivo',type:'text',maxWords:12}
];

// Design-only transformation preserves the previous slice's fixtures and stable existing ports.
export function slice36Project({singleCall=false,memory=false}={}){
 if(singleCall&&memory)throw new Error('A comparação single-call não recebe memória.');
 const document=slice35Project(memory);
 document.project.states[0].participationGuidance=inactiveGuidance;
 for(const state of document.project.states){
  const g=state.graph;
  g.nodes=g.nodes.filter(n=>n.kind!=='exit');
  g.edges=g.edges.filter(e=>!e.target.includes('transition-'));
  g.controlEdges=g.controlEdges.filter(e=>!e.target.includes('transition-'));
  for(const kind of ['dm','group']){
   const id=n=>`${kind}-${n}`, y=kind==='dm'?40:1700;
   const node=(name,kind,x,dy,rest={})=>g.nodes.push({id:id(name),kind,x,y:y+dy,...rest});
   const exec=(a,port,b)=>g.controlEdges.push({source:id(a),sourcePort:port,target:id(b),targetPort:'exec'});
   const data=(a,ap,b,bp)=>g.edges.push({source:id(a),sourcePort:ap,target:id(b),targetPort:bp});
   const transition=(name,target,dy)=>node(name,'exit',3350,dy,{settings:{mode:'switch',targetStateId:target}});
   const p=g.nodes.find(n=>n.id===id('presence'));
   p.prompt=presencePrompt;p.responseFields=structuredClone(presenceFields);
   if(!memory)p.inputs=p.inputs.filter(f=>f.id!=='memories-in');
   let observation='situation';
   if(singleCall){
    const situation=g.nodes.find(n=>n.id===id('situation'));
    const oldInputs=p.inputs;
    p.inputs=oldInputs.filter(f=>['batch-in','history-in','conversation-kind'].includes(f.id));
    p.responseFields=[...structuredClone(situation.responseFields),...structuredClone(presenceFields)];
    p.prompt=`Primeiro descreva objetivamente a situação observável; depois decida a participação. A orientação do estado deve afetar a decisão, não alterar os fatos observados. Retorne um único objeto conforme o contrato.\n\n${situationPrompt}\n\nNa mesma saída, após a leitura objetiva:\n${presencePrompt}`;
    p.title='Avaliar Participação';p.x=1150;
    g.nodes=g.nodes.filter(n=>n.id!==id('situation'));
    g.edges=g.edges.filter(e=>e.source!==id('situation')&&e.target!==id('situation'));
    g.controlEdges=g.controlEdges.filter(e=>e.source!==id('situation')&&e.target!==id('situation'));
    exec('history','then','presence');
    observation='presence';
   }
   transition('transition-responder',states[2].id,800);
   exec('action','case-responder','transition-responder');
   if(state.id!==states[2].id){
    node('follow','branch',2560,0,{settings:{operator:'eq',b:true}});
    data('presence','continuar_acompanhando','follow','a');
    exec('action','case-calar','follow');
    if(state.id===states[0].id){
     transition('transition-follow',states[1].id,0);exec('follow','true','transition-follow');
     transition('transition-wait',states[1].id,400);exec('action','case-esperar','transition-wait');
     // follow.false ends without changing state/version; no implicit transition.
    }else{
     transition('transition-disengage',states[0].id,0);exec('follow','false','transition-disengage');
     // follow.true and action.esperar end in Atenta.
    }
   }else{
    for(const [i,action] of ['calar','esperar'].entries()){
     node('involvement-'+action,'switch',2560,i*400,{settings:{cases:['nenhum','indireto','direto'].map(value=>({id:'case-'+value,value})),other:false}});
     data(observation,'envolvimento','involvement-'+action,'value');
     exec('action','case-'+action,'involvement-'+action);
     transition('transition-'+action,states[1].id,i*400);
     exec('involvement-'+action,'case-nenhum','transition-'+action);
     // indirect/direct remain Engaged. No duplicated LLM opinion, Join or timer.
    }
   }
  }
 }
 return document;
}
