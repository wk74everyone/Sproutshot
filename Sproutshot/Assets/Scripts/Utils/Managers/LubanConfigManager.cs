using UnityEngine;
using cfg;
using Luban;

namespace Sproutshot.Utils
{
    public class LubanConfigManager : Singleton<LubanConfigManager>
    {
        // 所有配置表的入口
        public Tables Tables { get; private set; }

        protected override void OnSingletonInit()
        {
            LoadAllTables();
        }

        private void LoadAllTables()
        {
            // Luban 的 Tables 构造函数接受一个 loader 委托
            // 参数是表名（不含扩展名），返回 ByteBuf
            Tables = new Tables(LoadBytes);
        }

        private ByteBuf LoadBytes(string file)
        {
            var asset = YooAssetManager.Instance.LoadAssetSync<TextAsset>(file);
            if (asset == null)
            {
                Debug.LogError($"[LubanConfigManager] 加载失败: {file}");
                return null;
            }
            return new ByteBuf(asset.bytes);
        }
    }
}