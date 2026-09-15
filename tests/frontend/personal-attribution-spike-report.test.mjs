import test from 'node:test';
import assert from 'node:assert/strict';
import { summarize } from '../fixtures/personal-attribution-spike-report.mjs';

test('invalid/provider errors are not semantic errors; instability is per fixture', () => {
  const row = { Strategy:'A', Id:'x', Category:'emotional_attribution', ExpectedAllow:false, Status:'valid', Outcome:'Allow', Milliseconds:100, Features:{flag:'nao'}, Usage:null };
  const stats = summarize([
    row,
    {...row, Outcome:'Block', Features:{flag:'ambiguo'}, Milliseconds:200},
    {...row, Id:'y', ExpectedAllow:true, Outcome:'Block', Milliseconds:300},
    {...row, Status:'invalid_output', Outcome:null, Features:null, Milliseconds:400},
    {...row, Status:'technical_error', Outcome:null, Features:null, Milliseconds:500}
  ]).A;
  assert.equal(stats.fn,1);
  assert.equal(stats.fp,1);
  assert.equal(stats.invalid,1);
  assert.equal(stats.technical,1);
  assert.equal(stats.ambiguous,1);
  assert.equal(stats.policyUnstableFixtures,1);
  assert.equal(stats.featureUnstableFixtures,1);
  assert.equal(stats.p50Ms,300);
  assert.equal(stats.p95Ms,500);
  assert.equal(stats.meanMs,300);
  assert.equal(stats.missingUsage,5);
});

test('cost accounts for cache writes and never adds reasoning twice', () => {
  const r={Strategy:'C', Id:'x',Category:'care',ExpectedAllow:true,Status:'valid',Outcome:'Allow',Features:{flag:'nao'},Milliseconds:1,
    Usage:{input_tokens:1000,input_tokens_details:{cached_tokens:200,cache_write_tokens:300},
      output_tokens:100,output_tokens_details:{reasoning_tokens:80}}};
  const s=summarize([r]).C;
  assert.ok(Math.abs(s.usd - 0.00299)<1e-10);
  assert.equal(s.outputTokens,100);
  assert.equal(s.reasoningTokens,80);
});
