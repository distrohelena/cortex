// Offline aggregation of explicitly named private development evidence. No providers.
import { readFileSync, writeFileSync } from 'node:fs';
import { basename } from 'node:path';
import { createHash } from 'node:crypto';

const [destination, ...files] = process.argv.slice(2);
if (!destination || files.length !== 3) throw Error('Expected destination, control and two candidate evidence files');
const runs = files.map(file => {
  const bytes = readFileSync(file);
  const rows = bytes.toString('utf8').trim().split('\n').map(line => JSON.parse(line));
  if (rows.length !== 10 || rows.some(r => r.provenance !== 'controlled_reconstruction_not_native_replay'
    || r.generationCalls !== 0 || r.transportCalls !== 0 || r.graphitiCalls !== 0)) throw Error('Unexpected evidence scope');
  const calls = rows.flatMap(r => r.serviceDiagnostics);
  const jevUsage = calls.filter(c => c.provider === 'jev').map(c => c.privateDecision ? JSON.parse(c.privateDecision).usage : null);
  const allUsage = jevUsage.every(u => Number.isSafeInteger(u?.input_tokens) && Number.isSafeInteger(u?.output_tokens));
  return {
    evidence: basename(file), evidenceSha256: createHash('sha256').update(bytes).digest('hex'),
    variant: rows[0].variant, revision: rows[0].revision, cases: rows.length,
    rubricPass: rows.filter(r => r.pass).length, careMismatch: rows.filter(r => !r.careMatches).length,
    runtimeFailures: rows.filter(r => r.Success !== true).length,
    callCount: calls.length, failedHttpCalls: calls.filter(c => c.status !== 200).length,
    meanSituationMs: rows.reduce((n, r) => n + r.SituationMilliseconds, 0) / rows.length,
    meanPresenceMs: rows.reduce((n, r) => n + (r.PresenceMilliseconds ?? 0), 0) / rows.length,
    jevInputTokens: allUsage ? jevUsage.reduce((n, u) => n + u.input_tokens, 0) : null,
    jevOutputTokens: allUsage ? jevUsage.reduce((n, u) => n + u.output_tokens, 0) : null,
    localTokens: null, cost: null, costStatus: 'not_fully_measured_not_zero',
    semanticAcceptance: 'separate_manual_review_not_implied_by_rubric'
  };
});
if (runs[0].variant !== 'context-control' || runs.slice(1).some(r => r.variant !== 'context-consolidated')) throw Error('Wrong comparison arms');
writeFileSync(destination, JSON.stringify({ version: 1, date: '2026-09-28',
  scope: 'development_corpus_not_blind_holdout_or_production_quality_claim', runs,
  generationCalls: 0, transportCalls: 0, graphitiCalls: 0 }, null, 2) + '\n', { flag: 'wx' });
console.log(JSON.stringify(runs));
