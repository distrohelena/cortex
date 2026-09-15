import { summarize } from './personal-attribution-spike-report.mjs';

// Original rows are immutable. Legacy technical_error is categorized only in this derived report.
export function statusOf(row) {
  if (row.Status !== 'technical_error') return row.Status;
  if (row.Error === 'http_' || row.Error === 'transport_cancelled') return 'transport_failure';
  return 'provider_failure';
}

export function summarizeResumed(rows) {
  const result={};
  for(const strategy of ['A','C']) {
    const all=rows.filter(r=>r.Strategy===strategy);
    if(!all.length) continue;
    const valid=all.filter(r=>r.Status==='valid');
    const failed=all.filter(r=>r.Status!=='valid');
    const stats=summarize(valid)[strategy] ?? {};
    const costs=summarize(failed)[strategy];
    const counts={};
    for(const r of valid) counts[r.Id]=(counts[r.Id]??0)+1;
    const transportFailures=all.filter(r=>statusOf(r)==='transport_failure').length;
    const providerFailures=all.filter(r=>statusOf(r)==='provider_failure').length;
    const timeouts=all.filter(r=>statusOf(r)==='timeout').length;
    result[strategy]={
      ...stats,attempts:all.length,valid:valid.length,validPerFixture:counts,
      transportFailures,providerFailures,timeouts,
      technicalFailures:transportFailures+providerFailures+timeouts,
      technical:transportFailures+providerFailures+timeouts,
      technicalFailureRate:(transportFailures+providerFailures+timeouts)/all.length,
      invalid:all.filter(r=>r.Status==='invalid_output').length,
      failureDurationsMs:failed.map(r=>r.Milliseconds),
      validKnownUsd:stats.usd??0,failedKnownUsd:costs?.usd??0,
      knownTotalUsd:(stats.usd??0)+(costs?.usd??0),
      unknownFailureUsage:failed.filter(r=>!r.Usage).length,
      unknownValidUsage:valid.filter(r=>!r.Usage).length,
      failedAttempts:failed.map(r=>({id:r.Id,repetition:r.Repetition,originalStatus:r.Status,status:statusOf(r),error:r.Error,durationMs:r.Milliseconds,usage:r.Usage}))
    };
  }
  return result;
}
