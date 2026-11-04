'use strict';
const { createRewardedVideoPlayer } = require('./rewarded-video.cjs');

function failure(code, message) { return Object.assign(new Error(message), { code }); }

function createAdapter(wxApi) {
  const playRewardedVideo = createRewardedVideoPlayer(wxApi);
  let environment = '';
  let initializing;
  function nativeCall(target, method, options) {
    return new Promise((resolve, reject) => {
      target[method]({ ...options, success: resolve, fail: reject });
    });
  }
  async function backend(action, input) {
    const reply = await nativeCall(wxApi.cloud, 'callFunction', {
      name: 'sproutshot-cloud', data: { ...input, action }, config: { env: environment }
    });
    if (!reply.result || reply.result.success !== true) {
      throw failure(reply.result?.code || 'INVALID_CLOUD_RESPONSE',
        reply.result?.message || '云函数返回了无效响应。');
    }
    return reply.result.data;
  }
  async function upload(input) {
    const bytes = await nativeCall(wxApi.getFileSystemManager(), 'readFile', { filePath: input.localFilePath });
    const size = bytes.data?.byteLength;
    if (!Number.isInteger(size) || size < 1 || size > 5242880) throw failure('INVALID_FILE', '文件必须在 1 字节至 5 MiB 之间。');
    const signed = await backend('signUpload', { fileName: input.fileName, contentType: input.contentType, size });
    const result = await nativeCall(wxApi, 'request', {
      url: signed.url, method: 'PUT', data: bytes.data, timeout: 20000,
      header: { 'Content-Type': input.contentType, 'Content-Length': String(size) }
    });
    if (result.statusCode < 200 || result.statusCode >= 300) throw failure('UPLOAD_FAILED', '文件上传失败。');
    return { objectKey: signed.objectKey };
  }
  async function download(input) {
    const signed = await backend('signDownload', input);
    const result = await nativeCall(wxApi, 'downloadFile', { url: signed.url, timeout: 20000 });
    if (result.statusCode !== 200 || !result.tempFilePath) throw failure('DOWNLOAD_FAILED', '文件下载失败。');
    return { objectKey: input.objectKey, tempFilePath: result.tempFilePath };
  }
  async function execute(input) {
    if (!wxApi?.cloud || typeof wxApi.cloud.init !== 'function') {
      throw failure('UNSUPPORTED_PLATFORM', '云开发接口仅在微信小游戏运行环境中可用。');
    }
    if (input.action === 'initialize') {
      if (!/^[a-zA-Z0-9-]+$/.test(input.envId || '')) throw failure('INVALID_ENVIRONMENT', '云开发环境 ID 无效。');
      if (environment && environment !== input.envId) throw failure('ENVIRONMENT_ALREADY_BOUND', '本次游戏会话已绑定其他环境。');
      if (!environment && !initializing) {
        const target = input.envId;
        initializing = Promise.resolve(wxApi.cloud.init({ env: target, traceUser: true }))
          .then(() => { environment = target; })
          .finally(() => { initializing = undefined; });
      }
      if (initializing) await initializing;
      if (environment !== input.envId) throw failure('ENVIRONMENT_ALREADY_BOUND', '本次游戏会话已绑定其他环境。');
      return {};
    }
    if (!environment) throw failure('NOT_INITIALIZED', '请先初始化云开发。');
    switch (input.action) {
      case 'playRewardedAd': return playRewardedVideo(input.adUnitId);
      case 'economy': return backend('economy', input);
      case 'loadProgress':
      case 'syncProgress': return backend(input.action, input);
      case 'uploadFile': return upload(input);
      case 'downloadFile': return download(input);
      case 'getFileUrl': return backend('signDownload', input);
      case 'deleteFile': return backend('deleteFile', input);
      case 'uploadText': {
        if (typeof input.text !== 'string' || input.text.length > 262144) throw failure('INVALID_TEXT', '文本文件最大 256K 字符。');
        const fs = wxApi.getFileSystemManager();
        const path = wxApi.env.USER_DATA_PATH + '/sproutshot-cloud-' + input.requestId + '.txt';
        try {
          await nativeCall(fs, 'writeFile', { filePath: path, data: input.text, encoding: 'utf8' });
          return await upload({ ...input, localFilePath: path });
        } finally {
          await nativeCall(fs, 'unlink', { filePath: path }).catch(() => undefined);
        }
      }
      case 'downloadText': {
        const file = await download(input);
        const fs = wxApi.getFileSystemManager();
        try {
          const read = await nativeCall(fs, 'readFile', { filePath: file.tempFilePath, encoding: 'utf8' });
          if (typeof read.data !== 'string' || read.data.length > 262144) throw failure('INVALID_TEXT', '文本文件最大 256K 字符。');
          return { objectKey: input.objectKey, text: read.data };
        } finally {
          await nativeCall(fs, 'unlink', { filePath: file.tempFilePath }).catch(() => undefined);
        }
      }
      default: throw failure('UNKNOWN_ACTION', '不支持的客户端云开发操作。');
    }
  }
  return {
    async dispatch(input) {
      try { return { requestId: input.requestId, success: true, data: await execute(input) }; }
      catch (error) {
        return { requestId: input.requestId, success: false, code: error.code || 'WECHAT_CLOUD_FAILED',
          message: error.message || error.errMsg || '微信云开发调用失败。' };
      }
    }
  };
}

module.exports = { createAdapter };
