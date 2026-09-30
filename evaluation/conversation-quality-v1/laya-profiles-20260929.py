"""Disposable offline probe. No production imports, credentials, or services."""
import copy, hashlib, json, math, statistics, time
from pathlib import Path
import torch
from laya.agent import Agent
from laya.common import build_sequence

OUT=Path(__file__).with_suffix('.json')
BASE=json.loads(Path(__file__).with_name('laya-comparison-20260929.json').read_text(encoding='utf-8'))
PT={'type':'choice','instructions':'A mensagem atual é dirigida a Filomena, continua uma conversa dela, ou é entre outras pessoas?', 'criteria':{
 'direto':'A pessoa está falando com Filomena.',
 'indireto':'A pessoa continua o assunto de Filomena, mas não fala com ela.',
 'nenhum':'A pessoa não está conversando com Filomena.'}}
EN={'type':'choice','instructions':'Is the current message addressed to Filomena, continuing her conversation, or unrelated to her?', 'criteria':{
 'direto':'The speaker is talking to Filomena.',
 'indireto':'The speaker continues her conversation without addressing her.',
 'nenhum':'The speaker is not talking with Filomena.'}}
PROFILES=[('baseline-json',BASE['questions']['envolvimento'],'json'),('short-pt-json',PT,'json'),('short-en-json',EN,'json'),('short-pt-text',PT,'text'),('short-en-text',EN,'text')]
semantic=copy.deepcopy(PT)
semantic['criteria']={'falando_com_Filomena':PT['criteria']['direto'],'continuando_assunto':PT['criteria']['indireto'],'conversa_dos_outros':PT['criteria']['nenhum']}
PROFILES.append(('semantic-labels-text',semantic,'text'))
LABELS={'falando_com_Filomena':'direto','continuando_assunto':'indireto','conversa_dos_outros':'nenhum','direto':'direto','indireto':'indireto','nenhum':'nenhum'}

def case(id,expected,text,history='',reply=None):
 return {'id':id,'expected':expected,'state':{'personagem':'Filomena','historico':history,'mensagem_atual':{'autor':'Helena','texto':text,'reply':reply}}}

HOLDOUT=[
 case('h01','direto','Filomena, qual desses dois filmes você escolheria?'),
 case('h02','direto','Filo, pode ficar aqui comigo um pouco?'),
 case('h03','direto','Você consegue mostrar um exemplo?',reply={'autor':'Filomena','texto':'Esse método aceita um CancellationToken.'}),
 case('h04','direto','Filomena, duvido que você ganhe de mim no Mario Kart.'),
 case('h05','nenhum','Felipe, você viu o que a Filomena escreveu ontem?'),
 case('h06','nenhum','Me mostra um exemplo?',reply={'autor':'Andreas','texto':'Resolvi usando um canal limitado.'}),
 case('h07','nenhum','Celso, acabou o café da cozinha.'),
 case('h08','nenhum','Andreas, outra coisa: o churrasco ficou para sábado?',history='Filomena: Pronto, esse é o comando.\nHelena: Deu certo, encerrado.'),
 case('h09','indireto','A fotografia desse filme também é linda.',history='Filomena: A trilha desse filme me pegou.\nCelso: A mim também.'),
 case('h10','indireto','O baixo nessa faixa ficou muito bom também.',history='Filomena: Eu gostei da bateria dessa música.\nFelipe: O baterista mandou bem.'),
 case('h11','nenhum','Celso, a frase do print era "Filomena, vem aqui". Achei o print antigo.'),
 case('h12','nenhum','Felipe, minha API voltou a funcionar.')]

def format_state(s,fmt):
 if fmt=='json': return s
 m=s['mensagem_atual']; reply=m['reply']
 cited='Nenhuma' if reply is None else reply['autor']+': '+reply['texto']
 return f"Personagem: {s['personagem']}\nHistórico (não é mensagem atual):\n{s['historico']}\nMensagem citada pela mensagem atual: {cited}\nMensagem atual de {m['autor']}: {m['texto']}"

def main():
 if OUT.exists(): raise RuntimeError('Refusing to overwrite prior experiment')
 torch.set_num_threads(4)
 protocol={'profiles':PROFILES,'development':BASE['cases'],'holdout':HOLDOUT,'selection':'highest development accuracy; tie = declaration order; holdout never used to revise profile','repeats':'development 1; selected holdout 2 each normal/reversed criteria','local_only':True}
 data={'protocol':protocol,'protocol_sha256':hashlib.sha256(json.dumps(protocol,ensure_ascii=False,sort_keys=True).encode()).hexdigest(),'results':[]}
 def save(): OUT.write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8')
 save()
 start=time.perf_counter(); agent=Agent(BASE['local']['checkpoint'],device='cpu')
 data['load_seconds']=time.perf_counter()-start
 data['checkpoint']=BASE['local']['checkpoint']; data['torch']=torch.__version__
 def run(profile,c,phase,repeat=0,reverse=False,budget=1024):
  name,q,fmt=profile; q=copy.deepcopy(q)
  if reverse: q['criteria']=dict(reversed(list(q['criteria'].items())))
  s=format_state(c['state'],fmt); agent.cfg['max_len']=budget
  internal=agent._to_internal(q)
  seq,_=build_sequence(agent.tok,s,internal,budget,256)
  full,_=build_sequence(agent.tok,s,internal,8192,256)
  start=time.perf_counter(); response=agent.system_one(s,{'envolvimento':q}); elapsed=time.perf_counter()-start
  a=response['answers']['envolvimento']; p=a['probabilities']
  assert set(p)==set(q['criteria']) and all(math.isfinite(v) and 0<=v<=1 for v in p.values())
  assert abs(sum(p.values())-1)<0.0002 and p[a['choice']]==max(p.values())
  answer=LABELS[a['choice']]
  row={'phase':phase,'profile':name,'case':c['id'],'repeat':repeat,'reversed':reverse,'max_len':budget,'expected':c['expected'],'answer':answer,'correct':answer==c['expected'],'seconds':elapsed,'sequence_tokens':len(seq),'full_tokens':len(full),'truncated':len(seq)<len(full),'response':response}
  data['results'].append(row); save()
  return row
 scores=[]
 for profile in PROFILES:
  rows=[run(profile,c,'development') for c in BASE['cases']]
  score=sum(r['correct'] for r in rows); scores.append(score)
  print(profile[0],score,'/14',flush=True)
 selected=PROFILES[max(range(len(scores)),key=lambda i:scores[i])]
 data['selected_profile']=selected[0]; save(); print('Frozen selected profile:',selected[0],flush=True)
 for reverse in (False,True):
  for rep in range(2):
   rows=[run(selected,c,'holdout',rep,reverse) for c in HOLDOUT]
   print('holdout','reversed',reverse,'repeat',rep,sum(r['correct'] for r in rows),'/12',flush=True)
 # Separate stress probe, not part of selection or holdout score.
 stress=[case('overflow-direct','direto','Filomena, pode me ajudar agora?',history='Celso: O almoço está combinado.\nAndreas: Certo, vamos amanhã.\n'*90),case('overflow-other','nenhum','Andreas, você trouxe o almoço?',history='Filomena: Posso ajudar com código.\nCelso: Já resolvi, obrigado.\n'*90)]
 data['stress_cases']=stress; save()
 for budget in (1024,4096):
  for c in stress:
   r=run(selected,c,'stress',budget=budget)
   print('stress',budget,c['id'],r['answer'],'truncated',r['truncated'],flush=True)
 assert len(data['results'])==136
 print('COMPLETE',len(data['results']),'local calls; remote calls=0',flush=True)

if __name__=='__main__': main()
