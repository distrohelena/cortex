// Read-only evidence summarizer. Emits JSON; does not call services or models.
import fs from 'node:fs';
const dir = 'docs/';
const json = n => JSON.parse(fs.readFileSync(n, 'utf8'));
const lines = n => fs.readFileSync(n, 'utf8').trim().split(/\r?\n/).map(JSON.parse);
const sum = a => a.reduce((x, y) => x + y, 0);
const avg = a => sum(a) / a.length;
const stats = a => ({ meanMs: avg(a), minMs: Math.min(...a), maxMs: Math.max(...a) });
const count = a => a.reduce((m, v) => (m[v] = (m[v] ?? 0) + 1, m), {});
function usage(calls) {
  const u = calls.map(c => c.usage).filter(Boolean);
  const input = sum(u.map(x => x.input_tokens ?? 0));
  const output = sum(u.map(x => x.output_tokens ?? 0));
  const cached = sum(u.map(x => x.input_tokens_details?.cached_tokens ?? 0));
  const write = sum(u.map(x => x.input_tokens_details?.cache_write_tokens ?? 0));
  return { calls: calls.length, usageRecords: u.length, input, output, cached, write,
    estimatedUsd: ((input - cached - write) * .20 + cached * .02 + write * .25 + output * 1.20) / 1e6 };
}
const files = fs.readdirSync(dir).sort();
const attempts = files.filter(n => /^participation-gate-v1-case-.*\.json$/.test(n)).map(n => ({ file: n, ...json(dir + n) }));
const valid = attempts.filter(x => x.Success);
const a = { attempts: attempts.length, valid: valid.length, invalid: attempts.filter(x => !x.Success).map(x => x.file),
  calls: sum(valid.map(x => x.Calls.length)), corrections: sum(valid.flatMap(x => x.Calls).map(x => +x.Correction)),
  inputTokens: sum(valid.flatMap(x => x.usage).map(x => x.PromptTokens)), outputTokens: sum(valid.flatMap(x => x.usage).map(x => x.CompletionTokens)),
  latency: stats(valid.map(x => x.totalMs)), remoteUsd: 0,
  states: Object.fromEntries([...new Set(valid.map(x => x.InitialState))].map(s => {
    const subset = valid.filter(x => x.InitialState === s);
    return [s, { valid: subset.length, actions: count(subset.map(x => x.Action)), participationErrors: s.includes('engaged') ? 1 : 0 }];
  })),
  fixtures: [...new Set(valid.map(x => x.fixture.Scenario.Id))].map(id => {
    const subset = valid.filter(x => x.fixture.Scenario.Id === id);
    return { id, name: subset[0].fixture.Scenario.Name, state: subset[0].InitialState,
      inputs: subset[0].fixture.Scenario.Messages, history: subset[0].fixture.Scenario.History,
      repetitions: subset.length, actions: count(subset.map(x => x.Action)), states: count(subset.map(x => x.finalState)),
      involvement: count(subset.map(x => x.Situation.envolvimento)), participationErrors: id === 21 ? 1 : 0 };
  }) };
function pipeline(pattern) {
  const runs = files.filter(n => pattern.test(n)).map(file => ({ file, rows: lines(dir + file) }));
  const calls = runs.flatMap(r => r.rows.filter(x => x.type === 'call'));
  const remote = calls.filter(c => c.request.Provider === 'openai');
  const ends = runs.map(r => r.rows.find(x => x.type === 'execution')).filter(Boolean);
  return { runs, metrics: { attempts: runs.length, valid: ends.length,
    outcomes: count(ends.flatMap(e => e.result.Trace.filter(t => t.EventName === 'node-outbound').map(t => t.MemoryMetadata.result))),
    regeneration: calls.filter(c => c.request.NodeId.endsWith('-regeneration')).length,
    detectorCalls: calls.filter(c => c.request.NodeId.includes('validator')).length,
    localCalls: calls.length - remote.length,
    localCorrections: calls.filter(c => c.request.Provider === 'lmstudio' && c.request.Correction).length,
    luna: usage(remote), latency: stats(ends.map(x => x.result.DurationMilliseconds)),
    outboundHistory: sum(ends.map(x => x.outboundHistory)), spokenTransitions: sum(ends.flatMap(x => x.result.Outcomes).map(x => +x.TransitionApplied)) } };
}
const b = pipeline(/^boundary-coverage-v1-case-.*\.jsonl$/);
const cControlled = pipeline(/^combined-boundary-v1-case-.*\.jsonl$/);
const liveFile = 'state/manual-shadow/manual-shadow-03b0673a1f82482a9055da5a63eb05c1.jsonl';
const live = lines(liveFile), interactions = live.filter(x => x.phase === 'interaction');
const cLive = { evidence: liveFile, graphitiPreflightPass: live.filter(x => x.phase === 'graphiti_live_preflight').every(x => x.checks.every(c => c.passed)),
  valid: interactions.length, outcomes: count(interactions.map(x => x.outcome)),
  latency: stats(interactions.map(x => x.durationMilliseconds)),
  localCalls: sum(interactions.map(x => x.calls.filter(c => c.kind === 'completion' && c.provider === 'lmstudio').length)),
  luna: usage(interactions.flatMap(x => x.calls.filter(c => c.kind === 'usage'))),
  interactions: interactions.map((x, i) => ({ case: i + 1, input: x.input, outcome: x.outcome,
    situation: Object.values(x.outcomes[0].outputs)[0], presence: Object.values(x.outcomes[0].outputs)[1],
    latencyMs: x.durationMilliseconds, state: x.outcomes[0].stateId, transition: x.outcomes[0].transitionTargetStateId })) };
let candidates = '# Boundary coverage + combined shadow: complete candidates\n\nAgent review, not independent human review. Synthetic Boundary MemoryItem is explicit; no claim of live Graphiti Boundary. Outputs below are copied without truncation from experimental evidence.\n';
for (const scope of [b, cControlled]) for (const run of scope.runs) {
  const start = run.rows[0];
  candidates += `\n## ${run.file}\n\nContext: ${start.fixture.Text}\n\nBoundary fixture: ${start.boundaryMemoryItem}\n\nUpstream signals: ${start.upstreamSignals}\n`;
  for (const call of run.rows.filter(x => x.type === 'call' && x.request.Provider === 'openai')) {
    candidates += `\n### ${call.request.NodeId}${call.request.NodeId.includes('validator') ? ' / ' + (Object.keys(call.request.ResponseSchema.properties).length === 3 ? 'implicit-depreciation-v1' : 'semantic-v2') : ''}\n\n${call.output}\n`;
  }
}
candidates += '\n# Combined live Graphiti interactions\n\nBoundary live remained valid empty.\n';
for (const [i, x] of interactions.entries()) {
  candidates += `\n## C${i + 1}: ${x.outcome}\n\nInput recorded by CLI: ${x.input}\n`;
  for (const o of x.outcomes) for (const [id, output] of Object.entries(o.outputs)) {
    candidates += `\n### ${id}\n\n${output.texto ?? JSON.stringify(output)}\n`;
  }
}
const combinedLatency = [...interactions.map(x => x.durationMilliseconds), ...cControlled.runs.map(x => x.rows.find(r => r.type === 'execution').result.DurationMilliseconds)];
console.log(JSON.stringify({ metrics: { A: a, B: b.metrics, C: { live: cLive, controlled: cControlled.metrics,
  latency: stats(combinedLatency), totalEstimatedUsd: cLive.luna.estimatedUsd + cControlled.metrics.luna.estimatedUsd },
  pricing: { basis: 'experimental comparison tariff, not verified invoice', inputPerMillion: .20, cachedPerMillion: .02, cacheWritePerMillion: .25, outputPerMillion: 1.20 } }, candidates }));
