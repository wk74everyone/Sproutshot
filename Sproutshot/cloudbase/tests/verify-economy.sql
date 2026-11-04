-- Management-only integration probe. A subtransaction rolls back ALL probe data and config.
DO $test$
DECLARE
  test_owner text := '00000000000000000000000000000000000000000000000000000000c0dec001';
  op uuid := gen_random_uuid(); r jsonb; again jsonb; battle uuid;
BEGIN
  IF EXISTS(SELECT 1 FROM public.sproutshot_players WHERE owner_id=test_owner) THEN
    RAISE EXCEPTION 'Probe identity already exists; refusing to alter it';
  END IF;
  BEGIN
    r:=public.sproutshot_economy(test_owner,'load',NULL,'{}');
    IF (r#>>'{catalog,duplicateSeeds}')::integer<>20 THEN RAISE EXCEPTION 'Duplicate refund is not 20'; END IF;
    UPDATE public.sproutshot_players SET star_dew=1000 WHERE owner_id=test_owner;
    INSERT INTO public.sproutshot_owned_spirits(owner_id,spirit_id) VALUES(test_owner,1001),(test_owner,1002),(test_owner,1003);
    INSERT INTO public.sproutshot_pity(owner_id,pool_id,misses) VALUES(test_owner,'standard',49);
    r:=public.sproutshot_economy(test_owner,'summon',op,'{"pool":"standard","count":10}');
    IF (r#>>'{profile,starDew}')::integer<>0 OR (r#>>'{profile,sproutSeed}')::integer<>200 OR
       (r#>>'{receipt,results,0,rarity}')::integer<>2 THEN RAISE EXCEPTION 'Charge/refund/pity assertion failed'; END IF;
    again:=public.sproutshot_economy(test_owner,'summon',op,'{"pool":"standard","count":10}');
    IF again->'receipt'<>r->'receipt' OR (again#>>'{profile,sproutSeed}')::integer<>200 THEN RAISE EXCEPTION 'Idempotency failed'; END IF;
    BEGIN
      PERFORM public.sproutshot_economy(test_owner,'summon',gen_random_uuid(),'{"pool":"standard","count":1}');
      RAISE EXCEPTION 'Insufficient funds accepted';
    EXCEPTION WHEN raise_exception THEN
      IF SQLERRM<>'INSUFFICIENT_DEW' THEN RAISE; END IF;
    END;
    IF has_function_privilege('authenticated','public.sproutshot_economy(text,text,uuid,jsonb)','EXECUTE') OR
       has_table_privilege('authenticated','public.sproutshot_players','UPDATE') OR
       has_table_privilege('authenticated','public.sproutshot_stage_progress','UPDATE') THEN
      RAISE EXCEPTION 'Direct client can write permanent state';
    END IF;
    RAISE EXCEPTION 'PROBE_ROLLBACK' USING ERRCODE='Z0001';
  EXCEPTION WHEN SQLSTATE 'Z0001' THEN NULL;
  END;
  IF EXISTS(SELECT 1 FROM public.sproutshot_players WHERE owner_id=test_owner) THEN RAISE EXCEPTION 'Probe did not rollback'; END IF;
END
$test$;
SELECT 'PASS: actual PG charge, 20-seed refund, pity, replay, insufficient balance, grants and rollback' AS result;
