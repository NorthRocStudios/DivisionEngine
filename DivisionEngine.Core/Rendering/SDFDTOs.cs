//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
namespace DivisionEngine.Rendering
{
    /// <summary>
    /// Transfers SDF world data to GPU for rendering.
    /// </summary>
    public struct SDFWorldDTO
    {
        // Camera Properties
        public float3 cameraOrigin; // 12b
        public float3 camForward; // 12b
        public float3 camRight; // 12b
        public float3 camUp; // 12b
        public float nearPlane; // 4b
        public float farPlane; // 4b
        public float camScreenDist; // 4b

        // Environment Properties
        public float4 skyColor; // 16b
        public float4 bottomSkyColor; // 16b
        public float4 middleSkyColor; // 16b
        public float4 topSkyColor; // 16b
        public int hdriTexMetaID; // 4b
        public int skyType; // 4b
        public float skyIntensity; // 4b
        public float ambientStrength; // 4b
        public int maxRaySteps; // 4b
        public int maxShadowRaySteps; // 4b
        public float3 mainLightDir; // 12b - precalculate main light direction

        // Procedural (physically based) sky
        public float planetRadius; // 4b
        public float atmosphereHeight; // 4b
        public float atmosphereDensity; // 4b
        public float atmosphereExposure; // 4b
        public float mieAnisotropy; // 4b
        public float sunDiscSize; // 4b
        public float sunFalloff; // 4b
        public float sunIntensity; // 4b
        public float4 sunColor; // 16b
        public float cloudCoverage; // 4b
        public float cloudDensity; // 4b
        public float cloudBaseHeight; // 4b
        public float cloudThickness; // 4b
        public float cloudScale; // 4b
        public float cloudSpeed; // 4b

        // Misc Properties
        public float time; // 4b
        public int enableAces; // 4b

        // Wind Properties
        public float2 windDirection; // 8b
        public float windStrength; // 4b
        public float windFrequency; // 4b

        // Fog Properties
        public float fogDensity; // 4b
        public float4 fogColor; // 16b
        public float fogAbsorption; // 4b
        public float fogScattering; // 4b
        public float fogAnisotropy; // 4b
    }

    /// <summary>
    /// Transfers SDF object data to GPU for rendering.
    /// </summary>
    public struct SDFObjectDTO
    {
        // Base Properties
        public uint entityID; // 4b
        public int type; // 4b
        public float3 position; // 12b
        public float4 rotation; // 16b
        public float3 scaling; // 12b
        public float4 parameters; // 16b
        /// <summary>
        /// Bounding sphere used for early rejection during ray marching.
        /// xyz is the world-space center, w is the radius. Infinite objects
        /// (planes, terrain) use a huge radius so they are never skipped.
        /// </summary>
        public float4 bounds; // 16b
        public float stepBias; // 4b
        public int terrainDataIndex; // 4b

        // Material Properties
        public int albedoTexMetaID; // 4b
        public int normalTexMetaID; // 4b
        public float normalStrength; // 4b
        public int displaceTexMetaID; // 4b
        public float displaceStrength; // 4b
        public int roughTexMetaID; // 4b
        public int metalTexMetaID; // 4b
        public int emissionTexMetaID; // 4b
        public float4 texTilingOffset; // 16b - xy is tiling, zw is offsets
        public float triplanarBlend; // 4b
        public float4 color; // 16b
        public float metallic; // 4b
        public float roughness; // 4b
        public float specular; // 4b
        public float ior; // 4b
        public float3 aoValues; // 12b - stores AO, AO Range, and AO Falloff respectively
        public float reflectance; // 4b

        // Precalculated Material Properties
        public float3 f0_reflectance; // 12b
        public float f0_dielectric; // 4b

        // Effect Properties
        public int shadowType; // 4b
        public float3 shadowDistances; // 12b
        public int2 shadowEffects; // 8b - (x = caster, y = receiver), 0/1 values
                                   // bool2 was 2 bytes in C# but 8 in HLSL, so DivisionCompute
                                   // rejects it as a structured-buffer element.
        public float4 absorptionColor; // 16b
        public int hasReflection; // 4b - must use integer type when in DTO
        public int hasRefraction; // 4b
        public int reflectionShadows; // 4b
        public float reflectRayStepFalloff; // 4b
        public int reflectionMaxBounces; // 4b
        public int refractionMaxSteps; // 4b
        public int refractMaxRecursion; // 4b
    }

    /// <summary>
    /// Transfers SDF light data to GPU for rendering.
    /// </summary>
    public struct SDFLightDTO
    {
        // Base Properties
        public int type; // 4b
        public float3 position; // 12b
        public float4 rotation; // 16b
        public float4 color; // 16b
        public float intensity; // 4b

        // Point Light Properties
        public float radius; // 4b
    }

    /// <summary>
    /// Metadata for a precomputed terrain heightfield.
    /// </summary>
    public struct TerrainDTO
    {
        public float3 worldMin;
        public float3 worldMax;
        public int2 resolution;   // Width, Height
        public int bufferOffset;  // Starting index in the big buffer
        public float heightScale;
        public float heightOffset;
        public int worldSpace;    // Is terrain in world space (infinite)
        public int terrainIndex;  // Which terrain this is
        public float size;        // Original terrain scale
    }
}



