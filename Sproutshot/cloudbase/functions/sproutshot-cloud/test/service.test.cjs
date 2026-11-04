'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const { randomUUID } = require('node:crypto');
const { createService, BUCKET } = require('../service');
const APP_ID = 'wx0123456789abcdef';

function fixture(overrides = {}) {
  const calls = [];
  const handler = createService({
    getContext: () => ({ OPENID: 'verified-player', APPID: APP_ID }),
    getAppId: () => APP_ID,
    db: {
      from: () => ({ select: () => ({ eq: (field, player) => ({ limit: async () => {
        calls.push({ field, player }); return { data: [] };
      } }) }) }),
      rpc: async (name, args) => { calls.push({ name, args }); return { data: {
        profile: { playerId: args.p_owner, highestClearedStage: 0, revealedMapMask: 0 }
      } }; }
    },
    storage: { from: (bucket) => {
      assert.equal(bucket, BUCKET);
      return {
        createSignedUploadUrl: async (key) => ({ data: { fullSignedURL: 'https://files.invalid/upload/' + key } }),
        createSignedUrl: async (key) => ({ data: { fullSignedURL: 'https://files.invalid/download/' + key } }),
        remove: async (keys) => { calls.push({ keys }); return { data: keys }; }
      };
    } }, ...overrides
  });
  return { handler, calls };
}

test('untrusted request fields cannot supply the native WeChat identity', async () => {
  const { handler } = fixture({ getContext: () => ({}) });
  const result = await handler({ action: 'loadProgress', OPENID: 'forged', APPID: APP_ID });
  assert.equal(result.code, 'WECHAT_IDENTITY_REQUIRED');
});
test('missing or different AppID fails closed', async () => {
  assert.equal((await fixture({ getAppId: () => '' }).handler({ action: 'loadProgress' })).code, 'WECHAT_APP_ID_REQUIRED');
  assert.equal((await fixture({ getAppId: () => 'wxaaaaaaaaaaaaaaaa' }).handler({ action: 'loadProgress' })).code, 'WRONG_WECHAT_APP');
});
test('verified identity is hashed and client owner IDs are ignored', async () => {
  const { handler, calls } = fixture();
  const result = await handler({ action: 'economy', economyAction: 'load', playerId: 'forged' });
  assert.equal(result.success, true);
  assert.match(result.data.profile.playerId, /^[a-f0-9]{64}$/);
  assert.equal(calls[0].args.p_owner, result.data.profile.playerId);
  assert.equal(calls[0].name, 'sproutshot_economy');
});
test('profession selection validates input, strips extra fields and derives the owner from WeChat', async () => {
  const { handler, calls } = fixture();
  const operationId = randomUUID();
  const result = await handler({ action: 'economy', economyAction: 'equipProfession', operationId,
    playerId: 'forged', payload: { professionId: 1, owner_id: 'forged', sproutSeed: 999999 } });
  assert.equal(result.success, true);
  assert.equal(calls[0].name, 'sproutshot_economy');
  assert.equal(calls[0].args.p_action, 'equipProfession');
  assert.equal(calls[0].args.p_operation, operationId);
  assert.equal(calls[0].args.p_owner, result.data.profile.playerId);
  assert.deepEqual(calls[0].args.p_payload, { professionId: 1 });
  for (const professionId of [undefined, null, 0, -1, 1.5, '1', true, {}, [], 2000000001]) {
    const rejected = await handler({ action: 'economy', economyAction: 'equipProfession', operationId, payload: { professionId } });
    assert.equal(rejected.code, 'INVALID_REQUEST');
  }
  assert.equal((await handler({ action: 'economy', economyAction: 'equipProfession', payload: { professionId: 1 } })).code, 'INVALID_REQUEST');
  assert.equal(calls.length, 1);
});

test('profession availability errors from the database reach the caller without raw details', async () => {
  const calls = [];
  const { handler } = fixture({ db: { rpc: async (name, args) => {
    calls.push(args); return { error: { message: 'INVALID_REQUEST internal database details' } };
  } } });
  const result = await handler({ action: 'economy', economyAction: 'equipProfession', operationId: randomUUID(), payload: { professionId: 2 } });
  assert.equal(calls[0].p_payload.professionId, 2);
  assert.deepEqual(result, { success: false, code: 'INVALID_REQUEST', message: '请求参数无效。' });
});

test('legacy progress writes are rejected even when their numbers look valid', async () => {
  const { handler, calls } = fixture();
  for (const highest of [-1, 51, 1.2, '12', null]) {
    assert.equal((await handler({ action: 'syncProgress', highestClearedStage: highest, revealedMapMask: 0 })).code, 'PROGRESS_SERVER_OWNED');
  }
  assert.equal(calls.length, 0);
});
test('file operations stay in the verified player directory', async () => {
  const { handler } = fixture();
  const upload = await handler({ action: 'signUpload', fileName: 'save.json', size: 12, contentType: 'application/json' });
  assert.equal(upload.success, true);
  assert.match(upload.data.url, /^https:\/\/files.invalid\/upload\//);
  const download = await handler({ action: 'signDownload', objectKey: upload.data.objectKey });
  assert.equal(download.data.expiresIn, 300);
  assert.equal((await handler({ action: 'signDownload', objectKey: 'a'.repeat(64) + '/save.json' })).code, 'FILE_ACCESS_DENIED');
  assert.equal((await handler({ action: 'deleteFile', objectKey: upload.data.objectKey + '/../other' })).code, 'FILE_ACCESS_DENIED');
  assert.equal((await handler({ action: 'signUpload', fileName: '../save', size: 12, contentType: 'application/json' })).code, 'INVALID_FILE');
  assert.equal((await handler({ action: 'signUpload', fileName: 'big.json', size: 5242881, contentType: 'application/json' })).code, 'INVALID_FILE');
});
test('SDK failures are not returned with headers, tokens or internal details', async () => {
  const { handler } = fixture({ db: { rpc: async () => ({ error: new Error('secret-token-and-headers') }) } });
  const result = await handler({ action: 'economy', economyAction: 'load' });
  assert.equal(result.code, 'CLOUD_OPERATION_FAILED');
  assert.ok(!JSON.stringify(result).includes('secret-token'));
});
