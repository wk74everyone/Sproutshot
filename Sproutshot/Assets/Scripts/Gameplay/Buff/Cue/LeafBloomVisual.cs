using Sproutshot.Utils;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    /// <summary>沿用项目叶片和终叶卡牌图案，叶刃围绕命中点绽放并向外散开。</summary>
    public sealed class LeafBloomVisual : WorldCueVisual
    {
        private const float Duration = .5f;
        private static Sprite _flower, _leaf, _spark;
        private static Material _material;
        private SpriteRenderer _core;
        private readonly SpriteRenderer[] _petals = new SpriteRenderer[8];
        private readonly SpriteRenderer[] _sparks = new SpriteRenderer[8];
        private RunContext _run;
        private float _elapsed, _radius;
        private bool _playing;

        private static bool LoadArt()
        {
            if (_flower != null && _leaf != null && _spark != null && _material != null) return true;
            var core = Resources.Load<Texture2D>("VFX/LeafBloomCore");
            var atlas = Resources.Load<Texture2D>("VFX/ThunderAtlas");
            _material = Resources.Load<Material>("VFX/ThunderVfx");
            if (core == null || atlas == null || _material == null) { Debug.LogError("[LeafBloomVisual] 缺少叶刃绽放素材。"); return false; }
            Sprite Slice(float x, float top, float width, float height) => Sprite.Create(atlas,
                new Rect(x / 1254f * atlas.width, (1254 - top - height) / 1254f * atlas.height, width / 1254f * atlas.width, height / 1254f * atlas.height),
                new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
            _flower = Sprite.Create(core, new Rect(0, 0, core.width, core.height), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
            _flower.name = "LastLeafFlower";
            _leaf = Slice(170, 805, 365, 340); _leaf.name = "BloomLeaf";
            _spark = Slice(770, 750, 345, 430); _spark.name = "BloomSpark";
            return true;
        }

        private SpriteRenderer Part(string name, Sprite sprite, int order)
        {
            var host = new GameObject(name); host.transform.SetParent(transform, false);
            var renderer = host.AddComponent<SpriteRenderer>(); renderer.sprite = sprite;
            renderer.sharedMaterial = _material; renderer.sortingOrder = order; return renderer;
        }

        public override bool Play(RunContext run, float radius, BulletElement elements = BulletElement.None)
        {
            if (run == null || run != RunContext.Current || !ThunderStrikeVisual.Valid(transform.position, radius) || !LoadArt()) return false;
            if (_core == null)
            {
                _core = Part("Leaf flower", _flower, 20003);
                for (int i = 0; i < _petals.Length; i++)
                {
                    _petals[i] = Part("Leaf blade " + i, _leaf, 20002);
                    _sparks[i] = Part("Dew sparkle " + i, _spark, 20001);
                }
            }
            _run = run; _radius = radius; _elapsed = 0; _playing = true;
            transform.localScale = Vector3.one; Animate(0); return true;
        }

        private static void Alpha(SpriteRenderer renderer, float alpha) => renderer.color = new Color(1, 1, 1, Mathf.Clamp01(alpha));
        private static void Size(SpriteRenderer renderer, float width, float height)
        {
            Vector3 size = renderer.sprite.bounds.size; renderer.transform.localScale = new Vector3(width / size.x, height / size.y, 1);
        }
        private void Animate(float time)
        {
            float t = Mathf.Clamp01(time / Duration), open = 1 - Mathf.Pow(1 - t, 3);
            float coreSize = Mathf.Lerp(.25f, 1.3f, Mathf.Clamp01(time / .11f));
            Size(_core, coreSize, coreSize); _core.transform.localRotation = Quaternion.Euler(0, 0, -t * 25);
            Alpha(_core, Mathf.Clamp01(1 - time / .3f));
            for (int i = 0; i < _petals.Length; i++)
            {
                float degrees = i * 45 + open * 20, angle = degrees * Mathf.Deg2Rad;
                Vector3 direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0);
                _petals[i].transform.localPosition = direction * _radius * Mathf.Lerp(.1f, .85f, open);
                _petals[i].transform.localRotation = Quaternion.Euler(0, 0, degrees - 45);
                float size = Mathf.Lerp(.75f, .35f, t); Size(_petals[i], size, size * .75f);
                Alpha(_petals[i], Mathf.Clamp01(1 - t * t));
                float starAngle = (degrees + 22.5f) * Mathf.Deg2Rad;
                _sparks[i].transform.localPosition = new Vector3(Mathf.Cos(starAngle), Mathf.Sin(starAngle), 0) * _radius * Mathf.Lerp(.2f, 1, open);
                float sparkle = .15f * Mathf.Sin(Mathf.Clamp01(time / .04f) * Mathf.PI * .5f) * (1 - t);
                Size(_sparks[i], sparkle, sparkle); Alpha(_sparks[i], 1 - t);
            }
        }
        private void Update() => Tick(Time.deltaTime);
        private void Tick(float dt)
        {
            if (!_playing) return;
            if (_run != RunContext.Current) { Recycle(); return; }
            if (Time.timeScale <= 0 || dt <= 0 || float.IsNaN(dt) || float.IsInfinity(dt)) return;
            _elapsed += dt; if (_elapsed >= Duration) { Recycle(); return; } Animate(_elapsed);
        }
        public override void Recycle()
        {
            ResetAll(); if (PoolManager.HasInstance) PoolManager.Instance.Put(gameObject);
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }
        public override void ResetAll()
        {
            _playing = false; _run = null; _elapsed = _radius = 0;
            if (_core == null) return; Alpha(_core, 0);
            foreach (var leaf in _petals) if (leaf != null) Alpha(leaf, 0);
            foreach (var spark in _sparks) if (spark != null) Alpha(spark, 0);
        }
        private void OnDisable() => ResetAll();
    }
}
