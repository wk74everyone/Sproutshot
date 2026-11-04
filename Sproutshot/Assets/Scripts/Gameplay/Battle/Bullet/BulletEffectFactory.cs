using System;
using System.Collections.Generic;

namespace Sproutshot.Gameplay
{
    public static class BulletEffectFactory
    {
        // 键为 buff_attribute ID（不是卡牌 ID），只注册表中已配置的子弹行为。
        private static readonly Dictionary<int, Func<BulletBuffSnapshot, BulletEffect>> Constructors = new()
        {
            { 7, snapshot => new PierceEffect(snapshot) },
            { 11, snapshot => new PiercingMomentumEffect(snapshot) },
            { 12, snapshot => new FireBulletEffect(snapshot) },
            { 13, snapshot => new FrozenBulletEffect(snapshot) },
            { 14, snapshot => new ThunderBombBulletEffect(snapshot) },
            { 15, snapshot => new ElementBombBulletEffect(snapshot) },
            { 16, snapshot => new FirstStrikeBulletEffect(snapshot) },
            { 17, snapshot => new LastLeafBloomEffect(snapshot) },
            { 18, snapshot => new ReturningLeafEffect(snapshot) },
        };

        public static bool Supports(int buffId) => Constructors.ContainsKey(buffId);
        public static BulletEffect Create(BulletBuffSnapshot snapshot) =>
            Constructors.TryGetValue(snapshot.BuffId, out var create) ? create(snapshot) : null;
    }
}
