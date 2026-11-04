using Sproutshot.Utils;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    /// <summary>固定世界位置的手绘落雷表现。只负责动画，不查询敌人、不修改属性。</summary>
    public sealed class ThunderStrikeVisual : WorldCueVisual
    {
        private const float Duration = .56f;
        private static Sprite[] _sprites;
        private static Material _material;
        private SpriteRenderer _bolt, _ring, _flash;
        private readonly SpriteRenderer[] _motes = new SpriteRenderer[10];
        private RunContext _run;
        private float _elapsed, _radius, _height;
        private bool _playing;

        internal static bool Valid(Vector3 point, float radius) => radius > 0 && Finite(radius) &&
            Finite(point.x) && Finite(point.y) && Finite(point.z);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool LoadArt()
        {
            if (_sprites != null && _sprites[0] != null && _material != null) return true;
            var atlas = Resources.Load<Texture2D>("VFX/ThunderAtlas");
            _material = Resources.Load<Material>("VFX/ThunderVfx");
            if (atlas == null || _material == null)
            {
                Debug.LogError("[ThunderStrikeVisual] 缺少 Resources/VFX/ThunderAtlas 或 ThunderVfx 材质。");
                return false;
            }
            // 原图为 1254×1254，保留原始透明通道；用规范化矩形兼容平台导入缩放。
            Sprite Slice(string name, float x, float top, float width, float height, Vector2 pivot)
            {
                var rect = new Rect(x / 1254f * atlas.width, (1254 - top - height) / 1254f * atlas.height,
                    width / 1254f * atlas.width, height / 1254f * atlas.height);
                var sprite = Sprite.Create(atlas, rect, pivot, 100, 0, SpriteMeshType.FullRect);
                sprite.name = name;
                return sprite;
            }
            _sprites = new[] {
                Slice("ThunderBolt", 160, 5, 320, 675, new Vector2(.5f, .02f)),
                Slice("ThunderRing", 675, 105, 550, 535, new Vector2(.5f, .5f)),
                Slice("ThunderLeaf", 170, 805, 365, 340, new Vector2(.5f, .5f)),
                Slice("ThunderSpark", 770, 750, 345, 430, new Vector2(.5f, .5f))
            };
            return true;
        }

        private SpriteRenderer Part(string name, Sprite sprite, int order)
        {
            var host = new GameObject(name);
            host.transform.SetParent(transform, false);
            var renderer = host.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sharedMaterial = _material;
            renderer.sortingOrder = order;
            return renderer;
        }

        public override bool Play(RunContext run, float radius, BulletElement elements = BulletElement.None)
        {
            if (run == null || run != RunContext.Current || !Valid(transform.position, radius) || !LoadArt()) return false;
            if (_bolt == null)
            {
                _ring = Part("Ground shock ring", _sprites[1], 20000);
                _bolt = Part("Branch lightning", _sprites[0], 20002);
                _flash = Part("Impact star", _sprites[3], 20003);
                for (int i = 0; i < _motes.Length; i++)
                    _motes[i] = Part(i < 4 ? "Leaf" : "Spark", _sprites[i < 4 ? 2 : 3], 20001);
            }
            _run = run;
            _radius = radius;
            _height = Mathf.Clamp(radius * 1.35f, 2.6f, 4f);
            _elapsed = 0;
            _playing = true;
            transform.localScale = Vector3.one;
            Animate(0);
            return true;
        }

        private void Update() => Tick(Time.deltaTime);
        private void Tick(float deltaTime)
        {
            if (!_playing) return;
            if (_run != RunContext.Current) { Recycle(); return; }
            if (Time.timeScale <= 0 || deltaTime <= 0 || !Finite(deltaTime)) return;
            _elapsed += deltaTime;
            if (_elapsed >= Duration) { Recycle(); return; }
            Animate(_elapsed);
        }

        private static void Alpha(SpriteRenderer renderer, float alpha) => renderer.color = new Color(1, 1, 1, Mathf.Clamp01(alpha));
        private static void Size(SpriteRenderer renderer, float width, float height)
        {
            var size = renderer.sprite.bounds.size;
            renderer.transform.localScale = new Vector3(width / size.x, height / size.y, 1);
        }

        private void Animate(float time)
        {
            float t = Mathf.Clamp01(time / Duration);
            float ease = 1 - Mathf.Pow(1 - t, 3);
            // 主落雷一闪、余电再闪，避免整段时间遮住角色与弹道。
            float boltAlpha = time < .13f ? 1 - time / .16f :
                time < .18f ? 0 : Mathf.Clamp01(1 - (time - .18f) / .12f) * .65f;
            Alpha(_bolt, boltAlpha);
            _bolt.transform.localPosition = Vector3.up * Mathf.Lerp(.12f, 0, Mathf.Clamp01(time / .055f));
            _bolt.transform.localRotation = Quaternion.Euler(0, 0, time < .18f ? -3 : 2);
            Size(_bolt, _height * .48f, _height);

            // 外圈到达实际伤害半径后渐隐，中间保持透明。
            float ringDiameter = _radius * 2 * Mathf.Lerp(.35f, 1, Mathf.Clamp01(time / .18f));
            Size(_ring, ringDiameter, ringDiameter);
            _ring.transform.localRotation = Quaternion.Euler(0, 0, ease * 18);
            Alpha(_ring, .58f * Mathf.Pow(1 - t, 1.3f));

            float flashSize = Mathf.Lerp(.3f, .85f, Mathf.Clamp01(time / .09f));
            Size(_flash, flashSize, flashSize);
            Alpha(_flash, Mathf.Clamp01(1 - time / .16f) * .85f);
            for (int i = 0; i < _motes.Length; i++)
            {
                var mote = _motes[i];
                float angle = (i * 137.508f + 17) * Mathf.Deg2Rad;
                float distance = _radius * Mathf.Lerp(.12f, i < 4 ? .7f : .9f, ease);
                mote.transform.localPosition = new Vector3(Mathf.Cos(angle) * distance, Mathf.Sin(angle) * distance, 0);
                mote.transform.localRotation = Quaternion.Euler(0, 0, i * 47 + ease * (i % 2 == 0 ? 100 : -100));
                float size = (i < 4 ? .28f : .16f) * Mathf.Lerp(1, .35f, t);
                Size(mote, size, size);
                Alpha(mote, .85f * Mathf.Sin(Mathf.Clamp01(time / .035f) * Mathf.PI * .5f) * (1 - t));
            }
        }

        public override void Recycle()
        {
            ResetAll();
            if (PoolManager.HasInstance) PoolManager.Instance.Put(gameObject);
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        public override void ResetAll()
        {
            _playing = false;
            _run = null;
            _elapsed = _radius = _height = 0;
            if (_bolt == null) return;
            Alpha(_bolt, 0); Alpha(_ring, 0); Alpha(_flash, 0);
            foreach (var mote in _motes) if (mote != null) Alpha(mote, 0);
        }

        private void OnDisable() => ResetAll();
    }
}
