'use strict';

const { createHash } = require('node:crypto');
const { economy } = require('./economy');
const BUCKET = 'sproutshot-player-files';
const MIME_TYPES = new Set(['application/json', 'text/plain', 'image/png', 'image/jpeg']);

function invalid(code, message) {
  return Object.assign(new Error(message), { publicCode: code });
}

function value(result) {
  if (result.error) throw result.error;
  return result.data;
}

function createService({ getContext, getAppId, db, storage }) {
  return async function handle(event) {
    try {
      const context = getContext();
      if (!context || !context.OPENID || !context.APPID) {
        throw invalid('WECHAT_IDENTITY_REQUIRED', '请通过正式微信小游戏调用。');
      }
      const appId = getAppId();
      if (!/^wx[a-f0-9]{16}$/.test(appId || '')) {
        throw invalid('WECHAT_APP_ID_REQUIRED', '尚未配置正式微信小游戏 AppID。');
      }
      if (context.APPID !== appId) throw invalid('WRONG_WECHAT_APP', '小游戏 AppID 与后端配置不一致。');
      const playerId = createHash('sha256').update(appId + ':' + context.OPENID).digest('hex');
      const input = event || {};
      let data;
      switch (input.action) {
        case 'economy': {
          if (input.expectedPlayerId && input.expectedPlayerId !== playerId)
            throw invalid('PLAYER_CHANGED', '玩家身份已变化，请重新进入游戏。');
          data = await economy(db, playerId, input);
          break;
        }
        case 'loadProgress': {
          const rows = value(await db.from('sproutshot_stage_progress')
            .select('highest_cleared_stage,revealed_map_mask').eq('owner_id', playerId).limit(1));
          const row = rows && rows[0];
          data = { playerId, highestClearedStage: row ? row.highest_cleared_stage : 0,
            revealedMapMask: row ? row.revealed_map_mask : 0 };
          break;
        }
        case 'syncProgress': {
          throw invalid('PROGRESS_SERVER_OWNED', '进度已改由服务端结算，请更新客户端。');
        }
        case 'signUpload': {
          if (!/^[a-zA-Z0-9][a-zA-Z0-9._-]{0,95}$/.test(input.fileName || '') ||
              !MIME_TYPES.has(input.contentType) || !Number.isInteger(input.size) ||
              input.size < 1 || input.size > 5 * 1024 * 1024) {
            throw invalid('INVALID_FILE', '文件名、类型或大小不符合要求（最大 5 MiB）。');
          }
          const objectKey = playerId + '/' + input.fileName;
          const signed = value(await storage.from(BUCKET).createSignedUploadUrl(objectKey, { upsert: true }));
          data = { objectKey, url: signed.fullSignedURL };
          break;
        }
        case 'signDownload':
        case 'deleteFile': {
          if (typeof input.objectKey !== 'string' || !input.objectKey.startsWith(playerId + '/') ||
              !/^[a-zA-Z0-9][a-zA-Z0-9._-]{0,95}$/.test(input.objectKey.slice(65))) {
            throw invalid('FILE_ACCESS_DENIED', '只能访问当前玩家自己的文件。');
          }
          if (input.action === 'deleteFile') {
            value(await storage.from(BUCKET).remove([input.objectKey]));
            data = { objectKey: input.objectKey };
          } else {
            const signed = value(await storage.from(BUCKET).createSignedUrl(input.objectKey, 300));
            data = { objectKey: input.objectKey, url: signed.fullSignedURL, expiresIn: 300 };
          }
          break;
        }
        default: throw invalid('UNKNOWN_ACTION', '不支持的云开发操作。');
      }
      if (!data || ((input.action === 'signUpload' || input.action === 'signDownload') && !data.url)) {
        throw new Error('Invalid CloudBase SDK response');
      }
      return { success: true, data };
    } catch (error) {
      // Do not return raw SDK errors, events, context, headers, or runtime credentials.
      return { success: false, code: error.publicCode || 'CLOUD_OPERATION_FAILED',
        message: error.publicCode ? error.message : '云服务操作失败，请稍后重试。' };
    }
  };
}

module.exports = { createService, BUCKET };
