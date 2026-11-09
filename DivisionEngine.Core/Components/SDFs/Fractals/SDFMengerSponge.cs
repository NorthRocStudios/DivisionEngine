//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.Components.FieldAttributes;

namespace DivisionEngine.Components.SDFs.Fractals
{
    /// <summary>
    /// Menger sponge fractal: a cube with square holes recursively punched
    /// through every face. Increase <see cref="iterations"/> for finer detail.
    /// </summary>
    public class SDFMengerSponge : IComponent
    {
        /// <summary>
        /// Half extent of the containing cube.
        /// </summary>
        [Header("Menger Sponge")]
        [Range(0.1f, 5f)]
        [Tooltip("Half extent of the containing cube")]
        public float size = 1f;

        /// <summary>
        /// Recursive hole scale; 3 is the classic sponge.
        /// </summary>
        [Range(2.05f, 5f)]
        [Tooltip("Recursive hole scale, 3 is the classic sponge")]
        public float holeScale = 3f;

        /// <summary>
        /// Number of subdivision levels.
        /// </summary>
        [Range(0f, 6f)]
        [Tooltip("Subdivision levels. Higher adds detail but costs render time")]
        public int iterations = 4;

        public IComponent Clone() => new SDFMengerSponge
        {
            size = size,
            holeScale = holeScale,
            iterations = iterations,
        };
    }
}
