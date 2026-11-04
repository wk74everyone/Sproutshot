using Sproutshot.Utils;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Sproutshot.Gameplay
{
    public enum CueType { Burn, Frost, Poison, Shock, ThunderStrike, ElementBurst, LeafBloom, SpiritFrost, SpiritScatter, SpiritHeal }
    public enum CueEvent { OnActive, WhileActive, Executed, Removed }

    /// <summary>分发表现通知；持续状态按目标和表现类型独立保存，不参与伤害结算。</summary>
    public sealed class CueManager : SingletonMono<CueManager>
    {
        private sealed class Entry
        {
            public BaseCue Cue;
            public uint Life;
            public bool BuffOwned, ManualOwned;
            public float BuffIntensity, ManualIntensity;
            public float Intensity => Mathf.Max(BuffOwned ? BuffIntensity : 0, ManualOwned ? ManualIntensity : 0);
        }

        private static readonly Dictionary<int, CueType> DefaultBuffs = new()
        { { 1, CueType.Burn }, { 2, CueType.Poison }, { 3, CueType.Frost },{ 4, CueType.Frost }, { 19, CueType.Frost } };
        private readonly Dictionary<int, CueType> _buffMappings = new(DefaultBuffs);
        private readonly Dictionary<CueType, BaseCue> _prefabs = new();
        private readonly Dictionary<(Entity target, CueType type), Entry> _active = new();
        private readonly List<(Entity target, CueType type)> _cleanup = new();
        private readonly List<WorldCueVisual> _worldCues = new();
        private readonly Dictionary<CueType, GameObject> _worldTemplates = new();
        public int ActiveCount => _active.Count;

        protected override void Awake()
        {
            base.Awake();
            if (Instance == this) SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        private void OnEnable()
        {
            if (!HasInstance || Instance != this) return;
            // 管理器晚于状态创建或重新启用时，从 ASC 补读快照。
            foreach (var asc in FindObjectsOfType<SimpleASC>())
            {
                var target = asc.GetComponent<Entity>();
                if (target != null) SyncBuffs(target, asc.ActiveBuffs);
            }
        }

        private static void Invoke(BaseCue cue, System.Action<BaseCue> callback)
        {
            if (cue == null) return;
            try { callback(cue); }
            catch (System.Exception error) { Debug.LogException(error, cue); }
        }

        /// <summary>注册可选处理器预制体；后续创建实例时使用，现有实例保持原样。</summary>
        public void Register(CueType type, BaseCue prefab)
        {
            if (prefab == null) throw new System.ArgumentNullException(nameof(prefab));
            _prefabs[type] = prefab;
        }

        /// <summary>允许多个状态映射到同一种表现；每次同步取仍存活状态的最大强度。</summary>
        public void RegisterBuff(int buffId, CueType type) => _buffMappings[buffId] = type;

        internal static bool HasDefaultBuff(IReadOnlyList<ActiveBuff> buffs)
        {
            foreach (var buff in buffs) if (DefaultBuffs.ContainsKey(buff.BuffDefinition.Id)) return true;
            return false;
        }

        private static bool ValidTarget(Entity target) => target != null && target.isActiveAndEnabled &&
            !(target is EnemyEntity enemy && enemy.IsDead);
        private static uint Life(Entity target) => target is EnemyEntity enemy ? enemy.LifeVersion : 0;
        private static float Normalize(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0 : Mathf.Clamp01(value);

        private BaseCue Create(Entity target, CueType type)
        {
            BaseCue cue;
            if (_prefabs.TryGetValue(type, out var prefab) && prefab != null) cue = Instantiate(prefab, target.transform);
            else
            {
                var host = new GameObject("Cue_" + type);
                host.transform.SetParent(target.transform, false);
                cue = host.AddComponent<CharacterMaterialCue>();
            }
            cue.Configure(target, type, 0);
            cue.gameObject.SetActive(true);
            return cue;
        }

        private Entry GetEntry(Entity target, CueType type, out bool created)
        {
            var key = (target, type);
            //如果是从对象池，重新取出来，却仍处于_active，就需要先移除，然后重新加入，防止表现效果沿用上一个entity的
            if (_active.TryGetValue(key, out var entry) && (entry.Cue == null || entry.Life != Life(target))) RemoveEntry(key);
            created = !_active.TryGetValue(key, out entry);
            if (created) { entry = new Entry { Cue = Create(target, type), Life = Life(target) }; _active.Add(key, entry); }
            return entry;
        }

        private void UpdateEntry((Entity target, CueType type) key, Entry entry, bool activate)
        {
            if (!entry.BuffOwned && !entry.ManualOwned) { RemoveEntry(key); return; }
            entry.Cue.Configure(key.target, key.type, entry.Intensity);
            if (activate) Invoke(entry.Cue, cue => cue.OnActive());
            Invoke(entry.Cue, cue => cue.WhileActive());
        }

        public void Handle(Entity target, CueType type, CueEvent cueEvent, float intensity = 1)
        {
            var key = (target, type);
            if (cueEvent == CueEvent.Removed)
            {
                if (_active.TryGetValue(key, out var old)) { old.ManualOwned = false; UpdateEntry(key, old, false); }
                return;
            }
            if (!ValidTarget(target) || !isActiveAndEnabled) return;
            intensity = Normalize(intensity);
            if (cueEvent == CueEvent.Executed)
            {
                // 瞬时事件不创建持续状态；处理器可自行生成独立粒子或播放音效。
                if (_active.TryGetValue(key, out var stale) && (stale.Cue == null || stale.Life != Life(target))) RemoveEntry(key);
                bool temporary = !_active.TryGetValue(key, out var current);
                var cue = temporary ? Create(target, type) : current.Cue;
                cue.Configure(target, type, intensity);
                try { Invoke(cue, handler => handler.OnExecute()); }
                finally
                {
                    if (temporary) Destroy(cue.gameObject);
                    else if (cue != null) cue.Configure(target, type, current.Intensity);
                }
                return;
            }
            var entry = GetEntry(target, type, out bool created);
            entry.ManualOwned = true; entry.ManualIntensity = intensity;
            UpdateEntry(key, entry, created && cueEvent == CueEvent.OnActive);
        }

        /// <summary>在固定命中点播放瞬时表现，不依附敌人，敌人死亡或回池也不会截断落雷。</summary>
        public void ExecuteAt(CueType type, RunContext run, Vector3 position, float radius, BulletElement elements = BulletElement.None)
        {
            bool spirit = type == CueType.SpiritFrost || type == CueType.SpiritScatter || type == CueType.SpiritHeal;
            if ((!spirit && type != CueType.ThunderStrike && type != CueType.ElementBurst && type != CueType.LeafBloom) || !isActiveAndEnabled || run == null ||
                run != RunContext.Current || !ThunderStrikeVisual.Valid(position, radius)) return;
            if (type == CueType.ElementBurst && elements == BulletElement.None) return;
            PruneWorldCues();
            // 限制极端情况下的同时播放数量，不影响伤害结算。
            if (_worldCues.Count >= 24) return;
            try
            {
                if (!_worldTemplates.TryGetValue(type, out var template) || template == null)
                {
                    template = new GameObject(type + "Template");
                    template.SetActive(false);
                    template.transform.SetParent(transform, false);
                    if (type == CueType.ThunderStrike)
                    {
                        template.AddComponent<ThunderStrikeVisual>();
                    }
                    else if (type == CueType.ElementBurst)
                    {
                        template.AddComponent<ElementBurstVisual>();
                    }
                    else if (type == CueType.LeafBloom)
                    {
                        template.AddComponent<LeafBloomVisual>();
                    }
                    else template.AddComponent<SpiritSkillVisual>();
                    _worldTemplates[type] = template;
                }
                var visual = PoolManager.Instance.Get(template, position, Quaternion.identity).GetComponent<WorldCueVisual>();
                if (visual is SpiritSkillVisual skillVisual) skillVisual.Kind = type;
                if (visual.Play(run, radius, elements))
                {
                    _worldCues.Add(visual);
                    if (type != CueType.SpiritHeal) AudioManager.Instance.PlaySfx(type == CueType.SpiritScatter ? AudioId.SeedShoot : AudioId.MagicBurst,
                        type == CueType.LeafBloom ? .85f : 1,
                        type == CueType.ThunderStrike ? 1.05f : type == CueType.LeafBloom ? .94f : 1);
                }
                else visual.Recycle();
            }
            catch (System.Exception error) { Debug.LogException(error, this); }
        }

        private void PruneWorldCues()
        {
            for (int i = _worldCues.Count - 1; i >= 0; i--)
                if (_worldCues[i] == null || !_worldCues[i].isActiveAndEnabled) _worldCues.RemoveAt(i);
        }

        internal void SyncBuffs(Entity target, IReadOnlyList<ActiveBuff> buffs)
        {
            if (!ValidTarget(target)) { ClearTarget(target); return; }
            var desired = new Dictionary<CueType, float>();
            foreach (var buff in buffs)
            {
                if (buff.StackCount <= 0 || (!buff.IsPermanent && buff.RemainingTime <= 0) ||
                    !_buffMappings.TryGetValue(buff.BuffDefinition.Id, out var type)) continue;
                int full = buff.BuffDefinition.MaxStack > 0 ? buff.BuffDefinition.MaxStack : 5;
                float strength = full <= 1 ? 1 : Mathf.Lerp(.3f, 1, Mathf.Clamp01((buff.StackCount - 1f) / (full - 1)));
                desired[type] = desired.TryGetValue(type, out float previous) ? Mathf.Max(previous, strength) : strength;
            }
            var removed = new List<(Entity target, CueType type)>();
            foreach (var pair in _active)
                if (ReferenceEquals(pair.Key.target, target) && pair.Value.BuffOwned && !desired.ContainsKey(pair.Key.type)) removed.Add(pair.Key);
            foreach (var key in removed)
                if (_active.TryGetValue(key, out var entry)) { entry.BuffOwned = false; UpdateEntry(key, entry, false); }
            foreach (var pair in desired)
            {
                var entry = GetEntry(target, pair.Key, out bool created);
                entry.BuffOwned = true; entry.BuffIntensity = pair.Value;
                UpdateEntry((target, pair.Key), entry, created);
            }
        }

        internal void ExecuteBuff(Entity target, ActiveBuff buff)
        {
            if (_buffMappings.TryGetValue(buff.BuffDefinition.Id, out var type))
                Handle(target, type, CueEvent.Executed, _active.TryGetValue((target, type), out var entry) ? entry.Intensity : 1);
        }

        private void RemoveEntry((Entity target, CueType type) key)
        {
            if (!_active.TryGetValue(key, out var entry)) return;
            _active.Remove(key);
            if (entry.Cue == null) return;
            Invoke(entry.Cue, cue => cue.OnRemove());
            Destroy(entry.Cue.gameObject);
        }

        public void ClearTarget(Entity target)
        {
            var keys = new List<(Entity target, CueType type)>();
            foreach (var key in _active.Keys) if (ReferenceEquals(key.target, target)) keys.Add(key);
            foreach (var key in keys) RemoveEntry(key);
        }

        public void ClearAll()
        {
            foreach (var key in new List<(Entity target, CueType type)>(_active.Keys)) RemoveEntry(key);
            foreach (var visual in _worldCues) if (visual != null) visual.Recycle();
            _worldCues.Clear();
        }

        private void Prune(Scene? unloaded = null)
        {
            _cleanup.Clear();
            foreach (var pair in _active)
                if (!ValidTarget(pair.Key.target) || pair.Value.Cue == null || Life(pair.Key.target) != pair.Value.Life ||
                    (unloaded.HasValue && pair.Key.target.gameObject.scene == unloaded.Value)) _cleanup.Add(pair.Key);
            foreach (var key in _cleanup) RemoveEntry(key);
            _cleanup.Clear();
        }

        private void LateUpdate() { Prune(); PruneWorldCues(); }
        private void OnSceneUnloaded(Scene scene) => Prune(scene);
        private void OnDisable() => ClearAll();
        protected override void OnDestroy()
        {
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            ClearAll();
            base.OnDestroy();
        }
    }
}
