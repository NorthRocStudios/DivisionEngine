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
    /// Component for volumetric fog effects.
    /// </summary>
    public class VolumetricFog : IComponent
    {
        /// <summary>
        /// Density of the fog (higher = thicker fog).
        /// </summary>
        public float density = 0.05f;

        /// <summary>
        /// Color of the fog.
        /// </summary>
        public float4 color = new float4(0.5f, 0.6f, 0.7f, 1.0f);

        /// <summary>
        /// Light absorption factor (how much light is absorbed by fog).
        /// </summary>
        public float absorption = 0.1f;

        /// <summary>
        /// Light scattering factor (how much light is scattered).
        /// </summary>
        public float scattering = 0.5f;

        /// <summary>
        /// Anisotropy for Henyey-Greenstein phase function (-1 = back scattering, 0 = isotropic, 1 = forward scattering).
        /// </summary>
        [Range(0f, 1f)] public float anisotropy = 0.6f;

        public IComponent Clone() => new VolumetricFog
        {
            density = density,
            color = color,
            absorption = absorption,
            scattering = scattering,
            anisotropy = anisotropy,
        };
    }
}
