import { setTimeout as delay } from 'node:timers/promises';
import { spawn } from 'node:child_process';
import { createInterface } from 'node:readline';
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

// Process lifecycle only. Every child runs the normal host/preflight/journal guards.
// Never refresh config, arm capture, repair journals, or replay a send here.
export async function supervise({ launch, signal, wait = (ms, token) => delay(ms, null, { signal: token }), report = () => {} }) {
  let failures = 0;
  while (!signal.aborted) {
    const result = await launch(signal);
    if (signal.aborted) break;
    const events = result.events ?? [];
    const failuresReported = events.filter(event => event.malformed || ['ABORT', 'FAIL', 'failed'].includes(event.status));
    const transient = failuresReported.some(event => event.status === 'ABORT' && event.diagnostic === 'unavailable');
    const unsafe = failuresReported.some(event => !(event.status === 'ABORT' && event.diagnostic === 'unavailable')
      && !(transient && event.phase === 'host' && event.status === 'failed' && event.diagnostic === 'host_failed'));
    if (unsafe) return { status: 'blocked', diagnostic: 'worker_requires_review' };
    if (result.code === 0) return { status: 'completed' };
    // Abrupt death has no terminal report. Starting a new host only attempts normal
    // preflight; inbox/outbox recovery still quarantines unfinished/uncertain work.
    const milliseconds = Math.min(30000, 1000 * 2 ** Math.min(failures++, 5));
    report({ status: 'reconnecting', attempt: failures, delayMilliseconds: milliseconds });
    try { await wait(milliseconds, signal); }
    catch (error) { if (!signal.aborted) throw error; }
  }
  return { status: 'stopped' };
}

export async function runChild({ file, args, cwd, signal, onLine = () => {}, onStart = () => {}, onErrorText = () => {} }) {
  if (signal.aborted) return { code: 0, events: [] };
  const child = spawn(file, args, { cwd, windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] });
  const events = [];
  const completed = new Promise((resolve, reject) => {
    child.once('error', reject);
    child.once('close', (code, exitSignal) => resolve({ code, exitSignal, events }));
  });
  let quitSent = false;
  const stop = () => {
    if (quitSent || child.exitCode !== null || child.signalCode !== null) return;
    quitSent = true;
    child.stdin.end('/quit\n');
  };
  child.stdin.on('error', () => { /* Exiting child may already have closed stdin. Await close, never kill/retry a send. */ });
  child.stderr.on('data', onErrorText);
  const lines = createInterface({ input: child.stdout });
  lines.on('line', line => {
    let terminal = false;
    try {
      const event = JSON.parse(line);
      if (['ABORT', 'FAIL', 'failed'].includes(event?.status)) { events.push(event); terminal = true; }
      // A process dying after an explicit runtime failure must not look like an
      // unreported crash just because its final ABORT line never made it to stdout.
      if (event?.outcomes?.some(outcome => outcome.success === false)
        || event?.decisions?.some(decision => decision.status === 'reconciliation_required'))
        { events.push({ status: 'ABORT', diagnostic: 'runtime_reconciliation_required' }); terminal = true; }
    } catch { events.push({ malformed: true }); terminal = true; }
    onLine(line);
    // A host can retain its stdin loop after its conversation worker has failed.
    // Drain through the existing quit contract; do not kill, retry or replay work.
    if (terminal) stop();
  });
  signal.addEventListener('abort', stop, { once: true });
  if (signal.aborted) stop();
  try {
    if (child.pid) onStart(child.pid);
    return await completed;
  } finally {
    signal.removeEventListener('abort', stop);
    lines.close();
  }
}

// Host-owned operational entrypoint. Optional exact-group enrollment companion;
// never arms capture, changes project configuration or bypasses child preflight.
export async function run({ runDirectory, hostFile, appFile, dotnet, cwd, memoryFactory, enrollmentFactory }) {
  for (const value of [runDirectory, hostFile, appFile, dotnet, cwd])
    if (!value || !path.isAbsolute(value)) throw new Error('absolute_runner_paths_required');
  const lockPath = hostFile + '.runner.lock';
  const lock = fs.openSync(lockPath, 'wx');
  const stop = new AbortController();
  const stopPath = path.join(runDirectory, 'stop.request');
  let timer;
  let memory;
  let enrollment;
  let enrollmentDrain;
  const drainEnrollment=()=>enrollmentDrain??=Promise.resolve().then(()=>enrollment?.stop());
  let memoryDrain;
  const drainMemory=()=>memoryDrain??=Promise.resolve().then(()=>memory?.stop());
  const abort = () => stop.abort();
  const stopEnrollment=()=>{ if(enrollment) void drainEnrollment(); };
  stop.signal.addEventListener('abort',stopEnrollment);
  const statusFile = path.join(runDirectory, 'supervisor-status.json');
  const report = state => {
    const data = { ...state, updatedAt: new Date().toISOString(), supervisorPid: process.pid };
    const temporary = statusFile + '.tmp';
    fs.writeFileSync(temporary, JSON.stringify(data));
    fs.renameSync(temporary, statusFile);
  };
  try {
    fs.writeSync(lock, JSON.stringify({ pid: process.pid, startedAt: new Date().toISOString() }));
    if (fs.existsSync(stopPath)) stop.abort();
    timer = setInterval(() => { if (fs.existsSync(stopPath)) stop.abort(); }, 250);
    process.on('SIGINT', abort); process.on('SIGTERM', abort);
    if (!stop.signal.aborted && enrollmentFactory) enrollment = await enrollmentFactory({hostFile,report:state=>{
      const destination=path.join(runDirectory,'member-dm-enrollment-status.json');
      fs.writeFileSync(destination+'.tmp',JSON.stringify({...state,updatedAt:new Date().toISOString()}));
      fs.renameSync(destination+'.tmp',destination);
    }});
    if(stop.signal.aborted) await drainEnrollment();
    if (!stop.signal.aborted && memoryFactory) memory = await memoryFactory({hostFile,report:state=>{
      const destination=path.join(runDirectory,'memory-status.json');
      fs.writeFileSync(destination+'.tmp',JSON.stringify({...state,updatedAt:new Date().toISOString()}));
      fs.renameSync(destination+'.tmp',destination);
    }});
    let attempt = 0;
    const result = await supervise({ signal: stop.signal, report,
      launch: signal => {
        attempt++;
        let workerFailed = false;
        report({ status: 'starting', attempt });
        return runChild({ file: dotnet, args: [appFile, '--native-conversation-host', hostFile], cwd, signal,
          onStart: workerPid => fs.writeFileSync(path.join(runDirectory, 'process.json'), JSON.stringify({
            runnerPid: process.pid, workerPid, attempt, startedAt: new Date().toISOString() })),
          onErrorText: text => fs.appendFileSync(path.join(runDirectory, 'stderr.private.log'), text),
          onLine: line => {
            fs.appendFileSync(path.join(runDirectory, 'events.private.jsonl'), line + '\n');
            try {
              const event = JSON.parse(line);
              workerFailed ||= ['ABORT', 'FAIL', 'failed'].includes(event?.status)
                || event?.outcomes?.some(outcome => outcome.success === false)
                || event?.decisions?.some(decision => decision.status === 'reconciliation_required');
              report({ status: workerFailed ? 'blocked' : event.phase === 'transport_wait' ? 'waiting_transport' : 'running', attempt,
                phase: event.phase, diagnostic: event.diagnostic, sequence: event.cursor?.sequence });
            } catch { workerFailed = true; report({ status: 'invalid_worker_output', attempt }); }
          } });
      } });
    await drainEnrollment();
    if(memory){report({status:'draining_memory'});await drainMemory();}
    report(result);
    fs.writeFileSync(path.join(runDirectory, 'exit.json'), JSON.stringify({ ...result,
      stopRequested: stop.signal.aborted, endedAt: new Date().toISOString() }));
    return result;
  } finally {
    clearInterval(timer);
    process.off('SIGINT', abort); process.off('SIGTERM', abort);
    stop.signal.removeEventListener('abort',stopEnrollment);
    try { await drainEnrollment(); await drainMemory(); }
    finally { fs.closeSync(lock); fs.unlinkSync(lockPath); }
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  const [runDirectory, hostFile, appFile, dotnet, cwd, ...extra] = process.argv.slice(2);
  try {
    if (extra.length) throw new Error('invalid_runner_arguments');
    const {createConversationMemory}=await import('./conversation-memory.mjs');
    const {createMemberDmEnrollment}=await import('./member-dm-enrollment.mjs');
    const result = await run({ runDirectory, hostFile, appFile, dotnet, cwd,
      enrollmentFactory:createMemberDmEnrollment,
      memoryFactory:options=>createConversationMemory({...options,configFile:process.env.FILOMENA_CONVERSATION_MEMORY_FILE}) });
    process.exitCode = result.status === 'blocked' ? 2 : 0;
  } catch {
    // Detailed child evidence is private. Never print inherited environment/secrets.
    console.error('conversation_supervisor_failed; inspect private evidence');
    process.exitCode = 2;
  }
}
