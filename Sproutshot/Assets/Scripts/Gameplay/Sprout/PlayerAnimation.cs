using UnityEngine;

namespace Sproutshot.Gameplay
{
    /// <summary>玩家受伤反馈，使用共享材质和每个渲染器独立的参数，不改变姿态或碰撞。</summary>
    [DisallowMultipleComponent]
    public class PlayerAnimation : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer _renderer;
        [SerializeField] private Color _damageColor = Color.red;
        [SerializeField, Min(.01f)] private float _damageFlashDuration = .12f;
        [SerializeField, Range(0, 1)] private float _damageFlashStrength = .85f;

        private MaterialPropertyBlock _properties;
        private float _remaining;
        private bool _justStarted;
        private static readonly int FlashColor = Shader.PropertyToID("_FlashColor");
        private static readonly int FlashAmount = Shader.PropertyToID("_FlashAmount");

        private bool Initialize()
        {
            if (_renderer == null) _renderer = GetComponentInChildren<SpriteRenderer>(true);
            if (_renderer == null) return false;
            if (_properties == null) _properties = new MaterialPropertyBlock();
            return true;
        }

        public void PlayDamage()
        {
            if (!isActiveAndEnabled || !Initialize()) return;
            _remaining = Mathf.Max(.01f, _damageFlashDuration);
            _justStarted = true;
            SetFlash(_damageFlashStrength);
        }

        private void LateUpdate()
        {
            // 命中这一帧先显示，避免低帧率下一次就扣完闪色时间。
            if (_justStarted) { _justStarted = false; return; }
            TickDamageFlash(Time.deltaTime);
        }

        private void TickDamageFlash(float deltaTime)
        {
            if (_remaining <= 0 || deltaTime <= 0) return;
            _remaining = Mathf.Max(0, _remaining - deltaTime);
            SetFlash(_damageFlashStrength * Mathf.Clamp01(_remaining / Mathf.Max(.01f, _damageFlashDuration)));
        }

        private void SetFlash(float amount)
        {
            if (_renderer == null) return;
            _renderer.GetPropertyBlock(_properties);
            _properties.SetColor(FlashColor, _damageColor);
            _properties.SetFloat(FlashAmount, amount);
            _renderer.SetPropertyBlock(_properties);
        }

        public void ResetAll()
        {
            _remaining = 0;
            _justStarted = false;
            if (Initialize()) SetFlash(0);
        }

        private void OnEnable() => ResetAll();
        private void OnDisable() => ResetAll();
    }
}
