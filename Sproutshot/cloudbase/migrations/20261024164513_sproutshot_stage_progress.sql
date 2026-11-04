-- Player progress only. Currency, purchases and reward authority are outside this table.
CREATE TABLE public.sproutshot_stage_progress (
    owner_id text PRIMARY KEY DEFAULT auth.uid(),
    highest_cleared_stage integer NOT NULL DEFAULT 0 CHECK (highest_cleared_stage BETWEEN 0 AND 50),
    revealed_map_mask integer NOT NULL DEFAULT 0 CHECK (revealed_map_mask BETWEEN 0 AND 31),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT sproutshot_revealed_maps_unlocked CHECK (
        (revealed_map_mask & ((1 << LEAST(5, highest_cleared_stage / 10 + 1)) - 1)) = revealed_map_mask
    )
);

ALTER TABLE public.sproutshot_stage_progress ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON public.sproutshot_stage_progress FROM PUBLIC, anon;
GRANT SELECT, INSERT, UPDATE ON public.sproutshot_stage_progress TO authenticated;
GRANT ALL ON public.sproutshot_stage_progress TO service_role;

CREATE POLICY sproutshot_progress_select_own ON public.sproutshot_stage_progress
    FOR SELECT TO authenticated USING (owner_id = auth.uid());
CREATE POLICY sproutshot_progress_insert_own ON public.sproutshot_stage_progress
    FOR INSERT TO authenticated WITH CHECK (owner_id = auth.uid());
CREATE POLICY sproutshot_progress_update_own ON public.sproutshot_stage_progress
    FOR UPDATE TO authenticated USING (owner_id = auth.uid()) WITH CHECK (owner_id = auth.uid());

CREATE FUNCTION public.sproutshot_load_progress()
RETURNS jsonb LANGUAGE plpgsql STABLE SECURITY INVOKER SET search_path = pg_catalog, public, auth AS $function$
DECLARE
    player_id text := auth.uid();
    progress public.sproutshot_stage_progress%ROWTYPE;
BEGIN
    IF player_id IS NULL OR player_id = '' THEN
        RAISE EXCEPTION 'Authenticated player required' USING ERRCODE = '42501';
    END IF;
    SELECT * INTO progress FROM public.sproutshot_stage_progress WHERE owner_id = player_id;
    RETURN jsonb_build_object('playerId', player_id,
        'highestClearedStage', COALESCE(progress.highest_cleared_stage, 0),
        'revealedMapMask', COALESCE(progress.revealed_map_mask, 0));
END
$function$;

CREATE FUNCTION public.sproutshot_sync_progress(p_highest integer, p_revealed integer)
RETURNS jsonb LANGUAGE plpgsql SECURITY INVOKER SET search_path = pg_catalog, public, auth AS $function$
DECLARE
    player_id text := auth.uid();
    progress public.sproutshot_stage_progress%ROWTYPE;
BEGIN
    IF player_id IS NULL OR player_id = '' THEN
        RAISE EXCEPTION 'Authenticated player required' USING ERRCODE = '42501';
    END IF;
    IF p_highest IS NULL OR p_highest NOT BETWEEN 0 AND 50 OR
       p_revealed IS NULL OR p_revealed NOT BETWEEN 0 AND 31 THEN
        RAISE EXCEPTION 'Invalid stage progress' USING ERRCODE = '22023';
    END IF;
    INSERT INTO public.sproutshot_stage_progress (owner_id, highest_cleared_stage, revealed_map_mask)
    VALUES (player_id, p_highest, p_revealed & ((1 << LEAST(5, p_highest / 10 + 1)) - 1))
    ON CONFLICT (owner_id) DO UPDATE SET
        highest_cleared_stage = GREATEST(sproutshot_stage_progress.highest_cleared_stage, EXCLUDED.highest_cleared_stage),
        revealed_map_mask = sproutshot_stage_progress.revealed_map_mask | EXCLUDED.revealed_map_mask,
        updated_at = now()
    RETURNING * INTO progress;
    RETURN jsonb_build_object('playerId', player_id,
        'highestClearedStage', progress.highest_cleared_stage, 'revealedMapMask', progress.revealed_map_mask);
END
$function$;

REVOKE ALL ON FUNCTION public.sproutshot_load_progress() FROM PUBLIC, anon;
REVOKE ALL ON FUNCTION public.sproutshot_sync_progress(integer, integer) FROM PUBLIC, anon;
GRANT EXECUTE ON FUNCTION public.sproutshot_load_progress() TO authenticated;
GRANT EXECUTE ON FUNCTION public.sproutshot_sync_progress(integer, integer) TO authenticated;
