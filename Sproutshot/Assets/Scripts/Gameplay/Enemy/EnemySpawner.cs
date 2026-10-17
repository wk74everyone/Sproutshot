using Sproutshot.Utils;
using System.Collections.Generic;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    public class EnemySpawner : MonoBehaviour
    {
        [SerializeField] private List<EnemyEntity> _entityList = new();
        private float _spawnInterval;
        private float _timer;

        private void Awake()
        {
            //_spawnInterval = RunContext.Current.LevelContext.SpawnInterval;
            _spawnInterval = 1f;
            _timer = _spawnInterval;
        }

        private void Update()
        {
            if (_timer <= 0f)
            {
                SpawnEntity();
                _timer = _spawnInterval;
            }
            else
            {
                _timer -= Time.deltaTime;
            }
        }

        private void SpawnEntity()
        {
            // 目前仅仅是测试用
            var entity = PoolManager.Instance.Get<Entity>(_entityList[0], transform.position, Quaternion.identity);
        }
    }
}