//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
namespace DivisionEngine.Audio
{
    /// <summary>
    /// How a 3D source attenuates with distance.
    /// </summary>
    public enum AudioRolloff
    {
        /// <summary>Volume falls off linearly between min and max distance.</summary>
        Linear,
        /// <summary>Inverse distance model (1/d), the default.</summary>
        Inverse,
        /// <summary>Inverse squared distance model (1/d^2).</summary>
        Exponent,
        /// <summary>No attenuation; the source is equally loud everywhere.</summary>
        None,
    }

    /// <summary>
    /// State of the listener (the ear) in the world.
    /// </summary>
    public struct AudioListenerState
    {
        public float3 Position;
        public float3 Forward;
        public float3 Up;
        public float3 Velocity;

        public static AudioListenerState Default => new()
        {
            Position = new float3(0, 0, 0),
            Forward = new float3(0, 0, -1),
            Up = new float3(0, 1, 0),
            Velocity = float3.Zero,
        };
    }

    /// <summary>
    /// Platform audio output. Only <see cref="OpenALBackend"/> and
    /// <see cref="NullAudioBackend"/> are provided, but the engine talks to this
    /// abstraction so other backends can be added without touching gameplay code.
    /// </summary>
    public abstract class AudioBackend : IDisposable
    {
        /// <summary>
        /// Backend name shown in logs / debug UI.
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// Whether the backend initialized successfully.
        /// </summary>
        public bool IsAvailable { get; protected set; }

        /// <summary>
        /// Creates the device and context.
        /// </summary>
        /// <returns>True when audio output is usable.</returns>
        public abstract bool Initialize();

        /// <summary>
        /// Releases all device resources.
        /// </summary>
        public abstract void Shutdown();

        /// <summary>
        /// Creates a voice attached to this backend.
        /// </summary>
        public abstract AudioVoice CreateVoice();

        /// <summary>
        /// Sets the master output gain (0-1).
        /// </summary>
        public abstract void SetMasterGain(float gain);

        /// <summary>
        /// Updates the listener transform used for 3D attenuation.
        /// </summary>
        public abstract void SetListener(in AudioListenerState state);

        /// <summary>
        /// Per frame backend update (streaming pumps are handled by voices).
        /// </summary>
        public virtual void Update() { }

        /// <summary>
        /// Makes the backend's context current on the calling thread. OpenAL
        /// contexts are bound per thread, and the player pumps audio from the
        /// ECS thread while the device may have been created on another one.
        /// </summary>
        public virtual void MakeCurrent() { }

        public virtual void Dispose() => Shutdown();
    }
}
