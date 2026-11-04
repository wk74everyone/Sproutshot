'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const { createAdapter } = require('../adapter.cjs');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');

function fixture() {
  const files = new Map();
  const stored = new Map();
  const calls = [];
  let initCount = 0;
  const manager = {
    writeFile: (o) => { files.set(o.filePath, Buffer.from(o.data)); o.success({}); },
    readFile: (o) => {
      const b = files.get(o.filePath);
      if (!b) return o.fail({ errMsg: 'File missing' });
      o.success({ data: o.encoding ? b.toString('utf8') : b.buffer.slice(b.byteOffset, b.byteOffset + b.length) });
    },
    unlink: (o) => { files.delete(o.filePath); o.success({}); }
  };
  const wx = {
    env: { USER_DATA_PATH: '/native' }, getFileSystemManager: () => manager,
    cloud: {
      init: () => { initCount++; },
      callFunction: (o) => {
        calls.push(o.data);
        const data = o.data.action === 'signUpload' ? { objectKey: 'player/' + o.data.fileName, url: 'https://upload.invalid/' + o.data.fileName }
          : o.data.action === 'signDownload' ? { url: 'https://download.invalid/' + o.data.objectKey.split('/')[1] }
          : { playerId: 'a'.repeat(64), highestClearedStage: 12, revealedMapMask: 2 };
        if (o.data.action === 'deleteFile') stored.delete(o.data.objectKey.split('/')[1]);
        o.success({ result: { success: true, data } });
      }
    },
    request: (o) => { assert.equal(o.method, 'PUT'); assert.equal(Number(o.header['Content-Length']), o.data.byteLength);
      stored.set(o.url.split('/').pop(), Buffer.from(o.data)); o.success({ statusCode: 200 }); },
    downloadFile: (o) => {
      const key = o.url.split('/').pop(); files.set('/download/' + key, stored.get(key));
      o.success({ statusCode: 200, tempFilePath: '/download/' + key });
    }
  };
  return { wx, files, stored, calls, count: () => initCount, adapter: createAdapter(wx) };
}
async function init(f) { return f.adapter.dispatch({ requestId: 1, action: 'initialize', envId: 'test-env' }); }

test('non-WeChat runtime and calls before initialization fail explicitly', async () => {
  assert.equal((await createAdapter(undefined).dispatch({ requestId: 7, action: 'initialize' })).code, 'UNSUPPORTED_PLATFORM');
  assert.equal((await fixture().adapter.dispatch({ requestId: 8, action: 'loadProgress' })).code, 'NOT_INITIALIZED');
});
test('initialization is bound to one environment and database errors are propagated', async () => {
  const f = fixture(); await init(f); await init(f);
  assert.equal(f.count(), 1);
  assert.equal((await f.adapter.dispatch({ action: 'initialize', envId: 'other-env' })).code, 'ENVIRONMENT_ALREADY_BOUND');
  f.wx.cloud.callFunction = (o) => o.success({ result: { success: false, code: 'WECHAT_APP_ID_REQUIRED', message: 'missing' } });
  assert.equal((await f.adapter.dispatch({ requestId: 9, action: 'loadProgress' })).code, 'WECHAT_APP_ID_REQUIRED');
});
test('native text upload/download round trip and deletion leave no temporary files', async () => {
  const f = fixture(); await init(f);
  const text = '{"进度":12}';
  const uploaded = await f.adapter.dispatch({ requestId: 2, action: 'uploadText', fileName: 'probe.json', contentType: 'application/json', text });
  assert.equal(uploaded.success, true); assert.equal(f.files.size, 0);
  const downloaded = await f.adapter.dispatch({ requestId: 3, action: 'downloadText', objectKey: uploaded.data.objectKey });
  assert.equal(downloaded.data.text, text); assert.equal(f.files.size, 0);
  await f.adapter.dispatch({ requestId: 4, action: 'deleteFile', objectKey: uploaded.data.objectKey });
  assert.equal(f.stored.size, 0);
});
test('concurrent callers share one native initialization', async () => {
  const f = fixture(); let release, calls = 0;
  f.wx.cloud.init = () => { calls++; return new Promise((resolve) => { release = resolve; }); };
  const first = init(f); const second = init(f);
  assert.equal(calls, 1); release();
  assert.equal((await first).success, true); assert.equal((await second).success, true);
});
test('failed uploads clean local files and do not report success', async () => {
  const f = fixture(); await init(f);
  f.wx.request = (o) => o.success({ statusCode: 403 });
  const result = await f.adapter.dispatch({ requestId: 2, action: 'uploadText', fileName: 'probe.json', contentType: 'application/json', text: 'x' });
  assert.equal(result.code, 'UPLOAD_FAILED'); assert.equal(f.files.size, 0);
});

test('economy retries preserve the operation UUID across different transport request IDs', async () => {
  const f = fixture(); await init(f);
  const operationId='00000000-0000-4000-8000-000000000123';
  for(const requestId of [11,12]) {
    const result=await f.adapter.dispatch({requestId,action:'economy',economyAction:'summon',operationId,
      expectedPlayerId:'a'.repeat(64),payload:{pool:'standard',count:1}});
    assert.equal(result.requestId,requestId); assert.equal(result.success,true);
  }
  assert.deepEqual(f.calls.map(c=>c.operationId),[operationId,operationId]);
  assert.deepEqual(f.calls.map(c=>c.payload),[{pool:'standard',count:1},{pool:'standard',count:1}]);
});
test('generated Unity JS library returns the matching request to the C# receiver', async () => {
  const f = fixture(); const replies = [];
  const sandbox = { wx: f.wx, LibraryManager: { library: {} }, UTF8ToString: (value) => value,
    mergeInto: Object.assign, SendMessage: (...args) => replies.push(args) };
  vm.runInNewContext(fs.readFileSync(path.resolve(__dirname, '../../../Assets/Plugins/WebGL/SproutshotCloudBase.jslib'), 'utf8'), sandbox);
  sandbox.SproutshotCloudBaseBridge = sandbox.LibraryManager.library.$SproutshotCloudBaseBridge;
  sandbox.LibraryManager.library.SproutshotCloudBase_Request(JSON.stringify({ requestId: 25, action: 'initialize', envId: 'test-env' }), 'SproutshotCloudBase');
  await new Promise(setImmediate);
  assert.equal(replies[0][0], 'SproutshotCloudBase'); assert.equal(replies[0][1], 'OnCloudBaseResponse');
  assert.equal(JSON.parse(replies[0][2]).requestId, 25); assert.equal(JSON.parse(replies[0][2]).success, true);
});
