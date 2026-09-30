import test from 'node:test';
import assert from 'node:assert/strict';
import { repairHistoryNames } from './native-history-names.mjs';
const row=(id,actor,name='',reply=null)=>({ProviderMessageId:id,ChannelId:'whatsapp',AccountId:'a',NativeConversationId:'g',Direction:0,Message:{texto:'hello',remetente:{stableId:actor,nome:name},reply}});
const proof={ProviderMessageId:'one',ChannelId:'whatsapp',AccountId:'a',NativeConversationId:'g',ActorId:'peer',DisplayName:'Helena'};
test('fills only missing names from exact native evidence and preserves source',()=>{
 const source=[row('one','peer')];
 const output=repairHistoryNames(source,[proof]);
 assert.equal(output[0].Message.remetente.nome,'Helena');
 assert.equal(source[0].Message.remetente.nome,'');
 assert.equal(output[0].Message.remetente.stableId,'peer');
 assert.equal(repairHistoryNames([row('one','peer','Registered')],[proof])[0].Message.remetente.nome,'Registered');
});
for(const key of ['ProviderMessageId','ChannelId','AccountId','NativeConversationId','ActorId'])
 test(`does not transfer identity/name across ${key}`,()=>assert.equal(repairHistoryNames([row('one','peer')],[{...proof,[key]:'foreign'}])[0].Message.remetente.nome,''));
test('reply uses exact older message, not current speaker or most recent message',()=>{
 const source=[row('one','peer'),row('two','other','Celso'),row('three','third','Third',{id:'one',texto:'hello',remetente:null,origem:'citacao_do_remetente'})];
 const reply=repairHistoryNames(source,[proof])[2].Message.reply;
 assert.deepEqual(reply,{id:'one',texto:'hello',remetente:{stableId:'peer',nome:'Helena'},origem:'citacao_do_remetente'});
});
test('unresolved, conflicting and foreign-conversation quotes stay unresolved',()=>{
 for(const change of [{id:'absent'},{texto:'different'},{remetente:{stableId:'wrong',nome:''}}]){
  const quote={id:'one',texto:'hello',remetente:null,...change};
  const source=[row('one','peer','Helena'),row('two','other','Celso',quote)];
  assert.deepEqual(repairHistoryNames(source,[])[1].Message.reply,quote);
 }
 const source=[row('one','peer','Helena'),{...row('two','other','Celso',{id:'one',texto:'hello',remetente:null}),NativeConversationId:'foreign'}];
 assert.equal(repairHistoryNames(source,[])[1].Message.reply.remetente,null);
});
test('future targets do not establish historical attribution',()=>{
 const source=[row('two','other','Celso',{id:'one',texto:'hello',remetente:null}),row('one','peer','Helena')];
 assert.equal(repairHistoryNames(source,[])[0].Message.reply.remetente,null);
});
test('ambiguous evidence and duplicate history fail closed',()=>{
 assert.throws(()=>repairHistoryNames([row('one','peer')],[proof,{...proof,DisplayName:'Other'}]),/ambiguous/);
 assert.throws(()=>repairHistoryNames([row('one','peer'),row('one','peer')],[]),/duplicate/);
});
