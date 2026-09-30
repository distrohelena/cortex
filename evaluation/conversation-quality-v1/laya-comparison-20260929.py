"""Isolated typed-decision evaluation; synthetic data, no runtime/transport imports."""
import json, os, sys, time, hashlib, statistics, urllib.request
from pathlib import Path

OUT = Path(__file__).with_suffix('.json')
Q = {'envolvimento': {'type': 'choice', 'instructions':
    'Classifique o envolvimento de Filomena na mensagem ATUAL. Use autor e destinatário da mensagem citada. Menção sobre ela não é convite. Interesse no assunto não é envolvimento.',
    'criteria': {'direto': 'Fala dirigida a Filomena, convite ou reply à fala dela.',
                 'indireto': 'Continuação de conversa com participação recente de Filomena, sem fala dirigida a ela.',
                 'nenhum': 'Conversa entre terceiros, mera menção ou assunto sem participação de Filomena.'}}}
CASES = [
 ('direct', 'direto', '', 'Filomena, me ajuda com esse erro?', None),
 ('direct-paraphrase', 'direto', '', 'Filo, sabe como resolver isso?', None),
 ('reply', 'direto', '', 'E como eu faço isso?', {'autor':'Filomena','texto':'Você pode limitar a concorrência com SemaphoreSlim.'}),
 ('reply-paraphrase', 'direto', '', 'Pode explicar esse passo?', {'autor':'Filomena','texto':'Primeiro limite as requisições simultâneas.'}),
 ('third-party-reply', 'nenhum', '', 'Como você resolveu?', {'autor':'Celso','texto':'Consegui consertar a API.'}),
 ('mention', 'nenhum', '', 'Celso, a Filomena respondeu você ontem?', None),
 ('indirect', 'indireto', 'Filomena: Esse jogo tem uma trilha ótima.\nCelso: Sim, principalmente a última fase.', 'A música da primeira fase também é ótima.', None),
 ('indirect-paraphrase', 'indireto', 'Filomena: A trilha desse jogo é ótima.\nCelso: Concordo, adoro a última fase.', 'Também gosto muito da música da primeira.', None),
 ('migration', 'nenhum', 'Filomena: Para limitar concorrência use SemaphoreSlim.\nCelso: Resolvido, obrigado.', 'Andreas, mudando de assunto, você vai ao aniversário do Felipe?', None),
 ('banal-tech', 'nenhum', '', 'Celso, deixei a caixa da GPU na sua mesa.', None),
 ('quoted-call', 'nenhum', '', 'Celso, ontem o Felipe escreveu "Filomena, me ajuda" e depois foi dormir.', None),
 ('social-direct', 'direto', '', 'Filomena, você fala isso mas quero ver jogar comigo.', None),
 ('long-direct', 'direto', 'Celso: Estamos combinando o almoço.\nAndreas: Pode ser amanhã.\n' * 35, 'Filomena, preciso da sua ajuda agora.', None),
 ('long-third-party', 'nenhum', 'Filomena: Posso ajudar com código.\nCelso: Obrigado, já terminei.\n' * 35, 'Andreas, mudando de assunto, você trouxe o almoço?', None),
]

def state(c):
    return {'personagem':'Filomena','historico':c[2], 'mensagem_atual':{'autor':'Helena','texto':c[3],'reply':c[4]}}

def main():
    mode = sys.argv[1]
    data = json.loads(OUT.read_text(encoding='utf-8')) if OUT.exists() else {'questions':Q,'cases':[{'id':c[0],'expected':c[1],'state':state(c)} for c in CASES], 'results':[], 'scope':'synthetic involvement only; not production flow; no training; no activation'}
    if mode == 'laya':
        import torch
        from laya.agent import Agent
        from laya.common import build_sequence
        torch.set_num_threads(4)
        checkpoint = str(Path.home()/'.cache/huggingface/hub/models--convaiinnovations--laya/snapshots/5e7b2b1b8ca2ecdd3f2322d94069c9b6ce7e844b/multilingual')
        started = time.perf_counter()
        agent = Agent(checkpoint, device='cpu')
        data['local'] = {'checkpoint':checkpoint,'torch':torch.__version__,'load_seconds':time.perf_counter()-started,'threads':4,'device':'cpu','default_config':dict(agent.cfg)}
        profiles = [('laya-default',1024),('laya-context4096',4096)]
    else:
        if not os.environ.get('TYPESAFE_API_KEY'): raise RuntimeError('credential unavailable')
        profiles = [('jev-compact',None)]
    for profile, budget in profiles:
        if mode == 'laya': agent.cfg['max_len'] = budget
        for c in CASES:
            for repeat in range(2):
                request = {'model':'jev-1.13.0','state':state(c),'questions':Q}
                start = time.perf_counter()
                row = {'profile':profile,'case':c[0],'repeat':repeat,'expected':c[1]}
                try:
                    if mode == 'laya':
                        raw = agent.system_one(state(c), Q)
                        internal = agent._to_internal(Q['envolvimento'])
                        seq, _ = build_sequence(agent.tok,state(c),internal,budget,256)
                        full, _ = build_sequence(agent.tok,state(c),internal,8192,256)
                        row['sequence_tokens'] = len(seq)
                        row['untruncated_tokens'] = len(full)
                        row['state_truncated'] = len(seq) < len(full)
                    else:
                        req = urllib.request.Request('https://api.typesafe.ai/v1/systemone',json.dumps(request,ensure_ascii=False).encode(),{'Authorization':'Bearer '+os.environ['TYPESAFE_API_KEY'],'Content-Type':'application/json'})
                        with urllib.request.urlopen(req,timeout=60) as response: raw = json.load(response)
                        if raw.get('model') != 'jev-1.13.0': raise ValueError('model mismatch')
                    answer = raw['answers']['envolvimento']['choice']
                    if answer not in Q['envolvimento']['criteria']: raise ValueError('invalid choice')
                    row.update(answer=answer,correct=answer==c[1],response=raw)
                except Exception as error:
                    row.update(error_type=type(error).__name__,correct=False)
                row['seconds'] = time.perf_counter()-start
                data['results'].append(row)
                OUT.write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8')
                print(profile,c[0],repeat,row.get('answer',row.get('error_type')),round(row['seconds'],3),flush=True)
                if row.get('error_type'): raise RuntimeError('experiment stopped on error; see sanitized artifact')
    for profile,_ in profiles:
        rows=[r for r in data['results'] if r['profile']==profile]
        print(profile, sum(r['correct'] for r in rows),'/',len(rows),'mean seconds',statistics.mean(r['seconds'] for r in rows),flush=True)

if __name__ == '__main__': main()
