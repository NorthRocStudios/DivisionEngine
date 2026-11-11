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

namespace DivisionEngine.Components.SDFs
{
    /// <summary>
    /// Represents material properties of an entity.
    /// </summary>
    public class SDFMaterial : IComponent
    {
        // Air/Vacuum
        const float IOR_AIR = 1.0f;

        // Liquids
        const float IOR_WATER = 1.333f;
        const float IOR_ICE = 1.31f;
        const float IOR_ALCOHOL = 1.36f;

        // Glass Types
        const float IOR_GLASS = 1.5f;
        const float IOR_CROWN_GLASS = 1.52f;
        const float IOR_FLINT_GLASS = 1.6f;

        // Crystals & Gems
        const float IOR_QUARTZ = 1.54f;
        const float IOR_EMERALD = 1.57f;
        const float IOR_RUBY = 1.77f;
        const float IOR_DIAMOND = 2.417f;

        // Plastics
        const float IOR_ACRYLIC = 1.49f;
        const float IOR_POLYCARBONATE = 1.58f;

        [Header("Textures")]
        [Tooltip("Base color texture of this material")]
        public AssetRef<TextureAsset> albedoMap = default;
        [Tooltip("Bump map for this material")]
        public AssetRef<TextureAsset> normalMap = default;
        public float normalStrength = 1f;
        public AssetRef<TextureAsset> heightMap = default;
        public float displaceStrength = 1f;
        [Tooltip("Roughness texture for this material")]
        public AssetRef<TextureAsset> roughnessMap = default;
        [Tooltip("Metallic texture for this material")]
        public AssetRef<TextureAsset> metallicMap = default;
        [Tooltip("Emission texture for this material")]
        public AssetRef<TextureAsset> emissiveMap = default;

        [Header("UV Scale / Offset")]
        [Tooltip("Texture pixel scale")]
        public float2 uvScale = new float2(1f, 1f);
        [Tooltip("Texture pixel offset")]
        public float2 uvOffset = float2.zero;
        [Tooltip("How smooth the blend is between each side of the texture")]
        public float triplanarBlend = 1f;

        [Space(10f)]
        [Header("Material Properties")]
        [Tooltip("Base color (overriden by albedo map)")]
        [Color(false)] public float4 albedoColor = ColorPalette.White;
        [Tooltip("Metalness (overridden by metallic map)")]
        [Range(0f, 1f)] public float metallic = 0.8f;
        [Tooltip("Roughness (overridden by roughness map)")]
        [Range(0f, 1f)] public float roughness = 0.2f;
        /// <summary>
        /// Intensity of specular highlights.
        /// </summary>
        [Tooltip("Intensity of specular highlights")]
        public float specular = 1f;

        /// <summary>
        /// Index of refraction.
        /// </summary>
        [Tooltip("Index of refraction")]
        [Range(1f, 3f)] public float ior = 1.0f;
        /// <summary>
        /// Ambient occlusion amount.
        /// </summary>
        [Tooltip("Ambient occlusion amount")]
        [Range(0f, 1f)] public float ambientOcclusion = 0.7f;
        /// <summary>
        /// Ambient occlusion maximum falloff distance.
        /// </summary>
        [Tooltip("Ambient occlusion maximum falloff distance")]
        [Min(0f)] public float ambientRange = 1f;
        /// <summary>
        /// Ambient occlusion falloff curve.
        /// </summary>
        /// <remarks>higher value = faster falloff</remarks>
        [Tooltip("Ambient occlusion falloff curve, higher value = faster falloff")]
        [Min(0f)] public float ambientFalloff = 2f;
        /// <summary>
        /// Raymarched reflection intensity.
        /// </summary>
        /// <remarks>Useful for combining with refractions</remarks>
        [Tooltip("Raymarched reflection intensity, useful for combining with refractions")]
        [Min(0f)] public float reflectance = 2f;
        /// <summary>
        /// A multiplier given to any SDF that can modify the step size to it.
        /// </summary>
        [Tooltip("A multiplier given to any SDF that can modify the step size to it")]
        [Range(0.1f, 1.5f)] public float stepBias = 1f;

        public IComponent Clone() => new SDFMaterial
        {
            albedoMap = albedoMap,
            normalMap = normalMap,
            normalStrength = normalStrength,
            heightMap = heightMap,
            displaceStrength = displaceStrength,
            roughnessMap = roughnessMap,
            metallicMap = metallicMap,
            emissiveMap = emissiveMap,
            uvScale = uvScale,
            uvOffset = uvOffset,
            triplanarBlend = triplanarBlend,

            albedoColor = albedoColor,
            metallic = metallic,
            roughness = roughness,
            specular = specular,
            ior = ior,
            ambientOcclusion = ambientOcclusion,
            ambientRange = ambientRange,
            ambientFalloff = ambientFalloff,
            reflectance = reflectance,
            stepBias = stepBias,
        };
    }
}
