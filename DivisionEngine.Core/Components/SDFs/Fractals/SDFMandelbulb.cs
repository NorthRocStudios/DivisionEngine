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
    /// Mandelbulb fractal: the 3D analogue of the Mandelbrot set, made from a
    /// triplex power iteration. The <see cref="power"/> field controls how many
    /// lobes the bulb has - 8 is the classic shape.
    /// </summary>
    public class SDFMandelbulb : IComponent
    {
        /// <summary>
        /// Triplex exponent; 8 is the classic bulb.
        /// </summary>
        [Header("Mandelbulb")]
        [Range(2f, 16f)]
        [Tooltip("Triplex exponent. 8 is the classic bulb, try 3-6 for other shapes")]
        public float power = 8f;

        /// <summary>
        /// Iteration count; higher is more detailed and slower.
        /// </summary>
        [Range(1f, 12f)]
        [Tooltip("Detail level. Higher adds detail but costs render time")]
        public int iterations = 6;

        /// <summary>
        /// Escape radius of the iteration.
        /// </summary>
        [Min(1.2f)]
        [Tooltip("Escape radius, 2 is standard")]
        public float bailout = 2f;

        public IComponent Clone() => new SDFMandelbulb
        {
            power = power,
            iterations = iterations,
            bailout = bailout,
        };
    }
}
