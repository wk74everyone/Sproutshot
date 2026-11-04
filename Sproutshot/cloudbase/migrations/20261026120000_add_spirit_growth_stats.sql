UPDATE public.sproutshot_economy_config AS c
SET config = jsonb_set(
    c.config,
    '{spirits}',
    (
        SELECT jsonb_agg(
            jsonb_build_object(
                'selfDamage', (item->>'damage')::integer,
                'defense', 1
            ) || item
            ORDER BY ord
        )
        FROM jsonb_array_elements(c.config->'spirits')
             WITH ORDINALITY AS s(item, ord)
    )
) || jsonb_build_object(
    'version', (c.config->>'version')::integer + 1
)
WHERE c.id = 1;