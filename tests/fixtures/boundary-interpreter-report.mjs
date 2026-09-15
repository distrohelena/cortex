import fs from 'node:fs';
const input=process.argv[2];
if(!input)throw new Error('Pass the generated synthetic benchmark JSON path.');
const rows=JSON.parse(fs.readFileSync(input,'utf8'));
const summaries=[];
for(const model of ['gpt-5.6-luna','gpt-5.6-terra']){
 const set=rows.filter(r=>r.model===model),positives=set.filter(r=>r.expected==='compiled');
 const unstable=[...new Set(set.map(r=>r.id))].filter(id=>new Set(set.filter(r=>r.id===id).map(r=>JSON.stringify(r.proposal))).size>1);
 const timings=set.map(r=>r.milliseconds).sort((a,b)=>a-b);
 const percentile=p=>timings[Math.ceil(timings.length*p)-1]??null;
 const tokens={input:0,output:0,reasoning:0,cached:0,cacheWrite:0};
 for(const row of set){const u=row.usage??{};tokens.input+=u.input_tokens??0;tokens.output+=u.output_tokens??0;tokens.reasoning+=u.output_tokens_details?.reasoning_tokens??0;tokens.cached+=u.input_tokens_details?.cached_tokens??0;tokens.cacheWrite+=u.input_tokens_details?.cache_write_tokens??0;}
 const rate=model==='gpt-5.6-luna'?.2:2;
 const cost=((tokens.input-tokens.cached-tokens.cacheWrite)*rate+tokens.cached*rate*.1+tokens.cacheWrite*rate*1.25+tokens.output*rate*6)/1e6;
 summaries.push({model,runs:set.length,rawFalseCompiled:set.filter(r=>r.expected!=='compiled'&&r.proposal?.Status==='compiled').map(r=>({id:r.id,repetition:r.repetition})),
  publishedFalseCompiled:set.filter(r=>r.expected!=='compiled'&&r.admission?.Status==='compiled').length,
  falseAbstentions:positives.filter(r=>r.admission?.Status!=='compiled').length,admissibleAttempts:positives.length,
  rejectedEvidence:positives.filter(r=>r.proposal?.Status==='compiled'&&r.admission?.Status!=='compiled').length,
  invalid:set.filter(r=>r.error).length,unstableCases:unstable,meanMs:timings.reduce((a,b)=>a+b,0)/timings.length,p50Ms:percentile(.5),p95Ms:percentile(.95),tokens,estimatedUsd:cost});
}
fs.writeFileSync('docs/boundary-interpreter-results.json',JSON.stringify({reasoningEffort:'medium',repetitions:3,baselineRetries:0,pricingAsOf:'2026-09-08',summaries,rows},null,2)+'\n');
console.log(JSON.stringify(summaries,null,2));
