'use strict';
const {test}=require('node:test');const assert=require('node:assert/strict');
const {createRewardedVideoPlayer}=require('../rewarded-video.cjs');
function fixture(options={}) {
  const close=new Set(),errors=new Set();let destroyed=0,shown=0;
  const ad={onClose:f=>close.add(f),offClose:f=>close.delete(f),onError:f=>errors.add(f),offError:f=>errors.delete(f),
    load:async()=>{},show:async()=>{shown++;},destroy:()=>{destroyed++;}};
  const play=createRewardedVideoPlayer({createRewardedVideoAd:()=>ad},options);
  return {ad,play,close,errors,emit:res=>[...close].forEach(f=>f(res)),fail:()=>[...errors].forEach(f=>f({})),
    counts:()=>({destroyed,shown,listeners:close.size+errors.size})};
}
const tick=()=>new Promise(setImmediate),unit='adunit-test';
test('only complete close grants; show success alone never completes; listeners are removed across watches',async()=>{
  const f=fixture();
  for(const ended of [true,false,undefined,true]) {
    let finished=false;const task=f.play(unit).then(r=>{finished=true;return r;});await tick();
    assert.equal(finished,false);f.emit(ended===undefined?undefined:{isEnded:ended});f.emit({isEnded:true});
    assert.deepEqual(await task,{completed:ended===true});assert.equal(f.counts().listeners,0);
  }
  assert.deepEqual(f.counts(),{shown:4,destroyed:4,listeners:0});
});
test('load, native errors and timeouts reject without rewards and release the busy gate',async()=>{
  const f=fixture({timeoutMs:15});f.ad.load=async()=>{throw Error('fixture');};
  await assert.rejects(f.play(unit),e=>e.code==='AD_PLAY_FAILED');
  f.ad.load=async()=>{};
  const error=f.play(unit);f.fail();await assert.rejects(error,e=>e.code==='AD_PLAY_FAILED');
  await assert.rejects(f.play(unit),e=>e.code==='AD_PLAY_TIMEOUT');assert.equal(f.counts().listeners,0);
});
test('invalid configuration and simultaneous shows are rejected',async()=>{
  const f=fixture();await assert.rejects(f.play(''),e=>e.code==='AD_UNIT_REQUIRED');
  const first=f.play(unit);await assert.rejects(f.play(unit),e=>e.code==='AD_BUSY');f.emit({isEnded:true});await first;
  await assert.rejects(createRewardedVideoPlayer({})(unit),e=>e.code==='AD_UNSUPPORTED');
});
