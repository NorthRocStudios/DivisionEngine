//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.Components.FieldAttributes;
using DivisionEngine.MathUtilities;
using DivisionEngine.Projects.Assets;

namespace DivisionEngine.Components
{
    /// <summary>
    /// Represents the world environment.
    /// </summary>
    public class Environment : IComponent
    {
        public enum SkyType
        {
            Solid = 0, Gradient = 1, HDRI = 2, Procedural = 3
        }

        /// <summary>
        /// Strength of the ambient lighting.
        /// </summary>
        [Header("Lighting")]
        [Tooltip("Strength of the ambient lighting")]
        [Range(0f, 1f)] public float ambientStrength = 0.15f;
        /// <summary>
        /// Type of sky rendered in the background
        /// </summary>
        [Tooltip("Type of sky rendered in the background")]
        public SkyType skyType = SkyType.Solid;
        /// <summary>
        /// Intensity of the rendered sky.
        /// </summary>
        /// <remarks>Useful for night skies</remarks>
        [Tooltip("This is useful for night skies")]
        [Min(0f)] public float skyIntensity = 1f;

        /// <summary>
        /// Color of the single color background sky.
        /// </summary>
        [Header("Single Color Sky")]
        [Color(false)] public float4 skyColor = ColorPalette.SkyBlue;

        /// <summary>
        /// Top color for the gradient sky.
        /// </summary>
        /// <remarks>Look Up!</remarks>
        [Header("Gradient Sky")]
        [Tooltip("Look Up!")]
        [Color(false)] public float4 topSkyColor = ColorPalette.DeepSkyBlue;
        /// <summary>
        /// Horizon color.
        /// </summary>
        /// <remarks>Zero Dawn</remarks>
        [Tooltip("Horizon color")]
        [Color(false)] public float4 middleSkyColor = ColorPalette.SkyBlue;
        /// <summary>
        /// Bottom color for the gradient sky.
        /// </summary>
        /// <remarks>Don't Look Up!</remarks>
        [Tooltip("Don't Look Up!")]
        [Color(false)] public float4 bottomSkyColor = ColorPalette.SandyBrown;

        [Header("HDRI Sky")]
        public AssetRef<TextureAsset> hdriMap = default;

        [Header("Procedural Sky")]
        [Tooltip("Planet radius in world units")]
        [Min(1f)] public float planetRadius = 6371000f;
        [Tooltip("Atmosphere shell thickness in world units")]
        [Min(1f)] public float atmosphereHeight = 60000f;
        [Tooltip("Overall atmosphere density")]
        [Range(0f, 5f)] public float atmosphereDensity = 1f;
        [Tooltip("Exposure multiplier for the atmosphere radiance")]
        [Min(0f)] public float atmosphereExposure = 12f;
        [Tooltip("Mie forward-scattering term")]
        [Range(-0.99f, 0.99f)] public float mieAnisotropy = 0.6f;

        [Header("Sun")]
        [Tooltip("Sun angular radius in radians")]
        [Range(0.001f, 0.2f)] public float sunDiscSize = 0.0093f;
        [Tooltip("Soft edge width of the sun disc in radians")]
        [Range(0.001f, 0.2f)] public float sunFalloff = 0.010f;
        [Tooltip("Sun radiance multiplier")]
        [Min(0f)] public float sunIntensity = 8f;
        [Color(false)] public float4 sunColor = new float4(1f, 0.95f, 0.85f, 1f);

        [Header("Clouds")]
        [Range(0f, 1f)] public float cloudCoverage = 0.40f;
        [Min(0f)] public float cloudDensity = 1.2f;
        [Min(0f)] public float cloudBaseHeight = 400f;
        [Min(1f)] public float cloudThickness = 900f;
        [Min(0.00001f)] public float cloudScale = 0.0012f;
        [Min(0f)] public float cloudSpeed = 1f;

        [Header("Wind")]
        public float2 windDirection = new float2(0.5f, 0.5f);
        [Range(0f, 1f)] public float windStrength = 0.3f;
        [Range(0f, 10f)] public float windFrequency = 1.6f;

        public IComponent Clone() => new Environment
        {
            ambientStrength = ambientStrength,
            skyType = skyType,
            skyIntensity = skyIntensity,

            skyColor = skyColor,
            bottomSkyColor = bottomSkyColor,
            middleSkyColor = middleSkyColor,
            topSkyColor = topSkyColor,
            hdriMap = hdriMap,

            planetRadius = planetRadius,
            atmosphereHeight = atmosphereHeight,
            atmosphereDensity = atmosphereDensity,
            atmosphereExposure = atmosphereExposure,
            mieAnisotropy = mieAnisotropy,
            sunDiscSize = sunDiscSize,
            sunFalloff = sunFalloff,
            sunIntensity = sunIntensity,
            sunColor = sunColor,
            cloudCoverage = cloudCoverage,
            cloudDensity = cloudDensity,
            cloudBaseHeight = cloudBaseHeight,
            cloudThickness = cloudThickness,
            cloudScale = cloudScale,
            cloudSpeed = cloudSpeed,

            windDirection = windDirection,
            windStrength = windStrength,
            windFrequency = windFrequency,
        };
    }
}









