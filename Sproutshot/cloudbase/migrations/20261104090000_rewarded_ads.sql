-- Ad rewards are server-owned. Client completion is an explicit trust mode, not a platform signature.
CREATE TABLE public.sproutshot_rewarded_ad_config (
  id integer PRIMARY KEY CHECK(id=1), config jsonb NOT NULL
);
INSERT INTO public.sproutshot_rewarded_ad_config VALUES(1,
  '{"version":1,"enabled":false,"timezone":"Asia/Shanghai","seedReward":180,"seedDailyLimit":3,"dewReward":100,"dewDailyLimit":2,"verificationMode":"client-report"}');
CREATE TABLE public.sproutshot_rewarded_ad_tickets (
  ticket_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  owner_id text NOT NULL REFERENCES public.sproutshot_players(owner_id),
  reward_type text NOT NULL CHECK(reward_type IN ('seed','dew')),
  reward_amount integer NOT NULL CHECK(reward_amount>0),
  reward_day date NOT NULL,
  issued_at timestamptz NOT NULL DEFAULT clock_timestamp(),
  expires_at timestamptz NOT NULL DEFAULT clock_timestamp()+interval '10 minutes',
  claimed_at timestamptz,
  receipt jsonb
);
CREATE INDEX sproutshot_rewarded_ad_usage ON public.sproutshot_rewarded_ad_tickets(owner_id,reward_day,reward_type);
ALTER TABLE public.sproutshot_rewarded_ad_config ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.sproutshot_rewarded_ad_tickets ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON public.sproutshot_rewarded_ad_config,public.sproutshot_rewarded_ad_tickets FROM PUBLIC,anon,authenticated;
GRANT ALL ON public.sproutshot_rewarded_ad_config,public.sproutshot_rewarded_ad_tickets TO service_role;

CREATE FUNCTION public.sproutshot_rewarded_ad_status(p_owner text)
RETURNS jsonb LANGUAGE sql SECURITY INVOKER SET search_path=pg_catalog,public AS $fn$
  SELECT c.config || jsonb_build_object(
    'seedRemaining',GREATEST(0,(c.config->>'seedDailyLimit')::integer-count(t.ticket_id) FILTER(WHERE t.reward_type='seed')),
    'dewRemaining',GREATEST(0,(c.config->>'dewDailyLimit')::integer-count(t.ticket_id) FILTER(WHERE t.reward_type='dew')))
  FROM public.sproutshot_rewarded_ad_config c LEFT JOIN public.sproutshot_rewarded_ad_tickets t
    ON t.owner_id=p_owner AND t.claimed_at IS NOT NULL
      AND t.reward_day=(clock_timestamp() AT TIME ZONE (c.config->>'timezone'))::date
  WHERE c.id=1 GROUP BY c.id,c.config
$fn$;

-- Keep the existing combat, gacha and growth transaction intact behind a small dispatcher.
ALTER FUNCTION public.sproutshot_economy(text,text,uuid,jsonb) RENAME TO sproutshot_economy_before_ads;
CREATE FUNCTION public.sproutshot_economy(p_owner text,p_action text,p_operation uuid,p_payload jsonb)
RETURNS jsonb LANGUAGE plpgsql SECURITY INVOKER SET search_path=pg_catalog,public AS $fn$
DECLARE
  result jsonb; cfg jsonb; day_now date; amount integer; daily_limit integer; used integer;
  kind text; old_op public.sproutshot_operations%ROWTYPE;
  ticket public.sproutshot_rewarded_ad_tickets%ROWTYPE; v_receipt jsonb;
BEGIN
  IF p_action NOT IN ('beginRewardedAd','claimRewardedAd') OR p_action IS NULL THEN
    result:=public.sproutshot_economy_before_ads(p_owner,p_action,p_operation,p_payload);
    RETURN result || jsonb_build_object('rewardedAds',public.sproutshot_rewarded_ad_status(p_owner));
  END IF;
  IF p_owner IS NULL OR p_owner !~ '^[a-f0-9]{64}$' OR p_payload IS NULL OR jsonb_typeof(p_payload)<>'object' THEN
    RAISE EXCEPTION 'INVALID_REQUEST';
  END IF;
  IF p_operation IS NULL THEN RAISE EXCEPTION 'OPERATION_REQUIRED'; END IF;
  INSERT INTO public.sproutshot_players(owner_id) VALUES(p_owner) ON CONFLICT DO NOTHING;
  -- Shared with gacha/growth: one player lock serializes grants and quota checks across devices.
  PERFORM 1 FROM public.sproutshot_players WHERE owner_id=p_owner FOR UPDATE;
  SELECT * INTO old_op FROM public.sproutshot_operations WHERE owner_id=p_owner AND operation_id=p_operation;
  IF FOUND THEN
    IF old_op.action<>p_action OR old_op.payload<>p_payload THEN RAISE EXCEPTION 'OPERATION_CONFLICT'; END IF;
    v_receipt:=old_op.receipt;
  ELSE
    IF p_action='claimRewardedAd' THEN
      SELECT * INTO ticket FROM public.sproutshot_rewarded_ad_tickets
        WHERE ticket_id=(p_payload->>'ticketId')::uuid AND owner_id=p_owner FOR UPDATE;
      IF NOT FOUND THEN RAISE EXCEPTION 'AD_TICKET_NOT_FOUND'; END IF;
    END IF;
    -- A consumed ticket always returns its original receipt, even after expiry or a config change.
    IF p_action='claimRewardedAd' AND ticket.claimed_at IS NOT NULL THEN
      v_receipt:=ticket.receipt;
    ELSE
      SELECT config INTO cfg FROM public.sproutshot_rewarded_ad_config WHERE id=1 FOR SHARE;
      IF cfg IS NULL THEN RAISE EXCEPTION 'CONFIG_MISSING'; END IF;
      IF cfg->'enabled' IS DISTINCT FROM 'true'::jsonb THEN RAISE EXCEPTION 'ADS_DISABLED'; END IF;
      IF cfg->>'verificationMode' IS DISTINCT FROM 'client-report' THEN RAISE EXCEPTION 'AD_VERIFICATION_REQUIRED'; END IF;
      day_now:=(clock_timestamp() AT TIME ZONE (cfg->>'timezone'))::date;
      kind:=CASE WHEN p_action='beginRewardedAd' THEN p_payload->>'rewardType' ELSE ticket.reward_type END;
      IF kind IS NULL OR kind NOT IN ('seed','dew') THEN RAISE EXCEPTION 'INVALID_REQUEST'; END IF;
      amount:=(cfg->>CASE kind WHEN 'seed' THEN 'seedReward' ELSE 'dewReward' END)::integer;
      daily_limit:=(cfg->>CASE kind WHEN 'seed' THEN 'seedDailyLimit' ELSE 'dewDailyLimit' END)::integer;
      IF amount IS NULL OR amount<=0 OR daily_limit IS NULL OR daily_limit<0 THEN RAISE EXCEPTION 'CONFIG_MISSING'; END IF;
      SELECT count(*) INTO used FROM public.sproutshot_rewarded_ad_tickets
        WHERE owner_id=p_owner AND reward_type=kind AND reward_day=day_now AND claimed_at IS NOT NULL;
      IF used>=daily_limit THEN RAISE EXCEPTION 'AD_DAILY_LIMIT'; END IF;
      IF p_action='beginRewardedAd' THEN
        SELECT * INTO ticket FROM public.sproutshot_rewarded_ad_tickets
          WHERE owner_id=p_owner AND reward_type=kind AND reward_day=day_now
            AND claimed_at IS NULL AND expires_at>clock_timestamp() ORDER BY issued_at DESC LIMIT 1;
        IF NOT FOUND THEN
          INSERT INTO public.sproutshot_rewarded_ad_tickets(owner_id,reward_type,reward_amount,reward_day)
            VALUES(p_owner,kind,amount,day_now) RETURNING * INTO ticket;
        END IF;
        v_receipt:=jsonb_build_object('operationId',p_operation,'kind',p_action,'ticketId',ticket.ticket_id,
          'rewardType',kind,'rewardAmount',ticket.reward_amount,'expiresAt',ticket.expires_at);
      ELSE
        IF ticket.expires_at<=clock_timestamp() OR ticket.reward_day<>day_now THEN RAISE EXCEPTION 'AD_TICKET_EXPIRED'; END IF;
        IF p_payload->'completed' IS DISTINCT FROM 'true'::jsonb THEN RAISE EXCEPTION 'AD_NOT_COMPLETED'; END IF;
        v_receipt:=jsonb_build_object('operationId',p_operation,'kind',p_action,'ticketId',ticket.ticket_id,
          'rewardType',kind,'rewardSeeds',CASE kind WHEN 'seed' THEN ticket.reward_amount ELSE 0 END,
          'rewardDew',CASE kind WHEN 'dew' THEN ticket.reward_amount ELSE 0 END);
        UPDATE public.sproutshot_players SET
          sprout_seed=sprout_seed+CASE kind WHEN 'seed' THEN ticket.reward_amount ELSE 0 END,
          star_dew=star_dew+CASE kind WHEN 'dew' THEN ticket.reward_amount ELSE 0 END WHERE owner_id=p_owner;
        UPDATE public.sproutshot_rewarded_ad_tickets SET claimed_at=clock_timestamp(),receipt=v_receipt WHERE ticket_id=ticket.ticket_id;
      END IF;
    END IF;
    INSERT INTO public.sproutshot_operations(owner_id,operation_id,action,payload,receipt)
      VALUES(p_owner,p_operation,p_action,p_payload,v_receipt);
    UPDATE public.sproutshot_players SET revision=revision+1,updated_at=now() WHERE owner_id=p_owner;
  END IF;
  RETURN jsonb_build_object('profile',public.sproutshot_profile_snapshot(p_owner),'receipt',v_receipt,
    'catalog',(SELECT config FROM public.sproutshot_economy_config WHERE id=1),
    'rewardedAds',public.sproutshot_rewarded_ad_status(p_owner));
END
$fn$;
REVOKE ALL ON FUNCTION public.sproutshot_economy(text,text,uuid,jsonb),public.sproutshot_rewarded_ad_status(text) FROM PUBLIC,anon,authenticated;
GRANT EXECUTE ON FUNCTION public.sproutshot_economy(text,text,uuid,jsonb),public.sproutshot_rewarded_ad_status(text) TO service_role;
