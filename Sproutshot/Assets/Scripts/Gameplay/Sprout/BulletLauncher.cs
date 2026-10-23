using Sproutshot.Utils;
using System.Collections;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    public class BulletLauncher : MonoBehaviour
    {
        [SerializeField] private Transform _shootPoint;
        [SerializeField] private PlayerBullet _bullet;
        [SerializeField, Min(.01f)] private float _fireInterval = 3f;
        [SerializeField, Min(.02f)] private float _searchInterval = .15f;

        private WaitForSeconds _delayBulletSpawn = new WaitForSeconds(0.2f);

        private float _timer;
        private float _searchTimer;
        private bool _isLaunching;

        private void OnEnable()
        {
            _timer = Mathf.Max(.01f, _fireInterval);
            _searchTimer = 0;
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            _isLaunching = false;
        }

        private void Update()
        {
            if (Time.timeScale <= 0f) return;
            Tick(Time.deltaTime);
        }

        private void Tick(float deltaTime)
        {
            if (deltaTime <= 0 || _bullet == null || _isLaunching) return;
            _timer = Mathf.Max(0, _timer - deltaTime);
            if (_timer > 0) return;
            _searchTimer -= deltaTime;
            if (_searchTimer > 0) return;
            _searchTimer = Mathf.Max(.02f, _searchInterval);

            StartCoroutine(Launch());
        }

        private IEnumerator Launch()
        {
            _isLaunching = true;
            bool fired = false;

            try
            {
                RunContext context = RunContext.Current;
                if (context == null || context.PlayerContext == null ||
                    context.PlayerEntity == null) yield break;

                SpatialGrid grid = context.SpatialGrid;
                if (grid == null || !grid.isActiveAndEnabled || !grid.IsInitialized) yield break;

                // 每轮只计算一次数量，每发一枚重新读取位置并选择最近目标。
                int bulletCount = context.PlayerContext.BulletCount;
                for (int i = 0; i < bulletCount; i++)
                {
                    if (!isActiveAndEnabled || RunContext.Current != context || grid == null ||
                        !grid.isActiveAndEnabled || !grid.IsInitialized || _bullet == null) yield break;

                    Vector3 position = _shootPoint != null ? _shootPoint.position : transform.position;
                    if (!grid.TryQueryNearest(position, _bullet.SeekRadius, out EnemyEntity target)) yield break;

                    PlayerBullet bullet = PoolManager.Instance.Get(_bullet, position, Quaternion.identity);
                    bullet.Launch(target);
                    fired = true;
                    yield return _delayBulletSpawn;
                }
            }
            finally
            {
                _isLaunching = false;
                if (fired)
                {
                    _timer = Mathf.Max(.01f, _fireInterval);
                    _searchTimer = 0;
                }
            }
        }
    }
}
