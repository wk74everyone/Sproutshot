using System;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    /// <summary>同一伙伴预制体按实体 ID 选择帧组，待机/追随/攻击中的实际移动驱动 Idle/Run。</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(SpiritEntity))]
    public sealed class SpiritAnimation : MonoBehaviour
    {
        [Serializable]
        public sealed class FrameSet
        {
            public int spiritId;
            public Sprite[] idle;
            public Sprite[] run;
        }
        [SerializeField] private FrameSet[] _sets;
        [SerializeField, Min(.1f)] private float _idleFrameRate = 8;
        [SerializeField, Min(.1f)] private float _runFrameRate = 12;
        private FrameSet _active;
        private SpiritEntity _entity;
        private SpriteRenderer _renderer;
        private double _elapsed;
        public bool IsRunning { get; private set; }
        public int BoundSpiritId => _active?.spiritId ?? 0;

        public bool Bind(SpiritEntity entity, SpriteRenderer renderer)
        {
            Clear();
            if (entity == null || entity.gameObject != gameObject || renderer == null || _sets == null) return false;
            foreach (var set in _sets)
                if (set != null && set.spiritId == entity.SpiritId && set.idle != null && set.idle.Length > 0 && set.idle[0] != null)
                { _active = set; break; }
            if (_active == null) return false; // 尚无动画的伙伴保留调用方传入的静态立绘。
            _entity = entity; _renderer = renderer; ResetPose(); return true;
        }

        private void LateUpdate() => Tick(Time.deltaTime);
        private void Tick(float deltaTime)
        {
            if (!isActiveAndEnabled || _active == null || _entity == null || !_entity.CanAct || _renderer == null ||
                Time.timeScale <= 0 || deltaTime <= 0 || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;
            Vector2 velocity = _entity.ActualVelocity;
            bool running = velocity.sqrMagnitude > .0001f;
            if (IsRunning != running) { IsRunning = running; _elapsed = 0; }
            if (running && Mathf.Abs(velocity.x) > .01f) _renderer.flipX = velocity.x < 0;
            bool hasRun = IsRunning && _active.run != null && _active.run.Length > 0;
            Sprite[] frames = hasRun ? _active.run : _active.idle;
            float fps = Mathf.Max(.1f, hasRun ? _runFrameRate : _idleFrameRate);
            _elapsed = (_elapsed + deltaTime) % (frames.Length / (double)fps);
            int index = Mathf.Min(frames.Length - 1, (int)(_elapsed * fps + 1e-6));
            if (frames[index] != null) _renderer.sprite = frames[index];
        }

        private void ResetPose()
        {
            _elapsed = 0; IsRunning = false;
            if (_renderer == null || _active == null) return;
            _renderer.flipX = false; _renderer.sprite = _active.idle[0];
        }
        public void Clear()
        {
            _elapsed = 0; IsRunning = false;
            if (_renderer != null) _renderer.flipX = false;
            _active = null; _entity = null; _renderer = null;
        }
        private void OnEnable() => ResetPose();
        private void OnDisable() => ResetPose();
    }
}
