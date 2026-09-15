import test from 'node:test';
import assert from 'node:assert/strict';
import {summarize} from '../fixtures/detector-ab-report.mjs';

test('A/B metrics distinguish wall time, summed requests, instability and cost',()=>{
 const call={Milliseconds:100,Invalid:false,Ambiguous:false,Usage:{input_tokens:100,output_tokens:10,input_tokens_details:{cached_tokens:40,cache_write_tokens:20}}};
 const rows=[{Phase:'initial',Strategy:'B',Id:'x',Allow:true,FN:true,FP:false,WallMilliseconds:110,SumMilliseconds:200,Calls:[call,call]},
 {Phase:'initial',Strategy:'B',Id:'x',Allow:false,FN:false,FP:false,WallMilliseconds:120,SumMilliseconds:210,Calls:[call,call]}];
 const [s]=summarize(rows);
 assert.equal(s.fn,1);assert.equal(s.fp,0);assert.equal(s.instabilityCases,1);assert.equal(s.calls,4);
 assert.equal(s.meanWallMs,115);assert.equal(s.meanSumMs,205);assert.equal(s.p95WallMs,120);
 assert.ok(Math.abs(s.usd-0.0001032)<1e-10);
});

test('provider failure is not misreported as invalid model output',()=>{
 const [s]=summarize([{Phase:'initial',Strategy:'B',Id:'x',Allow:false,FN:false,FP:true,
  WallMilliseconds:10,SumMilliseconds:15,Calls:[{Invalid:true,Diagnostic:'provider_failure'},
   {Invalid:true,Diagnostic:'invalid_output'}]}]);
 assert.equal(s.invalidCalls,2);assert.equal(s.providerFailures,1);assert.equal(s.invalidOutputs,1);
});
