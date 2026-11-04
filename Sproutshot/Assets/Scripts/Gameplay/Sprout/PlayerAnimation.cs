using UnityEngine;
using Sproutshot.MapGen;

namespace Sproutshot.Gameplay
{
    /// <summary>玩家 Idle/Run 序列帧和受伤闪色；只操作立绘，不改变根物体、碰撞体或发射点。</summary>
    [DisallowMultipleComponent]
    public class PlayerAnimation : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer _renderer;
        [SerializeField] private Sprite[] _idleFrames;
        [SerializeField] private Sprite[] _runFrames;
        [SerializeField, Min(.1f)] private float _idleFrameRate = 8f;
        [SerializeField, Min(.1f)] private float _runFrameRate = 12f;
        [SerializeField] private Color _damageColor = Color.red;
        [SerializeField, Min(.01f)] private float _damageFlashDuration = .12f;
        [SerializeField, Range(0, 1)] private float _damageFlashStrength = .85f;

        private MaterialPropertyBlock _properties;
        private float _remaining;
        private bool _justStarted;
        private PlayerClickToMove _movement;
        private PlayerEntity _entity;
        private RunContext _context;
        private double _animationTime;
        private bool _initialized, _initialFlipX;
        public bool IsRunning { get; private set; }
        private static readonly int FlashColor = Shader.PropertyToID("_FlashColor");
        private static readonly int FlashAmount = Shader.PropertyToID("_FlashAmount");

        private bool Initialize()
        {
            if (_renderer == null) _renderer = GetComponentInChildren<SpriteRenderer>(true);
            if (_renderer == null) return false;
            if (_properties == null) _properties = new MaterialPropertyBlock();
            if (!_initialized)
            {
                _movement = GetComponent<PlayerClickToMove>();
                _entity = GetComponent<PlayerEntity>();
                _initialFlipX = _renderer.flipX;
                _initialized = true;
            }
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
            TickMotion(Time.deltaTime);
            // 命中这一帧先显示，避免低帧率下一次就扣完闪色时间。
            if (_justStarted) { _justStarted = false; return; }
            TickDamageFlash(Time.deltaTime);
        }

        private void TickMotion(float deltaTime)
        {
            if (!Initialize() || Time.timeScale <= 0 || deltaTime <= 0 ||
                float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;
            // 战斗结束、死亡或旧场景不继续播放；独立无对局的预览仍可播放待机。
            if (_context != null && (_context != RunContext.Current || _context.LevelContext.IsStopped ||
                !_context.PlayerContext.IsInitialized || _context.PlayerContext.CurrentHp <= 0 ||
                (_entity != null && _context.PlayerEntity != _entity))) return;
            Vector2 velocity = _movement != null ? _movement.ActualVelocity : Vector2.zero;
            bool running = _movement != null && _movement.CanAcceptInput && _movement.IsMoving && velocity.sqrMagnitude > .0001f;
            if (running != IsRunning) { IsRunning = running; _animationTime = 0; }
            if (running && Mathf.Abs(velocity.x) > .01f) _renderer.flipX = _initialFlipX ^ (velocity.x < 0);
            Sprite[] frames = IsRunning && _runFrames != null && _runFrames.Length > 0 ? _runFrames : _idleFrames;
            if (frames == null || frames.Length == 0) return;
            float fps = Mathf.Max(.1f, IsRunning ? _runFrameRate : _idleFrameRate);
            _animationTime = (_animationTime + deltaTime) % (frames.Length / (double)fps);
            int index = Mathf.Min(frames.Length - 1, (int)(_animationTime * fps + 1e-6));
            if (frames[index] != null) _renderer.sprite = frames[index];
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
            _context = RunContext.Current;
            _animationTime = 0; IsRunning = false;
            if (Initialize())
            {
                _renderer.flipX = _initialFlipX;
                if (_idleFrames != null && _idleFrames.Length > 0 && _idleFrames[0] != null) _renderer.sprite = _idleFrames[0];
                SetFlash(0);
            }
        }

        private void OnEnable() => ResetAll();
        private void OnDisable() => ResetAll();
    }
}
