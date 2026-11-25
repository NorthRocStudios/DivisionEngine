//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE file in the project root
// for full license terms.
//
using DivisionEngine.Components.FieldAttributes;

namespace DivisionEngine.Components.SDFs.Effects
{
    /// <summary>
    /// Allows SDF objects to receive refractions.
    /// </summary>
    public class Refractions : IComponent
    {
        /// <summary>
        /// Turns refractions on and off for this material.
        /// </summary>
        public bool hasRefractions = true;
        /// <summary>
        /// Represents the wavelengths that are absorbed when traveling through a refractive material.
        /// </summary>
        /// <remarks>Effectively the "inner glass color"</remarks>
        [Color(ShowAlpha = true)] public float4 absorptionColor = new float4(1f, 1f, 1f, 0.1f);
        /// <summary>
        /// Maximum number of refraction ray steps for this material.
        /// </summary>
        public int maxRaySteps = 196;
        /// <summary>
        /// Maximum number of refraction objects included in the trace.
        /// </summary>
        [Range(1, 16)] public int maxRecursionTraces = 4;

        public IComponent Clone() => new Refractions
        {
            maxRaySteps = maxRaySteps,
            absorptionColor = absorptionColor,
            hasRefractions = hasRefractions,
            maxRecursionTraces = maxRecursionTraces,
        };
    }
}
