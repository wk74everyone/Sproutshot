using Sproutshot.Utils;
using UnityEngine;
using YooAsset;

namespace Sproutshot.MapGen
{
    /// <summary>Instantiates the authored player prefab once the level map is ready.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(GridMapGenerator), typeof(GridMapView))]
    public sealed class MapPlayerSpawner : MonoBehaviour
    {
        [SerializeField] private GridMapGenerator generator;
        [SerializeField] private GridMapView mapView;
        [SerializeField] private GameObject playerPrefab;
        [SerializeField] private string playerPrefabAddress = "Player";
        [SerializeField] private Camera inputCamera;

        private AssetHandle prefabHandle;
        public PlayerClickToMove Player { get; private set; }

        private void Awake()
        {
            if (generator == null) generator = GetComponent<GridMapGenerator>();
            if (mapView == null) mapView = GetComponent<GridMapView>();
        }

        private void OnEnable()
        {
            if (generator == null) generator = GetComponent<GridMapGenerator>();
            if (mapView == null) mapView = GetComponent<GridMapView>();
            generator.MapGenerated += OnMapGenerated;
            if (Application.isPlaying && generator.LastGeneratedMap != null && mapView.Map != null)
                OnMapGenerated(generator.LastGeneratedMap);
        }

        private void OnMapGenerated(GridMapData map)
        {
            if (!Application.isPlaying || mapView.Map != map) return;
            SpawnOrRepositionPlayer();
        }

        public bool SpawnOrRepositionPlayer()
        {
            if (mapView == null || mapView.Map == null) return false;
            if (Player == null)
            {
                if (playerPrefab == null)
                {
                    if (!YooAssetManager.Instance.IsInitialized) return false;
                    prefabHandle = YooAssetManager.Instance.LoadAssetHandleSync<GameObject>(playerPrefabAddress);
                    playerPrefab = prefabHandle?.AssetObject as GameObject;
                }
                if (playerPrefab == null || playerPrefab.GetComponent<PlayerClickToMove>() == null)
                {
                    Debug.LogError("[MapPlayerSpawner] Player 预制体必须预先挂载 PlayerClickToMove。", this);
                    return false;
                }
                GameObject instance = Instantiate(playerPrefab, mapView.WorldSpawnPosition, Quaternion.identity, transform);
                instance.name = "Player";
                Player = instance.GetComponent<PlayerClickToMove>();
            }
            Player.BindToMap(mapView, inputCamera != null ? inputCamera : Camera.main);
            Player.WarpToSpawn();
            generator.SetCameraTarget(Player.transform);
            return true;
        }

        private void OnDisable()
        {
            if (generator != null) generator.MapGenerated -= OnMapGenerated;
            if (Player != null) Player.StopMoving();
        }

        private void OnDestroy()
        {
            if (prefabHandle != null && prefabHandle.IsValid)
                YooAssetManager.Instance.Release(prefabHandle);
        }
    }
}
