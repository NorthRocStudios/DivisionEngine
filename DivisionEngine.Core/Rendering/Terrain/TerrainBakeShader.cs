//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.Graphics;
using DivisionEngine.MathLib;
using DivisionEngine.Rendering.ShaderUtilities;

namespace DivisionEngine.Rendering.Terrain
{
    [Shader]
    public struct TerrainBakeShader
    {
        // Uniforms: plain fields are packed into the b0 constant buffer.
        public int2 resolution;
        public float size;
        public float heightScale;
        public float frequency;
        public int octaves;
        public float lacunarity;
        public float persistence;
        public float ridgeBlend;
        public float ridgeWeight;

        /// <summary>Baked height output.</summary>
        [ShaderResource] public RWBuffer<float> output;

        /// <summary>Bakes one heightmap texel per thread.</summary>
        [Kernel(8, 8, 1)]
        public void Execute(uint3 id)
        {
            int2 pixel = new int2((int)id.x, (int)id.y);
            if (pixel.x >= resolution.x || pixel.y >= resolution.y) return;

            // Map texel to world-space XZ within [-size, size]
            float2 uv = new float2(pixel.x, pixel.y) / new float2(resolution.x - 1, resolution.y - 1) * 2f - 1f;
            float2 worldXZ = uv * size;

            // Sample simplex noise with FBM. FBM2D returns [0,1]; map it once to
            // [-heightScale/2, +heightScale/2]. (Scaling here *and* below applied
            // heightScale twice, which made heights grow with heightScale squared.)
            float noiseValue = SimplexNoise.FBM2D(worldXZ * frequency, octaves, persistence, lacunarity);

            // Optional ridge noise blend
            //float ridgeValue = 0f;
            //if (ridgeWeight > 0f)
            //{
            //    ridgeValue = SimplexNoise.FBM2D(new float3(worldXZ * frequency, 0f), octaves);
            //    noiseValue = math.lerp(noiseValue, ridgeValue, ridgeWeight);
            //}

            // Scale height: map from [0,1] to [-heightScale/2, heightScale/2]
            float height = (noiseValue - 0.5f) * heightScale;

            int index = pixel.y * resolution.x + pixel.x;
            output[index] = height;
        }
    }
}
