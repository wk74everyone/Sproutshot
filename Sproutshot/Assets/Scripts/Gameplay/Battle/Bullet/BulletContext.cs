using System.Collections.Generic;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    [System.Flags]
    public enum BulletElement { None = 0, Fire = 1, Frost = 2, Lightning = 4 }
    public enum BulletFlightEndReason { HitLimit, Range, Lifetime }

    /// <summary>同一轮子弹共享的状态。独立于发射器后续轮数，回收某枚子弹不会重置它。</summary>
    public sealed class BulletSalvo
    {
        public RunContext Run { get; }
        public Entity Owner { get; }
        public int Number { get; }
        private HashSet<int> _consumedEffects;

        public BulletSalvo(RunContext run, Entity owner, int number)
        {
            Run = run; Owner = owner; Number = number;
        }

        /// <summary>同轮同种效果只能领取一次；在结算伤害前领取，防止重复触发。</summary>
        public bool TryConsumeEffect(int buffId)
        {
            if (Number <= 0 || Run == null || Run != RunContext.Current) return false;
            _consumedEffects ??= new HashSet<int>();
            return _consumedEffects.Add(buffId);
        }
    }

    /// <summary>每枚子弹独有的运行数据，供各策略共同读取和修改。</summary>
    public sealed class BulletContext
    {
        public Entity Owner { get; private set; }
        public RunContext Run { get; private set; }
        public BulletSalvo Salvo { get; private set; }
        /// <summary>从本枚子弹的效果快照提取，不随主人后续增删 Buff 改变。</summary>
        public BulletElement Elements { get; internal set; }
        public float LaunchDamage { get; private set; }
        public bool IsReturning { get; private set; }
        /// <summary>本段飞行的基础伤害；返程时应用回程比例，穿透等增幅仍以此为基准。</summary>
        public float BaseDamage { get; private set; }
        public float Damage { get; set; }
        public bool HomingEnabled { get; set; }

        public void Initialize(Entity owner, RunContext run, float damage, BulletSalvo salvo = null)
        {
            Owner = owner; Run = run; Salvo = salvo;
            Elements = BulletElement.None;
            LaunchDamage = BaseDamage = Damage = damage; HomingEnabled = true; IsReturning = false;
        }

        internal bool BeginReturn(float ratio)
        {
            if (IsReturning || ratio <= 0 || float.IsNaN(ratio) || float.IsInfinity(ratio)) return false;
            IsReturning = true;
            HomingEnabled = false;
            BaseDamage = Damage = LaunchDamage * ratio;
            return true;
        }

        public void Reset()
        {
            Owner = null; Run = null; Salvo = null;
            Elements = BulletElement.None;
            LaunchDamage = BaseDamage = Damage = 0; HomingEnabled = true; IsReturning = false;
        }
    }

    public struct BulletHitContext
    {
        public readonly EnemyEntity Target;
        public readonly uint TargetLife;
        public readonly Vector3 Position;
        public float Damage;
        public bool ContinueFlight;
        public bool DamageApplied { get; internal set; }

        public BulletHitContext(EnemyEntity target, uint life, Vector3 position, float damage)
        {
            Target = target; TargetLife = life; Position = position;
            Damage = damage; ContinueFlight = false; DamageApplied = false;
        }
    }
}
