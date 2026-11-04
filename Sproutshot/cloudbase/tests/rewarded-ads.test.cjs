'use strict';
const {test,before,after}=require('node:test');
const assert=require('node:assert/strict');
const {randomUUID}=require('node:crypto');
const fs=require('node:fs'),path=require('node:path');
const {PGlite}=require('@electric-sql/pglite');
const {validate}=require('../functions/sproutshot-cloud/economy');
const A='a'.repeat(64),B='b'.repeat(64);let db;
async function command(owner,action,payload={},operation=randomUUID()) {
  return (await db.query('SELECT sproutshot_economy($1,$2,$3::uuid,$4::jsonb) AS data',[owner,action,operation,JSON.stringify(payload)])).rows[0].data;
}
const begin=(type='seed',owner=A,operation)=>command(owner,'beginRewardedAd',{rewardType:type},operation);
const claim=(ticket,owner=A,operation)=>command(owner,'claimRewardedAd',{ticketId:ticket,completed:true},operation);
async function reset(enabled=true) {
  await db.exec('TRUNCATE sproutshot_players,sproutshot_stage_progress CASCADE');
  await db.query("UPDATE sproutshot_rewarded_ad_config SET config=jsonb_set(config,'{enabled}',$1::jsonb)",[String(enabled)]);
  await command(A,'load');await command(B,'load');
}
before(async()=>{
  db=new PGlite();await db.exec(`CREATE ROLE anon;CREATE ROLE authenticated;CREATE ROLE service_role BYPASSRLS;
    CREATE SCHEMA auth;CREATE FUNCTION auth.uid() RETURNS text LANGUAGE sql AS $$ SELECT NULL::text $$;
    CREATE SCHEMA storage;CREATE TABLE storage.buckets(id text PRIMARY KEY,name text,public boolean,file_size_limit integer,allowed_mime_types text[]);
    CREATE TABLE storage.objects(id text);ALTER TABLE storage.objects ENABLE ROW LEVEL SECURITY;`);
  const dir=path.resolve(__dirname,'../migrations');for(const file of fs.readdirSync(dir).filter(n=>n.endsWith('.sql')).sort())await db.exec(fs.readFileSync(path.join(dir,file),'utf8'));
});
after(async()=>{if(db)await db.close();});
test('production defaults disabled; new tables and RPC are inaccessible to client roles',async()=>{
  assert.equal((await command(A,'load')).rewardedAds.enabled,false);
  await assert.rejects(begin(),/ADS_DISABLED/);
  for(const role of ['anon','authenticated']) {
    await db.exec('SET ROLE '+role);
    await assert.rejects(db.query('SELECT * FROM sproutshot_rewarded_ad_tickets'),/permission denied/);
    await assert.rejects(db.query('SELECT * FROM sproutshot_rewarded_ad_config'),/permission denied/);
    await assert.rejects(begin(),/permission denied/);await db.exec('RESET ROLE');
  }
  await db.exec('SET ROLE service_role');await command(A,'load');await db.exec('RESET ROLE');
});
test('ticket issuance gives no money; retries reuse tickets; claims grant exactly once across operation IDs',async()=>{
  await reset();const operation=randomUUID(),issued=await begin('seed',A,operation),id=issued.receipt.ticketId;
  assert.equal(issued.profile.sproutSeed,0);assert.equal(issued.receipt.rewardAmount,180);
  assert.equal(issued.rewardedAds.seedRemaining,3);
  assert.equal((await begin('seed',A,operation)).receipt.ticketId,id);
  assert.equal((await begin()).receipt.ticketId,id);
  await assert.rejects(begin('dew',A,operation),/OPERATION_CONFLICT/);
  const claimOperation=randomUUID(),paid=await claim(id,A,claimOperation);
  assert.equal(paid.profile.sproutSeed,180);assert.equal(paid.rewardedAds.seedRemaining,2);
  assert.deepEqual((await claim(id,A,claimOperation)).receipt,paid.receipt);
  assert.deepEqual((await claim(id)).receipt,paid.receipt);
  assert.equal((await command(A,'load')).profile.sproutSeed,180);
  // A consumed ticket remains recoverable after a feature switch / time change.
  await db.exec("UPDATE sproutshot_rewarded_ad_config SET config=jsonb_set(config,'{enabled}','false');UPDATE sproutshot_rewarded_ad_tickets SET expires_at=now()-interval '1 day'");
  assert.equal((await claim(id)).profile.sproutSeed,180);
});
test('cross-player, unfinished, expired, malformed and previous-day claims cannot grant',async()=>{
  await reset();const id=(await begin()).receipt.ticketId;
  await assert.rejects(claim(id,B),/AD_TICKET_NOT_FOUND/);
  await assert.rejects(command(A,'claimRewardedAd',{ticketId:id,completed:false}),/AD_NOT_COMPLETED/);
  await assert.rejects(begin('gold'),/INVALID_REQUEST/);
  await db.query("UPDATE sproutshot_rewarded_ad_tickets SET expires_at=now()-interval '1 second' WHERE ticket_id=$1",[id]);
  await assert.rejects(claim(id),/AD_TICKET_EXPIRED/);
  const fresh=(await begin()).receipt.ticketId;assert.notEqual(fresh,id);
  await db.query("UPDATE sproutshot_rewarded_ad_tickets SET reward_day=reward_day-1 WHERE ticket_id=$1",[fresh]);
  await assert.rejects(claim(fresh),/AD_TICKET_EXPIRED/);
  assert.equal((await command(A,'load')).profile.sproutSeed,0);
});
test('daily seed/dew quotas are independent and reset by the Shanghai server day',async()=>{
  await reset();
  for(let i=0;i<3;i++)await claim((await begin()).receipt.ticketId);
  await assert.rejects(begin(),/AD_DAILY_LIMIT/);
  for(let i=0;i<2;i++)await claim((await begin('dew')).receipt.ticketId);
  await assert.rejects(begin('dew'),/AD_DAILY_LIMIT/);
  const current=await command(A,'load');assert.equal(current.profile.sproutSeed,540);assert.equal(current.profile.starDew,200);
  assert.equal(current.rewardedAds.seedRemaining,0);assert.equal(current.rewardedAds.dewRemaining,0);
  await db.exec("UPDATE sproutshot_rewarded_ad_tickets SET reward_day=reward_day-1");
  const next=await command(A,'load');assert.equal(next.rewardedAds.seedRemaining,3);assert.equal(next.rewardedAds.dewRemaining,2);
});
test('concurrent claims recheck the last daily slot and cannot exceed the quota',async()=>{
  await reset();for(let i=0;i<2;i++)await claim((await begin()).receipt.ticketId);
  const first=(await begin()).receipt.ticketId;
  const second=(await db.query(`INSERT INTO sproutshot_rewarded_ad_tickets(owner_id,reward_type,reward_amount,reward_day)
    VALUES($1,'seed',180,(clock_timestamp() AT TIME ZONE 'Asia/Shanghai')::date) RETURNING ticket_id`,[A])).rows[0].ticket_id;
  const results=await Promise.allSettled([claim(first),claim(second)]);
  assert.equal(results.filter(r=>r.status==='fulfilled').length,1);
  assert.match(results.find(r=>r.status==='rejected').reason.message,/AD_DAILY_LIMIT/);
  assert.equal((await command(A,'load')).profile.sproutSeed,540);
});
test('receipt failure rolls back money, ticket consumption and the daily count',async()=>{
  await reset();const id=(await begin()).receipt.ticketId;
  await db.exec(`CREATE FUNCTION ad_test_fail() RETURNS trigger LANGUAGE plpgsql AS $$BEGIN IF NEW.action='claimRewardedAd' THEN RAISE EXCEPTION 'fixture_failure';END IF;RETURN NEW;END$$;
    CREATE TRIGGER ad_test_fail BEFORE INSERT ON sproutshot_operations FOR EACH ROW EXECUTE FUNCTION ad_test_fail();`);
  try {
    await assert.rejects(claim(id),/fixture_failure/);
    const snapshot=await command(A,'load');assert.equal(snapshot.profile.sproutSeed,0);assert.equal(snapshot.rewardedAds.seedRemaining,3);
    assert.equal((await db.query('SELECT claimed_at FROM sproutshot_rewarded_ad_tickets WHERE ticket_id=$1',[id])).rows[0].claimed_at,null);
  }finally{await db.exec('DROP TRIGGER ad_test_fail ON sproutshot_operations;DROP FUNCTION ad_test_fail()');}
  assert.equal((await claim(id)).profile.sproutSeed,180);
});
test('cloud validation strips client amounts, identity and spoofed quota data',()=>{
  const operation=randomUUID();
  assert.deepEqual(validate({economyAction:'beginRewardedAd',operationId:operation,payload:{rewardType:'seed',rewardAmount:99999,owner:B}}),
    {action:'beginRewardedAd',operation,payload:{rewardType:'seed'}});
  assert.throws(()=>validate({economyAction:'claimRewardedAd',operationId:operation,payload:{ticketId:randomUUID(),completed:false}}),/广告未完整观看/);
});
