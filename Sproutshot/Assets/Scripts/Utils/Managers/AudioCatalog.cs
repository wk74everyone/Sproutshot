using System;
using UnityEngine;
using UnityEngine.Audio;

namespace Sproutshot.Utils
{
    public enum AudioId { None, CampMusic, BattleMusic, UiTap, SeedShoot, EnemyHit, PlayerHurt,
        MagicBurst, PositiveChime, SummonBubbles, SummonPop, Victory, Defeat,
        ShieldBlock, Recovery, SpiritAppear }
    public enum AudioBus { Music, UI, Combat, Ambience }
    public enum AudioScope { Global, Scene, Battle }
    public enum AudioPausePolicy { Continue, Pause, Stop }

    [Serializable]
    public sealed class AudioDefinition
    {
        public AudioId Id;
        public string[] Addresses = Array.Empty<string>();
        public AudioBus Bus;
        public AudioScope Scope;
        public AudioPausePolicy PausePolicy;
        [Range(0, 1)] public float Volume = 1;
        public Vector2 PitchRange = Vector2.one;
        public bool Loop;
        [Min(1)] public int MaxInstances = 3;
        [Min(0)] public float Cooldown = .06f;
        [Range(0, 256)] public int Priority = 128;
    }

    [CreateAssetMenu(menuName = "Sproutshot/Audio Catalog")]
    public sealed class AudioCatalog : ScriptableObject
    {
        public AudioMixerGroup MusicGroup, UIGroup, CombatGroup, AmbienceGroup;
        public AudioDefinition[] Entries = Array.Empty<AudioDefinition>();
        public AudioMixerGroup Group(AudioBus bus) => bus switch {
            AudioBus.Music => MusicGroup, AudioBus.UI => UIGroup,
            AudioBus.Combat => CombatGroup, _ => AmbienceGroup };
    }

    /// <summary>声源槽位及其版本。默认值和已回收的旧句柄均不可停止新的声音。</summary>
    public readonly struct AudioHandle
    {
        internal readonly int Slot;
        internal readonly ulong Version;
        internal AudioHandle(int slot, ulong version) { Slot = slot; Version = version; }
        public bool IsValid => Version != 0;
    }
}
