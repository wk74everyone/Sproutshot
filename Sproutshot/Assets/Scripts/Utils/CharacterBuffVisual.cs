using System;
using UnityEngine;

namespace Sproutshot.Utils
{
    public enum CharacterBuffEffect { Burn, Frost, Poison, Shock }

    /// <summary>玩家/敌人共用的外观层。强度由 Buff 逻辑驱动，不计算伤害、减速或持续时间。</summary>
    [DisallowMultipleComponent]
    public sealed class CharacterBuffVisual : MonoBehaviour, IPoolable
    {
        [SerializeField] private SpriteRenderer _renderer;
        [SerializeField, Range(0, 1)] private float _burn;
        [SerializeField, Range(0, 1)] private float _frost;
        [SerializeField, Range(0, 1)] private float _poison;
        [SerializeField, Range(0, 1)] private float _shock;
        private static Material _sharedMaterial;
        private MaterialPropertyBlock _block;
        private Sprite _sprite;
        private Vector4 _bounds, _uvRect;
        private Vector4 _lastState;
        private bool _dirty = true;
        private static readonly int StateId = Shader.PropertyToID("_BuffState");
        private static readonly int BoundsId = Shader.PropertyToID("_BuffBounds");
        private static readonly int RectId = Shader.PropertyToID("_BuffUVRect");
        private static readonly int SeedId = Shader.PropertyToID("_BuffSeed");

        private void Awake() => Apply();
        private void OnEnable() { _dirty = true; Apply(); }
        private void LateUpdate() => Apply();

        /// <summary>0 关闭，1 为最高强度；不同元素独立保存并同时显示。</summary>
        public void SetIntensity(CharacterBuffEffect effect, float intensity)
        {
            if (float.IsNaN(intensity) || float.IsInfinity(intensity)) intensity = 0;
            intensity = Mathf.Clamp01(intensity);
            switch (effect)
            {
                case CharacterBuffEffect.Burn: _burn = intensity; break;
                case CharacterBuffEffect.Frost: _frost = intensity; break;
                case CharacterBuffEffect.Poison: _poison = intensity; break;
                case CharacterBuffEffect.Shock: _shock = intensity; break;
                default: throw new ArgumentOutOfRangeException(nameof(effect));
            }
            _dirty = true;
            Apply();
        }

        /// <summary>用当前总层数驱动强度；不是增加层数。无上限 Buff 可指定视觉饱和层数。</summary>
        public void SetStacks(CharacterBuffEffect effect, int stacks, int fullIntensityStacks = 5)
        {
            float strength = stacks <= 0 ? 0 : Mathf.Lerp(.3f, 1f,
                Mathf.Clamp01((stacks - 1f) / Mathf.Max(1, fullIntensityStacks - 1)));
            if (fullIntensityStacks <= 1 && stacks > 0) strength = 1;
            SetIntensity(effect, strength);
        }

        private void Apply()
        {
            if (_renderer == null) _renderer = GetComponentInChildren<SpriteRenderer>(true);
            if (_renderer == null) return;
            if (_sharedMaterial == null) _sharedMaterial = Resources.Load<Material>("CharacterBuff");
            if (_sharedMaterial == null) return;
            if (_renderer.sharedMaterial != _sharedMaterial)
            {
                _renderer.sharedMaterial = _sharedMaterial;
                _dirty = true;
            }
            if (_block == null) _block = new MaterialPropertyBlock();
            if (_sprite != _renderer.sprite)
            {
                _sprite = _renderer.sprite;
                if (_sprite != null)
                {
                    Bounds bounds = _sprite.bounds;
                    _bounds = new Vector4(bounds.center.x, bounds.center.y,
                        Mathf.Max(.0001f, bounds.size.x), Mathf.Max(.0001f, bounds.size.y));
                    // 使用 Sprite 的真实 UV，兼容图集和打包旋转，不使用整张纹理的 0..1。
                    Vector2[] uv = _sprite.uv;
                    Vector2 min = Vector2.one, max = Vector2.zero;
                    for (int i = 0; i < uv.Length; i++) { min = Vector2.Min(min, uv[i]); max = Vector2.Max(max, uv[i]); }
                    _uvRect = new Vector4(min.x, min.y, max.x, max.y);
                }
                _dirty = true;
            }
            Vector4 state = new Vector4(_burn, _frost, _poison, _shock);
            if (!_dirty && state == _lastState) return;
            // 与 EnemyAnimation / PlayerAnimation 的闪色共存，保留其他组件的属性。
            _renderer.GetPropertyBlock(_block);
            _block.SetVector(StateId, state);
            _block.SetVector(BoundsId, _bounds);
            _block.SetVector(RectId, _uvRect);
            _block.SetFloat(SeedId, Mathf.Abs(GetInstanceID() % 997) * .137f);
            _renderer.SetPropertyBlock(_block);
            _lastState = state;
            _dirty = false;
        }

        public void ResetAll()
        {
            _burn = _frost = _poison = _shock = 0;
            _dirty = true;
            Apply();
        }

        private void OnDisable() => ResetAll();
    }
}
