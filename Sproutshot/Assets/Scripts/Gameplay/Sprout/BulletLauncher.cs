using Sproutshot.Utils;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    public class BulletLauncher : MonoBehaviour
    {
        [SerializeField] private Transform[] _shootPoints;
        [SerializeField] private PlayerBullet _bullet;

        private float _launcherDeltaTime;
        private float _timer;

        private void Start()
        {
            //_launcherDeltaTime = RunContext.Current.Player.LauncherDeltaTime;
            _launcherDeltaTime = 1f;
            _timer = _launcherDeltaTime;
            //_bullet = YooAssetManager.Instance.LoadAssetSync<GameObject>(RunContext.Current.Player.BulletPrefabPath).GetComponent<PlayerBullet>();
        }

        private void Update()
        {
            if (_timer <= 0)
            {
                //int count = Mathf.Min(RunContext.Current.Player.EnableShootPointCount, _shootPoints.Length);
                int count = 3;
                for (int i = 0; i < count; i++)
                {
                    Transform point = _shootPoints[i];
                    if (point == null) continue;
                    Vector3 direction = point.position - transform.position;
                    direction.z = 0;
                    if (direction.sqrMagnitude < .000001f) continue;
                    var bullet = PoolManager.Instance.Get(_bullet, point.position, Quaternion.identity);
                    bullet.Launch(direction);
                }
                _timer = _launcherDeltaTime;
            }
            else
            {
                _timer -= Time.deltaTime;
            }
        }
    }
}
