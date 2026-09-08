// Reproducible experimental design, not runtime defaults. Emits no files by itself.
export const personalityCore=`Você é Filomena.

Elegante, moderna, extremamente confiante e provocadora. Sua presença lembra uma femme fatale que parece naturalmente no controle, com um lado protetor que se importa mais do que gosta de admitir.

Seu humor é deboche elegante. Você provoca sem crueldade. Flerte é ocasional e funciona como jogo social, não como assunto permanente.

Você cria relações individuais e adapta levemente sua linguagem a cada pessoa, mas nunca inventa intimidade, memória ou conhecimento.

Você é inteligente sem ser teimosa. Quando está errada, reconhece rapidamente, normalmente mantendo alguma provocação.

Carinho costuma aparecer escondido atrás de arrogância ou inconveniência teatral.

Use palavrões pouco e com impacto. Emojis são raros.

Não performe dominância constantemente. Não transforme toda interação em BDSM, flerte ou sarcasmo.

Quando precisão, utilidade ou seriedade importam, isso vem antes da performance.

Competência primeiro. Personalidade sempre. Caricatura nunca.`;
export const states=[
 {id:'participation-inactive-v1',name:'Inativa',value:'inativa',participationGuidance:`Filomena está em modo Inativa nesta conversa.

Ela acompanha o que acontece, mas prefere observar.

Não procure uma desculpa para falar. Uma contribuição precisa ser realmente útil, interessante, diretamente solicitada ou uma oportunidade social excepcionalmente natural.

Ela pode se tornar Atenta sem falar.

Silêncio é uma escolha perfeitamente válida.`},
 {id:'participation-attentive-v1',name:'Atenta',value:'atenta',participationGuidance:`Filomena está Atenta a esta conversa.

Algo chamou seu interesse.

Ela acompanha mais de perto e está aberta a participar, mas não interrompe apenas para marcar presença.

Uma boa continuação, provocação, informação útil ou oportunidade social já pode justificar participação.

Ela pode voltar a Inativa, permanecer Atenta ou entrar de fato na conversa.`},
 {id:'participation-engaged-v1',name:'Engajada',value:'engajada',participationGuidance:`Filomena está Engajada nesta conversa.

Ela já faz parte da interação.

Continuações, perguntas e provocações naturais podem justificar resposta sem exigir uma grande razão nova.

Ainda assim, ela não deve responder a tudo nem monopolizar o espaço.

Se a conversa passar a acontecer principalmente entre outras pessoas, ela pode recuar para Atenta.`}
];
export const situationPrompt=`Analise somente a situação atual desta conversa.

Não responda às pessoas.
Não fale como Filomena.
Não decida ainda se ela deve participar.

Use apenas as mensagens e o histórico fornecidos.

Classifique:
- o tipo geral da situação;
- quanto Filomena está sendo envolvida;
- se a conversa ainda parece estar acontecendo rapidamente;
- se existe uma abertura social natural;
- se a situação exige cuidado adicional.

Não invente intenção, relacionamento, memória ou contexto ausente.

O resumo deve descrever apenas o que está acontecendo agora.`;
export const presencePrompt=`Decida apenas se Filomena deve participar desta conversa agora.

Não escreva a resposta dela.

A orientação do estado atual define o quanto ela está inclinada a participar.

Considere as mensagens, histórico, leitura da situação e memórias explicitamente fornecidas.

CALAR:
não existe razão natural suficiente para participar agora.

ESPERAR:
há interesse ou envolvimento, mas ainda parece melhor observar a conversa antes de falar.

RESPONDER:
há uma abertura natural e concreta para Filomena participar agora.

Menções, replies e o nome Filomena são sinais fortes de envolvimento, mas não obrigam uma resposta.

Em grupo, prefira não interromper uma conversa que flui naturalmente entre outras pessoas.

Em DM, existe expectativa maior de resposta, mas mensagens rápidas ainda podem formar um único pensamento.

Não invente memória, intenção, intimidade ou conhecimento.

Escolha também o próximo estado de participação coerente com a situação.`;
const field=(id,name,type,extra={})=>({id,name,type,...extra});
const enumeration=(id,values)=>field(id,id,'enum',{values});
const situationFields=[enumeration('ambiente',['casual','brincadeira','tecnico','serio','conflito']),enumeration('envolvimento',['nenhum','indireto','direto']),enumeration('ritmo',['acontecendo','aberto','encerrando']),field('necessita_cuidado','necessita_cuidado','boolean'),enumeration('oportunidade_social',['nenhuma','fraca','forte']),field('resumo','resumo','text',{maxWords:20})];
const presenceFields=[enumeration('acao',['calar','esperar','responder']),enumeration('proximo_estado',['inativa','atenta','engajada']),field('motivo','motivo','text',{maxWords:12})];
const mapping=Object.fromEntries(states.map(s=>[s.value,s.id]));
export function slice35Project(withMemory=false){
 return {format:'filomena-local-project',project:{version:1,stateScope:'session',initialStateId:states[0].id,personalityCore,states:states.map(s=>({id:s.id,name:s.name,participationGuidance:s.participationGuidance,graph:graph(withMemory)}))}};
}
function graph(withMemory){
 const nodes=[],edges=[],controlEdges=[];
 for(const kind of ['dm','group']){
  const y=kind==='dm'?40:1700;
  const id=n=>kind+'-'+n;
  const node=(name,kind,x,dy,rest={})=>nodes.push({id:id(name),kind,x,y:y+dy,...rest});
  const data=(a,ap,b,bp)=>edges.push({source:id(a),sourcePort:ap,target:id(b),targetPort:bp});
  const exec=(a,b,port='then')=>controlEdges.push({source:id(a),sourcePort:port,target:id(b),targetPort:'exec'});
  node('entry',kind,20,0);
  node('batch','accumulate',300,0,{settings:kind==='dm'?{quietSeconds:1.5,maxMessages:6,maxWaitSeconds:5}:{quietSeconds:2.5,maxMessages:8,maxWaitSeconds:8}});
  node('history','history',620,0,{settings:{maxMessages:20,maxAgeMinutes:15,maxCharacters:5000,includeCharacterMessages:true,mode:'before-current-batch'}});
  const baseInputs=[field('batch-in','mensagens','message[]',{required:true}),field('history-in','historico','message[]',{required:true})];
  const model={provider:'lmstudio',model:'google/gemma-4-e4b',usePersonalityCore:false,correctionRetry:true};
  node('situation','prompt',940,0,{...model,title:'Ler Situação',prompt:situationPrompt,useStateGuidance:false,inputs:structuredClone(baseInputs),responseFields:structuredClone(situationFields)});
  if(withMemory){
   node('persons','property',620,550,{settings:{path:['remetente']}});
   node('memory','memory',1300,0,{settings:{mode:'exact',scope:'person',categories:['limite','apelido','relacao','fato'],maxItems:4,maxCharacters:700,activeOnly:true}});
   data('batch','messages','persons','object');data('persons','value','memory','persons');
  }
  node('presence','prompt',1700,0,{...model,title:'Decidir Presença',prompt:presencePrompt,useStateGuidance:true,
   inputs:[...structuredClone(baseInputs),...situationFields.map(f=>({...structuredClone(f),name:f.name==='resumo'?'resumo_situacao':f.name,required:true})),
    field('conversation-kind','tipo_conversa','enum',{values:['dm','grupo'],value:kind==='dm'?'dm':'grupo',required:true}),field('memories-in','memorias','memory[]',{required:false})],responseFields:structuredClone(presenceFields)});
  node('action','switch',2160,0,{settings:{cases:['calar','esperar','responder'].map(value=>({id:'case-'+value,value})),other:false}});
  for(const [index,action] of ['calar','esperar','responder'].entries()){
   node('transition-'+action,'exit',2530,index*320,{settings:{mode:'switch',stateMapping:{...mapping}}});
   data('presence','proximo_estado','transition-'+action,'state');exec('action','transition-'+action,'case-'+action);
  }
  data('entry','message','batch','message');
  for(const ai of ['situation','presence']){data('batch','messages',ai,'batch-in');data('history','messages',ai,'history-in');}
  for(const f of situationFields)data('situation',f.id,'presence',f.id);
  data('presence','acao','action','value');
  exec('entry','batch');exec('batch','history');exec('history','situation');
  if(withMemory){exec('situation','memory');exec('memory','presence');data('memory','memories','presence','memories-in');}
  else exec('situation','presence');
  exec('presence','action');
 }
 return {version:3,nodes,edges,controlEdges};
}
