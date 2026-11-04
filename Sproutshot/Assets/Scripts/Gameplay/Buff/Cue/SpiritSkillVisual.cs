using Sproutshot.Utils;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    /// <summary>霜圈、施法叶光、治疗露滴。只有表现，技能结算不依赖特效是否播放成功。</summary>
    public sealed class SpiritSkillVisual : WorldCueVisual
    {
        public CueType Kind { get; set; }
        private SpriteRenderer _ring, _echo, _drop;
        private readonly SpriteRenderer[] _motes = new SpriteRenderer[12];
        private RunContext _run;
        private PlayerEntity _follow;
        private float _elapsed, _radius;
        private bool _playing;
        private float Duration => Kind == CueType.SpiritHeal ? .8f : Kind == CueType.SpiritFrost ? .65f : .4f;

        private SpriteRenderer Part(string name, Sprite sprite, int order)
        {
            var host = new GameObject(name); host.transform.SetParent(transform, false);
            var renderer = host.AddComponent<SpriteRenderer>(); renderer.sprite = sprite;
            renderer.sharedMaterial = SpiritSkillArt.Material; renderer.sortingOrder = order;
            return renderer;
        }

        public override bool Play(RunContext run, float radius, BulletElement elements = BulletElement.None)
        {
            if (run == null || run != RunContext.Current || !ThunderStrikeVisual.Valid(transform.position, radius) || !SpiritSkillArt.Load()) return false;
            if (_ring == null)
            {
                _ring = Part("Dew ring", SpiritSkillArt.Ring, 20002);
                _echo = Part("Dew echo", SpiritSkillArt.Ring, 20001);
                _drop = Part("Healing dew", SpiritSkillArt.Drop, 20004);
                for (int i = 0; i < _motes.Length; i++) _motes[i] = Part("Spirit mote " + i, SpiritSkillArt.Spark, 20003);
            }
            _run = run; _follow = Kind == CueType.SpiritHeal ? run.PlayerEntity : null;
            _radius = radius; _elapsed = 0; _playing = true; transform.localScale = Vector3.one;
            foreach (var mote in _motes) mote.sprite = Kind == CueType.SpiritScatter ? SpiritSkillArt.Leaf : SpiritSkillArt.Spark;
            Animate(); return true;
        }

        private static void Color(SpriteRenderer renderer, Color tint, float alpha) { tint.a *= Mathf.Clamp01(alpha); renderer.color = tint; }
        private void Animate()
        {
            float t = Mathf.Clamp01(_elapsed / Duration), expand = 1 - Mathf.Pow(1 - t, 3);
            bool frost = Kind == CueType.SpiritFrost, heal = Kind == CueType.SpiritHeal;
            var tint = frost ? UnityEngine.Color.white : new Color(.65f, 1f, .62f);
            float diameter = frost ? Mathf.Lerp(.3f, _radius * 2 / .89f, expand) : Mathf.Lerp(.2f, _radius * 2, expand);
            SpiritSkillArt.Size(_ring, diameter, diameter); Color(_ring, tint, .9f * (1 - t));
            float echo = Mathf.Max(.1f, diameter * .82f); SpiritSkillArt.Size(_echo, echo, echo); Color(_echo, tint, .45f * (1 - t));
            _drop.enabled = heal;
            if (heal)
            {
                _drop.transform.localPosition = new Vector3(0, Mathf.Lerp(1.25f, .2f, Mathf.Clamp01(t / .65f)), 0);
                SpiritSkillArt.Size(_drop, .42f, .58f); Color(_drop, new Color(.8f, 1f, .85f), 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.65f, 1, t)));
            }
            for (int i = 0; i < _motes.Length; i++)
            {
                float angle = (i * 30 + t * (heal ? 50 : 15)) * Mathf.Deg2Rad;
                float spread = frost ? _radius * .92f * expand : _radius * Mathf.Lerp(.15f, 1, expand);
                _motes[i].transform.localPosition = new Vector3(Mathf.Cos(angle) * spread, Mathf.Sin(angle) * spread + (heal ? t * .5f : 0), 0);
                _motes[i].transform.localRotation = Quaternion.Euler(0, 0, i * 30 - 45);
                float size = (frost ? .16f : heal ? .13f : .3f) * (1 - t * .6f);
                SpiritSkillArt.Size(_motes[i], size, size); Color(_motes[i], tint, 1 - t);
            }
        }
        private void Update() => Tick(Time.deltaTime);
        private void Tick(float dt)
        {
            if (!_playing) return;
            if (_run != RunContext.Current || _run.LevelContext.IsStopped ||
                (Kind == CueType.SpiritHeal && (_follow == null || !_follow.isActiveAndEnabled))) { Recycle(); return; }
            if (Time.timeScale <= 0 || dt <= 0 || float.IsNaN(dt) || float.IsInfinity(dt)) return;
            if (_follow != null) transform.position = _follow.transform.position;
            _elapsed += dt; if (_elapsed >= Duration) { Recycle(); return; } Animate();
        }
        public override void Recycle()
        {
            ResetAll(); if (PoolManager.HasInstance) PoolManager.Instance.Put(gameObject);
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }
        public override void ResetAll()
        {
            _playing = false; _run = null; _follow = null; _elapsed = _radius = 0;
            if (_ring == null) return;
            Color(_ring, UnityEngine.Color.white, 0); Color(_echo, UnityEngine.Color.white, 0); Color(_drop, UnityEngine.Color.white, 0);
            foreach (var mote in _motes) if (mote != null) Color(mote, UnityEngine.Color.white, 0);
        }
        private void OnDisable() => ResetAll();
    }
}
