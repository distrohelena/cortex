"""Offline checks and summary; no inference."""
import hashlib,json,math,statistics
from pathlib import Path
p=Path(__file__).with_name('laya-profiles-20260929.json')
d=json.loads(p.read_text(encoding='utf-8')); rows=d['results']
assert len(rows)==136
assert hashlib.sha256(json.dumps(d['protocol'],ensure_ascii=False,sort_keys=True).encode()).hexdigest()==d['protocol_sha256']
assert len({(r['phase'],r['profile'],r['case'],r['repeat'],r['reversed'],r['max_len']) for r in rows})==136
for r in rows:
 assert r['correct']==(r['answer']==r['expected'])
 assert math.isfinite(r['seconds']) and r['seconds']>0
 probs=r['response']['answers']['envolvimento']['probabilities']
 assert all(math.isfinite(v) and 0<=v<=1 for v in probs.values())
 assert abs(sum(probs.values())-1)<0.0002
for name,_,_ in d['protocol']['profiles']:
 rs=[r for r in rows if r['phase']=='development' and r['profile']==name]
 assert len(rs)==14
 print(name,'correct',sum(r['correct'] for r in rs),'mean_ms',round(statistics.mean(r['seconds'] for r in rs)*1000,1),'truncated',sum(r['truncated'] for r in rs))
hold=[r for r in rows if r['phase']=='holdout']; assert len(hold)==48
changes=[]
for c in d['protocol']['holdout']:
 rs=[r for r in hold if r['case']==c['id']]
 for rev in (False,True): assert len({r['answer'] for r in rs if r['reversed']==rev})==1
 a=next(r for r in rs if not r['reversed']); b=next(r for r in rs if r['reversed'])
 if a['answer']!=b['answer']: changes.append({'case':c['id'],'expected':c['expected'],'normal':a['answer'],'reversed':b['answer']})
print('Order changes:',json.dumps(changes))
print('Holdout mistakes:',json.dumps([{'case':r['case'],'expected':r['expected'],'answer':r['answer']} for r in hold if r['repeat']==0 and not r['reversed'] and not r['correct']]))
print('Stress:',json.dumps([{k:r[k] for k in ('case','max_len','full_tokens','sequence_tokens','answer','expected','truncated')} for r in rows if r['phase']=='stress']))
print('Reported tokens:',sum(r['response']['usage']['input_tokens'] for r in rows),sum(r['response']['usage']['output_tokens'] for r in rows))
print('VALIDATED: 136 unique local calls; protocol hash intact; repetitions stable within order.')
