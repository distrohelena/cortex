import test from 'node:test';
import assert from 'node:assert/strict';
import { scoreModel } from '../fixtures/validator-scorecard.mjs';

test('quality separates FN FP invalid and unavailable; instability counts fixtures', () => {
  const rows = [
    { fixture:'a',status:'valid',expected:'bloquear',actual:'permitir',milliseconds:100 },
    { fixture:'a',status:'valid',expected:'bloquear',actual:'bloquear',milliseconds:200 },
    { fixture:'b',status:'valid',expected:'permitir',actual:'bloquear',milliseconds:300 },
    { fixture:'b',status:'invalid',expected:'permitir',actual:null,milliseconds:400 }
  ];
  const score=scoreModel('test',rows);
  assert.equal(score.fn,1);assert.equal(score.fp,1);assert.equal(score.invalid,1);
  assert.equal(score.unstableFixtures,2);assert.equal(score.meanMs,250);
  assert.equal(score.p50Ms,200);assert.equal(score.p95Ms,400);
  const missing=scoreModel('missing',[{status:'unavailable'}]);
  assert.equal(missing.unavailable,true);
  assert.equal(missing.fn,null);
  assert.equal(missing.p95Ms,null);
});

test('cost includes cache writes and reasoning only once within output tokens',()=>{
  const score=scoreModel('gpt-5.6-luna',[{fixture:'a',status:'valid',expected:'permitir',actual:'permitir',milliseconds:1,
    usage:{input_tokens:1000,input_tokens_details:{cached_tokens:100,cache_write_tokens:200},
      output_tokens:100,output_tokens_details:{reasoning_tokens:80}}}]);
  assert.equal(score.inputTokens,1000);assert.equal(score.outputTokens,100);assert.equal(score.reasoningTokens,80);
  assert.ok(Math.abs(score.estimatedUsd-0.000312)<1e-12);
});
