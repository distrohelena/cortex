import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

// Scheduling only; NativeMemoryRecovery continues to own admission, dedupe,
// durable receipts and reconciliation. No second memory engine or sender.
export async function startMemoryMaintenance({recovery,store,bindingRefs,intervalMs,report,
  schedule=(fn,ms)=>setTimeout(fn,ms),cancel=timer=>clearTimeout(timer)}) {
  let stopped=false, timer, pending, stopping;
  const emit=(status,diagnostic)=>report({phase:'memory',status,diagnostic,
    groups:bindingRefs.map(binding=>({binding,...recovery.status(binding)}))});
  const tick=()=>{
    if(stopped || pending) return;
    pending=Promise.resolve().then(()=>recovery.runOnce()).then(()=>{
      const degraded=bindingRefs.some(b=>{const s=recovery.status(b);return s.pending>0 || !!s.diagnostic;});
      emit(degraded?'degraded':'running');
    }).catch(()=>emit('degraded','memory_maintenance_failed')).finally(()=>{
      pending=undefined;
      if(!stopped) timer=schedule(tick,intervalMs);
    });
  };
  emit('starting'); timer=schedule(tick,0);
  return {stop(){return stopping??=(async()=>{
    stopped=true;cancel(timer);
    try { await recovery.drain(); if(pending) await pending; }
    finally { try { emit('stopped'); } finally { recovery.close();store.close(); } }
  })();}};
}

export function selectMemoryBinding(memory,host) {
  if(memory?.version!=='native-memory-host:v1'||!Array.isArray(memory.bindings)
    ||!memory.recovery||!Number.isSafeInteger(memory.recovery.intervalMs)||memory.recovery.intervalMs<1000)
    throw Error('conversation_memory_config_invalid');
  const bindings=memory.bindings.filter(b=>b.conversation?.channel==='whatsapp'
    &&b.conversation.kind==='group'&&b.conversation.accountId===host.accountId
    &&b.conversation.nativeId===host.sourceGroupJid);
  if(bindings.length!==1||!bindings[0].historyNotBefore) throw Error('conversation_memory_binding_missing_or_ambiguous');
  return bindings;
}

export function getMemoryExtractionPolicy(lifecycle) {
  const policy=lifecycle.SOCIAL_EXTRACTION_INSTRUCTIONS;
  if(typeof policy!=='string'||!policy.startsWith('Social extraction policy: conversation-evidence:v2.'))
    throw Error('memory_extraction_policy_unavailable');
  return policy;
}

export async function createConversationMemory({configFile,hostFile,report}) {
  const read=file=>JSON.parse(fs.readFileSync(file,'utf8'));
  if(!configFile||!path.isAbsolute(configFile))throw Error('conversation_memory_required');
  const cfg=read(configFile),host=read(hostFile);
  if(cfg.version!=='conversation-memory:v1'||!['pluginRoot','pluginConfigFile','memoryHostFile'].every(k=>typeof cfg[k]==='string'&&path.isAbsolute(cfg[k])))
    throw Error('conversation_memory_config_invalid');
  const memory=read(cfg.memoryHostFile),bindings=selectMemoryBinding(memory,host);
  for(const file of [memory.storePath,memory.recovery.storePath,memory.recovery.sourceDatabasePath])
    if(typeof file!=='string'||!path.isAbsolute(file)||!fs.existsSync(file))throw Error('conversation_memory_store_missing');
  if(new Set([memory.storePath,memory.recovery.storePath,memory.recovery.sourceDatabasePath].map(p=>path.resolve(p).toLowerCase())).size!==3)
    throw Error('conversation_memory_store_collision');
  const module=name=>import(pathToFileURL(path.join(cfg.pluginRoot,name+'.js')).href);
  const [{NativeIngestStore},{NativeIngestService},{NativeMemoryRecovery},{readNativeMemoryHistory},
    {GraphitiClient},{SpiceDbClient},lifecycle,{graphitiMemoryConfigSchema}]=await Promise.all(
    ['native-ingest-store','native-ingest','native-memory-recovery','native-memory-history','graphiti','spicedb','message-lifecycle','config'].map(module));
  const extractionInstructions=getMemoryExtractionPolicy(lifecycle);
  const {createMessageLifecycleRecorder}=lifecycle;
  const config=graphitiMemoryConfigSchema.parse(read(cfg.pluginConfigFile).plugins?.entries?.['openclaw-memory-graphiti']?.config);
  if(!config.spicedb.token)throw Error('conversation_memory_credentials_missing');
  const graphiti=new GraphitiClient(config.graphiti.endpoint),spicedb=new SpiceDbClient(config.spicedb);
  let lastToken;
  const recorder=createMessageLifecycleRecorder({graphiti,spicedb,subject:{type:config.subjectType,id:config.subjectId},
    extractionInstructions,
    logger:{info(){},warn(){report({phase:'memory',status:'degraded',diagnostic:'memory_recorder_warning'});}},
    getLastWriteToken:()=>lastToken,setLastWriteToken:value=>{lastToken=value;}});
  const store=new NativeIngestStore(memory.storePath);let recovery;
  try{
    const service=new NativeIngestService(store,bindings,recorder);
    recovery=new NativeMemoryRecovery(memory.recovery.storePath,bindings,service,b=>readNativeMemoryHistory(memory.recovery.sourceDatabasePath,b));
    const maintenance=await startMemoryMaintenance({store,recovery,bindingRefs:bindings.map(b=>b.bindingRef),intervalMs:memory.recovery.intervalMs,report});
    let stopping;
    return {stop(){return stopping??=maintenance.stop().finally(()=>spicedb.close());}};
  }catch(error){try{recovery?.close();store.close();}finally{spicedb.close();}throw error;}
}
