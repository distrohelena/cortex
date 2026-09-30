import fs from 'node:fs';
import path from 'node:path';

function matches(status, binding) {
  return typeof binding?.accountId === 'string' && binding.accountId.trim().length > 0
    && typeof binding.sessionRef === 'string' && binding.sessionRef.trim().length > 0
    && /^\d+(?:-\d+)?@g\.us$/.test(binding.groupJid ?? '')
    && status?.version === 'inbound-observation:v2' && status.channel === 'whatsapp'
    && status.kind === 'group' && status.sessionRef === binding.sessionRef
    && status.memberDmDiscovery?.version === 'native-group-member-dm-discovery:v1'
    && status.memberDmDiscovery.source?.accountId === binding.accountId
    && status.memberDmDiscovery.source?.groupJid === binding.groupJid;
}
function ready(status, binding) {
  const proof = status?.bootstrap?.effectiveSnapshotProof;
  const gate = status?.bootstrap?.isolationGate;
  return matches(status, binding) && status.bootstrap.subsystemOperational === true
    && proof?.status === 'valid' && proof.version === 'effective-runtime-config:v1'
    && typeof proof.revision === 'string' && proof.revision.trim().length > 0
    && gate?.gateVersion === 'native-group-isolation:v1' && gate.lifecycleState === 'armed'
    && gate.approvedEffectiveConfigRevision === proof.revision
    && status.dispatch?.status === 'inactive' && status.captureState === 'armed'
    && status.journalHealth === 'healthy';
}

// Restores only the permission to attempt membership admission. Each actual DM
// still obtains its own current membership lease, isolation and capture proofs.
// No cached snapshot, lease, child scope, epoch or cursor is restored here.
export async function reconcileEnrollment({ binding, enabled, readStatus, setEnabled }) {
  const before = await readStatus();
  const wanted = enabled === true ? 'enabled' : enabled === false ? 'disabled' : null;
  if (!wanted || !matches(before, binding) || (enabled && !ready(before, binding)))
    return { status:'not_ready', diagnostic:'current_group_proof_unavailable' };
  if (!['enabled','disabled'].includes(before.memberDmDiscovery.enrollment))
    return { status:'not_ready', diagnostic:'invalid_enrollment_state' };
  if (before.memberDmDiscovery.enrollment === wanted) return { status:wanted, changed:false };
  await setEnabled(enabled);
  const after = await readStatus();
  if (!matches(after, binding) || (enabled && (!ready(after, binding)
    || after.bootstrap.effectiveSnapshotProof.revision !== before.bootstrap.effectiveSnapshotProof.revision))
    || after.memberDmDiscovery.enrollment !== wanted)
    return { status:'not_ready', diagnostic:'enrollment_not_confirmed' };
  return { status:wanted, changed:true };
}

// Explicit host-owned policy lives beside host configuration, never in project JSON.
// Missing policy means no automation. Reloading on each pass permits revocation.
export async function createMemberDmEnrollment({ hostFile, report = () => {}, intervalMs = 5000 }) {
  const policyFile = hostFile + '.member-dms.json';
  if (!fs.existsSync(policyFile)) return undefined;
  const controller = new AbortController();
  let pending;
  const read = file => JSON.parse(fs.readFileSync(file, 'utf8'));
  const tick = () => pending ??= (async () => {
    try {
      if (controller.signal.aborted) return;
      const policy = read(policyFile);
      const host = read(hostFile);
      if (policy.version !== 'member-dm-enrollment-policy:v1' || typeof policy.enabled !== 'boolean'
        || !path.isAbsolute(policy.observationConfigFile ?? '') || !path.isAbsolute(policy.nativeConfigFile ?? '')
        || policy.accountId !== host.accountId || policy.groupJid !== host.sourceGroupJid)
        throw new Error('invalid_host_policy');
      const config = read(policy.observationConfigFile);
      const native = read(policy.nativeConfigFile);
      if (config.accountId !== policy.accountId || config.nativeSession !== policy.groupJid
        || native.nativeConversationId !== policy.groupJid
        || native.observationSessionRef !== policy.sessionRef
        || !Number.isInteger(config.port) || config.port < 1 || config.port > 65535
        || !config.readerToken || !config.controlToken) throw new Error('binding_mismatch');
      const base = `http://127.0.0.1:${config.port}/inbound-observation/v2`;
      const suffix = '?session=' + encodeURIComponent(policy.sessionRef);
      const request = async (endpoint, write) => {
        const result = await fetch(base + endpoint + suffix, {
          method:write ? 'POST' : 'GET', redirect:'error',
          signal:AbortSignal.any([controller.signal, AbortSignal.timeout(5000)]),
          headers:{Authorization:'Bearer ' + (write ? config.controlToken : config.readerToken),
            ...(write ? {'Content-Type':'application/json'} : {})},
          ...(write ? {body:JSON.stringify({version:'native-group-member-dm-control:v1',enabled:policy.enabled})} : {})
        });
        if (!result.ok) throw new Error('control_unavailable');
        const body = await result.json();
        if (write && (body.version !== 'native-group-member-dm-control:v1' || body.result?.enabled !== policy.enabled))
          throw new Error('control_unconfirmed');
        return body;
      };
      report(await reconcileEnrollment({binding:policy,enabled:policy.enabled,
        readStatus:()=>request('/status',false),setEnabled:()=>request('/control/member-dm/enrollment',true)}));
    } catch {
      // Never include response bodies, paths, identities or inherited credentials.
      if (!controller.signal.aborted) report({status:'not_ready',diagnostic:'enrollment_reconciliation_unavailable'});
    }
  })().finally(()=>{pending=undefined;});
  await tick();
  const timer = setInterval(tick, intervalMs);
  return { async stop() { clearInterval(timer); controller.abort(); await pending; } };
}
