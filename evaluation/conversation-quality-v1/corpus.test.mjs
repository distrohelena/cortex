import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const corpus = JSON.parse(readFileSync(new URL('./cases.json', import.meta.url), 'utf8'));

test('inbound quality cases have a human current batch, not a self event bypassing runtime suppression', () => {
  for (const entry of corpus.cases) {
    assert.ok(entry.context.currentBatch.length > 0, entry.id);
    for (const message of entry.context.currentBatch)
      assert.notEqual(message.sender.ref, 'filomena', entry.id);
  }
});

test('corpus case identities are unique and synthetic provenance is explicit', () => {
  const ids = new Set();
  for (const entry of corpus.cases) {
    assert.ok(!ids.has(entry.id), entry.id);
    ids.add(entry.id);
    assert.equal(entry.provenance.kind, 'synthetic');
    assert.equal(entry.provenance.historicalReplay, false);
    assert.ok(entry.expected.constraints.length > 0, entry.id);
  }
});
