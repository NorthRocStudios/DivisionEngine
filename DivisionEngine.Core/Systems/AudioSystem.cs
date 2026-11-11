//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.Audio;
using DivisionEngine.Components;
using DivisionEngine.Components.Audio;
using DivisionEngine.MathUtilities;
using DivisionEngine.Projects;
using DivisionEngine.Projects.Assets;

namespace DivisionEngine.Systems
{
    /// <summary>
    /// Drives the audio engine from the ECS: positions 3D sources, streams their
    /// voices, keeps the listener in sync and handles the play-on-awake lifetime
    /// of <see cref="Components.Audio.AudioSource"/> entities.
    /// </summary>
    public class AudioSystem : SystemBase
    {
        /// <summary>
        /// The audio system of the most recently woken world, for editor tooling.
        /// </summary>
        public static AudioSystem? Instance { get; private set; }

        private readonly Dictionary<uint, AudioVoice> voices = [];
        private readonly Dictionary<uint, float3> lastPositions = [];
        private readonly HashSet<uint> started = [];
        private readonly HashSet<string> loadingClips = [];
        private float3 lastListenerPosition;
        private bool hasListenerHistory;
        private bool warnedNoListener;

        /// <summary>
        /// Runs late so gameplay has already moved transforms this frame.
        /// </summary>
        public override int Priority => 100;

        public override void Awake()
        {
            // World start: this world's audio system is the one editor tooling should query.
            Instance = this;
            if (!AudioEngine.IsInitialized) AudioEngine.Initialize();
        }

        public override void Update()
        {
            if (!AudioEngine.IsInitialized) AudioEngine.Initialize();

            UpdateListener();
            UpdateSources();
            AudioEngine.Update();
        }

        public override void Unload()
        {
            foreach (AudioVoice voice in voices.Values) voice.Dispose();
            voices.Clear();
            lastPositions.Clear();
            started.Clear();
            hasListenerHistory = false;

            if (Instance == this) Instance = null;
        }

        public override void AppExit() => AudioEngine.Shutdown();

        /// <summary>
        /// Plays the audio source attached to an entity immediately, even if
        /// <see cref="AudioSource.playOnAwake"/> is disabled.
        /// </summary>
        public void Play(uint entityId)
        {
            if (!W.HasComponent<AudioSource>(entityId) || !W.HasComponent<Transform>(entityId)) return;

            AudioSource source = W.GetComponent<AudioSource>(entityId)!;
            Transform transform = W.GetComponent<Transform>(entityId)!;

            AudioAsset? asset = source.clip.LoadedAsset;
            if (asset?.Clip == null) return;

            Stop(entityId);

            AudioVoice voice = AudioEngine.CreateVoice();
            ApplySettings(voice, source, transform, entityId);
            voice.Play(new ClipProvider(asset.Clip));
            voices[entityId] = voice;
            started.Add(entityId);
        }

        /// <summary>
        /// Stops the voice belonging to an entity.
        /// </summary>
        public void Stop(uint entityId)
        {
            if (voices.TryGetValue(entityId, out AudioVoice? voice))
            {
                voice.Dispose();
                voices.Remove(entityId);
            }
            lastPositions.Remove(entityId);
        }

        /// <summary>
        /// Whether an entity currently has audio playing.
        /// </summary>
        public bool IsPlaying(uint entityId) =>
            voices.TryGetValue(entityId, out AudioVoice? voice) && voice.IsPlaying;

        /// <summary>
        /// Gets the live voice for an entity, for spectrum analysis or runtime tweaks.
        /// </summary>
        public AudioVoice? GetVoice(uint entityId) =>
            voices.TryGetValue(entityId, out AudioVoice? voice) ? voice : null;

        /// <summary>
        /// Analyzes the audio currently produced by an entity's voice.
        /// </summary>
        public AudioSpectrum? GetSpectrum(uint entityId, int fftSize = AudioAnalyzer.DefaultFftSize,
            int bands = AudioAnalyzer.DefaultBandCount) =>
            GetVoice(entityId)?.GetSpectrum(fftSize, bands);

        private void UpdateListener()
        {
            Transform? listenerTransform = null;

            // 1) An explicit AudioListener component always wins.
            foreach ((uint id, Transform transform) in W.QueryData<Transform>())
            {
                if (W.HasComponent<AudioListener>(id))
                {
                    listenerTransform = transform;
                    break;
                }
            }

            // 2) Otherwise follow the gameplay camera. The editor camera is skipped
            // explicitly: during play mode it can remain active (and it does not move
            // with the player), which made 3D audio appear to play at a constant volume.
            if (listenerTransform == null)
            {
                Transform? fallback = null;
                foreach ((uint id, Transform transform, Camera camera) in W.QueryData<Transform, Camera>())
                {
                    if (!camera.isActive) continue;
                    if (id == EditorCamera.EditorCameraId || W.HasComponent<EditorCamera>(id)) continue;

                    // Prefer the camera gameplay controls if there are several.
                    if (W.HasComponent<Player>(id))
                    {
                        listenerTransform = transform;
                        break;
                    }
                    fallback ??= transform;
                }
                listenerTransform ??= fallback;
            }

            if (listenerTransform == null)
            {
                if (!warnedNoListener)
                {
                    Debug.Warning("Audio: no AudioListener or active camera found - 3D audio will not follow the view");
                    warnedNoListener = true;
                }
                return;
            }
            warnedNoListener = false;

            float3 velocity = float3.zero;
            if (hasListenerHistory && TimeSystem.DeltaTimeF > 0f)
                velocity = (listenerTransform.position - lastListenerPosition).Divide(TimeSystem.DeltaTimeF);

            lastListenerPosition = listenerTransform.position;
            hasListenerHistory = true;

            AudioListener? listener = null;
            foreach ((uint id, _) in W.QueryData<AudioListener>())
            {
                listener = W.GetComponent<AudioListener>(id);
                break;
            }

            if (listener != null) AudioEngine.MasterVolume = listener.volume;

            AudioEngine.SetListener(new AudioListenerState
            {
                Position = listenerTransform.position,
                Forward = listenerTransform.Forward,
                Up = listenerTransform.Up,
                Velocity = velocity,
            });
        }

        private void UpdateSources()
        {
            foreach ((uint id, Transform transform, AudioSource source) in W.QueryData<Transform, AudioSource>())
            {
                if (!string.IsNullOrEmpty(source.clip.ID) && source.clip.LoadedAsset == null)
                {
                    RequestClipLoad(source.clip.ID);
                    continue;
                }

                AudioClip? clip = source.clip.LoadedAsset?.Clip;
                if (clip == null) continue;

                voices.TryGetValue(id, out AudioVoice? voice);

                if (voice == null)
                {
                    if (!source.playOnAwake || started.Contains(id)) continue;

                    voice = AudioEngine.CreateVoice();
                    ApplySettings(voice, source, transform, id);
                    voice.Play(new ClipProvider(clip));
                    voices[id] = voice;
                    started.Add(id);
                    continue;
                }

                if (voice.IsFinished)
                {
                    voice.Dispose();
                    voices.Remove(id);
                    lastPositions.Remove(id);
                    continue;
                }

                ApplySettings(voice, source, transform, id);
            }

            // Stop voices whose entities were deleted from the world.
            if (voices.Count > 0)
            {
                foreach (uint id in voices.Keys.ToList())
                {
                    if (W.EntityExists(id)) continue;
                    voices[id].Dispose();
                    voices.Remove(id);
                    lastPositions.Remove(id);
                }
            }
        }

        private void ApplySettings(AudioVoice voice, AudioSource source, Transform transform, uint entityId)
        {
            float deltaTime = TimeSystem.DeltaTimeF;

            if (lastPositions.TryGetValue(entityId, out float3 previous) && deltaTime > 0f)
                voice.Velocity = (transform.position - previous).Divide(deltaTime);
            else
                voice.Velocity = float3.zero;

            lastPositions[entityId] = transform.position;

            voice.Volume = source.mute ? 0f : source.volume;
            voice.Pitch = source.pitch;
            voice.Loop = source.loop;
            voice.Spatial = source.spatial;
            voice.Position = transform.position;
            voice.MinDistance = source.minDistance;
            voice.MaxDistance = source.maxDistance;
            voice.Rolloff = source.rolloff;
        }

        private async void RequestClipLoad(string assetId)
        {
            if (string.IsNullOrEmpty(assetId) || !loadingClips.Add(assetId)) return;

            try
            {
                if (ProjectManager.AssetManager == null) return;
                await ProjectManager.AssetManager.LoadAssetAsync<AudioAsset>(assetId);
            }
            catch (Exception ex)
            {
                Debug.Warning($"Audio: failed to load clip asset '{assetId}'", ex);
            }
            finally
            {
                loadingClips.Remove(assetId);
            }
        }
    }
}

