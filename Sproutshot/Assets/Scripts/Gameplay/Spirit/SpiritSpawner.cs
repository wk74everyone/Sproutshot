using Sproutshot.Utils;
using System.Collections;
using UnityEngine;
using YooAsset;

namespace Sproutshot.Gameplay
{
    /// <summary>挂在玩家身上，每局根据档案生成一只伙伴，玩家失活时清理。</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(PlayerEntity))]
    public sealed class SpiritSpawner : MonoBehaviour
    {
        [SerializeField] private SpiritEntity _spiritPrefab;
        private SpiritEntity _spirit;
        private AssetHandle _portraitHandle;
        public SpiritEntity Spirit => _spirit;

        private void OnEnable() { if (Application.isPlaying) StartCoroutine(SpawnWhenReady()); }
        private IEnumerator SpawnWhenReady()
        {
            var context = RunContext.Current;
            var player = GetComponent<PlayerEntity>();
            while (context != null && context == RunContext.Current && !context.PlayerContext.IsInitialized) yield return null;
            if (context == null || context != RunContext.Current || context.PlayerEntity != player || context.LevelContext.IsStopped) yield break;
            yield return AudioManager.Instance.Initialize();
            yield return AudioManager.Instance.PreloadScope(AudioScope.Battle);
            if (context != RunContext.Current || context.PlayerEntity != player || context.LevelContext.IsStopped) yield break;
            SpawnEquipped(context, player);
        }

        public bool SpawnEquipped(RunContext context, PlayerEntity player)
        {
            if (_spirit != null) return true;
            if (context == null || context != RunContext.Current || player == null || context.PlayerEntity != player ||
                !context.PlayerContext.IsInitialized || context.PlayerContext.CurrentHp <= 0 || context.LevelContext.IsStopped || _spiritPrefab == null) return false;
            var profile = CloudEconomy.Profile;
            if (profile == null || profile.equippedSpiritId <= 0 || profile.ownedSpirits == null) return false;
            OwnedSpirit equipped = null;
            foreach (var owned in profile.ownedSpirits)
                if (owned != null && owned.spiritId == profile.equippedSpiritId) { equipped = owned; break; }
            if (equipped == null) return false;
            var tables = LubanConfigManager.Instance.Tables;
            var attribute = tables.EntityTbAttribute.GetOrDefault(equipped.spiritId);
            if (attribute == null || tables.EntityTbGrowth.GetOrDefault(equipped.spiritId) == null) return false;
            if (context.SpiritEntity != null) return false;
            Sprite portrait = null;
            if (YooAssetManager.Instance.IsInitialized)
            {
                string address = attribute.IconPath?.Trim();
                var package = YooAssets.GetPackage("DefaultPackage");
                if (string.IsNullOrWhiteSpace(address) || !package.IsLocationValid(address)) address = "Sprout_hero_sprout";
                _portraitHandle = YooAssetManager.Instance.LoadAssetHandleSync<Sprite>(address);
                portrait = _portraitHandle?.AssetObject as Sprite;
            }
            _spirit = Instantiate(_spiritPrefab, player.transform.position, Quaternion.identity, player.transform.parent);
            _spirit.name = "Spirit_" + equipped.spiritId;
            _spirit.Initialize(context, player, equipped.spiritId, equipped.level, portrait);
            context.SpiritEntity = _spirit;
            AudioManager.Instance.PlaySfx(AudioId.SpiritAppear);
            return true;
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            if (_spirit != null) { _spirit.gameObject.SetActive(false); Destroy(_spirit.gameObject); _spirit = null; }
            if (_portraitHandle != null && _portraitHandle.IsValid) _portraitHandle.Release();
            _portraitHandle = null;
        }
    }
}
