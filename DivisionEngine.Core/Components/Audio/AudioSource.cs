//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.Audio;
using DivisionEngine.Components.FieldAttributes;
using DivisionEngine.Projects.Assets;

namespace DivisionEngine.Components.Audio
{
    /// <summary>
    /// Plays an audio clip. When <see cref="spatial"/> is enabled the sound is
    /// attenuated by distance from the <see cref="AudioListener"/> and panned
    /// into the world using this entity's <see cref="Transform"/>.
    /// </summary>
    public class AudioSource : IComponent
    {
        /// <summary>
        /// Audio clip asset to play.
        /// </summary>
        [Header("Clip")]
        [Tooltip("Audio clip to play (WAV or OGG)")]
        public AssetRef<AudioAsset> clip = default;

        /// <summary>
        /// Starts playing when the world starts.
        /// </summary>
        [Header("Playback")]
        [Tooltip("Play automatically when the world starts")]
        public bool playOnAwake = true;

        /// <summary>
        /// Volume of this source (0-1).
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("Volume of this source")]
        public float volume = 1f;

        /// <summary>
        /// Playback speed; also shifts pitch.
        /// </summary>
        [Range(0.1f, 3f)]
        [Tooltip("Playback speed, also shifts pitch")]
        public float pitch = 1f;

        /// <summary>
        /// Restarts when the end of the clip is reached.
        /// </summary>
        [Tooltip("Loop the clip when it reaches the end")]
        public bool loop = false;

        /// <summary>
        /// Silences the source without stopping playback.
        /// </summary>
        [Tooltip("Silence this source without stopping it")]
        public bool mute = false;

        /// <summary>
        /// Whether the sound is positioned in the world.
        /// </summary>
        [Header("3D")]
        [Tooltip("Attenuate with distance and pan in world space")]
        public bool spatial = true;

        /// <summary>
        /// Distance at which the source is at full volume.
        /// </summary>
        [Min(0.01f)]
        [Tooltip("Distance at which the source is at full volume")]
        public float minDistance = 1f;

        /// <summary>
        /// Distance at which attenuation stops.
        /// </summary>
        [Min(0.01f)]
        [Tooltip("Distance at which attenuation stops")]
        public float maxDistance = 50f;

        /// <summary>
        /// Distance attenuation curve.
        /// </summary>
        [Tooltip("Distance attenuation curve")]
        public AudioRolloff rolloff = AudioRolloff.Inverse;

        public IComponent Clone() => new AudioSource
        {
            clip = clip,
            playOnAwake = playOnAwake,
            volume = volume,
            pitch = pitch,
            loop = loop,
            mute = mute,
            spatial = spatial,
            minDistance = minDistance,
            maxDistance = maxDistance,
            rolloff = rolloff,
        };
    }
}
