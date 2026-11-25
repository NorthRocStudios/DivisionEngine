//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE.txt file in the project root
// for full license terms.
//
using DivisionEngine.Graphics;
using DivisionEngine.MathLib;
using DivisionEngine.Rendering.ShaderUtilities;

namespace DivisionEngine.Rendering.Terrain
{
    /// <summary>
    /// Bakes one heightmap chunk (fixed terrain or one quadtree LOD node) into
    /// packed terrain data. The height function lives in
    /// <see cref="TerrainRendering"/> and is shared with the CPU fallback used by
    /// the asynchronous chunk streamer, so GPU and CPU chunks agree.
    /// </summary>
    [Shader]
    public struct TerrainBakeShader
    {
        /// <summary>Which terrain of <see cref="terrainMetadata"/> to bake.</summary>
        public int terrainIndex;

        /// <summary>First texel column of the chunk in height-function space.</summary>
        public float2 chunkOrigin;

        /// <summary>World size of the chunk at its LOD level.</summary>
        public float levelSize;

        /// <summary>Texels per chunk side, excluding the 1-texel apron.</summary>
        public int resolution;

        /// <summary>Texels per stored row, including the 1-texel apron.</summary>
        public int stride;

        /// <summary>All terrain metadata.</summary>
        [ShaderResource] public StructuredBuffer<TerrainDTO> terrainMetadata;

        /// <summary>Packed output: (height, dHdX, dHdZ, ridge01) per texel.</summary>
        [ShaderResource] public RWBuffer<float4> output;

        /// <summary>Bakes one texel per thread.</summary>
        [Kernel(8, 8, 1)]
        public void Execute(uint3 id)
        {
            int2 pixel = new int2((int)id.x, (int)id.y);
            if (pixel.x >= stride || pixel.y >= stride) return;

            TerrainDTO meta = terrainMetadata[terrainIndex];

            // Texels -1..resolution are stored (the 1-texel apron lets cubic
            // interpolation read the neighbour chunk without a branch at the seam).
            float2 local = chunkOrigin + (new float2(pixel.x, pixel.y) - 1.0f)
                / math.max(resolution - 1, 1) * levelSize;
            float2 worldXZ = local + new float2(meta.position.x, meta.position.z);

            float ridge;
            float erosion;
            float4 terrainSample = TerrainRendering.Heightmap(worldXZ, meta, out ridge, out erosion);

            output[pixel.y * stride + pixel.x] = terrainSample;
        }
    }
}
