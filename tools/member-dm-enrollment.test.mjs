import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import http from 'node:http';
import { once } from 'node:events';
import { reconcileEnrollment, createMemberDmEnrollment } from './member-dm-enrollment.mjs';

const binding = { accountId:'account', groupJid:'123@g.us', sessionRef:'group-ref' };
function ready(revision='R1') { return {
  version:'inbound-observation:v2',channel:'whatsapp',kind:'group',sessionRef:'group-ref',
  captureState:'armed',journalHealth:'healthy',dispatch:{status:'inactive'},
  bootstrap:{subsystemOperational:true,effectiveSnapshotProof:{status:'valid',version:'effective-runtime-config:v1',revision},
    isolationGate:{gateVersion:'native-group-isolation:v1',lifecycleState:'armed',approvedEffectiveConfigRevision:revision}},
  memberDmDiscovery:{version:'native-group-member-dm-discovery:v1',source:{accountId:'account',groupJid:'123@g.us'},enrollment:'disabled'}
}; }
function harness(state=ready()) {
  let posts=0;
  return { get posts(){return posts;}, set state(value){state=value;},
    options:{binding,enabled:true,readStatus:async()=>structuredClone(state),
      setEnabled:async enabled=>{posts++;state.memberDmDiscovery.enrollment=enabled?'enabled':'disabled';}} };
}
test('fresh owner after restart restores approved enrollment without retaining epoch or revision',async()=>{
  const h=harness();
  assert.equal((await reconcileEnrollment(h.options)).status,'enabled');
  await reconcileEnrollment(h.options); assert.equal(h.posts,1);
  h.state=ready('R2');
  assert.equal((await reconcileEnrollment(h.options)).status,'enabled');assert.equal(h.posts,2);
});
for(const [name,mutate] of Object.entries({
  account:s=>s.memberDmDiscovery.source.accountId='other',
  group:s=>s.memberDmDiscovery.source.groupJid='999@g.us',
  session:s=>s.sessionRef='other',
  schema:s=>s.version='future',
  snapshot:s=>s.bootstrap.effectiveSnapshotProof.status='unknown',
  stale:s=>s.bootstrap.isolationGate.approvedEffectiveConfigRevision='old',
  disarmed:s=>s.bootstrap.isolationGate.lifecycleState='disarmed',
  dispatch:s=>s.dispatch.status='unknown',
  capture:s=>s.captureState='off',
  journal:s=>s.journalHealth='degraded',
  unavailable:s=>s.bootstrap.subsystemOperational=false,
})) test(`${name} mismatch never authorizes enrollment`,async()=>{
  const state=ready();mutate(state);const h=harness(state);
  assert.equal((await reconcileEnrollment(h.options)).status,'not_ready');assert.equal(h.posts,0);
});
test('explicit disabled policy disables enrollment and cannot be auto-restored',async()=>{
  const h=harness();await reconcileEnrollment(h.options);
  h.options.enabled=false;
  assert.equal((await reconcileEnrollment(h.options)).status,'disabled');
  await reconcileEnrollment(h.options);assert.equal(h.posts,2);
});
test('failed read-back does not claim restoration',async()=>{
  const h=harness();h.options.setEnabled=async()=>{};
  assert.equal((await reconcileEnrollment(h.options)).status,'not_ready');
});
test('real HTTP reconciliation reloads owner state and policy, validates auth, and stops cleanly',async t=>{
  const root=fs.mkdtempSync(path.join(os.tmpdir(),'dm-enrollment-'));
  let state=ready(), posts=0, reads=0;
  const server=http.createServer(async(req,res)=>{
    assert.equal(new URL(req.url,'http://localhost').searchParams.get('session'),'group-ref');
    if(req.method==='POST'){
      assert.equal(req.headers.authorization,'Bearer control');
      assert.equal(new URL(req.url,'http://localhost').pathname,'/inbound-observation/v2/control/member-dm/enrollment');
      let text='';for await(const chunk of req)text+=chunk;
      const body=JSON.parse(text);assert.equal(body.version,'native-group-member-dm-control:v1');
      state.memberDmDiscovery.enrollment=body.enabled?'enabled':'disabled';posts++;
      res.end(JSON.stringify({version:body.version,result:{enabled:body.enabled}}));
    }else{assert.equal(req.headers.authorization,'Bearer reader');reads++;res.end(JSON.stringify(state));}
  });
  server.listen(0,'127.0.0.1');await once(server,'listening');
  const hostFile=path.join(root,'host.json'), configFile=path.join(root,'observation.json'), nativeFile=path.join(root,'native.json');
  const write=(file,value)=>fs.writeFileSync(file,JSON.stringify(value));
  write(hostFile,{accountId:'account',sourceGroupJid:'123@g.us'});
  write(configFile,{accountId:'account',nativeSession:'123@g.us',port:server.address().port,readerToken:'reader',controlToken:'control'});
  write(nativeFile,{nativeConversationId:'123@g.us',nativeSourceGroupJid:null,observationSessionRef:'group-ref'});
  const policy={version:'member-dm-enrollment-policy:v1',enabled:true,...binding,observationConfigFile:configFile,nativeConfigFile:nativeFile};
  const policyFile=hostFile+'.member-dms.json';write(policyFile,policy);
  const reports=[];
  let worker;
  t.after(async()=>{await worker?.stop();server.closeAllConnections();await new Promise(resolve=>server.close(resolve));fs.rmSync(root,{recursive:true});});
  worker=await createMemberDmEnrollment({hostFile,intervalMs:10,report:s=>reports.push(s)});
  assert.equal(posts,1);assert.equal(reports.at(-1).status,'enabled');
  async function waitFor(predicate){for(let i=0;i<200&&!predicate();i++)await new Promise(r=>setTimeout(r,5));assert(predicate());}
  state=ready('R2');await waitFor(()=>posts===2);
  write(policyFile,{...policy,enabled:false});await waitFor(()=>posts===3);
  assert.equal(state.memberDmDiscovery.enrollment,'disabled');
  await worker.stop();
  // Abort does not retract a request already accepted by the server. Let that
  // request settle; there must be no subsequent scheduled requests.
  await new Promise(r=>setTimeout(r,40));const count=reads;
  await new Promise(r=>setTimeout(r,40));assert.equal(reads,count);
});
test('absent explicit host policy starts no reconciler',async()=>{
  assert.equal(await createMemberDmEnrollment({hostFile:path.join(os.tmpdir(),'absent-'+crypto.randomUUID())}),undefined);
});
