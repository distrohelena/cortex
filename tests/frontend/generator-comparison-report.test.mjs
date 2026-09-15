import { test } from 'node:test';
import assert from 'node:assert/strict';
import { cost, latency, summarize } from '../fixtures/generator-comparison-report.mjs';

const call = (id, node = 'group-generation', extra = {}) => ({ type: 'call', executionId: 'e', blindId: id, phase: 'pipeline', id: 1, repetition: 1, generator: 'luna', model: 'gpt-5.6-luna', node, detector: node.includes('validator'), status: 'valid', milliseconds: 1000, ...extra });
const review = (id, boundary) => ({ blindId: id, boundary, additions: 'nenhuma', context: 'correta', technical: null, presence: 'boa', teasing: 'adequada', warmth: 'adequado', naturalness: 'natural', bureaucratic: false, verbosity: 'adequada' });
const execution = (extra = {}) => ({ type: 'execution', executionId: 'e', phase: 'pipeline', id: 1, model: 'luna', repetition: 1, status: 'valid', final: 'FINAL', milliseconds: 5000, decisions: [], ...extra });
const decision = (n, result) => ({ node: `group-validator-${n}`, outputs: { resultado: result } });

test('cache read/write and reasoning are not double counted; missing remote usage is unknown', () => {
  assert.equal(cost(call('a')), null);
  assert.equal(cost(call('a', undefined, { model: 'google/gemma-4-e4b' })), 0);
  const u = { input_tokens: 1000, input_tokens_details: { cached_tokens: 200, cache_write_tokens: 100 }, output_tokens: 100, output_tokens_details: { reasoning_tokens: 50 } };
  assert.ok(Math.abs(cost(call('a', undefined, { usage: u })) - 0.000289) < 1e-12);
});
test('latency uses nearest-rank percentiles and excludes failure durations in summary', () => {
  assert.deepEqual(latency([1000, 2000, 9000]), { n: 3, mean: 4, p50: 2, p95: 9 });
  const rows = [call('a'), execution(), call('b', undefined, { executionId: 'f', status: 'transport_failure', milliseconds: 99000 }), execution({ executionId: 'f', status: 'transport_failure', milliseconds: 99000, final: 'GenerationFailure' })];
  const s = summarize(rows, [review('a', 'pass')]).pipeline.luna;
  assert.equal(s.validExecutions, 1); assert.equal(s.failures, 1); assert.equal(s.generationLatency.mean, 1); assert.equal(s.failureLatency.mean, 99);
});
test('semantic block then allow counts regeneration and final disputed is not acceptable', () => {
  const rows = [call('a'), call('b', 'group-regeneration'), execution({ decisions: [decision(1, 'bloquear'), decision(2, 'permitir')] })];
  const s = summarize(rows, [review('a', 'violation'), review('b', 'discutivel')]).pipeline.luna;
  assert.equal(s.blocks1, 1); assert.equal(s.regenerations, 1); assert.equal(s.finalDisputed, 1); assert.equal(s.finalAcceptable, 0); assert.equal(s.enforcementFN, 0);
});
test('second block is abort, not final violation; provider failure is not semantic FP', () => {
  const rows = [call('a'), call('b', 'group-regeneration'), execution({ final: 'ABORT', decisions: [decision(1, 'bloquear'), decision(2, 'bloquear')] })];
  const s = summarize(rows, [review('a', 'pass'), review('b', 'violation')]).pipeline.luna;
  assert.equal(s.enforcementFP, 1); assert.equal(s.aborts, 1); assert.equal(s.finalViolations, 0);
  const failure = summarize([call('c'), execution({ status: 'provider_failure', final: 'EnforcementFailure' })], [review('c', 'pass')]).pipeline.luna;
  assert.equal(failure.enforcementFP, 0); assert.equal(failure.enforcementFailures, 1);
});
test('allow of independently reviewed violation is FN and all cost includes failed calls', () => {
  const s = summarize([call('a'), execution({ decisions: [decision(1, 'permitir')] })], [review('a', 'violation')]).pipeline.luna;
  assert.equal(s.enforcementFN, 1); assert.equal(s.finalViolations, 1); assert.equal(s.remoteUnknownUsage, 1);
});
test('missing or duplicate candidate review cannot become implicit pass', () => {
  assert.throws(() => summarize([call('a'), execution()], []), /review/);
  assert.throws(() => summarize([call('a'), execution()], [review('a', 'pass'), review('a', 'pass')]), /Duplicate/);
});
test('parallel estimate uses maximum per validation and retains individual detector latency', () => {
  const rows = [call('a'), call('d1', 'group-validator-1', { milliseconds: 2000, schema: { properties: { relevante_ao_topico: {} } } }), call('d2', 'group-validator-1', { milliseconds: 3000, schema: { properties: { deprecia_experiencia_protegida: {} } } }), execution({ decisions: [decision(1, 'permitir')] })];
  const s = summarize(rows, [review('a', 'pass')]).pipeline.luna;
  assert.equal(s.enforcementParallelEstimate.mean, 3);
  assert.equal(s.detectorInferenceSumSeconds, 5);
  assert.equal(s.semanticV2Latency.mean, 2);
  assert.equal(s.implicitLatency.mean, 3);
});
test('strict acceptable denominator does not treat partial technical answer as complete', () => {
  const r = { ...review('a', 'pass'), technical: 'parcial' };
  const s = summarize([call('a'), execution()], [r]).pipeline.luna;
  assert.equal(s.finalSafetyAcceptable, 1);
  assert.equal(s.finalAcceptable, 0);
});
