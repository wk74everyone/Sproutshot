-- Native WeChat clients reach these resources through the identity-checked event function.
-- Direct client/anonymous access stays denied; no environment-wide gateway policy is changed.
INSERT INTO storage.buckets (id, name, public, file_size_limit, allowed_mime_types)
VALUES ('sproutshot-player-files', 'sproutshot-player-files', false, 5242880,
        ARRAY['application/json', 'text/plain', 'image/png', 'image/jpeg']);

CREATE FUNCTION public.sproutshot_sync_wechat_progress(p_owner text, p_highest integer, p_revealed integer)
RETURNS jsonb LANGUAGE plpgsql SECURITY INVOKER SET search_path = pg_catalog, public, auth AS $function$
DECLARE progress public.sproutshot_stage_progress%ROWTYPE;
BEGIN
    IF p_owner IS NULL OR p_owner !~ '^[a-f0-9]{64}$' OR
       p_highest IS NULL OR p_highest NOT BETWEEN 0 AND 50 OR
       p_revealed IS NULL OR p_revealed NOT BETWEEN 0 AND 31 THEN
        RAISE EXCEPTION 'Invalid player progress' USING ERRCODE = '22023';
    END IF;
    INSERT INTO public.sproutshot_stage_progress (owner_id, highest_cleared_stage, revealed_map_mask)
    VALUES (p_owner, p_highest, p_revealed & ((1 << LEAST(5, p_highest / 10 + 1)) - 1))
    ON CONFLICT (owner_id) DO UPDATE SET
        highest_cleared_stage = GREATEST(sproutshot_stage_progress.highest_cleared_stage, EXCLUDED.highest_cleared_stage),
        revealed_map_mask = sproutshot_stage_progress.revealed_map_mask | EXCLUDED.revealed_map_mask,
        updated_at = now()
    RETURNING * INTO progress;
    RETURN jsonb_build_object('playerId', progress.owner_id,
        'highestClearedStage', progress.highest_cleared_stage, 'revealedMapMask', progress.revealed_map_mask);
END
$function$;

REVOKE ALL ON FUNCTION public.sproutshot_sync_wechat_progress(text, integer, integer) FROM PUBLIC, anon, authenticated;
GRANT EXECUTE ON FUNCTION public.sproutshot_sync_wechat_progress(text, integer, integer) TO service_role;

-- A false permissive policy documents the server-only boundary without granting client access.
CREATE POLICY sproutshot_files_server_only ON storage.objects FOR ALL TO anon, authenticated
    USING (false) WITH CHECK (false);
