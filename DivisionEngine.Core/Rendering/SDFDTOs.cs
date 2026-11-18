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
        public int2 resolution;   // Texels per chunk (R), excluding the 1-texel apron
        public int bufferOffset;  // Starting index of this terrain's chunk block in the height buffer
        public float heightScale; // World height of the base terrain amplitude
        public float heightOffset; // Vertical offset applied to sampled heights (world units)
        public int worldSpace;    // 1 = infinite LOD clipmap, 0 = fixed bounds
        public int terrainIndex;  // Which terrain this is
        public float size;        // Fixed terrain half extent in world units

        // --- Placement and sampling -------------------------------------------
        public float3 position;      // Terrain entity world position
        public float2 lodCenter;     // XZ anchor of the LOD clipmap (camera), relative to position
        public int interpolation;    // 0 = bilinear, 1 = cubic Hermite
        public int levels;           // LOD level count (1 for fixed terrain)
        public int gridSize;         // Chunks per level side (1 for fixed terrain)
        public float chunkSize;      // World size of a level-0 chunk
        public int chunkStride;      // (resolution + 2): stored texels per row including apron
        public int chunkInfoOffset;  // First chunk identity record in the chunk-info buffer
        public float lodBaseRadius;  // World radius covered by level 0
        public int hasErosion;       // 0 = base noise only, 1 = erosion filter enabled
        public float normalStrength; // Terrain shading normal scale (1 = geometric)

        // --- Base height function (Rune Skovbo Johansen erosion demo values) ---
        public float frequency;         // World -> height-function domain scale
        public float heightFrequency;   // Noise cells per domain unit (3)
        public float heightAmp;         // Noise amplitude in domain space (0.125)
        public int heightOctaves;       // Base noise octaves (3)
        public float heightLacunarity;  // 2
        public float heightGain;        // 0.1
        public float erosionStrength;   // 0.22
        public float erosionGullyWeight;// 0.5
        public float erosionDetail;     // 1.5
        public float erosionScale;      // 0.15
        public int erosionOctaves;      // 5
        public float erosionLacunarity; // 2
        public float erosionGain;       // 0.5
        public float erosionCellScale;  // 0.7
        public float erosionNormalization; // 0.5
        public float4 erosionRounding;   // (ridgeRounding, creaseRounding, inputMul, octaveMul)
        public float4 erosionOnset;      // (input onset, octave onset, ridge onset, ridge octave onset)
        public float2 erosionAssumedSlope;// (assumed slope, blend)
        public float2 terrainHeightOffset;// (offset, fade-target overrides offset)
    }
}



