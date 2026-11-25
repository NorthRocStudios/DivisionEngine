//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE.txt file in the project root
// for full license terms.
//
using DivisionEngine.MathLib;

namespace DivisionEngine.Rendering.Terrain
{
    /// <summary>
    /// Identifies one quadtree node (heightmap chunk) in world space.
    /// </summary>
    public readonly record struct TerrainChunkKey(int Level, int ChunkX, int ChunkZ);

    /// <summary>
    /// The addressing scheme shared by the CPU streamer and the GPU samplers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The terrain is a quadtree whose nodes are heightmap chunks. Rather than a
    /// pointer tree, each level is addressed as a regular grid of world-aligned
    /// chunks; a quadtree cut is then just the set of grid cells selected by the
    /// camera distance. This keeps GPU lookups branch-free and makes the nodes
    /// stable in world space (a chunk never moves when the camera moves).
    /// </para>
    /// <para>
    /// Storage is toroidal: a chunk at (level, x, z) always lives in the same
    /// buffer slot (<c>x % gridSize</c>, <c>z % gridSize</c>) for that level, so
    /// moving the camera only replaces the chunks that enter the grid instead of
    /// shifting the entire heightmap. A slot carries the identity of the chunk it
    /// currently holds, so stale slots are detected and never sampled.
    /// </para>
    /// </remarks>
    public static class TerrainClipmap
    {
        /// <summary>World size of a chunk at <paramref name="level"/>.</summary>
        public static float LevelSize(float chunkSize, int level) => chunkSize * (1 << level);

        /// <summary>World-space coordinate of the chunk containing an axis position.</summary>
        public static int ChunkCoord(float position, float anchor, float levelSize)
            => (int)math.floor((position - anchor) / math.max(levelSize, 1e-6f));

        /// <summary>Positive modulo.</summary>
        public static int Mod(int value, int modulus)
        {
            int r = value % modulus;
            return r < 0 ? r + modulus : r;
        }

        /// <summary>Buffer slot holding a chunk's heightmap inside its terrain block.</summary>
        public static int SlotIndex(int level, int chunkX, int chunkZ, int gridSize)
            => level * gridSize * gridSize + Mod(chunkZ, gridSize) * gridSize + Mod(chunkX, gridSize);

        /// <summary>Buffer slot holding a chunk's toroidal identity record.</summary>
        public static int ChunkInfoIndex(int level, int chunkX, int chunkZ, int gridSize)
            => SlotIndex(level, chunkX, chunkZ, gridSize);

        /// <summary>
        /// Fills <paramref name="destination"/> with the quadtree cut for a camera
        /// position, ordered from most to least important (finest level first, then
        /// nearest chunk first). Returns the number of chunks written.
        /// </summary>
        public static int CollectDesiredChunks(
            float2 cameraXZ, float2 anchor, float chunkSize, int levels, int gridSize,
            System.Collections.Generic.List<TerrainChunkKey> destination)
        {
            destination.Clear();

            for (int level = 0; level < levels; level++)
            {
                float levelSize = LevelSize(chunkSize, level);
                int centerX = ChunkCoord(cameraXZ.x, anchor.x, levelSize);
                int centerZ = ChunkCoord(cameraXZ.y, anchor.y, levelSize);
                int gridHalf = gridSize / 2;

                // Spiral-ish ordering: nearest chunks first so the area under the
                // camera gets its detail before the edges of the level.
                for (int ring = 0; ring <= gridHalf; ring++)
                {
                    for (int dz = -ring; dz <= ring; dz++)
                    {
                        for (int dx = -ring; dx <= ring; dx++)
                        {
                            if (ring > 0 && math.abs(dx) != ring && math.abs(dz) != ring) continue;
                            if (math.abs(dx) > gridHalf || math.abs(dz) > gridHalf) continue;
                            destination.Add(new TerrainChunkKey(level, centerX + dx, centerZ + dz));
                        }
                    }
                }
            }

            return destination.Count;
        }
    }

    /// <summary>
    /// CPU mirror of the GPU terrain sampler.
    /// </summary>
    /// <remarks>
    /// This deliberately repeats the quadtree lookup and Hermite reconstruction from
    /// the shaders so tests can exercise the exact buffer layout (apron texels,
    /// toroidal slots, chunk identity records) without a GPU. If the shader sampler
    /// changes, this must change with it.
    /// </remarks>
    public static class TerrainSampling
    {
        /// <summary>
        /// Samples the packed terrain buffers at a position relative to the terrain
        /// origin. Returns (height, dHdX, dHdZ, ridge01).
        /// </summary>
        public static float4 Sample(TerrainDTO meta, float4[] data, int4[] chunkInfo,
            float2 localXZ, out int found, out int levelUsed)
        {
            found = 0;
            levelUsed = -1;

            // Fixed terrains clip the heightfield to a square prism; sampling from
            // outside the square must use the edge height, not report a miss.
            if (meta.worldSpace == 0)
            {
                float limit = math.max(meta.size - 0.01f, 0f);
                localXZ = new float2(math.clamp(localXZ.x, -limit, limit),
                    math.clamp(localXZ.y, -limit, limit));
            }

            int stride = meta.chunkStride;
            int resolution = meta.resolution.x;
            int gridSize = meta.gridSize;
            int gridArea = gridSize * gridSize;
            int gridHalf = gridSize / 2;
            float2 anchor = meta.worldSpace == 1
                ? new float2(0f, 0f)
                : new float2(-meta.size, -meta.size);
            // Bias the LOD by the camera's height above the terrain: an aerial
            // camera views the ground at a distance far larger than its XZ offset.
            float cameraDistance = math.length(localXZ - meta.lodCenter)
                + math.max(meta.worldMin.x, 0f) * 0.7f;

            int startLevel = meta.worldSpace == 1
                ? TerrainRendering.LodLevel(cameraDistance, meta.lodBaseRadius, meta.levels)
                : 0;

            for (int level = startLevel; level < meta.levels; level++)
            {
                float levelSize = meta.chunkSize * (1 << level);
                int chunkX = (int)math.floor((localXZ.x - anchor.x) / levelSize);
                int chunkZ = (int)math.floor((localXZ.y - anchor.y) / levelSize);
                int cameraChunkX = (int)math.floor((meta.lodCenter.x - anchor.x) / levelSize);
                int cameraChunkZ = (int)math.floor((meta.lodCenter.y - anchor.y) / levelSize);

                if (chunkX < cameraChunkX - gridHalf || chunkX > cameraChunkX + gridHalf) continue;
                if (chunkZ < cameraChunkZ - gridHalf || chunkZ > cameraChunkZ + gridHalf) continue;

                int slot = level * gridArea
                    + TerrainClipmap.Mod(chunkZ, gridSize) * gridSize
                    + TerrainClipmap.Mod(chunkX, gridSize);
                int4 info = chunkInfo[meta.chunkInfoOffset + slot];
                if (info.z == 0 || info.x != chunkX || info.y != chunkZ) continue;

                float2 chunkOrigin = anchor + new float2(chunkX, chunkZ) * levelSize;
                float2 texel = (localXZ - chunkOrigin) / levelSize * (resolution - 1) + 1.0f;
                int texelBase = meta.bufferOffset + slot * stride * stride;
                float texelWorldSize = levelSize / math.max(resolution - 1, 1);

                if (meta.interpolation == 0)
                {
                    int x0 = (int)math.floor(texel.x);
                    int z0 = (int)math.floor(texel.y);
                    x0 = System.Math.Clamp(x0, 0, stride - 2);
                    z0 = System.Math.Clamp(z0, 0, stride - 2);
                    float fx = texel.x - x0;
                    float fz = texel.y - z0;
                    float4 c00 = data[texelBase + z0 * stride + x0];
                    float4 c10 = data[texelBase + z0 * stride + x0 + 1];
                    float4 c01 = data[texelBase + (z0 + 1) * stride + x0];
                    float4 c11 = data[texelBase + (z0 + 1) * stride + x0 + 1];
                    float4 bilinear = TerrainRendering.BilinearTerrain4(c00, c10, c01, c11, fx, fz,
                        texelWorldSize, texelWorldSize);
                    if (meta.worldSpace == 1)
                    {
                        float maxRadius = meta.lodBaseRadius * (1 << (meta.levels - 1));
                        float fadeStart = maxRadius * 0.85f;
                        float fade = math.saturate((maxRadius - cameraDistance) / math.max(maxRadius - fadeStart, 1e-3f));
                        bilinear.x *= fade;
                        bilinear.y *= fade;
                        bilinear.z *= fade;
                    }
                    found = 1;
                    levelUsed = level;
                    return bilinear;
                }

                int hx0 = (int)math.floor(texel.x);
                int hz0 = (int)math.floor(texel.y);
                hx0 = System.Math.Clamp(hx0, 0, stride - 2);
                hz0 = System.Math.Clamp(hz0, 0, stride - 2);
                float tx = texel.x - hx0;
                float tz = texel.y - hz0;

                float4 p00 = data[texelBase + hz0 * stride + hx0];
                float4 p10 = data[texelBase + hz0 * stride + hx0 + 1];
                float4 p01 = data[texelBase + (hz0 + 1) * stride + hx0];
                float4 p11 = data[texelBase + (hz0 + 1) * stride + hx0 + 1];

                float4 result = TerrainRendering.Hermite4(p00, p10, p01, p11, tx, tz,
                    texelWorldSize, texelWorldSize);
                if (meta.worldSpace == 1)
                {
                    float maxRadius = meta.lodBaseRadius * (1 << (meta.levels - 1));
                    float fadeStart = maxRadius * 0.85f;
                    float fade = math.saturate((maxRadius - cameraDistance) / math.max(maxRadius - fadeStart, 1e-3f));
                    result.x *= fade;
                    result.y *= fade;
                    result.z *= fade;
                }
                found = 1;
                levelUsed = level;
                return result;
            }

            if (meta.worldSpace == 0)
            {
                return new float4(0f, 0f, 0f, 0f);
            }

            found = 1;
            return new float4(0f, 0f, 0f, 0.5f);
        }
    }

    /// <summary>
    /// CPU implementation of the terrain bake, used by the asynchronous chunk
    /// streamer. Calls exactly the same height function as the GPU bake shader, so
    /// streamed chunks match baked ones.
    /// </summary>
    public static class TerrainChunkBaker
    {
        /// <summary>
        /// Bakes one chunk into packed (height, dHdX, dHdZ, ridge01) texels.
        /// </summary>
        /// <param name="meta">Terrain parameters.</param>
        /// <param name="chunkOrigin">Chunk corner relative to the terrain position.</param>
        /// <param name="levelSize">World size of the chunk.</param>
        /// <param name="resolution">Texels per side, excluding the apron.</param>
        /// <param name="stride">Texels per stored row (resolution + 2).</param>
        public static float4[] BakeChunk(TerrainDTO meta, float2 chunkOrigin, float levelSize,
            int resolution, int stride)
        {
            float4[] data = new float4[stride * stride];
            float invResolution = 1.0f / math.max(resolution - 1, 1);
            float2 positionXZ = new float2(meta.position.x, meta.position.z);

            for (int y = 0; y < stride; y++)
            {
                for (int x = 0; x < stride; x++)
                {
                    float2 local = chunkOrigin + new float2(x - 1, y - 1) * invResolution * levelSize;
                    float ridge;
                    float erosion;
                    data[y * stride + x] = TerrainRendering.Heightmap(local + positionXZ, meta, out ridge, out erosion);
                }
            }

            return data;
        }

        /// <summary>
        /// Low-pass filters the stored slope channels. The height reconstruction uses
        /// the slopes as Hermite tangents; unfiltered per-texel erosion slopes carry
        /// detail at the texel Nyquist, which shows up as a comb of normal
        /// discontinuities at grazing angles. A 3x3 box filter keeps the silhouettes
        /// (heights untouched) while making the shaded normals stable.
        /// </summary>
        public static void SmoothSlopes(float4[] data, int stride)
        {
            if (data.Length < stride * stride || stride < 3) return;

            float2[] filtered = new float2[stride * stride];
            for (int y = 0; y < stride; y++)
            {
                for (int x = 0; x < stride; x++)
                {
                    float2 sum = new float2(0f, 0f);
                    int count = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int sy = System.Math.Clamp(y + dy, 0, stride - 1);
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int sx = System.Math.Clamp(x + dx, 0, stride - 1);
                            float4 value = data[sy * stride + sx];
                            sum += new float2(value.y, value.z);
                            count++;
                        }
                    }
                    filtered[y * stride + x] = sum / count;
                }
            }

            for (int i = 0; i < stride * stride; i++)
            {
                float4 value = data[i];
                data[i] = new float4(value.x, filtered[i].x, filtered[i].y, value.w);
            }
        }
    }
}
