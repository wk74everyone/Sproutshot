'use strict';

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const MESSAGES = Object.freeze({
  INSUFFICIENT_DEW: '星露不足。', INSUFFICIENT_SEEDS: '萌芽种子不足。',
  MAX_LEVEL: '已达到最高等级。', SPIRIT_NOT_OWNED: '尚未拥有该精灵。',
  STAGE_LOCKED: '关卡尚未解锁。', MAP_LOCKED: '章节尚未解锁。',
  BATTLE_NOT_FOUND: '对局不存在或不属于当前玩家。', BATTLE_CLOSED: '对局已结束。',
  BATTLE_EXPIRED: '对局已过期。', INVALID_BATTLE_RESULT: '战斗结果校验失败。',
  INVALID_POOL: '卡池或抽取次数无效。', OPERATION_CONFLICT: '操作编号已用于其他请求。',
  INVALID_REQUEST: '请求参数无效。', OPERATION_REQUIRED: '缺少唯一操作编号。',
  CONFIG_MISSING: '服务端配置尚未就绪。',
  ADS_DISABLED: '广告奖励尚未开放。', AD_DAILY_LIMIT: '今天该奖励的领取次数已用完。',
  AD_TICKET_NOT_FOUND: '广告领取凭证无效或不属于当前玩家。', AD_TICKET_EXPIRED: '广告领取凭证已过期，请重新观看。',
  AD_NOT_COMPLETED: '广告未完整观看，无法领取奖励。', AD_VERIFICATION_REQUIRED: '广告验证方式尚未接通。'
});
function fail(code) { return Object.assign(new Error(MESSAGES[code] || '经济服务请求失败。'), { publicCode: code }); }
function integer(value, min, max) {
  if (!Number.isInteger(value) || value < min || value > max) throw fail('INVALID_REQUEST');
  return value;
}
function uuid(value) { if (!UUID.test(value || '')) throw fail('INVALID_REQUEST'); return value; }
function validate(input) {
  const action = input.economyAction;
  const p = input.payload || {};
  if (typeof p !== 'object' || Array.isArray(p)) throw fail('INVALID_REQUEST');
  let payload;
  switch (action) {
    case 'load': case 'history': payload = {}; break;
    case 'migrate': payload = { installId: uuid(p.installId), highest: integer(p.highest, 0, 50), revealed: integer(p.revealed, 0, 31) }; break;
    case 'reveal': payload = { map: integer(p.map, 2, 5) }; break;
    case 'beginBattle': payload = { stage: integer(p.stage, 1, 50) }; break;
    case 'settleBattle':
      if (typeof p.victory !== 'boolean') throw fail('INVALID_REQUEST');
      payload = { battleId: uuid(p.battleId), victory: p.victory, defeated: integer(p.defeated, 0, 1000000) }; break;
    case 'summon':
      if (p.pool !== 'standard' || ![1, 10].includes(p.count)) throw fail('INVALID_POOL');
      payload = { pool: p.pool, count: p.count }; break;
    case 'upgradePlayer': payload = {}; break;
    case 'beginRewardedAd':
      if (!['seed','dew'].includes(p.rewardType)) throw fail('INVALID_REQUEST');
      payload = { rewardType: p.rewardType }; break;
    case 'claimRewardedAd':
      if (p.completed !== true) throw fail('AD_NOT_COMPLETED');
      payload = { ticketId: uuid(p.ticketId), completed: true }; break;
    case 'equipProfession': payload = { professionId: integer(p.professionId, 1, 2000000000) }; break;
    case 'upgradeSpirit': case 'equipSpirit': payload = { spiritId: integer(p.spiritId, action === 'equipSpirit' ? 0 : 1, 2000000000) }; break;
    default: throw fail('INVALID_REQUEST');
  }
  return { action, payload, operation: ['load', 'history'].includes(action) ? null : uuid(input.operationId) };
}
async function economy(db, playerId, input) {
  const request = validate(input);
  const result = await db.rpc('sproutshot_economy', {
    p_owner: playerId, p_action: request.action, p_operation: request.operation, p_payload: request.payload
  });
  if (result.error) {
    const message = String(result.error.message || '');
    const code = Object.keys(MESSAGES).find(key => new RegExp('\\b' + key + '\\b').test(message));
    if (code) throw fail(code);
    throw result.error;
  }
  if (!result.data?.profile || result.data.profile.playerId !== playerId) throw new Error('Invalid economy RPC response');
  return result.data;
}
module.exports = { economy, validate };
