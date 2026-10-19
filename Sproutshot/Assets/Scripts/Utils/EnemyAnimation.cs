using UnityEngine;

namespace Sproutshot.Utils
{
    /// <summary>只改变外观子物体，不缩放碰撞体；由状态推进，不创建协程或临时材质。</summary>
    public class EnemyAnimation : MonoBehaviour, IPoolable
    {
        [SerializeField] private Transform _visual;
        [SerializeField] private SpriteRenderer _renderer;
        [SerializeField, Min(0)] private float _swayAngle = 5f;
        [SerializeField, Min(0)] private float _swayFrequency = 3f;
        [SerializeField, Min(0)] private float _pullback = .08f;
        [SerializeField] private Color _warningColor = new Color(1f, .25f, .04f, 1f);

        private Vector3 _position, _scale;
        private Quaternion _rotation;
        private Color _color;
        private bool _initialized;
        private float _chaseTime;
        private MaterialPropertyBlock _properties;
        private static readonly int FlashColor = Shader.PropertyToID("_FlashColor");
        private static readonly int FlashAmount = Shader.PropertyToID("_FlashAmount");

        private void Awake() => Initialize();

        private bool Initialize()
        {
            if (_initialized) return true;
            if (_renderer == null) _renderer = GetComponentInChildren<SpriteRenderer>(true);
            if (_visual == null && _renderer != null) _visual = _renderer.transform;
            // 防止错误配置把根物体及其碰撞体一起压扁。
            if (_visual == null || _visual == transform || !_visual.IsChildOf(transform)) return false;
            _position = _visual.localPosition;
            _rotation = _visual.localRotation;
            _scale = _visual.localScale;
            _color = _renderer != null ? _renderer.color : Color.white;
            _properties = new MaterialPropertyBlock();
            _initialized = true;
            return true;
        }

        public void PlayChase(Vector2 direction, float deltaTime)
        {
            if (!Initialize()) return;
            if (direction.sqrMagnitude < .000001f) { ResetAll(); return; }
            _chaseTime += Mathf.Max(0, deltaTime);
            float sway = Mathf.Sin(_chaseTime * _swayFrequency * Mathf.PI * 2);
            Pose(direction, Vector2.one, 0, sway * _swayAngle);
            SetFlash(0);
        }

        public void PlayWindup(Vector2 direction, float progress)
        {
            if (!Initialize()) return;
            float t = Mathf.SmoothStep(0, 1, Mathf.Clamp01(progress));
            Pose(direction, Vector2.Lerp(Vector2.one, new Vector2(1.15f, .78f), t), -_pullback * t);
            // 预警在蓄力中段亮起，结束时仍保持可见。
            SetFlash(.75f * Mathf.Sin(Mathf.Clamp01(progress) * Mathf.PI * .8f));
        }

        public void PlayLunge(Vector2 direction, float progress)
        {
            if (!Initialize()) return;
            float t = Mathf.Clamp01(progress);
            Pose(direction, new Vector2(.82f, 1.25f), -_pullback * (1 - t));
            SetFlash(.45f * (1 - t));
        }

        public void PlayRecovery(Vector2 direction, float progress)
        {
            if (!Initialize()) return;
            float t = Mathf.SmoothStep(0, 1, Mathf.Clamp01(progress));
            Pose(direction, Vector2.Lerp(new Vector2(.82f, 1.25f), Vector2.one, t), 0);
            SetFlash(0);
        }

        public void PlayDeath(float progress)
        {
            if (!Initialize()) return;
            float t = Mathf.SmoothStep(0, 1, Mathf.Clamp01(progress));
            _visual.localPosition = _position;
            _visual.localRotation = _rotation;
            _visual.localScale = new Vector3(_scale.x * Mathf.Lerp(1, 1.2f, t),
                _scale.y * Mathf.Lerp(1, .15f, t), _scale.z);
            if (_renderer != null)
                _renderer.color = new Color(_color.r, _color.g, _color.b, _color.a * (1 - t));
            SetFlash(0);
        }

        private void Pose(Vector2 worldDirection, Vector2 shape, float offset, float sway = 0)
        {
            Vector3 local = _visual.parent.InverseTransformVector(new Vector3(worldDirection.x, worldDirection.y, 0));
            Vector2 forward = new Vector2(local.x, local.y).normalized;
            if (forward.sqrMagnitude == 0) forward = Vector2.up;
            float angle = Mathf.Atan2(forward.y, forward.x) * Mathf.Rad2Deg - 90f;
            _visual.localRotation = Quaternion.AngleAxis(angle + sway, Vector3.forward) * _rotation;
            _visual.localPosition = _position + new Vector3(forward.x, forward.y, 0) * offset;
            _visual.localScale = new Vector3(_scale.x * shape.x, _scale.y * shape.y, _scale.z);
        }

        private void SetFlash(float amount)
        {
            if (_renderer == null) return;
            _renderer.GetPropertyBlock(_properties);
            _properties.SetColor(FlashColor, _warningColor);
            _properties.SetFloat(FlashAmount, amount);
            _renderer.SetPropertyBlock(_properties);
        }

        public void ResetAll()
        {
            if (!Initialize()) return;
            _chaseTime = 0;
            _visual.localPosition = _position;
            _visual.localRotation = _rotation;
            _visual.localScale = _scale;
            if (_renderer != null) _renderer.color = _color;
            SetFlash(0);
        }

        private void OnDisable() => ResetAll();
    }
}
