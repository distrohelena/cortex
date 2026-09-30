import test from 'node:test';
import assert from 'node:assert/strict';
import { supervise } from './native-conversation-runner.mjs';
import * as runner from './native-conversation-runner.mjs';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';

test('memory startup failure prevents conversational child launch', async () => {
  const root=fs.mkdtempSync(path.join(os.tmpdir(),'filomena-memory-fail-'));
  try{
    const appFile=path.join(root,'child.cjs');
    fs.writeFileSync(appFile,'console.log("{\\"status\\":\\"ABORT\\",\\"diagnostic\\":\\"must_not_launch\\"}");process.exitCode=2;');
    const result=runner.run({runDirectory:root,hostFile:path.join(root,'host.json'),appFile,dotnet:process.execPath,cwd:root,
      memoryFactory:async()=>{throw Error('memory_authority_invalid');}});
    await assert.rejects(result,/memory_authority_invalid/);
    assert.equal(fs.existsSync(path.join(root,'process.json')),false);
    assert.equal(fs.existsSync(path.join(root,'host.json.runner.lock')),false);
  }finally{fs.rmSync(root,{recursive:true});}
});

test('enrollment companion drains before supervisor terminal evidence and is not started after stop',async()=>{
  const root=fs.mkdtempSync(path.join(os.tmpdir(),'filomena-enrollment-'));
  try{
    const appFile=path.join(root,'child.cjs');fs.writeFileSync(appFile,'console.log(JSON.stringify({phase:"run_started"}));');
    let starts=0,stops=0;
    const options={runDirectory:root,hostFile:path.join(root,'host.json'),appFile,dotnet:process.execPath,cwd:root,
      enrollmentFactory:async({report})=>{starts++;report({status:'enabled'});return{async stop(){stops++;assert.equal(fs.existsSync(path.join(root,'exit.json')),false);}};}};
    assert.equal((await runner.run(options)).status,'completed');assert.equal(starts,1);assert.equal(stops,1);
    assert.equal(JSON.parse(fs.readFileSync(path.join(root,'member-dm-enrollment-status.json'))).status,'enabled');
    fs.writeFileSync(path.join(root,'stop.request'),'stop');await runner.run(options);assert.equal(starts,1);
  }finally{fs.rmSync(root,{recursive:true});}
});

test('memory starts before child, closes with supervisor and stop request never restarts it', async () => {
  const root=fs.mkdtempSync(path.join(os.tmpdir(),'filomena-memory-bound-'));
  try{
    const appFile=path.join(root,'child.cjs');
    fs.writeFileSync(appFile,'if(!require("fs").existsSync("memory.started")){console.log("{\\"status\\":\\"ABORT\\",\\"diagnostic\\":\\"memory_missing\\"}");process.exit(3);}console.log("{\\"phase\\":\\"run_started\\"}");');
    let starts=0,stops=0;
    const result=await runner.run({runDirectory:root,hostFile:path.join(root,'host.json'),appFile,dotnet:process.execPath,cwd:root,
      memoryFactory:async()=>{starts++;fs.writeFileSync(path.join(root,'memory.started'),'started');return {async stop(){assert.equal(fs.existsSync(path.join(root,'exit.json')),false,'terminal status must wait for memory drain');stops++;}};}});
    assert.equal(result.status,'completed');assert.equal(starts,1);assert.equal(stops,1);
    fs.writeFileSync(path.join(root,'stop.request'),'stop');
    await runner.run({runDirectory:root,hostFile:path.join(root,'host.json'),appFile,dotnet:process.execPath,cwd:root,
      memoryFactory:async()=>{starts++;throw Error('must_not_start');}});
    assert.equal(starts,1);
  }finally{fs.rmSync(root,{recursive:true});}
});

test('operational runner enforces singleton, records status and respects stop.request', async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'filomena-runner-cli-'));
  const hostFile = path.join(root, 'host.json'), appFile = path.join(root, 'fake.cjs');
  fs.writeFileSync(hostFile, '{}');
  fs.writeFileSync(appFile, 'console.log(JSON.stringify({phase:"run_started"}));process.stdin.once("data",()=>{console.log(JSON.stringify({phase:"run_closed"}));process.stdin.pause();});');
  const config = { runDirectory: root, hostFile, appFile, dotnet: process.execPath, cwd: root };
  let pending;
  try {
    pending = runner.run(config);
    await assert.rejects(runner.run(config), { code: 'EEXIST' });
    // Bounded test deadline, not an operational startup timeout.
    const deadline = Date.now() + 10000;
    while (!fs.existsSync(path.join(root, 'events.private.jsonl'))) {
      assert.ok(Date.now() < deadline, 'synthetic child did not start'); await delay(10);
    }
    fs.writeFileSync(path.join(root, 'stop.request'), 'operator stop');
    assert.equal((await pending).status, 'stopped');
    assert.equal(JSON.parse(fs.readFileSync(path.join(root, 'exit.json'))).stopRequested, true);
    assert.equal(fs.existsSync(hostFile + '.runner.lock'), false);
    assert.equal(fs.readFileSync(hostFile, 'utf8'), '{}');
    const events = fs.readFileSync(path.join(root, 'events.private.jsonl'), 'utf8');
    assert.equal(events.trim().split('\n').length, 2);
    assert.match(events, /run_closed/);
  } finally {
    fs.writeFileSync(path.join(root, 'stop.request'), 'cleanup');
    await pending;
    fs.rmSync(root, { recursive: true });
  }
});

test('existing stop.request prevents even the first launch', async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'filomena-runner-stopped-'));
  try {
    fs.writeFileSync(path.join(root, 'stop.request'), 'stop');
    const result = await runner.run({ runDirectory: root, hostFile: path.join(root, 'host.json'),
      appFile: path.join(root, 'must-not-execute.js'), dotnet: process.execPath, cwd: root });
    assert.equal(result.status, 'stopped');
    assert.equal(fs.existsSync(path.join(root, 'process.json')), false);
  } finally { fs.rmSync(root, { recursive: true }); }
});

test('real child crash/restart preserves cursor, history and uncertain-send evidence', async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'filomena-runner-'));
  try {
    for (const name of ['cursor', 'history', 'uncertain-outbox']) fs.writeFileSync(path.join(root, name), `original-${name}`);
    let calls = 0;
    const seen = [];
    const result = await supervise({ signal: new AbortController().signal, wait: async () => {},
      launch: signal => runner.runChild({ file: process.execPath, cwd: root, signal, onLine: line => seen.push(line),
        args: ['-e', ++calls === 1 ? 'process.exit(19)' :
          'console.log(JSON.stringify({status:"ABORT",diagnostic:"reconciliation_required"}));process.exitCode=2;'] }) });
    assert.equal(calls, 2);
    assert.equal(result.status, 'blocked');
    assert.equal(seen.length, 1);
    for (const name of ['cursor', 'history', 'uncertain-outbox'])
      assert.equal(fs.readFileSync(path.join(root, name), 'utf8'), `original-${name}`);
  } finally { fs.rmSync(root, { recursive: true }); }
});

test('manual stop drains a real child via quit instead of killing or restarting it', async () => {
  const stop = new AbortController(); const lines = [];
  const result = await runner.runChild({ file: process.execPath, cwd: process.cwd(), signal: stop.signal,
    args: ['-e', 'console.log("{\\"phase\\":\\"ready\\"}");process.stdin.once("data",x=>{console.log(JSON.stringify({command:x.toString().trim()}));process.exitCode=0;process.stdin.pause();});'],
    onLine: line => { lines.push(JSON.parse(line)); if (lines.length === 1) stop.abort(); } });
  assert.equal(result.code, 0);
  assert.equal(lines[1].command, '/quit');
});

test('malformed child stdout fails closed instead of being a silent crash', async () => {
  const result = await runner.runChild({ file: process.execPath, cwd: process.cwd(), signal: new AbortController().signal,
    args: ['-e', 'console.log("not json");process.exitCode=1;'], onLine: () => {} });
  assert.deepEqual(result.events, [{ malformed: true }]);
});

test('terminal worker failure drains a waiting host instead of leaving it running', async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'filomena-terminal-drain-'));
  let pending;
  try {
    const appFile = path.join(root, 'child.cjs');
    fs.writeFileSync(appFile, 'process.stdin.once("data",x=>{require("fs").writeFileSync("quit.txt",x.toString());console.log(JSON.stringify({phase:"run_closed"}));process.stdin.pause();process.exitCode=2;});console.log(JSON.stringify({status:"ABORT",diagnostic:"preflight_or_runtime_failed",reconciliationRequired:true}));');
    pending = runner.run({runDirectory:root,hostFile:path.join(root,'host.json'),appFile,dotnet:process.execPath,cwd:root});
    const deadline = Date.now() + 3000;
    while (!fs.existsSync(path.join(root, 'exit.json')) && Date.now() < deadline) await delay(10);
    assert.equal(fs.existsSync(path.join(root, 'quit.txt')), true, 'terminal ABORT must request graceful shutdown without operator stop');
    assert.equal(fs.readFileSync(path.join(root, 'quit.txt'), 'utf8'), '/quit\n');
    assert.equal((await pending).status, 'blocked');
    assert.equal(JSON.parse(fs.readFileSync(path.join(root,'process.json'))).attempt, 1);
    assert.equal(JSON.parse(fs.readFileSync(path.join(root,'supervisor-status.json'))).status, 'blocked');
  } finally {
    fs.writeFileSync(path.join(root,'stop.request'),'test cleanup');
    await pending;
    fs.rmSync(root,{recursive:true});
  }
});

test('unexpected process death restarts with capped backoff and the same launch contract', async () => {
  let calls = 0;
  const delays = [];
  const result = await supervise({ signal: new AbortController().signal,
    launch: async () => ++calls <= 8 ? { code: 17, events: [] } : { code: 0, events: [] },
    wait: async ms => { delays.push(ms); }, report: () => {} });
  assert.equal(calls, 9);
  assert.deepEqual(delays, [1000, 2000, 4000, 8000, 16000, 30000, 30000, 30000]);
  assert.equal(result.status, 'completed');
});

test('transient reader unavailable retries, but successful restart still performs child preflight', async () => {
  let calls = 0;
  const result = await supervise({ signal: new AbortController().signal, wait: async () => {}, report: () => {},
    launch: async () => ++calls === 1
      ? { code: 2, events: [{ status: 'ABORT', diagnostic: 'unavailable' }, { phase: 'host', status: 'failed', diagnostic: 'host_failed' }] }
      : { code: 2, events: [{ status: 'ABORT', diagnostic: 'preflight_stale' }] } });
  assert.equal(calls, 2);
  assert.equal(result.status, 'blocked');
});

for (const diagnostic of ['preflight_stale', 'invalid_host_configuration', 'preflight_or_runtime_failed',
  'capture_epoch_closed', 'invalid_output', 'reconciliation_required', 'native_live_cursor_regression']) {
  test(`safety failure ${diagnostic} is terminal, even alongside transient errors`, async () => {
    let calls = 0;
    const result = await supervise({ signal: new AbortController().signal, wait: async () => {}, report: () => {},
      launch: async () => { calls++; return { code: 2, events: [
        { status: 'ABORT', diagnostic }, { status: 'ABORT', diagnostic: 'unavailable' }] }; } });
    assert.equal(calls, 1);
    assert.equal(result.status, 'blocked');
  });
}

test('manual stop during backoff never launches another process', async () => {
  const control = new AbortController(); let calls = 0;
  const result = await supervise({ signal: control.signal, report: () => {},
    launch: async () => { calls++; return { code: 1, events: [] }; },
    wait: async () => { control.abort(); } });
  assert.equal(calls, 1);
  assert.equal(result.status, 'stopped');
});

test('manual stop during child and normal completion are never retried', async () => {
  for (const stop of [false, true]) {
    const control = new AbortController(); let calls = 0;
    const result = await supervise({ signal: control.signal, report: () => {},
      launch: async () => { calls++; if (stop) control.abort(); return { code: stop ? 2 : 0, events: [] }; } });
    assert.equal(calls, 1);
    assert.equal(result.status, stop ? 'stopped' : 'completed');
  }
});

test('malformed output or unknown failure cannot become a recovery authorization', async () => {
  for (const events of [[{ status: 'ABORT' }], [{ malformed: true }], [{ phase: 'host', status: 'failed', diagnostic: 'host_failed' }]]) {
    const result = await supervise({ signal: new AbortController().signal, report: () => {},
      launch: async () => ({ code: 2, events }) });
    assert.equal(result.status, 'blocked');
  }
});
