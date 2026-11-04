'use strict';
const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const os=require('node:os');
const path=require('node:path');
const {randomUUID}=require('node:crypto');
const {createEditorBackend}=require('./editor-backend.cjs');
function temporaryDirectory(){return fs.mkdtempSync(path.join(os.tmpdir(),'sproutshot-editor-pg-'));}
function cleanup(directory){
  const resolved=path.resolve(directory);
  if(path.dirname(resolved)!==path.resolve(os.tmpdir()) || !path.basename(resolved).startsWith('sproutshot-editor-pg-'))
    throw new Error('Refusing to remove a directory outside this test fixture.');
  fs.rmSync(resolved,{recursive:true,force:true});
}
async function request(backend,account,input){
  const response=await fetch(backend.address+'/request',{method:'POST',headers:{Authorization:'Bearer '+backend.token,
    'Content-Type':'application/json','X-Sproutshot-Editor-Player':account},body:JSON.stringify(input)});
  return response.json();
}
const load=(backend,account='player-a')=>request(backend,account,{requestId:1,action:'economy',economyAction:'load'});

test('ad claim lost after commit survives backend restart without requiring another watch or paying twice',async()=>{
  const directory=temporaryDirectory();let drop=true;
  let backend=await createEditorBackend({stateDirectory:directory,afterCommit:input=>{
    if(drop&&input.economyAction==='claimRewardedAd'){drop=false;return true;}return false;
  }});
  try {
    const initial=await load(backend);
    assert.equal(initial.data.rewardedAds.enabled,true);
    const begun=await request(backend,'player-a',{action:'economy',economyAction:'beginRewardedAd',operationId:randomUUID(),payload:{rewardType:'seed'}});
    assert.equal(begun.data.profile.sproutSeed,initial.data.profile.sproutSeed);
    const input={action:'economy',economyAction:'claimRewardedAd',operationId:randomUUID(),payload:{ticketId:begun.data.receipt.ticketId,completed:true}};
    await assert.rejects(request(backend,'player-a',input));
    await backend.close();backend=await createEditorBackend({stateDirectory:directory});
    const retried=await request(backend,'player-a',input);
    assert.equal(retried.data.profile.sproutSeed,initial.data.profile.sproutSeed+180);
    assert.equal(retried.data.rewardedAds.seedRemaining,2);
    assert.deepEqual((await request(backend,'player-a',input)).data.receipt,retried.data.receipt);
    assert.equal((await load(backend)).data.profile.sproutSeed,initial.data.profile.sproutSeed+180);
  }finally{await backend.close();cleanup(directory);}
});

test('HTTP editor account executes real economy RPC, persists after restart, and seeds only once',async()=>{
  const directory=temporaryDirectory();let backend=await createEditorBackend({stateDirectory:directory});
  try{
    const initial=await load(backend);assert.equal(initial.success,true);assert.equal(initial.data.profile.starDew,10000);
    const operationId=randomUUID();
    const command={requestId:2,action:'economy',economyAction:'summon',operationId,
      expectedPlayerId:initial.data.profile.playerId,payload:{pool:'standard',count:10}};
    const drawn=await request(backend,'player-a',command);
    assert.equal(drawn.success,true);assert.equal(drawn.data.profile.starDew,9000);assert.equal(drawn.data.receipt.results.length,10);
    const duplicate=await request(backend,'player-a',{...command,requestId:3});
    assert.deepEqual(duplicate.data.receipt,drawn.data.receipt);assert.equal(duplicate.data.profile.starDew,9000);
    const other=await load(backend,'player-b');assert.equal(other.data.profile.starDew,10000);
    assert.notEqual(other.data.profile.playerId,initial.data.profile.playerId);
    const swapped=await request(backend,'player-b',command);assert.equal(swapped.code,'PLAYER_CHANGED');
    await backend.close();backend=await createEditorBackend({stateDirectory:directory});
    const restored=await load(backend);assert.equal(restored.data.profile.starDew,9000);
    assert.deepEqual(restored.data.profile.ownedSpirits,drawn.data.profile.ownedSpirits);
    assert.equal(restored.data.profile.pityCount,drawn.data.profile.pityCount);
    assert.deepEqual(restored.data.profile.lastSummon,drawn.data.receipt);
    const grantId=randomUUID();
    const credited=await request(backend,'player-a',{action:'editorGrant',operationId:grantId});
    assert.equal(credited.data.profile.starDew,10000);
    const replayGrant=await request(backend,'player-a',{action:'editorGrant',operationId:grantId});
    assert.equal(replayGrant.data.profile.starDew,10000);
  }finally{await backend.close();cleanup(directory);}
});

test('local backend rejects missing token, browser origins, invalid accounts and non-economy APIs',async()=>{
  const directory=temporaryDirectory();const backend=await createEditorBackend({stateDirectory:directory});
  try{
    assert.equal((await fetch(backend.address+'/health')).status,403);
    assert.equal((await fetch(backend.address+'/health',{headers:{Authorization:'Bearer '+backend.token,Origin:'https://untrusted.invalid'}})).status,403);
    assert.equal((await request(backend,'../player',{action:'economy',economyAction:'load'})).code,'INVALID_TEST_ACCOUNT');
    assert.equal((await request(backend,'player-a',{action:'signUpload'})).code,'UNSUPPORTED_EDITOR_ACTION');
  }finally{await backend.close();cleanup(directory);}
});

test('profession endpoint applies to an existing local save, survives restart and keeps other accounts isolated',async()=>{
  const directory=temporaryDirectory();
  const migrations=path.join(directory,'old-migrations');fs.mkdirSync(migrations);
  const source=path.resolve(__dirname,'../migrations');
  for(const name of fs.readdirSync(source).filter(name=>name.endsWith('.sql') && name<'20261028032330'))
    fs.copyFileSync(path.join(source,name),path.join(migrations,name));
  let backend=await createEditorBackend({stateDirectory:directory,migrationsDirectory:migrations});
  try{
    const original=(await load(backend)).data.profile;
    assert.equal(Object.hasOwn(original,'equippedProfessionId'),false);
    await backend.close();backend=await createEditorBackend({stateDirectory:directory});
    const migrated=(await load(backend)).data.profile;
    assert.deepEqual(migrated,{...original,equippedProfessionId:1});
    const input={action:'economy',economyAction:'equipProfession',operationId:randomUUID(),expectedPlayerId:migrated.playerId,payload:{professionId:1}};
    const equipped=await request(backend,'player-a',input);
    assert.equal(equipped.success,true);assert.equal(equipped.data.receipt.professionId,1);
    assert.deepEqual(equipped.data.profile,{...migrated,revision:migrated.revision+1});
    const invalid=await request(backend,'player-a',{...input,operationId:randomUUID(),payload:{professionId:2}});
    assert.equal(invalid.code,'INVALID_REQUEST');
    assert.equal((await request(backend,'player-b',input)).code,'PLAYER_CHANGED');
    await backend.close();backend=await createEditorBackend({stateDirectory:directory});
    assert.deepEqual((await load(backend)).data.profile,equipped.data.profile);
    assert.deepEqual((await request(backend,'player-a',input)).data.receipt,equipped.data.receipt);
  }finally{await backend.close();cleanup(directory);}
});

test('response lost after commit retries with the same receipt and charges only once',async()=>{
  const directory=temporaryDirectory();let drop=true;
  const backend=await createEditorBackend({stateDirectory:directory,afterCommit:input=>{
    if(drop&&input.economyAction==='summon'){drop=false;return true;}return false;
  }});
  try{
    const initial=await load(backend);
    const command={action:'economy',economyAction:'summon',operationId:randomUUID(),
      expectedPlayerId:initial.data.profile.playerId,payload:{pool:'standard',count:1}};
    await assert.rejects(request(backend,'player-a',command));
    const retry=await request(backend,'player-a',command);assert.equal(retry.success,true);
    assert.equal(retry.data.profile.starDew,9900);assert.equal(retry.data.receipt.operationId,command.operationId);
    assert.equal((await load(backend)).data.profile.starDew,9900);
  }finally{await backend.close();cleanup(directory);}
});
