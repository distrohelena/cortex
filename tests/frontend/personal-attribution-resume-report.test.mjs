import test from 'node:test';
import assert from 'node:assert/strict';
import { summarizeResumed } from '../fixtures/personal-attribution-resume-report.mjs';

test('technical attempts remain counted but do not affect semantic or latency statistics',()=>{
  const row={Strategy:'A',Id:'08',Category:'emotional_attribution',Repetition:1,ExpectedAllow:false,Status:'valid',Outcome:'Block',Features:{flag:'sim'},Milliseconds:100,Usage:null};
  const failure={...row,Status:'technical_error',Outcome:null,Features:null,Error:'http_',Milliseconds:15000};
  const report=summarizeResumed([failure,row,{...row,Repetition:2,Milliseconds:300}]).A;
  assert.equal(report.attempts,3);
  assert.equal(report.valid,2);
  assert.equal(report.transportFailures,1);
  assert.equal(report.technical,1);
  assert.equal(report.fn,0);
  assert.equal(report.meanMs,200);
  assert.deepEqual(report.failureDurationsMs,[15000]);
  assert.equal(report.unknownFailureUsage,1);
  assert.equal(failure.Status,'technical_error');
});
