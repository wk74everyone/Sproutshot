using Sproutshot.Utils;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    /// <summary>元素四溅：局部冲击花、向四周喷散的火滴/霜滴/电光。仅播放子弹实际携带的元素。</summary>
    public sealed class ElementBurstVisual : WorldCueVisual
    {
        private const float Duration = .58f;
        private static Sprite[] _sprites;
        private static Material _material;
        private SpriteRenderer _core, _echo;
        private readonly SpriteRenderer[] _drops = new SpriteRenderer[18];
        private readonly int[] _kinds = new int[3];
        private RunContext _run;
        private float _elapsed, _radius;
        private bool _playing;

        private static bool LoadArt()
        {
            if (_sprites != null && _sprites[0] != null && _material != null) return true;
            var atlas = Resources.Load<Texture2D>("VFX/ElementBurstAtlas");
            _material = Resources.Load<Material>("VFX/ThunderVfx");
            if (atlas == null || _material == null)
            {
                Debug.LogError("[ElementBurstVisual] 缺少 Resources/VFX/ElementBurstAtlas 或共享特效材质。");
                return false;
            }
            Sprite Slice(string name, float x, float top, float width, float height)
            {
                var rect = new Rect(x / 1254f * atlas.width, (1254 - top - height) / 1254f * atlas.height,
                    width / 1254f * atlas.width, height / 1254f * atlas.height);
                var sprite = Sprite.Create(atlas, rect, new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
                sprite.name = name; return sprite;
            }
            _sprites = new[] {
                Slice("ElementImpact", 110, 130, 490, 450),
                Slice("FireDroplet", 815, 160, 300, 430),
                Slice("FrostDroplet", 225, 735, 305, 410),
                Slice("ElectricShard", 825, 755, 250, 370)
            };
            return true;
        }

        private SpriteRenderer Part(string name, int order)
        {
            var host = new GameObject(name); host.transform.SetParent(transform, false);
            var renderer = host.AddComponent<SpriteRenderer>();
            renderer.sharedMaterial = _material; renderer.sortingOrder = order;
            return renderer;
        }

        public override bool Play(RunContext run, float radius, BulletElement elements = BulletElement.None)
        {
            if (run == null || run != RunContext.Current || !ThunderStrikeVisual.Valid(transform.position, radius) || !LoadArt()) return false;
            int count = 0;
            if ((elements & BulletElement.Fire) != 0) _kinds[count++] = 1;
            if ((elements & BulletElement.Frost) != 0) _kinds[count++] = 2;
            if ((elements & BulletElement.Lightning) != 0) _kinds[count++] = 3;
            if (count == 0) return false;
            if (_core == null)
            {
                _echo = Part("Splash afterimage", 20000); _echo.sprite = _sprites[0];
                _core = Part("Element impact", 20003); _core.sprite = _sprites[0];
                for (int i = 0; i < _drops.Length; i++) _drops[i] = Part("Element droplet " + i, 20002);
            }
            // 复用时重选全部粒子的类型，防止上一枚子弹的火/冰元素残留。
            for (int i = 0; i < _drops.Length; i++) _drops[i].sprite = _sprites[_kinds[i % count]];
            _run = run; _radius = radius; _elapsed = 0; _playing = true;
            transform.localScale = Vector3.one;
            Animate(0);
            return true;
        }

        private static void Alpha(SpriteRenderer renderer, float value) => renderer.color = new Color(1, 1, 1, Mathf.Clamp01(value));
        private static void Size(SpriteRenderer renderer, float width, float height)
        {
            Vector3 size = renderer.sprite.bounds.size;
            renderer.transform.localScale = new Vector3(width / size.x, height / size.y, 1);
        }

        private void Animate(float time)
        {
            float t = Mathf.Clamp01(time / Duration);
            float spread = 1 - Mathf.Pow(1 - t, 2.5f);
            float impactSize = Mathf.Lerp(.28f, 1.25f, Mathf.Clamp01(time / .14f));
            Size(_core, impactSize, impactSize);
            _core.transform.localRotation = Quaternion.Euler(0, 0, t * 35);
            Alpha(_core, Mathf.Clamp01(1 - time / .23f) * .9f);
            float echoSize = Mathf.Lerp(.5f, Mathf.Min(_radius * 1.3f, 3.2f), spread);
            Size(_echo, echoSize, echoSize);
            _echo.transform.localRotation = Quaternion.Euler(0, 0, -t * 25);
            Alpha(_echo, .24f * Mathf.Pow(1 - t, 2));

            for (int i = 0; i < _drops.Length; i++)
            {
                var drop = _drops[i];
                float degrees = i * 137.508f + 11;
                float angle = degrees * Mathf.Deg2Rad;
                float distance = _radius * Mathf.Lerp(.05f, .62f + (i % 4) * .1f, spread);
                float arc = .2f * Mathf.Sin(t * Mathf.PI);
                drop.transform.localPosition = new Vector3(Mathf.Cos(angle) * distance, Mathf.Sin(angle) * distance + arc, 0);
                float originalAngle = drop.sprite == _sprites[3] ? 45 : 90;
                drop.transform.localRotation = Quaternion.Euler(0, 0, degrees - originalAngle);
                float length = (.30f + (i % 3) * .07f) * Mathf.Lerp(1, .45f, t);
                Size(drop, length * .65f, length * (1 + .55f * Mathf.Sin(t * Mathf.PI)));
                Alpha(drop, Mathf.Min(1, .65f + time * 8) * Mathf.Pow(1 - t, .8f));
            }
        }

        private void Update() => Tick(Time.deltaTime);
        private void Tick(float deltaTime)
        {
            if (!_playing) return;
            if (_run != RunContext.Current) { Recycle(); return; }
            if (Time.timeScale <= 0 || deltaTime <= 0 || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;
            _elapsed += deltaTime;
            if (_elapsed >= Duration) { Recycle(); return; }
            Animate(_elapsed);
        }

        public override void Recycle()
        {
            ResetAll();
            if (PoolManager.HasInstance) PoolManager.Instance.Put(gameObject);
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        public override void ResetAll()
        {
            _playing = false; _run = null; _elapsed = _radius = 0;
            if (_core == null) return;
            Alpha(_core, 0); Alpha(_echo, 0);
            foreach (var drop in _drops) if (drop != null) Alpha(drop, 0);
        }
        private void OnDisable() => ResetAll();
    }
}
