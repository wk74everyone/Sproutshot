using Sproutshot.Utils;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    public class PlayerBullet : MonoBehaviour, IBullet, IPoolable
    {
        [SerializeField, Min(.01f)] private float speed = 14f;
        [SerializeField, Min(.01f)] private float maxDistance = 12f;
        [SerializeField, Min(.01f)] private float lifeTime = 2f;

        private Vector3 flightDirection;
        private float travelled;
        private float elapsed;
        private bool flying;

        /// <summary>由对象池取出后调用。方向只在发射时确定，后续移动使用世界坐标。</summary>
        public void Launch(Vector3 direction)
        {
            ResetAll();
            flightDirection = new Vector3(direction.x, direction.y, 0).normalized;
            if (flightDirection == Vector3.zero) { Recycle(); return; }
            flying = true;
        }

        private void Update() => Fly(Time.deltaTime);

        private void Fly(float deltaTime)
        {
            if (!flying || deltaTime <= 0) return;
            float stepTime = Mathf.Min(deltaTime, Mathf.Max(0, lifeTime - elapsed));
            float distance = Mathf.Min(speed * stepTime, Mathf.Max(0, maxDistance - travelled));
            transform.position += flightDirection * distance;
            travelled += distance;
            elapsed += deltaTime;
            if (travelled >= maxDistance || elapsed >= lifeTime) Recycle();
        }

        /// <summary>后续命中逻辑也可调用此方法归还子弹。</summary>
        public void Recycle()
        {
            flying = false;
            PoolManager.Instance.Put(this);
        }

        public void ResetAll()
        {
            flightDirection = Vector3.zero;
            travelled = elapsed = 0;
            flying = false;
        }

        private void OnDisable() => ResetAll();
    }
}
