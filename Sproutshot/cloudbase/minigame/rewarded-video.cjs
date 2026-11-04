'use strict';

function createRewardedVideoPlayer(wxApi, { timeoutMs = 180000 } = {}) {
  let busy = false;
  return async function play(adUnitId) {
    const fail = (code, message) => Object.assign(new Error(message), { code });
    if (busy) throw fail('AD_BUSY', '广告正在播放，请稍候。');
    if (!/^adunit-[a-zA-Z0-9_-]+$/.test(adUnitId || '')) throw fail('AD_UNIT_REQUIRED', '请先配置激励视频广告位 ID。');
    if (typeof wxApi?.createRewardedVideoAd !== 'function') throw fail('AD_UNSUPPORTED', '当前微信版本不支持激励视频广告。');
    busy = true;
    let ad, timer, onClose, onError;
    try {
      ad = wxApi.createRewardedVideoAd({ adUnitId });
      if (!ad) throw fail('AD_UNSUPPORTED', '当前环境无法创建激励视频广告。');
      return await new Promise((resolve, reject) => {
        let settled = false;
        const finish = (error, completed) => {
          if (settled) return;
          settled = true;
          clearTimeout(timer);
          if (error) reject(error); else resolve({ completed });
        };
        onClose = res => finish(null, res?.isEnded === true);
        onError = () => finish(fail('AD_PLAY_FAILED', '广告暂时无法播放，请稍后再试。'));
        ad.onClose(onClose);
        ad.onError(onError);
        timer = setTimeout(() => finish(fail('AD_PLAY_TIMEOUT', '广告播放响应超时，请稍后再试。')), timeoutMs);
        Promise.resolve().then(() => ad.load()).then(() => {
          if (!settled) return ad.show();
        }).catch(onError);
      });
    } finally {
      clearTimeout(timer);
      // Remove only our listeners. Never accumulate callbacks across repeated watches.
      try { if (onClose) ad?.offClose(onClose); } catch (_) { /* native cleanup */ }
      try { if (onError) ad?.offError(onError); } catch (_) { /* native cleanup */ }
      try { ad?.destroy(); } catch (_) { /* native cleanup */ }
      busy = false;
    }
  };
}
module.exports = { createRewardedVideoPlayer };
