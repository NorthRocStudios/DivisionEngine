//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE.txt file in the project root
// for full license terms.
//
using DivisionEngine.Components.FieldAttributes;

namespace DivisionEngine.Components.SDFs.Effects
{
    /// <summary>
    /// Allows SDF objects to receive reflections.
    /// </summary>
    public class Reflections : IComponent
    {
        /// <summary>
        /// Turns reflections on and off for this material.
        /// </summary>
        public bool hasReflections = true;
        /// <summary>
        /// Turns reflection shadows on and off.
        /// </summary>
        public bool reflectionShadows = true;
        /// <summary>
        /// How many fewer steps each reflection bounce has.
        /// </summary>
        /// <remarks>ex. ray steps falloff = 2: 1st bounce 128 steps, 2nd bounce 64 steps, 3rd bounce 32 steps, etc. </remarks>
        [Range(1f, 10f)] public float rayStepsFalloff = 3f;
        [Range(1, 16)] public int maxBounces = 2;

        public IComponent Clone() => new Reflections
        {
            hasReflections = hasReflections,
            reflectionShadows = reflectionShadows,
            rayStepsFalloff = rayStepsFalloff,
            maxBounces = maxBounces,
        };
    }
}
