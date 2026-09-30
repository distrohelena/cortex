import test from 'node:test';
import assert from 'node:assert/strict';
import { startMemoryMaintenance, selectMemoryBinding, createConversationMemory, getMemoryExtractionPolicy } from './conversation-memory.mjs';

test('maintenance uses the plugin policy verbatim and rejects an obsolete artifact',()=>{
  const policy='Social extraction policy: conversation-evidence:v2. Preserve questions.';
  assert.equal(getMemoryExtractionPolicy({SOCIAL_EXTRACTION_INSTRUCTIONS:policy}),policy);
  for(const value of [{},{SOCIAL_EXTRACTION_INSTRUCTIONS:''},{SOCIAL_EXTRACTION_INSTRUCTIONS:'old'}])
    assert.throws(()=>getMemoryExtractionPolicy(value),/memory_extraction_policy_unavailable/);
});

const host={accountId:'account-a',sourceGroupJid:'123@g.us'};
const binding={bindingRef:'approved',historyNotBefore:'2026-01-01T00:00:00Z',conversation:{channel:'whatsapp',kind:'group',accountId:'account-a',nativeId:'123@g.us'}};
test('host exact group scope excludes unrelated memory bindings',()=>{
  const config={version:'native-memory-host:v1',recovery:{intervalMs:30000},bindings:[binding,{...binding,bindingRef:'other',conversation:{...binding.conversation,nativeId:'456@g.us'}}]};
  assert.deepEqual(selectMemoryBinding(config,host).map(b=>b.bindingRef),['approved']);
  for(const changed of [{accountId:'other'}, {sourceGroupJid:'other'}, {sourceGroupJid:undefined}])
    assert.throws(()=>selectMemoryBinding(config,{...host,...changed}),/binding_missing_or_ambiguous/);
  assert.throws(()=>selectMemoryBinding({...config,bindings:[binding,binding]},host),/binding_missing_or_ambiguous/);
});
test('missing memory configuration cannot silently run without ingestion',async()=>{
  await assert.rejects(createConversationMemory({configFile:undefined,hostFile:'unused',report(){}}),/conversation_memory_required/);
});

test('memory is opened once, ticks without overlap and drains before closing', async () => {
  const events=[]; let finish; let tick; let cancelled=false;
  const recovery={runOnce(){events.push('pass');return new Promise(r=>{finish=r;});},
    status(){return {discovered:2,persisted:1,pending:1,coverage:'partial'};},
    async drain(){events.push('drain');finish();},close(){events.push('recovery-close');}};
  const memory=await startMemoryMaintenance({recovery,store:{close(){events.push('store-close');}},bindingRefs:['one'],
    intervalMs:30000,report:e=>events.push(e.status),schedule:fn=>{tick=fn;return 1;},cancel:()=>{cancelled=true;}});
  await tick(); // Schedule must not await remote processing in the conversation path.
  const stopping=memory.stop();
  await stopping;
  assert.equal(cancelled,true);
  assert.deepEqual(events.filter(e=>['pass','drain','recovery-close','store-close'].includes(e)),
    ['pass','drain','recovery-close','store-close']);
  await memory.stop();
  assert.equal(events.filter(e=>e==='store-close').length,1);
});

test('outage and pending work are reported as degraded, not healthy or learned', async () => {
  const reports=[]; const queue=[];
  const memory=await startMemoryMaintenance({recovery:{async runOnce(){},status(){return {pending:3,discovered:3,persisted:0,diagnostic:'ingestion_not_yet_confirmed'};},async drain(){},close(){}},
    store:{close(){}},bindingRefs:['one'],intervalMs:30000,report:e=>reports.push(e),schedule:fn=>{queue.push(fn);return queue.length;},cancel() {}});
  queue.shift()(); await new Promise(r=>setImmediate(r));
  assert.ok(reports.some(r=>r.status==='degraded'&&r.groups[0].pending===3));
  await memory.stop();
  assert.equal(reports.at(-1).status,'stopped');
});

test('failed memory pass reports sanitized failure and retains retry schedule', async () => {
  const reports=[];const queue=[];
  const memory=await startMemoryMaintenance({recovery:{async runOnce(){throw Error('private payload');},status(){return {pending:1};},async drain(){},close(){}},
    store:{close(){}},bindingRefs:['one'],intervalMs:30000,report:e=>reports.push(e),schedule:fn=>{queue.push(fn);return queue.length;},cancel(){}});
  queue.shift()(); await new Promise(r=>setImmediate(r));
  assert.ok(reports.some(r=>r.status==='degraded'&&r.diagnostic==='memory_maintenance_failed'));
  assert.equal(JSON.stringify(reports).includes('private payload'),false);
  assert.equal(queue.length,1);
  await memory.stop();
});
