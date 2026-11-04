'use strict';

// Local editor tool only. This directory is outside the deployed functionRoot.
const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const { createHash, randomBytes, timingSafeEqual } = require('node:crypto');
const { PGlite } = require('@electric-sql/pglite');
const { createService } = require('../functions/sproutshot-cloud/service');

const ROOT = path.resolve(__dirname, '../..');
const TEST_APP_ID = 'wx0000000000000000';
const PROJECT_KEY = createHash('sha256').update(ROOT.toLowerCase()).digest('hex');
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
function ownerFor(account) {
  return createHash('sha256').update(TEST_APP_ID + ':editor:' + PROJECT_KEY + ':' + account).digest('hex');
}
function error(code, message) { return Object.assign(new Error(message), { code }); }

async function openDatabase(directory, migrationsDirectory = path.join(ROOT, 'cloudbase/migrations')) {
  fs.mkdirSync(directory, { recursive: true });
  const db = new PGlite(directory);
  try {
    const marker = await db.query("SELECT to_regclass('public.sproutshot_editor_migrations') AS name");
    if (!marker.rows[0].name) {
      const existing = await db.query("SELECT to_regclass('public.sproutshot_players') AS name");
      if (existing.rows[0].name) throw error('NOT_EDITOR_DATABASE', 'Refusing to initialize an unmarked database.');
      await db.exec(`BEGIN;
        CREATE ROLE anon; CREATE ROLE authenticated; CREATE ROLE service_role BYPASSRLS;
        CREATE SCHEMA auth; CREATE FUNCTION auth.uid() RETURNS text LANGUAGE sql AS $$ SELECT NULL::text $$;
        CREATE SCHEMA storage;
        CREATE TABLE storage.buckets(id text PRIMARY KEY,name text,public boolean,file_size_limit integer,allowed_mime_types text[]);
        CREATE TABLE storage.objects(id text); ALTER TABLE storage.objects ENABLE ROW LEVEL SECURITY;
        CREATE TABLE public.sproutshot_editor_migrations(version text PRIMARY KEY,checksum text NOT NULL);
        CREATE TABLE public.sproutshot_editor_accounts(owner_id text PRIMARY KEY);
        COMMIT;`);
    }
    const migrations = migrationsDirectory;
    for (const name of fs.readdirSync(migrations).filter(n => /^\d{14}_.+\.sql$/.test(n)).sort()) {
      const sql = fs.readFileSync(path.join(migrations, name), 'utf8');
      const checksum = createHash('sha256').update(sql).digest('hex');
      const applied = await db.query('SELECT checksum FROM public.sproutshot_editor_migrations WHERE version=$1', [name]);
      if (applied.rows.length) {
        if (applied.rows[0].checksum !== checksum) throw error('MIGRATION_CHANGED', 'An applied migration changed: ' + name);
        continue;
      }
      await db.exec('BEGIN');
      try {
        await db.exec(sql);
        await db.query('INSERT INTO public.sproutshot_editor_migrations(version,checksum) VALUES($1,$2)', [name, checksum]);
        await db.exec('COMMIT');
      } catch (cause) { await db.exec('ROLLBACK'); throw cause; }
    }
    return db;
  } catch (cause) { await db.close(); throw cause; }
}

async function createEditorBackend(options = {}) {
  const stateDirectory = options.stateDirectory || path.join(ROOT, 'Library/SproutshotEditorBackend');
  const db = await openDatabase(path.join(stateDirectory, 'database'), options.migrationsDirectory);
  // Editor-only override in this marked, isolated database; never shipped in the cloud function.
  if ((await db.query("SELECT to_regclass('public.sproutshot_rewarded_ad_config') AS name")).rows[0].name)
    await db.query('UPDATE public.sproutshot_rewarded_ad_config SET config=$1::jsonb WHERE id=1',
      [JSON.stringify({...require('../functions/sproutshot-cloud/rewarded-ad-config.json'),enabled:true})]);
  const token = randomBytes(32).toString('hex');
  const sessionFile = path.join(stateDirectory, 'session.json');
  let queue = Promise.resolve();
  let closing = false;
  let closeTask;
  function serialize(work) {
    const result = queue.then(work);
    queue = result.catch(() => undefined); // Keep the queue usable; the caller still receives the rejection.
    return result;
  }
  async function seedAccount(owner) {
    if ((await db.query('SELECT 1 FROM public.sproutshot_editor_accounts WHERE owner_id=$1', [owner])).rows.length) return;
    await db.exec('BEGIN');
    try {
      await db.query("SELECT public.sproutshot_economy($1,'load',NULL,'{}')", [owner]);
      await db.query('UPDATE public.sproutshot_players SET sprout_seed=10000,star_dew=10000,legacy_imported=true,revision=revision+1 WHERE owner_id=$1', [owner]);
      await db.query('INSERT INTO public.sproutshot_editor_accounts(owner_id) VALUES($1)', [owner]);
      await db.exec('COMMIT');
    } catch (cause) { await db.exec('ROLLBACK'); throw cause; }
  }
  const databaseAdapter = {
    async rpc(name, args) {
      if (name !== 'sproutshot_economy') return { error: new Error('Unsupported editor RPC') };
      await db.exec('BEGIN; SET LOCAL ROLE service_role;');
      try {
        const result = await db.query('SELECT public.sproutshot_economy($1,$2,$3::uuid,$4::jsonb) AS result',
          [args.p_owner, args.p_action, args.p_operation, JSON.stringify(args.p_payload)]);
        await db.exec('COMMIT');
        return { data: result.rows[0].result };
      } catch (cause) { await db.exec('ROLLBACK'); return { error: cause }; }
    }
  };
  async function grant(owner, input) {
    if (!UUID.test(input.operationId || '')) throw error('INVALID_REQUEST', 'A grant operation UUID is required.');
    await db.exec('BEGIN');
    try {
      const previous = await db.query('SELECT action,receipt FROM public.sproutshot_operations WHERE owner_id=$1 AND operation_id=$2::uuid', [owner,input.operationId]);
      let receipt;
      if (previous.rows.length) {
        if (previous.rows[0].action !== 'editorGrant') throw error('OPERATION_CONFLICT','Operation ID already used.');
        receipt = previous.rows[0].receipt;
      } else {
        receipt = { operationId:input.operationId,kind:'editorGrant',rewardSeeds:1000,rewardDew:1000 };
        await db.query('UPDATE public.sproutshot_players SET sprout_seed=sprout_seed+1000,star_dew=star_dew+1000,revision=revision+1 WHERE owner_id=$1', [owner]);
        await db.query("INSERT INTO public.sproutshot_operations(owner_id,operation_id,action,payload,receipt) VALUES($1,$2::uuid,'editorGrant','{}',$3::jsonb)", [owner,input.operationId,JSON.stringify(receipt)]);
      }
      const snapshot = await db.query('SELECT public.sproutshot_profile_snapshot($1) AS profile', [owner]);
      await db.exec('COMMIT');
      return { success:true,data:{profile:snapshot.rows[0].profile,receipt} };
    } catch (cause) { await db.exec('ROLLBACK'); throw cause; }
  }
  function authorized(request) {
    const actual = Buffer.from(request.headers.authorization || '');
    const expected = Buffer.from('Bearer ' + token);
    return actual.length === expected.length && timingSafeEqual(actual, expected);
  }
  function send(response, status, body) {
    response.writeHead(status, { 'Content-Type':'application/json; charset=utf-8','Cache-Control':'no-store' });
    response.end(JSON.stringify(body));
  }
  async function readBody(request) {
    const chunks=[]; let size=0;
    for await (const chunk of request) {
      size+=chunk.length;
      if (size>65536) throw error('REQUEST_TOO_LARGE','Request too large.');
      chunks.push(chunk);
    }
    return JSON.parse(Buffer.concat(chunks).toString('utf8'));
  }
  const server = http.createServer(async (request,response) => {
    if (!authorized(request) || request.headers.origin) return send(response,403,{success:false,code:'EDITOR_ACCESS_DENIED',message:'Local editor access required.'});
    if (request.url === '/health' && request.method === 'GET') return send(response,200,{ok:true,projectKey:PROJECT_KEY,protocol:1,pid:process.pid});
    if (request.url === '/shutdown' && request.method === 'POST') {
      send(response,200,{success:true});
      setImmediate(()=>close().catch(cause=>console.error('Editor backend shutdown failed: '+cause.message)));
      return;
    }
    if (request.method !== 'POST' || request.url !== '/request') return send(response,404,{success:false,code:'NOT_FOUND'});
    const account=request.headers['x-sproutshot-editor-player'];
    if (typeof account !== 'string' || !/^[a-zA-Z0-9_-]{1,40}$/.test(account)) return send(response,400,{success:false,code:'INVALID_TEST_ACCOUNT'});
    let input;
    try {
      input=await readBody(request);
      if (closing) throw error('EDITOR_STOPPING','Editor backend is stopping.');
      const result=await serialize(async () => {
        if(input.action==='initialize') return {success:true,data:{}};
        const owner=ownerFor(account);
        await seedAccount(owner);
        if(input.action==='editorGrant') return grant(owner,input);
        if(input.action!=='economy') throw error('UNSUPPORTED_EDITOR_ACTION','Only economy operations are available in the editor backend.');
        const service=createService({getContext:()=>({APPID:TEST_APP_ID,OPENID:'editor:'+PROJECT_KEY+':'+account}),
          getAppId:()=>TEST_APP_ID,db:databaseAdapter,storage:null});
        return service(input);
      });
      if(options.afterCommit && await options.afterCommit(input,result)) { response.destroy(); return; }
      send(response,200,{requestId:input.requestId,...result});
    } catch(cause) {
      send(response,400,{requestId:input?.requestId,success:false,code:cause.code || 'EDITOR_BACKEND_ERROR',
        message:cause.code ? cause.message : '本地测试服务请求失败，请查看本地测试日志。'});
    }
  });
  try {
    await new Promise((resolve,reject)=>{server.once('error',reject);server.listen(options.port || 0,'127.0.0.1',resolve);});
  } catch(cause) { await db.close(); throw cause; }
  const address='http://127.0.0.1:'+server.address().port;
  const session={protocol:1,endpoint:address,token,pid:process.pid,projectKey:PROJECT_KEY};
  try {
    fs.writeFileSync(sessionFile+'.tmp',JSON.stringify(session),{mode:0o600});
    fs.renameSync(sessionFile+'.tmp',sessionFile);
  } catch(cause) {
    await new Promise(resolve=>{server.close(resolve);server.closeIdleConnections?.();});
    await db.close();
    throw cause;
  }
  function close() {
    if (!closeTask) closeTask = (async () => {
      closing=true;
      await new Promise(resolve=>{server.close(resolve);server.closeIdleConnections?.();});
      await queue;
      await db.close();
      try {
        if(JSON.parse(fs.readFileSync(sessionFile,'utf8')).token===token) fs.unlinkSync(sessionFile);
      } catch(cause) { if(cause.code!=='ENOENT') throw cause; }
    })();
    return closeTask;
  }
  return {address,token,sessionFile,close};
}

if(require.main===module) {
  createEditorBackend().then(backend=>{
    console.log('Sproutshot editor backend ready at '+backend.address+' (isolated local PostgreSQL).');
    let stopped=false;
    const stop=async()=>{if(stopped)return;stopped=true;await backend.close();process.exit(0);};
    process.on('SIGINT',stop);process.on('SIGTERM',stop);
  }).catch(cause=>{console.error('Editor backend startup failed: '+cause.message);process.exitCode=1;});
}
module.exports={createEditorBackend,ownerFor};
