//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.Components.FieldAttributes;

namespace DivisionEngine.Components.SDFs
{
    /// <summary>
    /// Used for building SDF terrains.
    /// </summary>
    public class SDFTerrain : IComponent
    {
        /// <summary>How the baked heightmap is reconstructed between texels.</summary>
        public enum Interpolation
        {
            /// <summary>Classic bilinear filtering. Fast, but shows polygonal ridges.</summary>
            Bilinear = 0,
            /// <summary>Cubic Hermite filtering from the stored analytic slopes.
            /// C1-continuous surfaces and smooth normals for four texels per sample.</summary>
            Bicubic = 1,
        }

        [Header("Terrain Settings")]
        [Tooltip("Generate an endless terrain that follows the camera using a quadtree of heightmap chunks")]
        public bool infinite = false;

        [Tooltip("Half extent of a fixed terrain in world units (ignored when infinite)")]
        [Range(16f, 8192f)] public float size = 500f;

        [Tooltip("Height of the base terrain noise in world units")]
        [Range(1f, 1000f)] public float height = 70f;

        [Tooltip("Terrain feature frequency in world units (1 / metres per height-function unit)")]
        [Range(0.0001f, 0.05f)] public float frequency = 0.0015f;

        [Header("Heightmap")]
        [Tooltip("Filtering used between heightmap texels")]
        public Interpolation interpolation = Interpolation.Bicubic;

        [Tooltip("Texels per side of a fixed terrain heightmap")]
        [Range(32, 4096)] public int heightmapSize = 1024;

        [Header("Infinite Terrain Quadtree")]
        [Tooltip("Number of LOD levels in the quadtree")]
        [Range(2, 10)] public int levels = 8;

        [Tooltip("Chunks per side of each LOD level")]
        [Range(3, 9)] public int chunksPerLevel = 5;

        [Tooltip("Texels per side of a streamed chunk (excluding the 1-texel apron)")]
        [Range(16, 256)] public int chunkResolution = 80;

        [Tooltip("World size of a level-0 chunk in metres")]
        [Range(4f, 256f)] public float chunkSize = 24f;

        [Tooltip("Maximum number of background chunk bake jobs that can run at once")]
        [Range(1, 16)] public int bakeThreads = 4;

        [Tooltip("Maximum number of finished chunks uploaded to the GPU per frame")]
        [Range(1, 32)] public int chunksPerFrame = 6;

        [Header("Erosion Filter")]
        [Tooltip("Apply the procedural erosion filter while baking the heightmap")]
        public bool erosion = true;

        [Tooltip("Erosion strength, scaling all gully octaves")]
        [Range(0f, 1f)] public float erosionStrength = 0.22f;

        [Tooltip("Gully weight: 0 sharpens peaks, 1 produces full gullies")]
        [Range(0f, 1f)] public float erosionGullyWeight = 0.5f;

        [Tooltip("Overall detail of the erosion (higher restricts detail to steeper slopes)")]
        [Range(0.1f, 6f)] public float erosionDetail = 1.5f;

        [Tooltip("Horizontal scale of the erosion in height-function units")]
        [Range(0.02f, 1f)] public float erosionScale = 0.15f;

        [Tooltip("Number of erosion octaves")]
        [Range(1, 10)] public int erosionOctaves = 5;

        [Tooltip("Frequency multiplier between erosion octaves")]
        [Range(1.1f, 4f)] public float erosionLacunarity = 2f;

        [Tooltip("Amplitude multiplier between erosion octaves")]
        [Range(0.1f, 0.9f)] public float erosionGain = 0.5f;

        [Tooltip("Size of the Phacelle noise cells relative to the gully width")]
        [Range(0.2f, 2f)] public float erosionCellScale = 0.7f;

        [Tooltip("How aggressively the stripe magnitude is normalized")]
        [Range(0f, 1f)] public float erosionNormalization = 0.5f;

        [Header("Ridge Noise")]
        [Range(0f, 1f)] public float ridgeBlend = 0f;
        [Range(0f, 2f)] public float ridgeWeight = 0.5f;

        [Header("Edge Falloff")]
        [Range(0f, 1f)] public float edgeFalloff = 0.1f;

        public IComponent Clone() => new SDFTerrain
        {
            infinite = infinite,
            size = size,
            height = height,
            frequency = frequency,
            interpolation = interpolation,
            heightmapSize = heightmapSize,
            levels = levels,
            chunksPerLevel = chunksPerLevel,
            chunkResolution = chunkResolution,
            chunkSize = chunkSize,
            bakeThreads = bakeThreads,
            chunksPerFrame = chunksPerFrame,
            erosion = erosion,
            erosionStrength = erosionStrength,
            erosionGullyWeight = erosionGullyWeight,
            erosionDetail = erosionDetail,
            erosionScale = erosionScale,
            erosionOctaves = erosionOctaves,
            erosionLacunarity = erosionLacunarity,
            erosionGain = erosionGain,
            erosionCellScale = erosionCellScale,
            erosionNormalization = erosionNormalization,
            ridgeBlend = ridgeBlend,
            ridgeWeight = ridgeWeight,
            edgeFalloff = edgeFalloff,
        };
    }
}
