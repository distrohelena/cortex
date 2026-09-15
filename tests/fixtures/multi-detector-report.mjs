// Synthetic benchmark only. Rates USD / million tokens: input .20, cache-read .02,
// cache-write .25, output 1.20. Reasoning is already included in output tokens.
export function summarize(rows) {
  const mean = values => values.length ? values.reduce((a,b)=>a+b,0)/values.length : 0;
  const percentile = (values, p) => values.length ? [...values].sort((a,b)=>a-b)[Math.ceil(values.length*p)-1] : 0;
  const wall = rows.map(r=>r.result.WallMilliseconds);
  const sums = rows.map(r=>r.result.Calls.reduce((n,c)=>n+c.ModelMilliseconds,0));
  const cases = new Map();
  let inputTokens=0, outputTokens=0, reasoningTokens=0, knownUsd=0, missingUsage=0;
  for (const row of rows) {
    const id = `${row.phase}/${row.Id}`;
    if (!cases.has(id)) cases.set(id,new Set());
    if (row.outcome !== 'EnforcementFailure') cases.get(id).add(row.outcome);
    for (const usage of row.usage) {
      if (!usage) { missingUsage++; continue; }
      const input=usage.input_tokens??0, output=usage.output_tokens??0;
      const cached=usage.input_tokens_details?.cached_tokens??0, write=usage.input_tokens_details?.cache_write_tokens??0;
      inputTokens+=input; outputTokens+=output; reasoningTokens+=usage.output_tokens_details?.reasoning_tokens??0;
      knownUsd+=(Math.max(0,input-cached-write)*.20+cached*.02+write*.25+output*1.20)/1e6;
    }
  }
  return {
    runs:rows.length, cases:cases.size,
    fn:rows.filter(r=>!r.expectedAllow && r.outcome==='Allow').length,
    fp:rows.filter(r=>r.expectedAllow && r.outcome==='Block').length,
    aborts:rows.filter(r=>r.outcome==='EnforcementFailure').length,
    instabilityCases:[...cases.values()].filter(v=>v.size>1).length,
    calls:rows.reduce((n,r)=>n+r.result.Calls.length,0),
    meanWallMs:mean(wall), p50WallMs:percentile(wall,.5), p95WallMs:percentile(wall,.95), meanSumMs:mean(sums),
    principalMeanMs:mean(rows.map(r=>r.result.Calls[0]?.ModelMilliseconds??0)),
    complementaryMeanMs:mean(rows.map(r=>r.result.Calls[1]?.ModelMilliseconds??0)),
    inputTokens,outputTokens,reasoningTokens,knownUsd,missingUsage
  };
}
