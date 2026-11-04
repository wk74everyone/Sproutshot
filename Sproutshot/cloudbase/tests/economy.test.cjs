'use strict';
const { test, before, after } = require('node:test');
const assert = require('node:assert/strict');
const { randomUUID } = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');
const { PGlite } = require('@electric-sql/pglite');
const { createService } = require('../functions/sproutshot-cloud/service');
const root = path.resolve(__dirname, '../..');
const A = 'a'.repeat(64), B = 'b'.repeat(64);
let db;
async function command(owner, action, payload = {}, operation = randomUUID()) {
  const result = await db.query('SELECT public.sproutshot_economy($1,$2,$3::uuid,$4::jsonb) AS result',
    [owner, action, operation, JSON.stringify(payload)]);
  return result.rows[0].result;
}
async function reset() {
  await db.exec('TRUNCATE sproutshot_operations,sproutshot_battles,sproutshot_legacy_claims,sproutshot_pity,sproutshot_owned_spirits,sproutshot_players,sproutshot_stage_progress CASCADE');
  await command(A, 'load');
}
before(async () => {
  db = new PGlite();
  await db.exec(`CREATE ROLE anon; CREATE ROLE authenticated; CREATE ROLE service_role BYPASSRLS;
    CREATE SCHEMA auth; CREATE FUNCTION auth.uid() RETURNS text LANGUAGE sql AS $$ SELECT NULL::text $$;
    CREATE SCHEMA storage; CREATE TABLE storage.buckets(id text PRIMARY KEY,name text,public boolean,file_size_limit integer,allowed_mime_types text[]);
    CREATE TABLE storage.objects(id text); ALTER TABLE storage.objects ENABLE ROW LEVEL SECURITY;`);
  for (const name of fs.readdirSync(path.join(root, 'cloudbase/migrations')).filter(n => n.endsWith('.sql')).sort()) {
    await db.exec(fs.readFileSync(path.join(root, 'cloudbase/migrations', name), 'utf8'));
  }
});
after(async () => { if (db) await db.close(); });

test('migrations compile in PostgreSQL and balances are not client-writable', async () => {
  await reset();
  await db.exec('SET ROLE authenticated');
  await assert.rejects(db.query('SELECT * FROM public.sproutshot_players'), /permission denied/);
  await assert.rejects(command(A, 'load'), /permission denied/);
  await db.exec('RESET ROLE');
  const result = await command(A, 'load');
  assert.equal(result.profile.starDew, 0); assert.equal(result.profile.playerLevel, 1);
  assert.equal(result.catalog.duplicateSeeds, 20);
  assert.ok(result.catalog.spirits.every(spirit=>!Object.hasOwn(spirit,'sprite')));
});

test('profile returns the persisted profession per player and keeps snapshot access server-only', async () => {
  await reset();
  const before = (await command(A, 'load')).profile;
  assert.equal(before.equippedProfessionId, 1);
  assert.equal((await command(B, 'load')).profile.equippedProfessionId, 1);

  // Fixture only: the profession-switching operation is a separate implementation step.
  await db.query('UPDATE public.sproutshot_players SET equipped_profession=2 WHERE owner_id=$1', [A]);
  const after = (await command(A, 'load')).profile;
  assert.deepEqual(after, { ...before, equippedProfessionId: 2 });
  assert.equal((await command(B, 'load')).profile.equippedProfessionId, 1);

  const access = (await db.query(`SELECT
    has_function_privilege('service_role','public.sproutshot_profile_snapshot(text)','EXECUTE') AS server,
    has_function_privilege('authenticated','public.sproutshot_profile_snapshot(text)','EXECUTE') AS authenticated,
    has_function_privilege('anon','public.sproutshot_profile_snapshot(text)','EXECUTE') AS anon`)).rows[0];
  assert.deepEqual(access, { server: true, authenticated: false, anon: false });
});

test('profession selection persists only for its owner, costs nothing, and is idempotent', async () => {
  await reset(); await command(B, 'load');
  // Seed an alternate stored ID to prove the operation writes, without opening the guardian.
  await db.query('UPDATE sproutshot_players SET equipped_profession=2,sprout_seed=123,star_dew=456 WHERE owner_id=$1', [A]);
  const before = (await command(A, 'load')).profile;
  const operation = randomUUID();
  const result = await command(A, 'equipProfession', { professionId: 1 }, operation);
  assert.deepEqual(result.profile, { ...before, equippedProfessionId: 1, revision: before.revision + 1 });
  assert.equal(result.receipt.professionId, 1); assert.equal(result.receipt.totalCost, 0);
  assert.equal((await command(B, 'load')).profile.equippedProfessionId, 1);
  const repeated = await command(A, 'equipProfession', { professionId: 1 }, operation);
  assert.deepEqual(repeated, result);
  assert.equal((await db.query('SELECT count(*)::integer AS count FROM sproutshot_operations WHERE owner_id=$1 AND operation_id=$2', [A, operation])).rows[0].count, 1);
  await assert.rejects(command(A, 'equipProfession', { professionId: 2 }, operation), /OPERATION_CONFLICT/);
  await assert.rejects(command(A, 'upgradePlayer', {}, operation), /OPERATION_CONFLICT/);
});

test('unavailable or malformed professions are rejected without changing player or operation history', async () => {
  await reset();
  const before = (await command(A, 'load')).profile;
  for (const professionId of [undefined, null, 0, -1, 2, 9999, 1.5, '1', true, {}, [], 2147483648]) {
    await assert.rejects(command(A, 'equipProfession', { professionId }), /INVALID_REQUEST/);
    assert.deepEqual((await command(A, 'load')).profile, before);
  }
  assert.equal((await db.query('SELECT count(*)::integer AS count FROM sproutshot_operations')).rows[0].count, 0);
});

test('profession changes roll back if writing the operation receipt fails', async () => {
  await reset();
  await db.query('UPDATE sproutshot_players SET equipped_profession=2 WHERE owner_id=$1', [A]);
  const before = (await command(A, 'load')).profile;
  await db.exec(`CREATE FUNCTION reject_profession_receipt() RETURNS trigger LANGUAGE plpgsql AS $$
    BEGIN IF NEW.action='equipProfession' THEN RAISE EXCEPTION 'TEST_RECEIPT_FAILURE'; END IF; RETURN NEW; END $$;
    CREATE TRIGGER reject_profession_receipt BEFORE INSERT ON sproutshot_operations FOR EACH ROW EXECUTE FUNCTION reject_profession_receipt();`);
  try {
    await assert.rejects(command(A, 'equipProfession', { professionId: 1 }), /TEST_RECEIPT_FAILURE/);
    assert.deepEqual((await command(A, 'load')).profile, before);
  } finally { await db.exec('DROP TRIGGER reject_profession_receipt ON sproutshot_operations; DROP FUNCTION reject_profession_receipt();'); }
});

test('legacy progress migrates once, never grants resources and cannot cross accounts', async () => {
  await reset(); const install = randomUUID();
  const migrated = await command(A, 'migrate', { installId: install, highest: 12, revealed: 31 });
  assert.equal(migrated.profile.highestClearedStage, 12); assert.equal(migrated.profile.revealedMapMask, 3);
  assert.equal(migrated.profile.sproutSeed, 0); assert.equal(migrated.profile.starDew, 0);
  const again = await command(A, 'migrate', { installId: randomUUID(), highest: 50, revealed: 31 });
  assert.equal(again.profile.highestClearedStage, 12);
  const other = await command(B, 'migrate', { installId: install, highest: 12, revealed: 3 });
  assert.equal(other.profile.highestClearedStage, 0);
});

test('duplicate spirits of any rarity refund exactly 20 seeds, with idempotent charging', async () => {
  await reset();
  await db.exec(`UPDATE sproutshot_players SET star_dew=2000 WHERE owner_id='${A}';
    INSERT INTO sproutshot_owned_spirits(owner_id,spirit_id) VALUES('${A}',1001),('${A}',1002),('${A}',1003);`);
  const operation = randomUUID();
  const first = await command(A, 'summon', { pool: 'standard', count: 10 }, operation);
  assert.equal(first.profile.starDew, 1000); assert.equal(first.profile.sproutSeed, 200);
  assert.equal(first.receipt.results.length, 10);
  assert.ok(first.receipt.results.every(r => !r.isNew && r.duplicateSeedAmount === 20));
  const replay = await command(A, 'summon', { pool: 'standard', count: 10 }, operation);
  assert.deepEqual(replay.receipt, first.receipt); assert.equal(replay.profile.starDew, 1000);
  await assert.rejects(command(A, 'summon', { pool: 'standard', count: 1 }, operation), /OPERATION_CONFLICT/);
});

test('ten draws handle duplicates inside the batch and hard pity at its exact position', async () => {
  await reset(); await db.exec(`UPDATE sproutshot_players SET star_dew=2000 WHERE owner_id='${A}';
    INSERT INTO sproutshot_pity(owner_id,pool_id,misses) VALUES('${A}','standard',59);`);
  const result = await command(A, 'summon', { pool: 'standard', count: 10 });
  assert.equal(result.receipt.results[0].rarity, 2); assert.equal(result.receipt.results[0].isHardPity, true);
  const newCount = result.receipt.results.filter(r => r.isNew).length;
  assert.equal(result.profile.sproutSeed, (10 - newCount) * 20);
  assert.equal(result.profile.ownedSpirits.length, newCount);
  assert.ok(result.profile.pityCount <= 9);
});

test('60-draw pity does not trigger at 59, guarantees the 60th, resets and replays without charging twice', async () => {
  await reset();
  await db.exec('BEGIN');
  try {
    // Disable natural high-rarity rolls in this fixture to test the boundary deterministically.
    await db.exec(`UPDATE sproutshot_economy_config SET config=jsonb_set(config,'{rarityWeights}','[10000,0,0]');
      UPDATE sproutshot_players SET star_dew=200 WHERE owner_id='${A}';
      INSERT INTO sproutshot_pity(owner_id,pool_id,misses) VALUES('${A}','standard',58);`);
    const penultimate=await command(A,'summon',{pool:'standard',count:1});
    assert.equal(penultimate.catalog.hardPity,60);
    assert.equal(penultimate.receipt.results[0].isHardPity,false);
    assert.equal(penultimate.receipt.results[0].rarity,0);
    assert.equal(penultimate.profile.pityCount,59);
    const operation=randomUUID();
    const final=await command(A,'summon',{pool:'standard',count:1},operation);
    assert.equal(final.receipt.results[0].isHardPity,true);
    assert.equal(final.receipt.results[0].rarity,2);
    assert.equal(final.profile.pityCount,0);
    assert.equal(final.profile.starDew,0);
    const replay=await command(A,'summon',{pool:'standard',count:1},operation);
    assert.deepEqual(replay,final);
  } finally { await db.exec('ROLLBACK'); }
});

test('insufficient balance and mid-transaction errors leave no partial changes', async () => {
  await reset(); const before = (await command(A, 'load')).profile;
  await assert.rejects(command(A, 'summon', { pool: 'standard', count: 1 }), /INSUFFICIENT_DEW/);
  assert.deepEqual((await command(A, 'load')).profile, before);
  await db.exec(`UPDATE sproutshot_players SET star_dew=100 WHERE owner_id='${A}'`);
  await db.exec('BEGIN');
  try {
    await db.exec("UPDATE sproutshot_economy_config SET config=jsonb_set(config,'{spirits}','[]')");
    await assert.rejects(command(A, 'summon', { pool: 'standard', count: 1 }), /CONFIG_MISSING/);
  } finally { await db.exec('ROLLBACK'); }
  const result = (await command(A, 'load')).profile;
  assert.equal(result.starDew, 100); assert.equal(result.ownedSpirits.length, 0);
});

test('server battle tickets settle once, legacy clears count as repeats, failures pay nothing', async () => {
  await reset();
  await command(A, 'migrate', { installId: randomUUID(), highest: 2, revealed: 0 });
  await assert.rejects(command(A, 'beginBattle', { stage: 4 }), /STAGE_LOCKED/);
  const ticket = await command(A, 'beginBattle', { stage: 1 });
  await db.exec(`UPDATE sproutshot_battles SET started_at=now()-interval '5 seconds' WHERE owner_id='${A}'`);
  const stage = ticket.catalog.stages.find(s => s.stage === 1);
  const payload = { battleId: ticket.receipt.battleId, victory: true, defeated: stage.enemies };
  const cleared = await command(A, 'settleBattle', payload);
  assert.equal(cleared.receipt.firstClear, false); assert.equal(cleared.profile.sproutSeed, Math.floor(stage.seeds / 2));
  const duplicate = await command(A, 'settleBattle', payload);
  assert.equal(duplicate.profile.sproutSeed, cleared.profile.sproutSeed);
  await assert.rejects(command(B, 'settleBattle', payload), /BATTLE_NOT_FOUND/);
  const second = await command(A, 'beginBattle', { stage: 3 });
  const failure = await command(A, 'settleBattle', { battleId: second.receipt.battleId, victory: false, defeated: 0 });
  assert.equal(failure.profile.highestClearedStage, 2); assert.equal(failure.receipt.rewardSeeds, 0);
});

test('two concurrent requests cannot overspend a one-draw balance', async () => {
  await reset(); await db.exec(`UPDATE sproutshot_players SET star_dew=100 WHERE owner_id='${A}'`);
  const responses = await Promise.allSettled([command(A, 'summon', { pool:'standard',count:1 }), command(A, 'summon', { pool:'standard',count:1 })]);
  assert.equal(responses.filter(r => r.status === 'fulfilled').length, 1);
  assert.equal((await command(A, 'load')).profile.starDew, 0);
});

test('growth and equipment persist atomically and reject unowned spirits', async () => {
  await reset();
  await db.exec(`UPDATE sproutshot_players SET sprout_seed=100 WHERE owner_id='${A}';
    INSERT INTO sproutshot_owned_spirits(owner_id,spirit_id) VALUES('${A}',1002);`);
  const levelUp = await command(A,'upgradePlayer');
  assert.equal(levelUp.profile.playerLevel,2); assert.equal(levelUp.profile.sproutSeed,70);
  const spiritUp = await command(A,'upgradeSpirit',{spiritId:1002});
  assert.equal(spiritUp.profile.ownedSpirits[0].level,2); assert.equal(spiritUp.profile.sproutSeed,40);
  const equipped = await command(A,'equipSpirit',{spiritId:1002});
  assert.equal(equipped.profile.equippedSpiritId,1002);
  await assert.rejects(command(A,'equipSpirit',{spiritId:9999}),/SPIRIT_NOT_OWNED/);
  assert.equal((await command(A,'load')).profile.equippedSpiritId,1002);
  const removed = await command(A,'equipSpirit',{spiritId:0});
  assert.equal(removed.profile.equippedSpiritId,0);
  assert.equal((await command(A,'load')).profile.equippedSpiritId,0);
  assert.deepEqual(removed.profile.ownedSpirits,equipped.profile.ownedSpirits);
  await db.exec(`UPDATE sproutshot_players SET player_level=20 WHERE owner_id='${A}'`);
  await assert.rejects(command(A,'upgradePlayer'),/MAX_LEVEL/);
  assert.equal((await command(A,'load')).profile.sproutSeed,40);
});

test('last growth price upgrades both hero and spirit from 19 to 20, persists final attributes and never charges at the cap', async () => {
  await reset();
  await db.exec(`UPDATE sproutshot_players SET player_level=19,sprout_seed=1249 WHERE owner_id='${A}';
    INSERT INTO sproutshot_owned_spirits(owner_id,spirit_id,level) VALUES('${A}',1001,19);`);
  const before = (await command(A,'load')).profile;
  await assert.rejects(command(A,'upgradePlayer'),/INSUFFICIENT_SEEDS/);
  assert.deepEqual((await command(A,'load')).profile,before);
  await db.exec(`UPDATE sproutshot_players SET sprout_seed=2500 WHERE owner_id='${A}'`);
  const operation = randomUUID();
  const up = await command(A,'upgradePlayer',{},operation);
  assert.equal(up.profile.playerLevel,20); assert.equal(up.receipt.totalCost,1250); assert.equal(up.profile.sproutSeed,1250);
  assert.deepEqual(up.catalog.growth.find(row=>row.level===20),{level:20,cost:0,damage:38,hp:190,terminal:true});
  const replay = await command(A,'upgradePlayer',{},operation);
  assert.deepEqual(replay.receipt,up.receipt); assert.equal(replay.profile.sproutSeed,1250);
  await assert.rejects(command(A,'upgradePlayer'),/MAX_LEVEL/);
  const spirit = await command(A,'upgradeSpirit',{spiritId:1001});
  assert.equal(spirit.profile.ownedSpirits[0].level,20); assert.equal(spirit.profile.sproutSeed,0);
  await assert.rejects(command(A,'upgradeSpirit',{spiritId:1001}),/MAX_LEVEL/);
  assert.deepEqual((await command(A,'load')).profile,spirit.profile);
});

test('cloud function derives identity, strips client prices, and rejects legacy progress writes', async () => {
  const calls = [];
  const handler = createService({ getContext: () => ({ OPENID:'test',APPID:'wx0123456789abcdef' }),
    getAppId: () => 'wx0123456789abcdef', storage: {}, db: { rpc: async (name,args) => {
      calls.push({name,args}); return {data:{profile:{playerId:args.p_owner}}};
    } } });
  const result = await handler({action:'economy',economyAction:'summon',operationId:randomUUID(),
    playerId:'forged',payload:{pool:'standard',count:1,cost:0,results:[1003],sproutSeed:9999}});
  assert.equal(result.success,true); assert.match(calls[0].args.p_owner,/^[a-f0-9]{64}$/);
  assert.deepEqual(calls[0].args.p_payload,{pool:'standard',count:1});
  assert.equal((await handler({action:'syncProgress',highestClearedStage:50})).code,'PROGRESS_SERVER_OWNED');
  assert.equal((await handler({action:'economy',economyAction:'load',expectedPlayerId:'b'.repeat(64)})).code,'PLAYER_CHANGED');
});
