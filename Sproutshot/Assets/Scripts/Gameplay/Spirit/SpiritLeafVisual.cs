using UnityEngine;

namespace Sproutshot.Gameplay
{
    /// <summary>技能飞叶的独立外观；结束时恢复池中普通子弹的渲染状态。</summary>
    public sealed class SpiritLeafVisual : MonoBehaviour
    {
        private SpriteRenderer _leaf;
        private SpriteRenderer[] _originals;
        private bool[] _enabled;
        private bool _playing;
        public void Play(Vector3 direction)
        {
            Clear();
            if (!SpiritSkillArt.Load()) return;
            if (_leaf == null)
            {
                _originals = GetComponentsInChildren<SpriteRenderer>(true);
                _enabled = new bool[_originals.Length];
                var host = new GameObject("Skill leaf"); host.transform.SetParent(transform, false);
                _leaf = host.AddComponent<SpriteRenderer>(); _leaf.sprite = SpiritSkillArt.Leaf;
                _leaf.sharedMaterial = SpiritSkillArt.Material; _leaf.sortingOrder = 20006;
            }
            for (int i = 0; i < _originals.Length; i++)
                if (_originals[i] != null) { _enabled[i] = _originals[i].enabled; _originals[i].enabled = false; }
            _playing = true; _leaf.enabled = true; _leaf.color = Color.white;
            _leaf.transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 45);
            SpiritSkillArt.Size(_leaf, .65f, .45f);
        }
        public void Clear()
        {
            if (_leaf != null) _leaf.enabled = false;
            if (_playing)
                for (int i = 0; i < _originals.Length; i++) if (_originals[i] != null) _originals[i].enabled = _enabled[i];
            _playing = false;
        }
        private void OnDisable() => Clear();
    }
}
