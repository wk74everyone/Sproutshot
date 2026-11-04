'use strict';
const {test} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const {PGlite} = require('@electric-sql/pglite');
const {validateRows,migrationSQL,nextVersion,publishMigration} = require('../tools/update-growth-config.cjs');
const {createEditorBackend} = require('./editor-backend.cjs');
const {randomUUID} = require('node:crypto');
const ROOT = path.resolve(__dirname,'../..');
function fixture() {
  const root=fs.mkdtempSync(path.join(os.tmpdir(),'sproutshot-growth-'));
  fs.mkdirSync(path.join(root,'cloudbase'),{recursive:true});
  fs.cpSync(path.join(ROOT,'cloudbase/migrations'),path.join(root,'cloudbase/migrations'),{recursive:true});
  return root;
}
function cleanup(root) {
  const full=path.resolve(root);
  if(path.dirname(full)!==path.resolve(os.tmpdir()) || !path.basename(full).startsWith('sproutshot-growth-')) throw Error('Unsafe fixture cleanup');
  fs.rmSync(full,{recursive:true,force:true});
}
async function load(backend) {
  const response=await fetch(backend.address+'/request',{method:'POST',headers:{Authorization:'Bearer '+backend.token,
    'Content-Type':'application/json','X-Sproutshot-Editor-Player':'player-a'},
    body:JSON.stringify({action:'economy',economyAction:'load'})});
  const reply=await response.json();assert.equal(reply.success,true);return reply.data;
}

test('portrait migration removes only catalog addresses, preserves historical draws and does not charge an old retry again',async()=>{
  const root=fixture();let backend;
  try {
    const directory=path.join(root,'cloudbase/migrations');
    const filename=fs.readdirSync(directory).find(name=>name.endsWith('_remove_spirit_portrait_paths.sql'));
    assert.ok(filename);
    // Recreate the historical boundary, excluding later migrations that also replace the RPC.
    for(const name of fs.readdirSync(directory).filter(name=>name.endsWith('.sql') && name>=filename))
      fs.renameSync(path.join(directory,name),path.join(directory,name+'.disabled'));
    const migration=path.join(directory,filename);
    const options={stateDirectory:path.join(root,'state'),migrationsDirectory:directory};
    backend=await createEditorBackend(options);
    async function draw(operationId) {
      const response=await fetch(backend.address+'/request',{method:'POST',headers:{Authorization:'Bearer '+backend.token,
        'Content-Type':'application/json','X-Sproutshot-Editor-Player':'player-a'},
        body:JSON.stringify({action:'economy',economyAction:'summon',operationId,payload:{pool:'standard',count:1}})});
      const reply=await response.json();assert.equal(reply.success,true);return reply.data;
    }
    const operation=randomUUID();const legacy=await draw(operation);
    assert.ok(legacy.receipt.results[0].spriteAddress);
    await backend.close();fs.renameSync(migration+'.disabled',migration);
    backend=await createEditorBackend(options);
    const after=await load(backend);
    assert.deepEqual(after.profile,legacy.profile);
    assert.deepEqual(after.catalog.spirits,legacy.catalog.spirits.map(({sprite,...rest})=>rest));
    const replay=await draw(operation);assert.deepEqual(replay.receipt,legacy.receipt);assert.deepEqual(replay.profile,legacy.profile);
    const next=await draw(randomUUID());assert.equal(Object.hasOwn(next.receipt.results[0],'spriteAddress'),false);
    assert.equal(next.profile.starDew,legacy.profile.starDew-legacy.catalog.singleCost);
    const costRows=next.catalog.growth.filter(row=>!row.terminal).map(({level,cost})=>({level,cost}));
    await backend.close();publishMigration(root,costRows);
    backend=await createEditorBackend(options);
    const synced=await load(backend);
    assert.ok(synced.catalog.spirits.every(spirit=>!Object.hasOwn(spirit,'sprite')));
    assert.deepEqual(synced.profile,next.profile);
  } finally {if(backend)await backend.close();cleanup(root);}
});
test('growth input accepts actual Luban JSON fields and rejects duplicate, missing or invalid levels and prices',()=>{
  assert.deepEqual(validateRows([{level:2,levelup_cost:20},{level:1,levelup_cost:10}]),[{level:1,cost:10},{level:2,cost:20}]);
  for(const rows of [[],[{level:2,cost:10}],[{level:1,cost:-1}],[{level:1,cost:1.5}],
    [{level:1,cost:1},{level:1,cost:2}],[{level:1,cost:2000000001}]]) assert.throws(()=>validateRows(rows));
});
test('growth migration updates fees, preserves other config and custom growth attributes, and repeats without a version bump',async()=>{
  const db=new PGlite();
  try {
    await db.exec('CREATE TABLE sproutshot_economy_config(id int,config jsonb);CREATE TABLE sproutshot_players(player_level int);CREATE TABLE sproutshot_owned_spirits(level int)');
    const config={version:7,spirits:[{id:1001,selfDamage:9,defense:4}],hardPity:50,stages:[{stage:1,seeds:4}],
      growth:[{level:1,cost:10,damage:5,hp:8,defense:2},{level:2,cost:20,damage:7,hp:11,custom:'keep'}]};
    await db.query('INSERT INTO sproutshot_economy_config VALUES(1,$1)',[JSON.stringify(config)]);
    const sql=migrationSQL([{level:1,cost:15},{level:2,cost:25},{level:3,cost:30}]);await db.exec(sql);
    const updated=(await db.query('SELECT config FROM sproutshot_economy_config')).rows[0].config;
    assert.deepEqual(updated,{...config,version:8,growth:[{...config.growth[0],cost:15},{...config.growth[1],cost:25},{level:3,cost:30,damage:2,hp:10},
      {level:4,cost:0,damage:2,hp:10,terminal:true}]});
    await db.exec(sql);assert.deepEqual((await db.query('SELECT config FROM sproutshot_economy_config')).rows[0].config,updated);
    await assert.rejects(db.exec(migrationSQL([{level:1,cost:1}])),/GROWTH_LEVEL_REMOVAL_NOT_ALLOWED/);
    assert.deepEqual((await db.query('SELECT config FROM sproutshot_economy_config')).rows[0].config,updated);
  } finally {await db.close();}
});
test('migration filenames follow the latest version even when the clock is older and existing files are never overwritten',()=>{
  const root=fixture();
  try {
    const dir=path.join(root,'cloudbase/migrations');fs.writeFileSync(path.join(dir,'20991231235959_future.sql'),'-- fixture');
    assert.equal(nextVersion(dir,new Date('2026-10-26T00:00:00Z')),'21000101000000');
    const file=publishMigration(root,[{level:1,cost:10}],new Date('2026-10-26T00:00:00Z'));
    assert.ok(file.endsWith('21000101000000_update_growth_costs.sql'));
    const another=publishMigration(root,[{level:1,cost:20}],new Date('2026-10-26T00:00:00Z'));
    assert.notEqual(another,file);assert.ok(fs.readFileSync(file,'utf8').includes('"cost":10'));
  } finally {cleanup(root);}
});
test('backend restart applies a newly exported growth migration while retaining wallet, spirits and spirit configuration',async()=>{
  const root=fixture();let backend;
  try {
    const options={stateDirectory:path.join(root,'state'),migrationsDirectory:path.join(root,'cloudbase/migrations')};
    backend=await createEditorBackend(options);
    const before=await load(backend);const costs=before.catalog.growth.filter(row=>!row.terminal).map(({level,cost})=>({level,cost:cost+1}));
    await backend.close();publishMigration(root,costs);
    backend=await createEditorBackend(options);const after=await load(backend);
    assert.deepEqual(after.profile,before.profile);assert.deepEqual(after.catalog.spirits,before.catalog.spirits);
    assert.deepEqual(after.catalog.growth.filter(row=>!row.terminal).map(({level,cost})=>({level,cost})),costs);
    assert.equal(after.catalog.version,before.catalog.version+1);
  } finally {if(backend) await backend.close();cleanup(root);}
});

test('cost sync keeps a capped save and terminal attributes, and extending the price table unlocks the next level', async()=>{
  const root=fixture();let backend;
  try {
    const options={stateDirectory:path.join(root,'state'),migrationsDirectory:path.join(root,'cloudbase/migrations')};
    backend=await createEditorBackend(options);
    const first=await load(backend);
    const costs=first.catalog.growth.filter(row=>!row.terminal).map(({level,cost})=>({level,cost}));
    const cap=costs.length+1;
    // Inspect the persisted database through a separate reopen below, avoiding any cloud account.
    await backend.close();
    const file=path.join(root,'cloudbase/migrations',nextVersion(path.join(root,'cloudbase/migrations'))+'_fixture_level_cap.sql');
    fs.writeFileSync(file,`UPDATE public.sproutshot_players SET player_level=${cap};`);
    backend=await createEditorBackend(options);
    const capped=await load(backend);assert.equal(capped.profile.playerLevel,cap);
    await backend.close();publishMigration(root,costs);
    backend=await createEditorBackend(options);
    const synced=await load(backend);assert.deepEqual(synced.profile,capped.profile);assert.deepEqual(synced.catalog.growth,capped.catalog.growth);
    await backend.close();publishMigration(root,[...costs,{level:cap,cost:400}]);
    backend=await createEditorBackend(options);
    const extended=await load(backend);
    assert.deepEqual(extended.profile,capped.profile);
    assert.equal(extended.catalog.growth.find(row=>row.level===cap).terminal,undefined);
    assert.equal(extended.catalog.growth.find(row=>row.level===cap).cost,400);
    assert.equal(extended.catalog.growth.find(row=>row.level===cap+1).terminal,true);
  } finally {if(backend) await backend.close();cleanup(root);}
});
test('graceful shutdown requires local authorization and retains committed data after reopen',async()=>{
  const root=fixture();let backend;
  try {
    const options={stateDirectory:path.join(root,'state'),migrationsDirectory:path.join(root,'cloudbase/migrations')};
    backend=await createEditorBackend(options);const before=await load(backend);
    assert.equal((await fetch(backend.address+'/shutdown',{method:'POST'})).status,403);
    assert.equal((await fetch(backend.address+'/shutdown',{method:'POST',headers:{Authorization:'Bearer '+backend.token,Origin:'https://untrusted.invalid'}})).status,403);
    const stopped=await fetch(backend.address+'/shutdown',{method:'POST',headers:{Authorization:'Bearer '+backend.token}});
    assert.equal(stopped.status,200);
    await new Promise(resolve=>setTimeout(resolve,20));await backend.close();
    assert.equal(fs.existsSync(backend.sessionFile),false);
    backend=await createEditorBackend(options);assert.deepEqual((await load(backend)).profile,before.profile);
  } finally {if(backend) await backend.close();cleanup(root);}
});
