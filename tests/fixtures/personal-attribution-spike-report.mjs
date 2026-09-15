// USD / million tokens, official model pages checked 2026-09-09.
// Output includes reasoning; cache writes cost 1.25x uncached input.
export function summarize(rows) {
  const result = {};
  for (const strategy of ['A', 'C']) {
    const selected = rows.filter(r => r.Strategy === strategy);
    if (!selected.length) continue;
    const valid = selected.filter(r => r.Status === 'valid');
    const times = selected.map(r => r.Milliseconds).sort((a, b) => a - b);
    const unstable = key => {
      const cases = new Map();
      for (const r of valid) {
        if (!cases.has(r.Id)) cases.set(r.Id, new Set());
        cases.get(r.Id).add(key(r));
      }
      return [...cases].filter(([,values]) => values.size > 1).map(([id]) => id);
    };
    const fn = r => !r.ExpectedAllow && r.Outcome === 'Allow';
    const fp = r => r.ExpectedAllow && r.Outcome === 'Block';
    const errors = valid.filter(r => fn(r) || fp(r));
    const policyUnstable = unstable(r => r.Outcome);
    const featureUnstable = unstable(r => JSON.stringify(Object.entries(r.Features ?? {}).sort(([a],[b]) => a.localeCompare(b))));
    let usd = 0, inputTokens = 0, outputTokens = 0, reasoningTokens = 0, cachedTokens = 0, cacheWriteTokens = 0, missingUsage = 0;
    for (const r of selected) {
      const u = r.Usage;
      if (!u) { missingUsage++; continue; }
      const input = u.input_tokens ?? 0, output = u.output_tokens ?? 0;
      const cached = u.input_tokens_details?.cached_tokens ?? 0;
      const write = u.input_tokens_details?.cache_write_tokens ?? 0;
      const rate = strategy === 'A' ? 0.20 : 2;
      usd += (Math.max(0,input-cached-write)*rate + cached*rate*0.1 + write*rate*1.25 + output*rate*6)/1e6;
      inputTokens += input; outputTokens += output;
      reasoningTokens += u.output_tokens_details?.reasoning_tokens ?? 0;
      cachedTokens += cached; cacheWriteTokens += write;
    }
    result[strategy] = {
      calls: selected.length, fixtures: new Set(selected.map(r => r.Id)).size,
      fn: valid.filter(fn).length, fp: valid.filter(fp).length,
      ambiguous: valid.filter(r => Object.values(r.Features ?? {}).includes('ambiguo')).length,
      invalid: selected.filter(r => r.Status === 'invalid_output').length,
      technical: selected.filter(r => r.Status === 'technical_error').length,
      policyUnstableFixtures: policyUnstable.length, policyUnstable,
      featureUnstableFixtures: featureUnstable.length, featureUnstable,
      meanMs: times.reduce((s,v) => s+v,0)/times.length,
      p50Ms: times[Math.ceil(times.length*0.5)-1], p95Ms: times[Math.ceil(times.length*0.95)-1],
      inputTokens, outputTokens, reasoningTokens, cachedTokens, cacheWriteTokens, usd, missingUsage,
      categories: Object.fromEntries([...new Set(selected.map(r=>r.Category))].map(category=>{
        const group=valid.filter(r=>r.Category===category);
        return [category,{calls:group.length,fn:group.filter(fn).length,fp:group.filter(fp).length}];
      })),
      errors: errors.map(r => ({id:r.Id,repetition:r.Repetition,type:fn(r)?'FN':'FP',candidate:r.Candidate,features:r.Features}))
    };
  }
  return result;
}
