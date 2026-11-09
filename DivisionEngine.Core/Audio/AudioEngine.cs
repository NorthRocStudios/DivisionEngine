//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.MathLib;

namespace DivisionEngine.Audio
{
    /// <summary>
    /// Global audio engine facade. Owns the active backend and all playing voices.
    /// Gameplay and the ECS <see cref="Systems.AudioSystem"/> talk to this class;
    /// everything below it is backend specific.
    /// </summary>
    public static class AudioEngine
    {
        private static readonly Lock gate = new();
        private static readonly List<AudioVoice> voices = [];
        private static AudioBackend backend = new NullAudioBackend();
        private static bool initialized;
        private static float masterVolume = 1f;

        /// <summary>
        /// Whether <see cref="Initialize"/> has run.
        /// </summary>
        public static bool IsInitialized
        {
            get { lock (gate) return initialized; }
        }

        /// <summary>
        /// Whether the active backend can actually produce sound.
        /// </summary>
        public static bool IsAvailable
        {
            get { lock (gate) return initialized && backend.IsAvailable; }
        }

        /// <summary>
        /// Name of the active backend, for debug UI.
        /// </summary>
        public static string BackendName
        {
            get { lock (gate) return backend.Name; }
        }

        /// <summary>
        /// Number of voices currently being pumped.
        /// </summary>
        public static int ActiveVoiceCount
        {
            get { lock (gate) return voices.Count; }
        }

        /// <summary>
        /// Master output volume (0-1).
        /// </summary>
        public static float MasterVolume
        {
            get { lock (gate) return masterVolume; }
            set
            {
                lock (gate)
                {
                    masterVolume = Math.Clamp(value, 0f, 1f);
                    backend.MakeCurrent();
                    backend.SetMasterGain(masterVolume);
                }
            }
        }

        /// <summary>
        /// Initializes the audio device. Safe to call more than once.
        /// </summary>
        /// <param name="useNullBackend">Force the silent backend (tests / headless).</param>
        /// <returns>True when real audio output is available.</returns>
        public static bool Initialize(bool useNullBackend = false)
        {
            lock (gate)
            {
                if (initialized) return backend.IsAvailable;

                AudioBackend candidate = useNullBackend ? new NullAudioBackend() : new OpenALBackend();
                if (!candidate.Initialize())
                {
                    if (!useNullBackend) Debug.Warning("Audio: no device available, falling back to silent backend.");
                    candidate = new NullAudioBackend();
                    candidate.Initialize();
                }

                backend = candidate;
                backend.SetMasterGain(masterVolume);
                initialized = true;
                Debug.Info($"Audio: engine ready using '{backend.Name}' backend.");
                return backend.IsAvailable;
            }
        }

        /// <summary>
        /// Stops all voices and releases the device.
        /// </summary>
        public static void Shutdown()
        {
            lock (gate)
            {
                foreach (AudioVoice voice in voices) voice.Dispose();
                voices.Clear();
                backend.Shutdown();
                backend = new NullAudioBackend();
                initialized = false;
            }
        }

        /// <summary>
        /// Pumps all playing voices. Call once per frame from the audio system.
        /// </summary>
        public static void Update()
        {
            lock (gate)
            {
                if (!initialized) return;
                backend.MakeCurrent();

                for (int i = voices.Count - 1; i >= 0; i--)
                {
                    AudioVoice voice = voices[i];
                    try
                    {
                        voice.Update();
                    }
                    catch (Exception ex)
                    {
                        Debug.Error("Audio: voice update failed, stopping voice.", ex);
                        voice.Stop();
                    }

                    if (voice.IsFinished) voices.RemoveAt(i);
                }

                backend.Update();
            }
        }

        /// <summary>
        /// Updates the listener position/orientation for 3D audio.
        /// </summary>
        public static void SetListener(in AudioListenerState state)
        {
            lock (gate)
            {
                if (!initialized) return;
                backend.MakeCurrent();
                backend.SetListener(state);
            }
        }

        /// <summary>
        /// Creates a voice that can be configured and played manually.
        /// </summary>
        public static AudioVoice CreateVoice()
        {
            lock (gate)
            {
                if (!initialized) Initialize();
                backend.MakeCurrent();
                AudioVoice voice = backend.CreateVoice();
                voices.Add(voice);
                return voice;
            }
        }

        /// <summary>
        /// Plays a decoded clip as a non-positional sound.
        /// </summary>
        public static AudioVoice Play(AudioClip clip, float volume = 1f, float pitch = 1f, bool loop = false)
        {
            AudioVoice voice = CreateVoice();
            voice.Spatial = false;
            voice.Volume = volume;
            voice.Pitch = pitch;
            voice.Loop = loop;
            voice.Play(new ClipProvider(clip));
            return voice;
        }

        /// <summary>
        /// Plays a decoded clip at a world position with distance attenuation.
        /// </summary>
        public static AudioVoice PlayAt(AudioClip clip, float3 position, float volume = 1f, float pitch = 1f,
            bool loop = false, float minDistance = 1f, float maxDistance = 50f, AudioRolloff rolloff = AudioRolloff.Inverse)
        {
            AudioVoice voice = CreateVoice();
            voice.Spatial = true;
            voice.Position = position;
            voice.Volume = volume;
            voice.Pitch = pitch;
            voice.Loop = loop;
            voice.MinDistance = minDistance;
            voice.MaxDistance = maxDistance;
            voice.Rolloff = rolloff;
            voice.Play(new ClipProvider(clip));
            return voice;
        }

        /// <summary>
        /// Plays a procedural generator as a non-positional sound.
        /// </summary>
        public static AudioVoice PlayGenerator(IAudioGenerator generator, float volume = 1f, float pitch = 1f)
        {
            AudioVoice voice = CreateVoice();
            voice.Spatial = false;
            voice.Volume = volume;
            voice.Pitch = pitch;
            voice.Play(new GeneratorProvider(generator));
            return voice;
        }

        /// <summary>
        /// Plays a procedural generator at a world position.
        /// </summary>
        public static AudioVoice PlayGeneratorAt(IAudioGenerator generator, float3 position, float volume = 1f,
            float pitch = 1f, float minDistance = 1f, float maxDistance = 50f)
        {
            AudioVoice voice = CreateVoice();
            voice.Spatial = true;
            voice.Position = position;
            voice.Volume = volume;
            voice.Pitch = pitch;
            voice.MinDistance = minDistance;
            voice.MaxDistance = maxDistance;
            voice.Play(new GeneratorProvider(generator));
            return voice;
        }

        /// <summary>
        /// Stops and disposes every voice.
        /// </summary>
        public static void StopAll()
        {
            lock (gate)
            {
                foreach (AudioVoice voice in voices) voice.Dispose();
                voices.Clear();
            }
        }
    }
}

