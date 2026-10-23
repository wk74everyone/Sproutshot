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
        [SerializeField] private Color _warningColor = Color.white;
        [SerializeField] private Color _damageColor = Color.red;
        [SerializeField, Min(.01f)] private float _damageFlashDuration = .12f;
        [SerializeField, Range(0, 1)] private float _damageFlashStrength = .85f;

        private Vector3 _position, _scale;
        private Quaternion _rotation;
        private Color _color;
        private bool _initialized;
        private float _chaseTime;
        private float _warningAmount;
        private float _damageFlashRemaining;
        private bool _damageJustStarted;
        private MaterialPropertyBlock _properties;
        private static readonly int FlashColor = Shader.PropertyToID("_FlashColor");
        private static readonly int FlashAmount = Shader.PropertyToID("_FlashAmount");

        private void Awake() => Initialize();
        private void LateUpdate()
        {
            // 受伤这一帧先显示一次，避免低帧率时直接扣完闪色时间。
            if (_damageJustStarted) { _damageJustStarted = false; return; }
            TickDamageFlash(Time.deltaTime);
        }

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
            if (direction.sqrMagnitude < .000001f) { ResetPose(); return; }
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

        /// <summary>受伤闪红独立于移动/攻击姿态，连续受伤刷新计时。</summary>
        public void PlayDamage()
        {
            if (!isActiveAndEnabled || !Initialize()) return;
            _damageFlashRemaining = Mathf.Max(.01f, _damageFlashDuration);
            _damageJustStarted = true;
            ApplyFlash();
        }

        private void TickDamageFlash(float deltaTime)
        {
            if (_damageFlashRemaining <= 0 || deltaTime <= 0) return;
            _damageFlashRemaining = Mathf.Max(0, _damageFlashRemaining - deltaTime);
            ApplyFlash();
        }

        private void SetFlash(float amount)
        {
            _warningAmount = amount;
            ApplyFlash();
        }

        private void ApplyFlash()
        {
            if (_renderer == null) return;
            bool damaged = _damageFlashRemaining > 0;
            float amount = damaged ? _damageFlashStrength *
                Mathf.Clamp01(_damageFlashRemaining / Mathf.Max(.01f, _damageFlashDuration)) : _warningAmount;
            _renderer.GetPropertyBlock(_properties);
            _properties.SetColor(FlashColor, damaged ? _damageColor : _warningColor);
            _properties.SetFloat(FlashAmount, amount);
            _renderer.SetPropertyBlock(_properties);
        }

        /// <summary>状态转换只复原姿态和攻击预警，不取消正在播放的受伤闪红。</summary>
        public void ResetPose()
        {
            if (!Initialize()) return;
            _chaseTime = 0;
            _visual.localPosition = _position;
            _visual.localRotation = _rotation;
            _visual.localScale = _scale;
            if (_renderer != null) _renderer.color = _color;
            SetFlash(0);
        }

        public void ResetAll()
        {
            _damageFlashRemaining = 0;
            _damageJustStarted = false;
            ResetPose();
        }

        private void OnDisable() => ResetAll();
    }
}
