'use strict';

const wxCloud = require('wx-server-sdk');
const cloudbase = require('@cloudbase/js-sdk');
const { createService } = require('./service');

wxCloud.init({ env: wxCloud.DYNAMIC_CURRENT_ENV });
const app = cloudbase.init({ env: process.env.SPROUTSHOT_ENV_ID || 'sad-flea-d6gr2xr73135d4637' });
exports.main = createService({
  getContext: () => wxCloud.getWXContext(),
  getAppId: () => process.env.SPROUTSHOT_WECHAT_APP_ID,
  db: app.rdb(),
  storage: app.storage
});
