using Sproutshot.Utils;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    /// <summary>现有角色材质表现的统一处理器，独立实例关联一个目标和一种表现。</summary>
    public sealed class CharacterMaterialCue : BaseCue
    {
        private CharacterBuffVisual _visual;
        public override void OnActive() => WhileActive();
        public override void WhileActive()
        {
            if (Target == null) return;
            if (_visual == null) _visual = Target.BuffVisuals ?? Target.GetComponent<CharacterBuffVisual>() ?? Target.gameObject.AddComponent<CharacterBuffVisual>();
            _visual.SetIntensity((CharacterBuffEffect)Type, Intensity);
        }
        // 周期受击闪色已由伤害系统处理；这里保留瞬时粒子/音效的扩展入口。
        public override void OnExecute() { }
        public override void OnRemove()
        {
            if (_visual != null) _visual.SetIntensity((CharacterBuffEffect)Type, 0);
        }
    }
}
