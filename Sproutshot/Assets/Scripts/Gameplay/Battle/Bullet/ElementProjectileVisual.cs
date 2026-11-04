using UnityEngine;

namespace Sproutshot.Gameplay
{
    /// <summary>只读取发射时的元素快照；复用现有图集和材质，不参与伤害或碰撞。</summary>
    public sealed class ElementProjectileVisual : MonoBehaviour
    {
        private static Sprite[] _sprites;
        private static Material _material;
        private readonly SpriteRenderer[,] _parts = new SpriteRenderer[3, 3];
        private readonly Vector3[] _history = new Vector3[8];
        private SpriteRenderer[] _originals;
        private bool[] _originalEnabled;
        private BulletElement _elements;
        private float _age, _sampleTime;
        private bool _playing;
        public BulletElement Elements => _elements;

        private static bool LoadArt()
        {
            if (_sprites != null && _sprites[0] != null && _material != null) return true;
            var atlas = Resources.Load<Texture2D>("VFX/ElementBurstAtlas");
            _material = Resources.Load<Material>("VFX/ThunderVfx");
            if (atlas == null || _material == null) return false;
            Sprite Slice(string name, float x, float top, float w, float h)
            {
                var sprite = Sprite.Create(atlas, new Rect(x / 1254 * atlas.width,
                    (1254 - top - h) / 1254 * atlas.height, w / 1254 * atlas.width, h / 1254 * atlas.height),
                    new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
                sprite.name = name; return sprite;
            }
            _sprites = new[] { Slice("ProjectileFire", 815, 160, 300, 430),
                Slice("ProjectileFrost", 225, 735, 305, 410), Slice("ProjectileLightning", 825, 755, 250, 370) };
            return true;
        }

        public void Play(BulletElement elements, Vector3 direction)
        {
            Clear();
            if (elements == BulletElement.None || !LoadArt()) return;
            if (_originals == null)
            {
                // 必须在创建特效子物体前收集，避免把自己的渲染器当成原始子弹隐藏。
                _originals = GetComponentsInChildren<SpriteRenderer>(true);
                _originalEnabled = new bool[_originals.Length];
            }
            for (int i = 0; i < _originals.Length; i++)
                if (_originals[i] != null) { _originalEnabled[i] = _originals[i].enabled; _originals[i].enabled = false; }
            _elements = elements; _age = _sampleTime = 0; _playing = true;
            for (int i = 0; i < _history.Length; i++) _history[i] = transform.position;
            for (int kind = 0; kind < 3; kind++)
                if (((int)elements & (1 << kind)) != 0)
                    for (int part = 0; part < 3; part++)
                    {
                        if (_parts[kind, part] == null)
                        {
                            var host = new GameObject("Projectile " + (BulletElement)(1 << kind) + " " + part);
                            host.transform.SetParent(transform, false);
                            var renderer = host.AddComponent<SpriteRenderer>();
                            renderer.sprite = _sprites[kind]; renderer.sharedMaterial = _material;
                            renderer.sortingLayerID = _originals.Length > 0 ? _originals[0].sortingLayerID : 0;
                            renderer.sortingOrder = 20005 - part;
                            _parts[kind, part] = renderer;
                        }
                        _parts[kind, part].enabled = true;
                    }
            Animate(direction);
        }

        public void Tick(float deltaTime, Vector3 direction)
        {
            if (!_playing || Time.timeScale <= 0 || deltaTime <= 0) return;
            _age += deltaTime; _sampleTime += deltaTime;
            if (_sampleTime >= .025f)
            {
                _sampleTime %= .025f;
                for (int i = _history.Length - 1; i > 0; i--) _history[i] = _history[i - 1];
                _history[0] = transform.position;
            }
            Animate(direction);
        }

        private void Animate(Vector3 direction)
        {
            if (direction.sqrMagnitude < .00001f) direction = Vector3.right;
            direction.Normalize();
            Vector3 side = new Vector3(-direction.y, direction.x, 0);
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            bool mixed = ((int)_elements & ((int)_elements - 1)) != 0;
            for (int kind = 0; kind < 3; kind++)
            {
                if (((int)_elements & (1 << kind)) == 0) continue;
                float pulse = 1 + .10f * Mathf.Sin(_age * (kind == 2 ? 48 : 22) + kind * 2);
                float offset = mixed ? (kind - 1) * .15f : 0;
                for (int part = 0; part < 3; part++)
                {
                    var renderer = _parts[kind, part];
                    float size = part == 0 ? 1 : part == 1 ? .55f : .30f;
                    Vector3 position = part == 0 ? transform.position : _history[part * 3];
                    position += side * (offset + (kind == 2 ? Mathf.Sin(_age * 55 + part * 2) * .035f : 0));
                    renderer.transform.position = position;
                    renderer.transform.rotation = Quaternion.Euler(0, 0, angle + (kind == 2 ? -45 : 90));
                    var bounds = renderer.sprite.bounds.size;
                    // 使用世界尺寸，避免子弹预制体缩放放大特效。
                    var parentScale = transform.lossyScale;
                    renderer.transform.localScale = new Vector3((kind == 0 ? .46f : .36f) * size * pulse / bounds.x / Mathf.Max(.001f, Mathf.Abs(parentScale.x)),
                        .70f * size * pulse / bounds.y / Mathf.Max(.001f, Mathf.Abs(parentScale.y)), 1);
                    renderer.color = new Color(1, 1, 1, part == 0 ? 1 : part == 1 ? .42f : .20f);
                }
            }
        }

        public void Clear()
        {
            for (int kind = 0; kind < 3; kind++)
                for (int part = 0; part < 3; part++)
                    if (_parts[kind, part] != null) _parts[kind, part].enabled = false;
            if (_playing && _originals != null)
                for (int i = 0; i < _originals.Length; i++)
                    if (_originals[i] != null) _originals[i].enabled = _originalEnabled[i];
            _playing = false; _elements = BulletElement.None; _age = _sampleTime = 0;
            System.Array.Clear(_history, 0, _history.Length);
        }
        private void OnDisable() => Clear();
    }
}
