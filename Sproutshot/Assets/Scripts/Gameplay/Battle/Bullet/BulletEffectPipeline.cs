using System.Collections.Generic;

namespace Sproutshot.Gameplay
{
    public sealed class BulletEffectPipeline
    {
        private readonly List<BulletEffect> _effects = new();
        private BulletElement _elements;
        public IReadOnlyList<BulletEffect> Effects => _effects;

        public void Build(IReadOnlyList<BulletBuffSnapshot> snapshots)
        {
            _effects.Clear();
            _elements = BulletElement.None;
            foreach (var snapshot in snapshots)
            {
                var effect = BulletEffectFactory.Create(snapshot);
                if (effect == null) continue;
                _effects.Add(effect);
                switch (snapshot.BuffId)
                {
                    case 12: _elements |= BulletElement.Fire; break;
                    case 13: _elements |= BulletElement.Frost; break;
                    case 14: _elements |= BulletElement.Lightning; break;
                }
            }
            _effects.Sort((a, b) => a.Priority != b.Priority ? a.Priority.CompareTo(b.Priority) : a.BuffId.CompareTo(b.BuffId));
        }

        public T Find<T>() where T : BulletEffect
        {
            foreach (var effect in _effects) if (effect is T result) return result;
            return null;
        }

        /// <summary>添加技能自身的固有效果，不伪造选卡 Buff 或修改主人的 ASC。</summary>
        public void Add(BulletEffect effect)
        {
            if (effect == null) return;
            _effects.Add(effect);
            _effects.Sort((a, b) => a.Priority != b.Priority ? a.Priority.CompareTo(b.Priority) : a.BuffId.CompareTo(b.BuffId));
        }

        public void OnLaunch(BulletContext context)
        {
            context.Elements = _elements;
            foreach (var effect in _effects) effect.OnLaunch(context);
        }

        public bool ProcessHit(BulletContext context, ref BulletHitContext hit)
        {
            foreach (var effect in _effects) effect.BeforeHit(context, ref hit);
            hit.DamageApplied = BulletDamageResolver.TryApply(context, ref hit);
            if (!hit.DamageApplied) return false;
            foreach (var effect in _effects) effect.AfterHit(context, ref hit);
            if (hit.ContinueFlight)
                foreach (var effect in _effects) effect.OnPierce(context, ref hit);
            else
                foreach (var effect in _effects) effect.OnTerminalHit(context, ref hit);
            return true;
        }

        public void Clear(BulletContext context)
        {
            foreach (var effect in _effects) effect.OnRecycle(context);
            _effects.Clear();
            _elements = BulletElement.None;
        }

        public bool TryBeginReturn(BulletContext context, BulletFlightEndReason reason)
        {
            if (context.IsReturning) return false;
            foreach (var effect in _effects)
                if (effect.TryBeginReturn(context, reason))
                {
                    foreach (var current in _effects) current.OnReturn(context);
                    return true;
                }
            return false;
        }
    }
}
