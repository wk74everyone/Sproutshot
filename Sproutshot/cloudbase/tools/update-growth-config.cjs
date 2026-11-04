'use strict';
// Editor-only management tool. Never deployed with sproutshot-cloud.
const fs = require('node:fs');
const path = require('node:path');
const {execFile, spawn} = require('node:child_process');
const {promisify, isDeepStrictEqual} = require('node:util');
const {createHash} = require('node:crypto');
const execute = promisify(execFile);
const ROOT = path.resolve(__dirname, '../..');
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const hash = value => createHash('sha256').update(value).digest('hex');

function validateRows(input) {
  if (!Array.isArray(input) || !input.length || input.length > 10000) throw Error('成长表需要包含从 1 级开始的连续等级。');
  const rows = input.map(row => ({level:row.level, cost:row.cost ?? row.levelup_cost})).sort((a,b)=>a.level-b.level);
  rows.forEach((row,i)=>{
    if (!Number.isSafeInteger(row.level) || row.level !== i+1 || !Number.isSafeInteger(row.cost) || row.cost < 0 || row.cost > 2000000000)
      throw Error('成长表等级必须从 1 连续递增且不重复，消耗必须是 0～2000000000 的整数。');
  });
  return rows;
}
// Append battle attributes for the attainable final level, without another upgrade price.
function terminalRowSQL() {
  return `
  SELECT value INTO last_growth FROM jsonb_array_elements(updated_growth) ORDER BY (value->>'level')::integer DESC LIMIT 1;
  SELECT value INTO prior_growth FROM jsonb_array_elements(updated_growth) WHERE (value->>'level')::integer=(last_growth->>'level')::integer-1;
  SELECT value INTO terminal_growth FROM jsonb_array_elements(previous->'growth')
    WHERE COALESCE((value->>'terminal')::boolean,false) AND (value->>'level')::integer=(last_growth->>'level')::integer+1;
  terminal_growth := COALESCE(terminal_growth, last_growth || jsonb_build_object(
    'damage',COALESCE((last_growth->>'damage')::integer,0)+GREATEST(0,COALESCE((last_growth->>'damage')::integer,0)-COALESCE((prior_growth->>'damage')::integer,(last_growth->>'damage')::integer-1,-1)),
    'hp',COALESCE((last_growth->>'hp')::integer,0)+GREATEST(0,COALESCE((last_growth->>'hp')::integer,0)-COALESCE((prior_growth->>'hp')::integer,(last_growth->>'hp')::integer-5,-5))))
    || jsonb_build_object('level',(last_growth->>'level')::integer+1,'cost',0,'terminal',true);
  updated_growth := updated_growth || jsonb_build_array(terminal_growth);`;
}
function migrationSQL(input) {
  const rows = validateRows(input);
  return `-- growth_level.xlsx level/cost export. Preserve spirit configuration and existing growth attributes.
DO $growth_sync$
DECLARE
  input_rows jsonb := '${JSON.stringify(rows)}'::jsonb;
  previous jsonb;
  updated_growth jsonb;
  last_growth jsonb; prior_growth jsonb; terminal_growth jsonb;
BEGIN
  SELECT config INTO previous FROM public.sproutshot_economy_config WHERE id=1 FOR UPDATE;
  IF previous IS NULL THEN RAISE EXCEPTION 'CONFIG_MISSING'; END IF;
  IF EXISTS (SELECT 1 FROM public.sproutshot_players WHERE player_level>${rows.length+1})
    OR EXISTS (SELECT 1 FROM public.sproutshot_owned_spirits WHERE level>${rows.length+1})
    OR EXISTS (SELECT 1 FROM jsonb_array_elements(previous->'growth') g WHERE NOT COALESCE((g->>'terminal')::boolean,false) AND (g->>'level')::integer>${rows.length})
  THEN RAISE EXCEPTION 'GROWTH_LEVEL_REMOVAL_NOT_ALLOWED'; END IF;
  SELECT jsonb_agg(
    COALESCE((SELECT old FROM jsonb_array_elements(previous->'growth') old
      WHERE (old->>'level')::integer=(incoming->>'level')::integer),
      jsonb_build_object('damage',(incoming->>'level')::integer-1,'hp',((incoming->>'level')::integer-1)*5))
    - 'terminal' || incoming ORDER BY (incoming->>'level')::integer)
  INTO updated_growth FROM jsonb_array_elements(input_rows) incoming;
${terminalRowSQL()}
  IF previous->'growth' IS DISTINCT FROM updated_growth THEN
    UPDATE public.sproutshot_economy_config SET config=jsonb_set(previous,'{growth}',updated_growth)
      || jsonb_build_object('version',(previous->>'version')::integer+1) WHERE id=1;
  END IF;
END
$growth_sync$;
`;
}
function nextVersion(directory, now = new Date()) {
  const versions = fs.readdirSync(directory).filter(n=>/^\d{14}_.+\.sql$/.test(n)).map(n=>n.slice(0,14)).sort();
  const latest = versions.at(-1);
  const latestDate = latest ? Date.UTC(+latest.slice(0,4), +latest.slice(4,6)-1, +latest.slice(6,8), +latest.slice(8,10), +latest.slice(10,12), +latest.slice(12,14)) : 0;
  return new Date(Math.max(now.getTime(), latestDate+1000)).toISOString().replace(/\D/g,'').slice(0,14);
}
function publishMigration(root, rows, now) {
  const directory = path.join(root,'cloudbase/migrations');
  const file = path.join(directory,nextVersion(directory,now)+'_update_growth_costs.sql');
  fs.writeFileSync(file,migrationSQL(rows),{encoding:'utf8',flag:'wx'});
  return file;
}
function alive(pid) {
  if (!Number.isSafeInteger(pid) || pid<1) return false;
  try {process.kill(pid,0); return true;} catch(cause) {if(cause.code==='ESRCH') return false; throw cause;}
}
function readSession(root) {
  const file = path.join(root,'Library/SproutshotEditorBackend/session.json');
  if (!fs.existsSync(file)) return null;
  const session = JSON.parse(fs.readFileSync(file,'utf8'));
  const uri = new URL(session.endpoint);
  if (session.protocol!==1 || session.projectKey!==hash(root.toLowerCase()) || !/^[a-f0-9]{64}$/.test(session.token || '') ||
      uri.protocol!=='http:' || uri.hostname!=='127.0.0.1' || uri.pathname!=='/' || uri.username || uri.password || !Number.isSafeInteger(session.pid))
    throw Error('本地服务会话信息无效，已停止更新。');
  return session;
}
async function health(session) {
  if (!session || !alive(session.pid)) return null;
  try {
    const response = await fetch(session.endpoint+'/health',{headers:{Authorization:'Bearer '+session.token},signal:AbortSignal.timeout(3000)});
    const data = await response.json();
    return response.ok && data.ok && data.protocol===1 && data.projectKey===session.projectKey && (!data.pid || data.pid===session.pid) ? data : null;
  } catch {return null;}
}
async function load(session, account) {
  const response = await fetch(session.endpoint+'/request',{method:'POST',signal:AbortSignal.timeout(10000),
    headers:{Authorization:'Bearer '+session.token,'Content-Type':'application/json','X-Sproutshot-Editor-Player':account},
    body:JSON.stringify({requestId:1,action:'economy',economyAction:'load'})});
  const reply = await response.json();
  if (!response.ok || !reply.success) throw Error(reply.message || '读取测试账号失败。');
  return reply.data;
}
async function stopBackend(root, session) {
  if (!session || !alive(session.pid)) return;
  if (!await health(session)) throw Error('无法确认旧进程属于当前项目，未结束任何进程。');
  const response = await fetch(session.endpoint+'/shutdown',{method:'POST',headers:{Authorization:'Bearer '+session.token},signal:AbortSignal.timeout(5000)});
  if (response.status===404) {
    // Compatibility with the first editor backend, which did not expose graceful shutdown.
    // Check both authenticated project identity and the exact script path before stopping it.
    const script = path.join(root,'cloudbase/tests/editor-backend.cjs');
    let command;
    if (process.platform==='win32') {
      const result = await execute('powershell.exe',['-NoProfile','-Command',
        `Get-CimInstance Win32_Process -Filter 'ProcessId=${session.pid}' | Select-Object Name,CommandLine | ConvertTo-Json -Compress`],{windowsHide:true,timeout:10000});
      const info = JSON.parse(result.stdout); if (info.Name!=='node.exe') throw Error('旧服务进程类型不符。'); command=info.CommandLine;
    } else command=fs.readFileSync('/proc/'+session.pid+'/cmdline','utf8');
    if (!command.replaceAll('\\','/').toLowerCase().includes(script.replaceAll('\\','/').toLowerCase())) throw Error('旧进程路径不符，未结束进程。');
    process.kill(session.pid,'SIGTERM');
  } else if (!response.ok) throw Error('本地服务拒绝重启。');
  const deadline = Date.now()+15000;
  while (alive(session.pid) && Date.now()<deadline) await sleep(100);
  if (alive(session.pid)) throw Error('本地测试后端尚未退出，请稍后重试。');
}
async function startBackend(root) {
  const directory = path.join(root,'Library/SproutshotEditorBackend'); fs.mkdirSync(directory,{recursive:true});
  const log = fs.openSync(path.join(directory,'backend.log'),'a');
  let child;
  try {
    child=spawn(process.execPath,[path.join(root,'cloudbase/tests/editor-backend.cjs')],
      {cwd:root,detached:true,windowsHide:true,stdio:['ignore',log,log]});
  } finally {fs.closeSync(log);}
  let failure; child.on('error',cause=>{failure=cause;}); child.unref();
  const deadline=Date.now()+25000;
  while (Date.now()<deadline) {
    if (failure) throw failure;
    if (child.exitCode!==null) throw Error('本地服务启动失败，请查看 Library/SproutshotEditorBackend/backend.log。');
    const session=readSession(root);
    if (session?.pid===child.pid && await health(session)) return session;
    await sleep(150);
  }
  throw Error('本地服务启动超时，请查看 Library/SproutshotEditorBackend/backend.log。');
}
async function restartBackend(root) {
  const old = readSession(root);
  if (old && alive(old.pid)) await stopBackend(root,old);
  return startBackend(root);
}
async function exportRows(root) {
  const source = path.join(root,'LubanConfig/MiniTemplate/Data/growth_level.xlsx');
  const before = hash(fs.readFileSync(source));
  const directory = path.join(root,'Temp/SproutshotGrowthExport'); fs.mkdirSync(directory,{recursive:true});
  const args=[path.join(root,'LubanConfig/Tools/Luban/Luban.dll'),'-t','all','-d','json','--conf','luban.conf','-x','outputDataDir='+directory];
  const result=await execute('dotnet',args,{cwd:path.join(root,'LubanConfig/MiniTemplate'),windowsHide:true,timeout:60000,maxBuffer:4*1024*1024});
  fs.writeFileSync(path.join(directory,'export.log'),result.stdout+result.stderr);
  if (before!==hash(fs.readFileSync(source))) throw Error('导出期间成长表发生变化，请保存后重新更新。');
  return validateRows(JSON.parse(fs.readFileSync(path.join(directory,'growth_tblevel.json'),'utf8')));
}
async function updateLocalGrowth(root = ROOT, options = {}) {
  root=path.resolve(root);
  const directory=path.join(root,'Library/SproutshotEditorBackend'); fs.mkdirSync(directory,{recursive:true});
  const lockFile=path.join(directory,'growth-update.lock');
  if (fs.existsSync(lockFile)) {
    const pid=Number(fs.readFileSync(lockFile,'utf8'));
    if (!Number.isSafeInteger(pid) || alive(pid)) throw Error('已有成长配置更新正在运行，请等待完成。');
    fs.unlinkSync(lockFile);
  }
  fs.writeFileSync(lockFile,String(process.pid),{flag:'wx'});
  try {
    const settingsFile=path.join(root,'UserSettings/SproutshotEditorTest.json');
    const account=options.account || (fs.existsSync(settingsFile) ? JSON.parse(fs.readFileSync(settingsFile,'utf8')).account : 'player-a');
    if (!/^[a-zA-Z0-9_-]{1,40}$/.test(account || '')) throw Error('测试账号无效。');
    const rows=options.restartOnly ? null : await exportRows(root);
    let before; const existing=readSession(root);
    if (await health(existing)) before=await load(existing,account);
    let session=await restartBackend(root); // Also applies any earlier pending migration, including spirit fields.
    const current=await load(session,account);
    // Older backends do not return the profession yet; the migration defaults it to archer.
    const expectedProfile=before && Object.hasOwn(before.profile,'equippedProfessionId') ? before.profile :
      before ? {...before.profile,equippedProfessionId:1} : null;
    if (before && !isDeepStrictEqual(expectedProfile,current.profile)) throw Error('测试账号存档在重启前后发生变化，请查看日志。');
    let migration=null;
    if (rows) {
      if (current.catalog.growth.some(row=>!row.terminal && row.level>rows.length)) throw Error('成长表不能删除已有等级，请保留现有等级后再更新。');
      const costs=current.catalog.growth.filter(row=>!row.terminal).map(({level,cost})=>({level,cost})).sort((a,b)=>a.level-b.level);
      if (!isDeepStrictEqual(costs,rows)) {migration=publishMigration(root,rows);session=await restartBackend(root);}
    }
    const after=await load(session,account);
    if (!isDeepStrictEqual(current.profile,after.profile)) throw Error('配置更新后玩家存档发生变化，请查看日志。');
    const oldOther={...current.catalog}; const newOther={...after.catalog};
    delete oldOther.growth;delete newOther.growth;delete oldOther.version;delete newOther.version;
    if (!isDeepStrictEqual(oldOther,newOther)) throw Error('检测到成长费用以外的配置变化，请查看日志。');
    if (rows && !isDeepStrictEqual(after.catalog.growth.filter(row=>!row.terminal).map(({level,cost})=>({level,cost})).sort((a,b)=>a.level-b.level),rows)) throw Error('数据库费用与成长表不一致。');
    const report={success:true,restarted:true,changed:!!migration,migration:migration ? path.relative(root,migration).replaceAll('\\','/') : null,
      version:after.catalog.version,levels:after.catalog.growth.length,growth:after.catalog.growth,profilePreserved:true,spiritConfigPreserved:true};
    const reportDir=path.join(root,'Temp/SproutshotGrowthExport');fs.mkdirSync(reportDir,{recursive:true});
    fs.writeFileSync(path.join(reportDir,'last-update.json'),JSON.stringify(report,null,2));
    return report;
  } finally {fs.unlinkSync(lockFile);}
}
module.exports={validateRows,migrationSQL,terminalRowSQL,nextVersion,publishMigration,updateLocalGrowth,readSession,health,load,restartBackend};
if (require.main===module) updateLocalGrowth(ROOT,{restartOnly:process.argv.includes('--restart-only')})
  .then(result=>console.log(JSON.stringify(result))).catch(cause=>{console.error('成长配置更新失败：'+cause.message);process.exitCode=1;});
