-- Read-only schema/security contract after the economy migration.
-- Stateful charge/refund probes live in verify-economy.sql and rollback their own data.
DO $test$
BEGIN
    IF to_regclass('public.sproutshot_players') IS NULL OR
       to_regclass('public.sproutshot_owned_spirits') IS NULL OR
       to_regclass('public.sproutshot_operations') IS NULL THEN
        RAISE EXCEPTION 'Economy schema is missing';
    END IF;
    IF has_function_privilege('authenticated','public.sproutshot_economy(text,text,uuid,jsonb)','EXECUTE') OR
       has_function_privilege('authenticated','public.sproutshot_sync_progress(integer,integer)','EXECUTE') OR
       has_table_privilege('authenticated','public.sproutshot_stage_progress','UPDATE') OR
       has_table_privilege('anon','public.sproutshot_players','SELECT') THEN
        RAISE EXCEPTION 'Direct client permissions can bypass the event function';
    END IF;
    IF (SELECT (config->>'duplicateSeeds')::integer FROM public.sproutshot_economy_config WHERE id=1) IS DISTINCT FROM 20 THEN
        RAISE EXCEPTION 'Duplicate refund configuration is not 20';
    END IF;
END
$test$;
SELECT 'PASS: database schema, server-only writes, and 20-seed duplicate refund' AS result;