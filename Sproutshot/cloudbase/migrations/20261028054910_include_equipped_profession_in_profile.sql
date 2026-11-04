-- Return the persisted profession with the existing server-owned player profile.
-- CREATE OR REPLACE retains the function's existing service_role-only grants.
CREATE OR REPLACE FUNCTION public.sproutshot_profile_snapshot(p_owner text)
RETURNS jsonb LANGUAGE sql SECURITY INVOKER SET search_path = pg_catalog, public AS $fn$
 SELECT jsonb_build_object(
   'playerId', p.owner_id, 'sproutSeed', p.sprout_seed, 'starDew', p.star_dew,
   'playerLevel', p.player_level, 'equippedProfessionId', p.equipped_profession,
   'equippedSpiritId', p.equipped_spirit,
   'legacyImported', p.legacy_imported, 'revision', p.revision,
   'highestClearedStage', COALESCE(g.highest_cleared_stage,0),
   'revealedMapMask', COALESCE(g.revealed_map_mask,0),
   'ownedSpirits', COALESCE((SELECT jsonb_agg(jsonb_build_object('spiritId',s.spirit_id,'level',s.level)
      ORDER BY s.spirit_id) FROM public.sproutshot_owned_spirits s WHERE s.owner_id=p_owner),'[]'::jsonb),
   'pityCount', COALESCE((SELECT misses FROM public.sproutshot_pity WHERE owner_id=p_owner AND pool_id='standard'),0),
   'lastSummon', (SELECT receipt FROM public.sproutshot_operations WHERE owner_id=p_owner AND action='summon'
      ORDER BY created_at DESC, operation_id DESC LIMIT 1)
 ) FROM public.sproutshot_players p LEFT JOIN public.sproutshot_stage_progress g USING(owner_id)
 WHERE p.owner_id=p_owner
$fn$;
