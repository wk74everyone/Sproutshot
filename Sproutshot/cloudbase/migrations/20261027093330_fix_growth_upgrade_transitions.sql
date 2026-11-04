-- Row N pays for N -> N+1; the final price row remains usable.
CREATE OR REPLACE FUNCTION public.sproutshot_economy(p_owner text, p_action text, p_operation uuid, p_payload jsonb)
RETURNS jsonb LANGUAGE plpgsql SECURITY INVOKER SET search_path = pg_catalog, public AS $fn$
DECLARE
  player public.sproutshot_players%ROWTYPE;
  old_op public.sproutshot_operations%ROWTYPE;
  battle public.sproutshot_battles%ROWTYPE;
  cfg jsonb; v_receipt jsonb := '{}'::jsonb; row_cfg jsonb; spirit jsonb;
  results jsonb := '[]'::jsonb; item jsonb;
  highest integer; mask integer; stage_id integer; count_draw integer; cost integer;
  pity_misses integer; rarity integer; roll integer; total_weight integer; n integer; i integer;
  selected_spirit integer; compensation integer := 0; gained_seed integer := 0; gained_dew integer := 0;
  first_clear boolean; is_new boolean; pity_hit boolean; claimed_owner text; levels integer;
BEGIN
  IF p_owner IS NULL OR p_owner !~ '^[a-f0-9]{64}$' OR p_payload IS NULL OR jsonb_typeof(p_payload)<>'object'
    OR p_action IS NULL OR p_action NOT IN ('load','migrate','reveal','beginBattle','settleBattle',
      'summon','history','upgradePlayer','upgradeSpirit','equipSpirit') THEN
    RAISE EXCEPTION 'INVALID_REQUEST' USING ERRCODE='22023';
  END IF;
  INSERT INTO public.sproutshot_players(owner_id) VALUES(p_owner) ON CONFLICT DO NOTHING;
  SELECT * INTO player FROM public.sproutshot_players WHERE owner_id=p_owner FOR UPDATE;
  SELECT config INTO cfg FROM public.sproutshot_economy_config WHERE id=1;
  IF cfg IS NULL THEN RAISE EXCEPTION 'CONFIG_MISSING'; END IF;
  IF p_action IN ('load','history') THEN
    RETURN jsonb_build_object('profile',public.sproutshot_profile_snapshot(p_owner), 'catalog',cfg,
      'history',COALESCE((SELECT jsonb_agg(x.receipt) FROM (SELECT o.receipt FROM public.sproutshot_operations o
        WHERE o.owner_id=p_owner AND o.action='summon' ORDER BY o.created_at DESC,o.operation_id DESC LIMIT 20) x),'[]'::jsonb));
  END IF;
  IF p_operation IS NULL THEN RAISE EXCEPTION 'OPERATION_REQUIRED'; END IF;
  SELECT * INTO old_op FROM public.sproutshot_operations WHERE owner_id=p_owner AND operation_id=p_operation;
  IF FOUND THEN
    IF old_op.action<>p_action OR old_op.payload<>p_payload THEN RAISE EXCEPTION 'OPERATION_CONFLICT'; END IF;
    RETURN jsonb_build_object('profile',public.sproutshot_profile_snapshot(p_owner),'receipt',old_op.receipt,'catalog',cfg);
  END IF;
  SELECT COALESCE(max(highest_cleared_stage),0),COALESCE(max(revealed_map_mask),0) INTO highest,mask
    FROM public.sproutshot_stage_progress WHERE owner_id=p_owner;
  v_receipt := jsonb_build_object('operationId',p_operation,'kind',p_action,'configVersion',cfg->'version');

  IF p_action='migrate' THEN
    IF NOT player.legacy_imported THEN
      INSERT INTO public.sproutshot_legacy_claims(install_id,owner_id)
        VALUES((p_payload->>'installId')::uuid,p_owner) ON CONFLICT DO NOTHING;
      SELECT owner_id INTO claimed_owner FROM public.sproutshot_legacy_claims WHERE install_id=(p_payload->>'installId')::uuid;
      IF claimed_owner=p_owner THEN
        highest:=GREATEST(highest,LEAST(50,GREATEST(0,(p_payload->>'highest')::integer)));
        mask:=(mask | COALESCE((p_payload->>'revealed')::integer,0)) & ((1<<LEAST(5,highest/10+1))-1);
      END IF;
      INSERT INTO public.sproutshot_stage_progress(owner_id,highest_cleared_stage,revealed_map_mask)
        VALUES(p_owner,highest,mask) ON CONFLICT(owner_id) DO UPDATE SET
        highest_cleared_stage=EXCLUDED.highest_cleared_stage,revealed_map_mask=EXCLUDED.revealed_map_mask,updated_at=now();
      UPDATE public.sproutshot_players SET legacy_imported=true WHERE owner_id=p_owner;
    END IF;

  ELSIF p_action='reveal' THEN
    n:=(p_payload->>'map')::integer;
    IF n IS NULL OR n NOT BETWEEN 2 AND 5 OR highest<(n-1)*10 THEN RAISE EXCEPTION 'MAP_LOCKED'; END IF;
    UPDATE public.sproutshot_stage_progress SET revealed_map_mask=revealed_map_mask | (1<<(n-1)),updated_at=now() WHERE owner_id=p_owner;

  ELSIF p_action='beginBattle' THEN
    stage_id:=(p_payload->>'stage')::integer;
    IF stage_id IS NULL OR stage_id NOT BETWEEN 1 AND 50 OR stage_id>highest+1 THEN RAISE EXCEPTION 'STAGE_LOCKED'; END IF;
    SELECT value INTO row_cfg FROM jsonb_array_elements(cfg->'stages') WHERE (value->>'stage')::integer=stage_id;
    IF row_cfg IS NULL THEN RAISE EXCEPTION 'CONFIG_MISSING'; END IF;
    UPDATE public.sproutshot_battles SET closed_at=clock_timestamp(),receipt=jsonb_build_object('abandoned',true)
      WHERE owner_id=p_owner AND closed_at IS NULL;
    INSERT INTO public.sproutshot_battles(owner_id,stage,reward) VALUES(p_owner,stage_id,row_cfg) RETURNING * INTO battle;
    v_receipt:=v_receipt || jsonb_build_object('battleId',battle.battle_id,'stage',stage_id);

  ELSIF p_action='settleBattle' THEN
    SELECT * INTO battle FROM public.sproutshot_battles WHERE battle_id=(p_payload->>'battleId')::uuid AND owner_id=p_owner FOR UPDATE;
    IF NOT FOUND THEN RAISE EXCEPTION 'BATTLE_NOT_FOUND'; END IF;
    IF battle.closed_at IS NOT NULL THEN
      IF battle.receipt ? 'abandoned' THEN RAISE EXCEPTION 'BATTLE_CLOSED'; END IF;
      v_receipt:=battle.receipt;
    ELSE
      IF jsonb_typeof(p_payload->'victory') IS DISTINCT FROM 'boolean' THEN RAISE EXCEPTION 'INVALID_REQUEST'; END IF;
      IF (p_payload->>'victory')::boolean THEN
        -- Server validates ticket, unlocked stage, expiry and expected defeat count. Client-side
        -- combat is not a cryptographic proof; replay/anti-cheat is a separate future layer.
        IF clock_timestamp()>battle.started_at+interval '6 hours' THEN RAISE EXCEPTION 'BATTLE_EXPIRED'; END IF;
        IF (p_payload->>'defeated')::integer IS DISTINCT FROM (battle.reward->>'enemies')::integer
          OR clock_timestamp()<battle.started_at+interval '3 seconds' THEN RAISE EXCEPTION 'INVALID_BATTLE_RESULT'; END IF;
        first_clear:=battle.stage>highest;
        gained_seed:=(battle.reward->>'seeds')::integer;
        gained_dew:=(battle.reward->>'dew')::integer;
        IF NOT first_clear THEN gained_seed:=gained_seed/2; gained_dew:=gained_dew/2; END IF;
        UPDATE public.sproutshot_players SET sprout_seed=sprout_seed+gained_seed,star_dew=star_dew+gained_dew WHERE owner_id=p_owner;
        INSERT INTO public.sproutshot_stage_progress(owner_id,highest_cleared_stage,revealed_map_mask)
          VALUES(p_owner,GREATEST(highest,battle.stage),mask) ON CONFLICT(owner_id) DO UPDATE SET
          highest_cleared_stage=EXCLUDED.highest_cleared_stage,updated_at=now();
      END IF;
      v_receipt:=v_receipt || jsonb_build_object('battleId',battle.battle_id,'stage',battle.stage,'victory',p_payload->'victory',
        'rewardSeeds',gained_seed,'rewardDew',gained_dew,'firstClear',COALESCE(first_clear,false));
      UPDATE public.sproutshot_battles SET closed_at=clock_timestamp(),receipt=v_receipt WHERE battle_id=battle.battle_id;
    END IF;

  ELSIF p_action='summon' THEN
    count_draw:=(p_payload->>'count')::integer;
    IF p_payload->>'pool' IS DISTINCT FROM cfg->>'pool' OR count_draw IS NULL OR count_draw NOT IN(1,10) THEN RAISE EXCEPTION 'INVALID_POOL'; END IF;
    cost:=(CASE WHEN count_draw=1 THEN cfg->>'singleCost' ELSE cfg->>'tenCost' END)::integer;
    IF player.star_dew<cost THEN RAISE EXCEPTION 'INSUFFICIENT_DEW'; END IF;
    INSERT INTO public.sproutshot_pity(owner_id,pool_id) VALUES(p_owner,cfg->>'pool') ON CONFLICT DO NOTHING;
    SELECT p.misses INTO pity_misses FROM public.sproutshot_pity p WHERE p.owner_id=p_owner AND p.pool_id=cfg->>'pool';
    FOR i IN 1..count_draw LOOP
      pity_hit:=pity_misses>=(cfg->>'hardPity')::integer-1;
      roll:=floor(random()*10000)::integer;
      rarity:=CASE WHEN pity_hit THEN 2 WHEN roll<(cfg->'rarityWeights'->>0)::integer THEN 0
        WHEN roll<(cfg->'rarityWeights'->>0)::integer+(cfg->'rarityWeights'->>1)::integer THEN 1 ELSE 2 END;
      SELECT sum((value->>'weight')::integer) INTO total_weight FROM jsonb_array_elements(cfg->'spirits') WHERE (value->>'rarity')::integer=rarity;
      IF total_weight IS NULL OR total_weight<=0 THEN RAISE EXCEPTION 'CONFIG_MISSING'; END IF;
      roll:=floor(random()*total_weight)::integer;
      FOR spirit IN SELECT value FROM jsonb_array_elements(cfg->'spirits') WHERE (value->>'rarity')::integer=rarity ORDER BY (value->>'id')::integer LOOP
        roll:=roll-(spirit->>'weight')::integer;
        EXIT WHEN roll<0;
      END LOOP;
      selected_spirit:=(spirit->>'id')::integer;
      INSERT INTO public.sproutshot_owned_spirits(owner_id,spirit_id) VALUES(p_owner,selected_spirit) ON CONFLICT DO NOTHING;
      GET DIAGNOSTICS n=ROW_COUNT;
      is_new:=n=1;
      compensation:=CASE WHEN is_new THEN 0 ELSE (cfg->>'duplicateSeeds')::integer END;
      gained_seed:=gained_seed+compensation;
      pity_misses:=CASE WHEN rarity=2 THEN 0 ELSE pity_misses+1 END;
      item:=jsonb_build_object('spiritId',selected_spirit,'rarity',rarity,'spriteAddress',spirit->>'sprite',
        'name',spirit->>'name','isNew',is_new,'duplicateSeedAmount',compensation,'isHardPity',pity_hit);
      results:=results || jsonb_build_array(item);
    END LOOP;
    UPDATE public.sproutshot_players SET star_dew=star_dew-cost,sprout_seed=sprout_seed+gained_seed WHERE owner_id=p_owner;
    UPDATE public.sproutshot_pity AS p SET misses=pity_misses WHERE p.owner_id=p_owner AND p.pool_id=cfg->>'pool';
    v_receipt:=v_receipt || jsonb_build_object('totalCost',cost,'rewardSeeds',gained_seed,'results',results,'pityAfter',pity_misses);

  ELSIF p_action IN ('upgradePlayer','upgradeSpirit') THEN
    selected_spirit:=COALESCE((p_payload->>'spiritId')::integer,0);
    levels:=player.player_level;
    IF p_action='upgradeSpirit' THEN
      SELECT s.level INTO levels FROM public.sproutshot_owned_spirits s WHERE s.owner_id=p_owner AND s.spirit_id=selected_spirit;
      IF levels IS NULL THEN RAISE EXCEPTION 'SPIRIT_NOT_OWNED'; END IF;
    END IF;
    SELECT value INTO row_cfg FROM jsonb_array_elements(cfg->'growth') WHERE (value->>'level')::integer=levels;
    IF row_cfg IS NULL OR COALESCE((row_cfg->>'terminal')::boolean,false) THEN RAISE EXCEPTION 'MAX_LEVEL'; END IF;
    cost:=(row_cfg->>'cost')::integer;
    IF cost IS NULL OR cost<0 THEN RAISE EXCEPTION 'CONFIG_MISSING'; END IF;
    IF player.sprout_seed<cost THEN RAISE EXCEPTION 'INSUFFICIENT_SEEDS'; END IF;
    UPDATE public.sproutshot_players SET sprout_seed=sprout_seed-cost,
      player_level=CASE WHEN p_action='upgradePlayer' THEN levels+1 ELSE player_level END WHERE owner_id=p_owner;
    IF p_action='upgradeSpirit' THEN UPDATE public.sproutshot_owned_spirits SET level=levels+1 WHERE owner_id=p_owner AND spirit_id=selected_spirit; END IF;
    v_receipt:=v_receipt || jsonb_build_object('level',levels+1,'totalCost',cost);

  ELSIF p_action='equipSpirit' THEN
    selected_spirit:=(p_payload->>'spiritId')::integer;
    IF selected_spirit IS NULL OR (selected_spirit<>0 AND NOT EXISTS(SELECT 1 FROM public.sproutshot_owned_spirits s WHERE s.owner_id=p_owner AND s.spirit_id=selected_spirit)) THEN RAISE EXCEPTION 'SPIRIT_NOT_OWNED'; END IF;
    UPDATE public.sproutshot_players SET equipped_spirit=selected_spirit WHERE owner_id=p_owner;
  END IF;
  UPDATE public.sproutshot_players SET revision=revision+1,updated_at=now() WHERE owner_id=p_owner;
  INSERT INTO public.sproutshot_operations(owner_id,operation_id,action,payload,receipt) VALUES(p_owner,p_operation,p_action,p_payload,v_receipt);
  RETURN jsonb_build_object('profile',public.sproutshot_profile_snapshot(p_owner),'receipt',v_receipt,'catalog',cfg);
END
$fn$;
REVOKE ALL ON FUNCTION public.sproutshot_profile_snapshot(text) FROM PUBLIC,anon,authenticated;
REVOKE ALL ON FUNCTION public.sproutshot_economy(text,text,uuid,jsonb) FROM PUBLIC,anon,authenticated;
GRANT EXECUTE ON FUNCTION public.sproutshot_profile_snapshot(text) TO service_role;
GRANT EXECUTE ON FUNCTION public.sproutshot_economy(text,text,uuid,jsonb) TO service_role;

-- Keep the last attainable level's battle attributes even though it has no upgrade cost.
DO $terminal_growth$
DECLARE
  previous jsonb; updated_growth jsonb;
  last_growth jsonb; prior_growth jsonb; terminal_growth jsonb;
BEGIN
  SELECT config INTO previous FROM public.sproutshot_economy_config WHERE id=1 FOR UPDATE;
  IF previous IS NULL THEN RAISE EXCEPTION 'CONFIG_MISSING'; END IF;
  SELECT jsonb_agg(value - 'terminal' ORDER BY (value->>'level')::integer) INTO updated_growth
    FROM jsonb_array_elements(previous->'growth') WHERE NOT COALESCE((value->>'terminal')::boolean,false);
  IF updated_growth IS NULL THEN RAISE EXCEPTION 'CONFIG_MISSING'; END IF;

  SELECT value INTO last_growth FROM jsonb_array_elements(updated_growth) ORDER BY (value->>'level')::integer DESC LIMIT 1;
  SELECT value INTO prior_growth FROM jsonb_array_elements(updated_growth) WHERE (value->>'level')::integer=(last_growth->>'level')::integer-1;
  SELECT value INTO terminal_growth FROM jsonb_array_elements(previous->'growth')
    WHERE COALESCE((value->>'terminal')::boolean,false) AND (value->>'level')::integer=(last_growth->>'level')::integer+1;
  terminal_growth := COALESCE(terminal_growth, last_growth || jsonb_build_object(
    'damage',COALESCE((last_growth->>'damage')::integer,0)+GREATEST(0,COALESCE((last_growth->>'damage')::integer,0)-COALESCE((prior_growth->>'damage')::integer,(last_growth->>'damage')::integer-1,-1)),
    'hp',COALESCE((last_growth->>'hp')::integer,0)+GREATEST(0,COALESCE((last_growth->>'hp')::integer,0)-COALESCE((prior_growth->>'hp')::integer,(last_growth->>'hp')::integer-5,-5))))
    || jsonb_build_object('level',(last_growth->>'level')::integer+1,'cost',0,'terminal',true);
  updated_growth := updated_growth || jsonb_build_array(terminal_growth);
  IF previous->'growth' IS DISTINCT FROM updated_growth THEN
    UPDATE public.sproutshot_economy_config SET config=jsonb_set(previous,'{growth}',updated_growth)
      || jsonb_build_object('version',(previous->>'version')::integer+1) WHERE id=1;
  END IF;
END
$terminal_growth$;
