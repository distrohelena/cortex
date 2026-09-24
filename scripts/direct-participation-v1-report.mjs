// Read-only report builder. No model, transport or memory calls.
import fs from 'node:fs';
const read = f => JSON.parse(fs.readFileSync(f, 'utf8'));
const sum = xs => xs.reduce((a, b) => a + b, 0);
const mean = xs => xs.length ? sum(xs) / xs.length : null;
const counts = xs => xs.reduce((o, x) => (o[x] = (o[x] ?? 0) + 1, o), {});
const a = fs.readdirSync('docs').filter(n => /^direct-participation-v1-case-.*\.json$/.test(n)).sort().map(n => ({ file: n, ...read('docs/' + n) }));
const valid = a.filter(x => x.Success);
const cases = [...new Set(valid.map(x => x.fixture.Scenario.Id))].map(id => {
  const rs = valid.filter(x => x.fixture.Scenario.Id === id);
  return { id, fixture: rs[0].fixture, valid: rs.length, actions: counts(rs.map(x => x.action)), involvement: counts(rs.map(x => x.Situation.envolvimento)),
    finalStates: counts(rs.map(x => x.CurrentState)), direct: rs.filter(x => x.direct).length,
    presenceCalls: sum(rs.map(x => x.Calls.filter(c => c.NodeId.endsWith('-presence')).length)), meanMs: mean(rs.map(x => x.elapsedMs)), passed: rs.every(x => x.passed) };
});
const comparison = [[2, 9], [3, 17]].map(([current, old]) => {
  const before = [1, 2, 3, 4, 5].map(r => read(`docs/participation-gate-v1-case-${String(old).padStart(2, '0')}-r${r}.json`));
  const after = valid.filter(x => x.fixture.Scenario.Id === current);
  const beforeMs = mean(before.map(x => sum(x.Calls.map(c => c.Milliseconds))));
  const afterMs = mean(after.map(x => sum(x.Calls.map(c => c.Milliseconds))));
  return { current, old, beforeModelMs: beforeMs, afterModelMs: afterMs, savedMs: beforeMs - afterMs, savedPercent: 100 * (beforeMs - afterMs) / beforeMs,
    beforePresenceMs: mean(before.map(x => sum(x.Calls.filter(c => c.NodeId.endsWith('-presence')).map(c => c.Milliseconds)))), note: 'same fixtures/config; historical samples, not randomized paired timing' };
});
const result = { projectRevision: '47D1D4521BCF013E919DDCB3DE5900485DDA7F8BACEFA7437FEDFBDFFC9BC55E', attempts: a.length, valid: valid.length,
  allPassed: a.length === 35 && a.every(x => x.passed), cases, comparison,
  calls: sum(valid.map(x => x.Calls.length)), tokens: { input: sum(valid.flatMap(x => x.usage).map(x => x.PromptTokens)), output: sum(valid.flatMap(x => x.usage).map(x => x.CompletionTokens)) },
  meanMs: mean(valid.map(x => x.elapsedMs)), directRequiredMeanMs: mean(valid.filter(x => x.fixture.RequireDirect).map(x => x.elapsedMs)), remoteUsd: 0 };
let audit = '# Direct participation V1 — audit\n\nControlled participation evidence; no generation in this section. Derived decisions below are runtime switch trace interpretations, not invented LLM outputs.\n';
for (const x of valid) audit += `\n## ${x.file}\n\n${x.fixture.Scenario.Messages.map(m => `${m.Speaker}: ${m.Text}`).join('\n\n')}\n\nSituation: ${JSON.stringify(x.Situation)}\n\nPresence: ${JSON.stringify(x.Presence)}\n\nDirect decision: ${JSON.stringify(x.decision)}\n\nAction: ${x.action}; state: ${x.CurrentState}; calls: ${x.Calls.length}; ${x.elapsedMs.toFixed(2)} ms.\n`;
if (process.argv[2]) {
  const rows = fs.readFileSync(process.argv[2], 'utf8').trim().split(/\r?\n/).map(JSON.parse);
  const interactions = rows.filter(x => x.phase === 'interaction');
  const completions = interactions.flatMap(x => x.calls.filter(c => c.kind === 'completion'));
  const us = interactions.flatMap(x => x.calls.filter(c => c.kind === 'usage').map(c => c.usage));
  const input = sum(us.map(u => u.input_tokens)), output = sum(us.map(u => u.output_tokens));
  const cached = sum(us.map(u => u.input_tokens_details?.cached_tokens ?? 0)), write = sum(us.map(u => u.input_tokens_details?.cache_write_tokens ?? 0));
  result.shadow = { file: process.argv[2], preflightPassed: rows.filter(x => x.phase === 'graphiti_live_preflight').every(x => x.checks.every(c => c.passed)),
    count: interactions.length, outcomes: counts(interactions.map(x => x.outcome)), meanMs: mean(interactions.map(x => x.durationMilliseconds)),
    minMs: Math.min(...interactions.map(x => x.durationMilliseconds)), maxMs: Math.max(...interactions.map(x => x.durationMilliseconds)),
    directRoutes: sum(interactions.map(x => x.trace.filter(t => t.eventName === 'node-completed' && t.exit === 'direct_involvement_rule').length)),
    localCalls: completions.filter(c => c.provider === 'lmstudio').length, lunaCalls: us.length,
    generations: completions.filter(c => c.nodeId.endsWith('-generation')).length, regenerations: completions.filter(c => c.nodeId.endsWith('-regeneration')).length,
    detectorCalls: completions.filter(c => c.nodeId.includes('validator')).length,
    usage: { input, output, cached, write }, estimatedUsd: ((input - cached - write) * .20 + cached * .02 + write * .25 + output * 1.20) / 1e6,
    pricingBasis: 'experimental comparison tariff; not verified invoice',
    interactions: interactions.map((x, i) => ({ id: i + 1, input: x.input, outcome: x.outcome, durationMs: x.durationMilliseconds, outputs: x.outcomes?.map(o => o.outputs),
      decisions: x.trace.filter(t => t.eventName === 'node-completed' && (t.exit === 'direct_involvement_rule' || t.nodeId.endsWith('-action'))),
      state: x.outcomes?.[0]?.stateId, transition: x.outcomes?.[0]?.transitionTargetStateId })) };
  audit += '\n# Manual live shadow: complete outputs\n\nGraphiti LIVE, Boundaries reported as actually recalled; zero transport.\n';
  for (const x of result.shadow.interactions) {
    audit += `\n## Interaction ${x.id}: ${x.outcome}\n\nInput: ${x.input}\n\nState: ${x.state}; transition: ${x.transition}; ${x.durationMs.toFixed(2)} ms.\n\nRouting: ${JSON.stringify(x.decisions)}\n`;
    for (const outputs of x.outputs ?? []) for (const [id, o] of Object.entries(outputs)) audit += `\n### ${id}\n\n${o.texto ?? JSON.stringify(o)}\n`;
  }
}
console.log(JSON.stringify({ metrics: result, audit }));
