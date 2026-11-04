'use strict';
// Local build/management tool, never deployed with the function runtime.
const fs=require('node:fs'),path=require('node:path');
const {execFileSync}=require('node:child_process');
const {isDeepStrictEqual}=require('node:util');
const ROOT=path.resolve(__dirname,'../..');
function exportConfig(root=ROOT,{launch=false,migration=false}={}) {
  const output=path.join(root,'Temp/EconomyBalanceExport');fs.mkdirSync(output,{recursive:true});
  execFileSync('dotnet',[path.join(root,'LubanConfig/Tools/Luban/Luban.dll'),'-t','all','-d','json','--conf','luban.conf','-x','outputDataDir='+output],
    {cwd:path.join(root,'LubanConfig/MiniTemplate'),windowsHide:true,stdio:'pipe'});
  const read=name=>JSON.parse(fs.readFileSync(path.join(output,name+'.json'),'utf8'));
  const file=path.join(root,'cloudbase/functions/sproutshot-cloud/economy-config.json');
  const previous=JSON.parse(fs.readFileSync(file,'utf8'));
  const attributes=read('entity_tbattribute'),growth=read('entity_tbgrowth'),spawns=read('level_tbspawner');
  const hero=growth.find(r=>r.id===1);
  const prices=read('growth_tblevel').sort((a,b)=>a.level-b.level);
  if(!prices.length||prices.some((r,i)=>r.level!==i+1||!Number.isSafeInteger(r.levelup_cost)||r.levelup_cost<0))throw Error('Invalid growth levels/costs');
  const stages=read('level_tbreward').map(r=>({stage:r.level,seeds:r.sprout_seed,dew:r.star_dew,enemies:spawns.find(s=>s.level===r.level)?.limit_count})).sort((a,b)=>a.stage-b.stage);
  if(stages.length!==50||stages.some((s,i)=>s.stage!==i+1||![s.seeds,s.dew,s.enemies].every(v=>Number.isSafeInteger(v)&&v>=0)))throw Error('Expected 50 valid stage rewards/spawn quotas');
  const spirits=previous.spirits.map(old=>{
    const a=attributes.find(r=>r.id===old.id),g=growth.find(r=>r.id===old.id);
    if(!a||!g)throw Error('Missing spirit attribute/growth '+old.id);
    return {id:a.id,name:a.name,rarity:old.rarity,weight:old.weight,damage:g.damage,hp:g.hp,defense:g.defense,selfDamage:g.self_damage};
  });
  const levels=prices.map(r=>({level:r.level,cost:r.levelup_cost,damage:hero.damage*(r.level-1),hp:hero.hp*(r.level-1)}));
  levels.push({level:prices.length+1,cost:0,damage:hero.damage*prices.length,hp:hero.hp*prices.length,terminal:true});
  const config={...previous,stages,growth:levels,spirits};
  if(launch)Object.assign(config,{singleCost:100,tenCost:1000,hardPity:60,duplicateSeeds:20,rarityWeights:[6000,3000,1000]});
  const withoutVersion=value=>{const copy={...value};delete copy.version;return copy;};
  if(!isDeepStrictEqual(withoutVersion(previous),withoutVersion(config)))config.version=previous.version+1;
  fs.writeFileSync(file,JSON.stringify(config,null,2)+'\n');
  let migrationFile=null;
  if(migration){
    const {nextVersion}=require('./update-growth-config.cjs');
    const patch={singleCost:config.singleCost,tenCost:config.tenCost,hardPity:config.hardPity,duplicateSeeds:config.duplicateSeeds,
      rarityWeights:config.rarityWeights,spirits,stages,growth:levels};
    const json=JSON.stringify(patch).replaceAll("'","''");
    const sql=`-- Launch balance v1: configuration only; preserve wallets, levels, ownership and outstanding battle tickets.\nDO $balance$\nDECLARE previous jsonb; incoming jsonb := '${json}'::jsonb;\nBEGIN\n  SELECT config INTO previous FROM public.sproutshot_economy_config WHERE id=1 FOR UPDATE;\n  IF previous IS NULL THEN RAISE EXCEPTION 'CONFIG_MISSING'; END IF;\n  IF EXISTS(SELECT 1 FROM public.sproutshot_players WHERE player_level>${levels.length}) OR EXISTS(SELECT 1 FROM public.sproutshot_owned_spirits WHERE level>${levels.length}) THEN RAISE EXCEPTION 'GROWTH_LEVEL_REMOVAL_NOT_ALLOWED'; END IF;\n  IF (previous || incoming) IS DISTINCT FROM previous THEN\n    UPDATE public.sproutshot_economy_config SET config=previous || incoming || jsonb_build_object('version',(previous->>'version')::integer+1) WHERE id=1;\n  END IF;\nEND\n$balance$;\n`;
    migrationFile=path.join(root,'cloudbase/migrations',nextVersion(path.join(root,'cloudbase/migrations'))+'_launch_balance_v1.sql');
    fs.writeFileSync(migrationFile,sql,{flag:'wx'});
  }
  return {config,migration:migrationFile};
}
async function main(){
  const result=exportConfig(ROOT,{launch:process.argv.includes('--launch-v1'),migration:process.argv.includes('--write-migration')});
  let profilePreserved;
  if(process.argv.includes('--apply-local')){
    const management=require('./update-growth-config.cjs');
    const accountFile=path.join(ROOT,'UserSettings/SproutshotEditorTest.json');
    const account=fs.existsSync(accountFile)?JSON.parse(fs.readFileSync(accountFile,'utf8')).account:'player-a';
    const old=management.readSession(ROOT);let before;
    if(old&&await management.health(old))before=await management.load(old,account);
    const session=await management.restartBackend(ROOT);const after=await management.load(session,account);
    if(before&&!isDeepStrictEqual(before.profile,after.profile))throw Error('Local player profile changed');
    if(after.catalog.growth.length!==result.config.growth.length)throw Error('Local migration not applied');
    profilePreserved=!before||isDeepStrictEqual(before.profile,after.profile);
  }
  console.log(JSON.stringify({success:true,levels:result.config.growth.length,stages:result.config.stages.length,migration:result.migration,profilePreserved}));
}
module.exports={exportConfig};
if(require.main===module)main().catch(error=>{console.error(error.message);process.exitCode=1;});
