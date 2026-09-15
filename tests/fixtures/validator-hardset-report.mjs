// Synthetic benchmark artifact generator; never reads credentials or application data.
import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import assert from 'node:assert/strict';
import { scoreModel } from './validator-scorecard.mjs';

const suite = JSON.parse(readFileSync(new URL('./validator-hardset.json', import.meta.url), 'utf8'));
const rows = readFileSync(process.argv[2], 'utf8').trim().split(/\r?\n/).map(JSON.parse);
const models = ['gpt-5.6-luna', 'gpt-5.6-terra'];
assert.equal(rows.length, 250, 'Incomplete matrix: do not publish final scorecard');
assert.equal(new Set(rows.map(r => [r.model,r.fixture,r.repetition].join('/'))).size, 250);
for (const model of models) {
  for (const item of suite.cases) {
    const results = rows.filter(r => r.model === model && r.fixture === item.id);
    assert.deepEqual(results.map(r => r.repetition).sort(), [1,2,3,4,5]);
    assert.ok(results.every(r => r.expected === item.expected && r.candidate === item.candidate));
  }
}
const scores = models.map(model => scoreModel(model, rows.filter(r => r.model === model)));
const errors = rows.filter(r => r.status !== 'valid' || r.actual !== r.expected);
const artifact = {
  protocol: { repetitions:5, effort:'medium', maxOutputTokens:1024, retries:0,
    temperature:'omitted for both', endpoint:'https://api.openai.com/v1/responses',
    syntheticOnly:true, productionPromptUnchanged:true,
    boundarySemantics:'Expanded in shared synthetic boundary; not model-specific tuning',
    instability:'Cases with different decisions/status across five repetitions',
    cost:'USD estimate from reported tokens and validator-scorecard prices; not an invoice' },
  suite, scores, errors, rows
};
const target = new URL('../../docs/validator-hardset-results.json', import.meta.url);
writeFileSync(target, JSON.stringify(artifact,null,2)+'\n');
console.log(JSON.stringify({ path:fileURLToPath(target),scores,errors },null,2));
