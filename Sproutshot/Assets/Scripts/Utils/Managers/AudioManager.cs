using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using YooAsset;

namespace Sproutshot.Utils
{
    /// <summary>统一播放入口。所有瞬时音效只播放已预热资源，避免异步完成后补播过期事件。</summary>
    public sealed class AudioManager : SingletonMono<AudioManager>
    {
        private sealed class ClipEntry { public AssetHandle Handle; public AudioClip Clip; public AudioScope Scope; }
        private sealed class Voice
        {
            public AudioSource Source;
            public AudioDefinition Definition;
            public ulong Version;
            public bool Active, Paused;
            public float Gain, Started, FadeRemaining, FadeDuration;
        }
        private readonly Dictionary<AudioId, AudioDefinition> _definitions = new();
        private readonly Dictionary<string, ClipEntry> _clips = new();
        private readonly Dictionary<AudioId, float> _lastPlayed = new();
        private readonly List<Voice> _voices = new();
        private readonly AudioSource[] _music = new AudioSource[2];
        private readonly float[] _musicGains = new float[2];
        private readonly float[] _musicBase = new float[2];
        private readonly string[] _musicAddresses = new string[2];
        private AudioCatalog _catalog;
        private bool _initialized, _initializing, _battleEnded;
        private uint _scopeVersion, _musicVersion;
        private ulong _voiceVersion;
        private Coroutine _musicRoutine;
        private AudioId _requestedMusic;
        private float _duck = 1;
        public float MasterVolume { get; private set; }
        public float MusicVolume { get; private set; }
        public float SfxVolume { get; private set; }
        public bool Muted { get; private set; }
        private bool CombatPaused => Time.timeScale <= 0 || _manualPause;
        private bool _manualPause;

        protected override void Awake()
        {
            base.Awake();
            if (Instance != this) return;
            _catalog = Resources.Load<AudioCatalog>("AudioCatalog");
            if (_catalog != null)
            {
                foreach (var definition in _catalog.Entries)
                    if (definition != null && definition.Id != AudioId.None) _definitions[definition.Id] = definition;
            }
            else Debug.LogError("[AudioManager] 缺少 Resources/AudioCatalog。");
            MasterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat("Audio.Master", 1));
            MusicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat("Audio.Music", .65f));
            SfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat("Audio.Sfx", .8f));
            Muted = PlayerPrefs.GetInt("Audio.Muted", 0) != 0;
            for (int i = 0; i < 2; i++) _music[i] = CreateSource("Music" + i, AudioBus.Music);
            for (int i = 0; i < 20; i++) _voices.Add(new Voice { Source = CreateSource("Sfx" + i, i < 4 ? AudioBus.UI : AudioBus.Combat) });
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        private AudioSource CreateSource(string label, AudioBus bus)
        {
            var host = new GameObject(label);
            host.transform.SetParent(transform, false);
            var source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0;
            source.ignoreListenerPause = bus != AudioBus.Combat;
            source.outputAudioMixerGroup = _catalog != null ? _catalog.Group(bus) : null;
            return source;
        }

        public IEnumerator Initialize()
        {
            if (_initialized) yield break;
            if (_initializing) { while (_initializing) yield return null; yield break; }
            _initializing = true;
            try
            {
                yield return YooAssetManager.Instance.InitDefaultPackage();
                if (!YooAssetManager.Instance.IsInitialized) yield break;
                yield return PreloadScope(AudioScope.Global);
                _initialized = true;
            }
            finally { _initializing = false; }
        }

        public IEnumerator PreloadScope(AudioScope scope)
        {
            uint version = _scopeVersion;
            foreach (var definition in _definitions.Values)
                if (definition.Scope == scope && definition.Bus != AudioBus.Music)
                    foreach (string address in definition.Addresses)
                    {
                        if (scope != AudioScope.Global && version != _scopeVersion) yield break;
                        yield return LoadClip(address, scope);
                    }
        }

        private IEnumerator LoadClip(string address, AudioScope scope)
        {
            if (string.IsNullOrEmpty(address)) yield break;
            if (!_clips.TryGetValue(address, out var entry))
            {
                entry = new ClipEntry { Scope = scope, Handle = YooAssetManager.Instance.LoadAssetHandleAsync<AudioClip>(address) };
                _clips.Add(address, entry);
            }
            // Shared files (e.g. victory and positive chime) stay alive for the widest owner.
            if (scope == AudioScope.Global) entry.Scope = scope;
            var handle = entry.Handle;
            if (handle == null) yield break;
            yield return handle;
            if (!_clips.TryGetValue(address, out var current) || current != entry || !handle.IsValid) yield break;
            if (handle.Status == EOperationStatus.Succeeded)
            {
                entry.Clip = handle.AssetObject as AudioClip;
                if (entry.Clip != null && entry.Clip.loadState == AudioDataLoadState.Unloaded) entry.Clip.LoadAudioData();
                while (entry.Clip != null && entry.Clip.loadState == AudioDataLoadState.Loading && handle.IsValid) yield return null;
            }
            else
            {
                Debug.LogWarning($"[AudioManager] 音频加载失败：{address}: {handle.Error}");
                _clips.Remove(address);
                YooAssetManager.Instance.Release(handle);
            }
        }

        public AudioHandle PlaySfx(AudioId id, float volume = 1, float pitch = 1) => Play(id, volume, false, pitch);
        public AudioHandle PlayLoop(AudioId id, float volume = 1) => Play(id, volume, true, 1);
        private AudioHandle Play(AudioId id, float volume, bool loop, float pitch)
        {
            if (!_definitions.TryGetValue(id, out var definition) || definition.Bus == AudioBus.Music ||
                (definition.Scope == AudioScope.Battle && (_battleEnded || CombatPaused)) ||
                definition.Addresses.Length == 0) return default;
            string address = definition.Addresses[Random.Range(0, definition.Addresses.Length)];
            if (!_clips.TryGetValue(address, out var entry) || entry.Clip == null || entry.Clip.loadState != AudioDataLoadState.Loaded) return default;
            float now = Time.unscaledTime;
            if (_lastPlayed.TryGetValue(id, out float previous) && now - previous < definition.Cooldown) return default;
            int count = 0, candidate = -1;
            int begin = definition.Bus == AudioBus.UI ? 0 : 4, end = definition.Bus == AudioBus.UI ? 4 : _voices.Count;
            for (int i = begin; i < end; i++)
            {
                var voice = _voices[i];
                if (voice.Active && !voice.Paused && !voice.Source.isPlaying) Recycle(voice);
                if (voice.Active && voice.Definition.Id == id) count++;
                if (!voice.Active) { if (candidate < 0 || _voices[candidate].Active) candidate = i; }
                else if ((candidate < 0 || _voices[candidate].Active) && voice.Definition.Priority > definition.Priority &&
                    (candidate < 0 || voice.Started < _voices[candidate].Started)) candidate = i;
            }
            if (count >= Mathf.Max(1, definition.MaxInstances) || candidate < 0) return default;
            var selected = _voices[candidate];
            Recycle(selected);
            selected.Definition = definition;
            selected.Gain = Mathf.Clamp01(volume) * definition.Volume;
            selected.Started = now;
            selected.Active = true;
            selected.Version = ++_voiceVersion;
            var source = selected.Source;
            source.clip = entry.Clip;
            source.loop = loop || definition.Loop;
            source.pitch = Mathf.Clamp(Random.Range(definition.PitchRange.x, definition.PitchRange.y) * pitch, .1f, 3);
            source.priority = definition.Priority;
            source.ignoreListenerPause = definition.PausePolicy == AudioPausePolicy.Continue;
            source.outputAudioMixerGroup = _catalog.Group(definition.Bus);
            source.volume = selected.Gain * EffectiveSfx;
            source.Play();
            _lastPlayed[id] = now;
            return new AudioHandle(candidate, selected.Version);
        }

        public bool IsPlaying(AudioHandle handle) => TryVoice(handle, out var voice) && (voice.Paused || voice.Source.isPlaying);
        private bool TryVoice(AudioHandle handle, out Voice voice)
        {
            voice = handle.IsValid && handle.Slot >= 0 && handle.Slot < _voices.Count ? _voices[handle.Slot] : null;
            return voice != null && voice.Active && voice.Version == handle.Version;
        }
        public void Stop(AudioHandle handle, float fadeSeconds = 0)
        {
            if (!TryVoice(handle, out var voice)) return;
            if (fadeSeconds <= 0 || voice.Paused) Recycle(voice);
            else { voice.FadeRemaining = voice.FadeDuration = fadeSeconds; }
        }
        /// <summary>重大反馈替换同类短提示，避免获得音和结算音叠加。</summary>
        public void StopSfx(AudioId id)
        {
            foreach (var voice in _voices)
                if (voice.Active && voice.Definition.Id == id) Recycle(voice);
        }
        private static void Recycle(Voice voice)
        {
            voice.Source.Stop(); voice.Source.clip = null;
            voice.Active = voice.Paused = false;
            voice.FadeRemaining = voice.FadeDuration = 0;
        }

        public void BeginBattle() { _battleEnded = false; _manualPause = false; }
        public void EndBattle()
        {
            _battleEnded = true;
            foreach (var voice in _voices) if (voice.Active && voice.Definition.Scope == AudioScope.Battle) Recycle(voice);
        }
        public void SetCombatPaused(bool paused) => _manualPause = paused;
        private float EffectiveSfx => Muted ? 0 : MasterVolume * SfxVolume;
        private void Update()
        {
            _duck = Mathf.MoveTowards(_duck, CombatPaused || _battleEnded ? .45f : 1, Time.unscaledDeltaTime * 2);
            foreach (var voice in _voices)
            {
                if (!voice.Active) continue;
                bool pause = CombatPaused && voice.Definition.PausePolicy != AudioPausePolicy.Continue;
                if (pause && voice.Definition.PausePolicy == AudioPausePolicy.Stop) { Recycle(voice); continue; }
                if (pause != voice.Paused)
                {
                    if (pause) voice.Source.Pause(); else voice.Source.UnPause();
                    voice.Paused = pause;
                }
                if (!voice.Paused && !voice.Source.isPlaying) { Recycle(voice); continue; }
                float fade = 1;
                if (voice.FadeDuration > 0)
                {
                    voice.FadeRemaining -= Time.unscaledDeltaTime;
                    if (voice.FadeRemaining <= 0) { Recycle(voice); continue; }
                    fade = voice.FadeRemaining / voice.FadeDuration;
                }
                voice.Source.volume = voice.Gain * EffectiveSfx * fade;
            }
            ApplyMusicVolume();
        }
        private void ApplyMusicVolume()
        {
            for (int i = 0; i < 2; i++) _music[i].volume = Muted ? 0 : _musicGains[i] * _musicBase[i] * MasterVolume * MusicVolume * _duck;
        }

        public void PlayMusic(AudioId id, float fadeSeconds = .8f)
        {
            if (!_definitions.TryGetValue(id, out var definition) || definition.Bus != AudioBus.Music || definition.Addresses.Length == 0) return;
            if (_requestedMusic == id) return;
            _requestedMusic = id;
            uint version = ++_musicVersion;
            if (_musicRoutine != null) StopCoroutine(_musicRoutine);
            _musicRoutine = StartCoroutine(ChangeMusic(definition, Mathf.Max(0, fadeSeconds), version));
        }
        private IEnumerator ChangeMusic(AudioDefinition definition, float duration, uint version)
        {
            yield return Initialize();
            if (!_initialized || version != _musicVersion) { _requestedMusic = AudioId.None; yield break; }
            string address = definition.Addresses[0];
            yield return LoadClip(address, AudioScope.Global);
            if (version != _musicVersion) yield break;
            if (!_clips.TryGetValue(address, out var entry) || entry.Clip == null || entry.Clip.loadState == AudioDataLoadState.Failed)
            { _requestedMusic = AudioId.None; yield break; }
            int incoming = _musicGains[0] <= _musicGains[1] ? 0 : 1;
            // A -> B -> A during a fade resumes A instead of restarting it.
            for (int i = 0; i < 2; i++) if (_music[i].isPlaying && _musicAddresses[i] == address) incoming = i;
            int outgoing = 1 - incoming;
            if (!_music[incoming].isPlaying || _musicAddresses[incoming] != address)
            {
                _music[incoming].Stop();
                _music[incoming].clip = entry.Clip;
                _music[incoming].loop = definition.Loop;
                _music[incoming].priority = 0;
                _music[incoming].volume = 0;
                _musicGains[incoming] = 0;
                _musicAddresses[incoming] = address;
                _music[incoming].Play();
            }
            _musicBase[incoming] = definition.Volume;
            float startIn = _musicGains[incoming], startOut = _musicGains[outgoing], elapsed = 0;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                _musicGains[incoming] = Mathf.Lerp(startIn, 1, t);
                _musicGains[outgoing] = Mathf.Lerp(startOut, 0, t);
                ApplyMusicVolume();
                yield return null;
            }
            _musicGains[incoming] = 1; _musicGains[outgoing] = 0;
            _music[outgoing].Stop(); _music[outgoing].clip = null; _musicAddresses[outgoing] = null;
            // Only current/transition music is retained; common SFX retain their own handles.
            var release = new List<string>();
            foreach (var pair in _clips)
                if (pair.Key != address && IsMusicAddress(pair.Key)) release.Add(pair.Key);
            foreach (var key in release) ReleaseClip(key);
            _musicRoutine = null;
        }
        private bool IsMusicAddress(string address)
        {
            foreach (var definition in _definitions.Values)
                if (definition.Bus == AudioBus.Music && System.Array.IndexOf(definition.Addresses, address) >= 0) return true;
            return false;
        }
        public void SetVolumes(float master, float music, float sfx)
        {
            MasterVolume = Mathf.Clamp01(master); MusicVolume = Mathf.Clamp01(music); SfxVolume = Mathf.Clamp01(sfx);
            PlayerPrefs.SetFloat("Audio.Master", MasterVolume); PlayerPrefs.SetFloat("Audio.Music", MusicVolume); PlayerPrefs.SetFloat("Audio.Sfx", SfxVolume);
            PlayerPrefs.Save();
        }
        public void SetMuted(bool muted) { Muted = muted; PlayerPrefs.SetInt("Audio.Muted", muted ? 1 : 0); PlayerPrefs.Save(); }
        private void OnSceneUnloaded(Scene scene)
        {
            ++_scopeVersion;
            _battleEnded = _manualPause = false;
            _lastPlayed.Clear();
            foreach (var voice in _voices) if (voice.Active && voice.Definition.Scope != AudioScope.Global) Recycle(voice);
            var release = new List<string>();
            foreach (var pair in _clips) if (pair.Value.Scope != AudioScope.Global) release.Add(pair.Key);
            foreach (string key in release) ReleaseClip(key);
        }
        private void ReleaseClip(string address)
        {
            if (!_clips.TryGetValue(address, out var entry)) return;
            YooAssetManager.Instance.Release(entry.Handle);
            _clips.Remove(address);
        }
        protected override void OnDestroy()
        {
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            StopAllCoroutines();
            foreach (var voice in _voices) Recycle(voice);
            foreach (var source in _music) if (source != null) { source.Stop(); source.clip = null; }
            foreach (var entry in _clips.Values) YooAssetManager.Instance.Release(entry.Handle);
            _clips.Clear();
            base.OnDestroy();
        }
    }
}