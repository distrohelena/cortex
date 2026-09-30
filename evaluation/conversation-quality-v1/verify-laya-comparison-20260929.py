"""Offline evidence checks; never invokes inference or remote services."""
import json, math
from pathlib import Path
from transformers import AutoTokenizer
from laya.common import render_options
data = json.loads(Path(__file__).with_name('laya-comparison-20260929.json').read_text(encoding='utf-8'))
assert len(data['results']) == 84
for profile in ('laya-default','laya-context4096','jev-compact'):
    rows = [r for r in data['results'] if r['profile'] == profile]
    assert len(rows) == 28
    assert len({(r['case'],r['repeat']) for r in rows}) == 28
    for r in rows:
        assert 'error_type' not in r
        answer = r['response']['answers']['envolvimento']
        p = answer['probabilities']
        assert set(p) == {'direto','indireto','nenhum'}
        assert all(math.isfinite(v) and 0 <= v <= 1 for v in p.values())
        assert abs(sum(p.values())-1) < (0.016 if profile=='jev-compact' else 0.0002)
        assert p[r['answer']] == max(p.values())
        assert r['correct'] == (r['answer'] == r['expected'])
    print(profile,'correct',sum(r['correct'] for r in rows),'repeat disagreements',sum(len({r['answer'] for r in rows if r['case']==c['id']})!=1 for c in data['cases']))
tok=AutoTokenizer.from_pretrained(str(Path(data['local']['checkpoint'])/'tokenizer'),local_files_only=True)
q=data['questions']['envolvimento']
head=len(tok('choice question: '+q['instructions'],add_special_tokens=False)['input_ids'])
options=[1+len(tok(' '+s,add_special_tokens=False)['input_ids']) for s in render_options({'t':'choice','ins':q['instructions'],'crit':q['criteria']})]
assert all(n<=49 for n in options)
assert head+sum(options)<=256
assert not any(r.get('state_truncated') for r in data['results'])
print('No head/option/state truncation; head tokens',head,'option tokens',options)
print('All 84 records validated offline; no inference executed.')
