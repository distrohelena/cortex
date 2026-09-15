// Benchmark-only accounting. Quantiles use nearest rank; invalid responses are timed too.
export function scoreModel(model, rows) {
  const attempts=rows.filter(r=>r.fixture);
  const unavailable=rows.some(r=>r.status==='unavailable');
  const times=attempts.map(r=>r.milliseconds).sort((a,b)=>a-b);
  const quantile=p=>times.length?times[Math.ceil(p*times.length)-1]:null;
  const outcomes=new Map();
  let inputTokens=0,outputTokens=0,reasoningTokens=0,cachedTokens=0,cacheWriteTokens=0,reportedUsage=0;
  for(const r of attempts){
    const variants=outcomes.get(r.fixture)??new Set();
    variants.add(r.status==='valid'?r.actual:r.status);outcomes.set(r.fixture,variants);
    if(!r.usage)continue;
    reportedUsage++;
    inputTokens+=r.usage.input_tokens??r.usage.prompt_tokens??0;
    outputTokens+=r.usage.output_tokens??r.usage.completion_tokens??0;
    reasoningTokens+=r.usage.output_tokens_details?.reasoning_tokens??r.usage.completion_tokens_details?.reasoning_tokens??0;
    cachedTokens+=r.usage.input_tokens_details?.cached_tokens??0;
    cacheWriteTokens+=r.usage.input_tokens_details?.cache_write_tokens??0;
  }
  const prices={'gpt-5.6-luna':[.2,.02,1.2],'gpt-5.6-terra':[2,.2,12],'gpt-5.6-sol':[4,.4,20]};
  const price=prices[model];
  const estimatedUsd=price&&reportedUsage===attempts.length&&attempts.length
    ? ((inputTokens-cachedTokens-cacheWriteTokens)*price[0]+cachedTokens*price[1]+cacheWriteTokens*price[0]*1.25+outputTokens*price[2])/1e6:null;
  return {model,attempts:attempts.length,unavailable,
    fn:attempts.length?attempts.filter(r=>r.status==='valid'&&r.expected==='bloquear'&&r.actual==='permitir').length:null,
    fp:attempts.length?attempts.filter(r=>r.status==='valid'&&r.expected==='permitir'&&r.actual==='bloquear').length:null,
    invalid:attempts.length?attempts.filter(r=>r.status==='invalid').length:null,
    unstableFixtures:[...outcomes.values()].filter(v=>v.size>1).length,
    meanMs:times.length?times.reduce((a,b)=>a+b,0)/times.length:null,p50Ms:quantile(.5),p95Ms:quantile(.95),
    inputTokens,outputTokens,reasoningTokens,cachedTokens,cacheWriteTokens,reportedUsage,estimatedUsd};
}
