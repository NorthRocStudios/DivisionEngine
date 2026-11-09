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
    /// Mandelbox fractal: built by repeatedly folding space in a box and a
    /// sphere, then scaling. Produces architectural, corridor-like structures.
    /// </summary>
    public class SDFMandelbox : IComponent
    {
        /// <summary>
        /// Fold scale; negative values keep the fractal bounded.
        /// </summary>
        [Header("Mandelbox")]
        [Range(-3f, -1.1f)]
        [Tooltip("Fold scale, usually between -1.5 and -2.5. Negative values keep the fractal bounded")]
        public float scale = -1.5f;

        /// <summary>
        /// Radius of the inner sphere fold.
        /// </summary>
        [Range(0.1f, 1f)]
        [Tooltip("Radius of the inner sphere fold")]
        public float minRadius = 0.5f;

        /// <summary>
        /// Box fold limit.
        /// </summary>
        [Range(0.25f, 2f)]
        [Tooltip("Box fold limit")]
        public float foldLimit = 1f;

        /// <summary>
        /// Number of iterations.
        /// </summary>
        [Range(1f, 16f)]
        [Tooltip("Iteration count. Higher adds detail but costs render time")]
        public int iterations = 8;

        public IComponent Clone() => new SDFMandelbox
        {
            scale = scale,
            minRadius = minRadius,
            foldLimit = foldLimit,
            iterations = iterations,
        };
    }
}
