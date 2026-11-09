//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.Components.FieldAttributes;

namespace DivisionEngine.Components.Audio
{
    /// <summary>
    /// Marks the entity whose <see cref="Transform"/> is used as the 3D audio
    /// listener (the "ears" of the world). If no listener exists the active
    /// camera is used instead.
    /// </summary>
    public class AudioListener : IComponent
    {
        /// <summary>
        /// Master volume applied to all audio while this listener is active.
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("Master volume for this listener")]
        public float volume = 1f;

        public IComponent Clone() => new AudioListener
        {
            volume = volume,
        };
    }
}
