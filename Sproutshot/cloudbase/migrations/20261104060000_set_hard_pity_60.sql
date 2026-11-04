-- Change only the configured hard-pity threshold. Preserve balances, ownership and existing pity progress.
UPDATE public.sproutshot_economy_config
SET config = jsonb_set(config, '{hardPity}', '60'::jsonb)
  || jsonb_build_object('version', (config->>'version')::integer + 1)
WHERE id = 1 AND config->'hardPity' IS DISTINCT FROM '60'::jsonb;
