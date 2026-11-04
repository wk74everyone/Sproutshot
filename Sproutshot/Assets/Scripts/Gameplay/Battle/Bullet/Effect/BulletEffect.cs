namespace Sproutshot.Gameplay
{
    /// <summary>效果只修改运行上下文，不直接回收子弹。固定阶段消除添加顺序的依赖。</summary>
    public abstract class BulletEffect
    {
        public int BuffId { get; }
        public virtual int Priority => 100;
        protected BulletEffect(int buffId) => BuffId = buffId;
        public virtual void OnLaunch(BulletContext context) { }
        public virtual void BeforeHit(BulletContext context, ref BulletHitContext hit) { }
        public virtual void AfterHit(BulletContext context, ref BulletHitContext hit) { }
        /// <summary>全部命中策略处理完后，本段已无后续命中机会时通知；不受 AfterHit 排序影响。</summary>
        public virtual void OnTerminalHit(BulletContext context, ref BulletHitContext hit) { }
        public virtual void OnPierce(BulletContext context, ref BulletHitContext hit) { }
        public virtual bool TryBeginReturn(BulletContext context, BulletFlightEndReason reason) => false;
        public virtual void OnReturn(BulletContext context) { }
        public virtual void OnRecycle(BulletContext context) { }
    }
}
