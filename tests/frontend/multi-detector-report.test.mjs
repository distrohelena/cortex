import test from 'node:test';
import assert from 'node:assert/strict';
import { summarize } from '../fixtures/multi-detector-report.mjs';

test('technical failures are neither semantic FP nor instability', () => {
  const rows = ['Allow', 'EnforcementFailure', 'Block'].map(outcome => ({
    phase: 'corpus', Id: 'x', outcome, expectedAllow: true,
    result: { WallMilliseconds: 100, Calls: [{ModelMilliseconds: 90}, {ModelMilliseconds: 80}] }, usage: [null, null]
  }));
  const s = summarize(rows);
  assert.equal(s.fn, 0); assert.equal(s.fp, 1); assert.equal(s.aborts, 1);
  assert.equal(s.instabilityCases, 1); assert.equal(s.meanWallMs, 100); assert.equal(s.meanSumMs, 170);
  assert.equal(s.missingUsage, 6);
  assert.equal(summarize(rows.slice(0, 2)).instabilityCases, 0);
});

test('reasoning is included in output cost, not billed twice', () => {
  const s = summarize([{phase:'a', Id:'x', outcome:'Allow', expectedAllow:false,
    result:{WallMilliseconds:1, Calls:[{ModelMilliseconds:1}]},
    usage:[{input_tokens:100, output_tokens:10, input_tokens_details:{cached_tokens:40,cache_write_tokens:20}, output_tokens_details:{reasoning_tokens:7}}]}]);
  assert.equal(s.fn, 1); assert.equal(s.reasoningTokens, 7);
  assert.ok(Math.abs(s.knownUsd - 0.0000258) < 1e-10);
});
