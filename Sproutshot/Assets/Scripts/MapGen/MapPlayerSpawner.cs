using Sproutshot.Utils;
using UnityEngine;
using UnityEngine.Profiling;
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
        public int ProfessionId { get; private set; }
        public int AttributeId { get; private set; }
        private GameObject resolvedPrefab;
        private int resolvedProfessionId;

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
                HeroProfession profession;
                try { profession = HeroProfessionCatalog.Equipped(CloudEconomy.Profile); }
                catch (System.Exception error) { Debug.LogError("[MapPlayerSpawner] " + error.Message, this); return false; }
                ProfessionId = profession.id;
                AttributeId = profession.attributeId;
                if (resolvedProfessionId != profession.id)
                {
                    resolvedPrefab = null;
                    if (prefabHandle != null && prefabHandle.IsValid) prefabHandle.Release();
                    prefabHandle = null;
                    resolvedProfessionId = profession.id;
                }
                // Only reuse the authored override when it belongs to this address.
                if (resolvedPrefab == null && profession.playerPrefabAddress == playerPrefabAddress) resolvedPrefab = playerPrefab;
                if (resolvedPrefab == null)
                {
                    if (!YooAssetManager.Instance.IsInitialized) return false;
                    if (prefabHandle != null && prefabHandle.IsValid) prefabHandle.Release();
                    prefabHandle = YooAssetManager.Instance.LoadAssetHandleSync<GameObject>(profession.playerPrefabAddress);
                    resolvedPrefab = prefabHandle?.AssetObject as GameObject;
                }
                if (resolvedPrefab == null || resolvedPrefab.GetComponent<PlayerClickToMove>() == null)
                {
                    Debug.LogError("[MapPlayerSpawner] Player 预制体必须预先挂载 PlayerClickToMove。", this);
                    return false;
                }
                GameObject instance = Instantiate(resolvedPrefab, mapView.WorldSpawnPosition, Quaternion.identity, transform);
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
