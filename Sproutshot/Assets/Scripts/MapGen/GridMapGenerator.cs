using System;
using System.Collections;
using Cinemachine;
using Sproutshot.Utils;
using UnityEngine;

namespace Sproutshot.MapGen
{
    /// <summary>Generates and displays continuous themed maps while retaining scene component references.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(GridMapView))]
    [DefaultExecutionOrder(-200)]
    public sealed class GridMapGenerator : MonoBehaviour
    {
        public GridMapRules Rules = new GridMapRules();
        [SerializeField] private bool generateOnStart = true;
        [SerializeField] private GridMapView view;
        [SerializeField] private CinemachineVirtualCamera virtualCamera;
        [SerializeField, Range(4f, 12f)] private float visibleWorldWidth = 7.293f;

        private Transform cameraTarget;
        private Transform previewTarget;

        public GridMapData LastGeneratedMap { get; private set; }
        public event Action<GridMapData> MapGenerated;

        /// <summary>Pure deterministic data creation for previews and future battle setup.</summary>
        public static GridMapData CreateMapData(GridMapRules rules, int seed) => GridMapBuilder.Generate(rules, seed);

        /// <summary>可由新框架提供跟随对象；不提供时相机只看地图中心。</summary>
        public void SetCameraTarget(Transform target)
        {
            cameraTarget = target;
            if (LastGeneratedMap != null) FrameWithCinemachine();
        }

        private void Awake()
        {
            if (view == null) view = GetComponent<GridMapView>();
            if (virtualCamera == null) virtualCamera = FindHighestPriorityVirtualCamera();
        }

        private IEnumerator Start()
        {
            if (!generateOnStart) yield break;
            if (MapSceneSelection.TryConsumeStage(out int stageNumber))
                SelectThemeForStage(stageNumber);

            if (!YooAssetManager.Instance.IsInitialized)
                yield return YooAssetManager.Instance.InitDefaultPackage();

            if (!YooAssetManager.Instance.IsInitialized)
            {
                Debug.LogError("[GridMapGenerator] YooAsset 初始化失败，地图未生成。", this);
                yield break;
            }

            Generate();
        }

        /// <summary>The five ten-stage chapters share this scene and select their own art theme.</summary>
        public void SelectThemeForStage(int stageNumber)
        {
            Rules.Theme = (MapTheme)((Mathf.Clamp(stageNumber, 1, 50) - 1) / 10);
        }

        [ContextMenu("Generate And Display Map")]
        private void GenerateFromInspector() => Generate();

        public GridMapData Generate()
        {
            GridMapData map = PrepareMap();
            if (map != null) DisplayMap(map);
            return map;
        }

        /// <summary>准备资源和地图数据，不替换当前场景，供切关入口先检查是否可生成。</summary>
        public GridMapData PrepareMap()
        {
            if (!EnsureAssetsReady()) return null;
            Rules.Clamp();
            int seed = Rules.RandomizeSeed ? unchecked(Environment.TickCount * 397 ^ GetInstanceID()) : Rules.Seed;
            return GridMapBuilder.Generate(Rules, seed);
        }

        public void DisplayMap(GridMapData map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (view == null) view = GetComponent<GridMapView>();
            view.Build(map, Rules);
            LastGeneratedMap = map;
            FrameWithCinemachine();
            EventBus.Publish(new MapGeneratedEvent(LastGeneratedMap));
            MapGenerated?.Invoke(LastGeneratedMap);
        }

        public GridMapData Generate(int seed)
        {
            if (Application.isPlaying && !EnsureAssetsReady()) return null;
            Rules.Clamp();
            GridMapData map = GridMapBuilder.Generate(Rules, seed);
            if (Application.isPlaying)
            {
                DisplayMap(map);
                return map;
            }
            LastGeneratedMap = map;
            EventBus.Publish(new MapGeneratedEvent(LastGeneratedMap));
            MapGenerated?.Invoke(LastGeneratedMap);
            return LastGeneratedMap;
        }

        private bool EnsureAssetsReady()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) return GridMapArtLibrary.LoadEditorPreview(Rules.Theme);
#endif
            if (!YooAssetManager.Instance.IsInitialized)
            {
                Debug.LogError("[GridMapGenerator] 请先初始化 YooAsset，再生成地图。", this);
                return false;
            }
            return GridMapArtLibrary.LoadMapSprites(Rules.Theme);
        }

        private void FrameWithCinemachine()
        {
            if (virtualCamera == null) virtualCamera = FindHighestPriorityVirtualCamera();
            if (virtualCamera == null)
            {
                Debug.LogError("[GridMapGenerator] 场景中没有可用的 CinemachineVirtualCamera。", this);
                return;
            }

            if (previewTarget == null)
            {
                previewTarget = new GameObject("Map Preview Focus").transform;
                previewTarget.SetParent(transform, false);
            }
            previewTarget.localPosition = view.CellToLocal(LastGeneratedMap.Center);
            Transform target = cameraTarget != null ? cameraTarget : previewTarget;
            virtualCamera.Follow = target;
            virtualCamera.LookAt = null;
            Quaternion cameraRotation = Quaternion.identity;
            virtualCamera.transform.rotation = cameraRotation;

            CinemachineFramingTransposer body = virtualCamera.GetCinemachineComponent<CinemachineFramingTransposer>();
            if (body == null) body = virtualCamera.AddCinemachineComponent<CinemachineFramingTransposer>();
            body.m_CameraDistance = 10f;
            body.m_TrackedObjectOffset = Vector3.zero;
            body.m_ScreenX = .5f;
            body.m_ScreenY = .42f;
            body.m_XDamping = 0f;
            body.m_YDamping = 0f;
            body.m_ZDamping = 0f;
            body.m_DeadZoneWidth = 0f;
            body.m_DeadZoneHeight = 0f;
            body.m_SoftZoneWidth = 0f;
            body.m_SoftZoneHeight = 0f;

            // Camera framing is independent of the hidden grid resolution.
            float viewportWidth = visibleWorldWidth;
            float screenAspect = Screen.width / (float)Mathf.Max(1, Screen.height);
            LensSettings lens = virtualCamera.m_Lens;
            lens.ModeOverride = LensSettings.OverrideModes.Orthographic;
            lens.OrthographicSize = viewportWidth / (2f * Mathf.Max(.25f, screenAspect));
            Vector3 groundSize=view.GroundLocalBounds.size, mapScale=view.transform.lossyScale;
            float maxSize=Mathf.Min(groundSize.y*Mathf.Abs(mapScale.y)*.5f,
                groundSize.x*Mathf.Abs(mapScale.x)/(2f*Mathf.Max(.25f,screenAspect)));
            lens.OrthographicSize=Mathf.Min(lens.OrthographicSize,maxSize*.99f);
            virtualCamera.m_Lens = lens;

            // 背景不再无限外扩，限制镜头完整视口留在绘制范围内。
            var confiner=virtualCamera.GetComponent<CinemachineConfiner2D>();
            if(confiner==null)confiner=virtualCamera.gameObject.AddComponent<CinemachineConfiner2D>();
            confiner.m_BoundingShape2D=view.CameraBoundary;
            confiner.m_Damping=0;confiner.InvalidateCache();

            Vector3 cameraPosition = target.position + Vector3.back * body.m_CameraDistance;
            virtualCamera.ForceCameraPosition(cameraPosition, cameraRotation);
            virtualCamera.PreviousStateIsValid = false;
        }

        private static CinemachineVirtualCamera FindHighestPriorityVirtualCamera()
        {
            CinemachineVirtualCamera[] cameras = FindObjectsOfType<CinemachineVirtualCamera>();
            CinemachineVirtualCamera best = null;
            foreach (CinemachineVirtualCamera candidate in cameras)
            {
                if (!candidate.isActiveAndEnabled) continue;
                if (best == null || candidate.Priority > best.Priority) best = candidate;
            }
            return best;
        }
    }

    /// <summary>One scene-entry selection, with no dependency on the old battle runtime.</summary>
    public static class MapSceneSelection
    {
        private static int pendingStage;
        /// <summary>当前战斗关卡；0 表示未选择关卡的训练入口。</summary>
        public static int CurrentStage { get; private set; }

        public static void SelectStage(int stageNumber) => pendingStage = Mathf.Clamp(stageNumber, 1, 50);

        internal static bool TryConsumeStage(out int stageNumber)
        {
            stageNumber = pendingStage;
            CurrentStage = stageNumber;
            pendingStage = 0;
            return stageNumber > 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSelection()
        {
            pendingStage = 0;
            CurrentStage = 0;
        }
    }
}
