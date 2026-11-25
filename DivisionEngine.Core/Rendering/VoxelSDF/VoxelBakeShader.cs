//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE file in the project root
// for full license terms.
//
using DivisionEngine.Graphics;
using DivisionEngine.MathLib;
using DivisionEngine.Rendering;
using DivisionEngine.Rendering.ShaderUtilities;
using DivisionEngine.Rendering.Terrain;

namespace DivisionEngine
{
    /// <summary>
    /// Samples the exact scene SDF onto the voxel grid. One thread owns one grid
    /// point and writes its signed distance plus the index of the nearest object.
    /// The render shader then de-voxelizes this data with trilinear interpolation.
    /// </summary>
    [Shader]
    public struct VoxelBakeShader
    {
        // World-space cell range to bake. Each level stores cells toroidally by
        // absolute world cell index, so writing a shell of newly exposed cells is
        // independent of where the level's active window currently sits.
        public int3 cellOrigin;
        public int3 cellCount;
        public int voxelResolution;
        public int atlasZOffset;
        public float cellSize;
        public float maxDistance;

        [ShaderResource] public StructuredBuffer<SDFObjectDTO> sdfObjects;
        [ShaderResource] public Buffer<float4> terrainHeightData;
        [ShaderResource] public Buffer<int4> terrainChunkInfo;
        [ShaderResource] public StructuredBuffer<TerrainDTO> terrainMetadata;
        [ShaderResource] public RWTexture3D<float> outputDistance;
        [ShaderResource] public RWTexture3D<uint> outputObjectId;

        const float EPSILON = 0.0001f;
        const float MIN_TRAVERSE_DIST = 100000000.0f;

        private static int WrapCell(int value, int size)
        {
            int wrapped = value % size;
            return wrapped < 0 ? wrapped + size : wrapped;
        }

        [Kernel(4, 4, 4)]
        public void Execute(uint3 id)
        {
            if (id.x >= (uint)cellCount.x ||
                id.y >= (uint)cellCount.y ||
                id.z >= (uint)cellCount.z)
                return;

            int3 worldCell = new int3(
                cellOrigin.x + (int)id.x,
                cellOrigin.y + (int)id.y,
                cellOrigin.z + (int)id.z);

            float3 pt = new float3(
                worldCell.x * cellSize,
                worldCell.y * cellSize,
                worldCell.z * cellSize);

            int3 texel = new int3(
                WrapCell(worldCell.x, voxelResolution),
                WrapCell(worldCell.y, voxelResolution),
                atlasZOffset + WrapCell(worldCell.z, voxelResolution));

            float dist = WorldSDF(pt, out int closest);
            outputDistance[texel] = math.clamp(dist, -maxDistance, maxDistance);
            outputObjectId[texel] = closest < 0 ? uint.MaxValue : (uint)closest;
        }

        // Positive modulo, used by the toroidal chunk addressing.
        private static int TerrainMod(int value, int modulus)
        {
            int r = value % modulus;
            return r < 0 ? r + modulus : r;
        }

        private static int ClampTexel(int value, int stride)
        {
            if (value < 0) return 0;
            if (value > stride - 1) return stride - 1;
            return value;
        }

        /// <summary>
        /// Samples the streamed terrain quadtree at a position relative to the
        /// terrain origin. Falls back to the procedural base height for infinite
        /// terrains whose chunk has not streamed in yet.
        /// </summary>
        /// <returns>(height, dHdX, dHdZ, ridge01), with <paramref name="found"/> = 0
        /// outside a bounded terrain.</returns>
        private float4 SampleTerrainData(TerrainDTO meta, float2 localXZ, out int found)
        {
            found = 0;
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

                int slot = level * gridArea + TerrainMod(chunkZ, gridSize) * gridSize + TerrainMod(chunkX, gridSize);
                int4 info = terrainChunkInfo[meta.chunkInfoOffset + slot];
                if (info.z == 0 || info.x != chunkX || info.y != chunkZ) continue;

                float2 chunkOrigin = anchor + new float2(chunkX, chunkZ) * levelSize;
                float2 texel = (localXZ - chunkOrigin) / levelSize * (resolution - 1) + 1.0f;
                int texelBase = meta.bufferOffset + slot * stride * stride;
                float texelWorldSize = levelSize / math.max((float)(resolution - 1), 1.0f);

                float fade = 1.0f;
                if (meta.worldSpace == 1)
                {
                    float maxRadius = meta.lodBaseRadius * (1 << (meta.levels - 1));
                    float fadeStart = maxRadius * 0.85f;
                    fade = math.saturate((maxRadius - cameraDistance) / math.max(maxRadius - fadeStart, 1e-3f));
                }

                if (meta.interpolation == 0)
                {
                    int x0 = (int)math.floor(texel.x);
                    int z0 = (int)math.floor(texel.y);
                    if (x0 < 0) x0 = 0;
                    if (z0 < 0) z0 = 0;
                    if (x0 > stride - 2) x0 = stride - 2;
                    if (z0 > stride - 2) z0 = stride - 2;
                    int x1 = x0 + 1;
                    int z1 = z0 + 1;
                    float fx = texel.x - (float)x0;
                    float fz = texel.y - (float)z0;

                    float4 c00 = terrainHeightData[texelBase + z0 * stride + x0];
                    float4 c10 = terrainHeightData[texelBase + z0 * stride + x1];
                    float4 c01 = terrainHeightData[texelBase + z1 * stride + x0];
                    float4 c11 = terrainHeightData[texelBase + z1 * stride + x1];
                    float4 bilinear = TerrainRendering.BilinearTerrain4(c00, c10, c01, c11, fx, fz,
                        texelWorldSize, texelWorldSize);
                    bilinear.x *= fade;
                    bilinear.y *= fade;
                    bilinear.z *= fade;
                    found = 1;
                    return bilinear;
                }

                // Cubic Hermite reconstruction: four texels plus the stored
                // analytic slopes give a C1 surface without the sixteen-texel
                // Catmull-Rom footprint.
                int hx0 = (int)math.floor(texel.x);
                int hz0 = (int)math.floor(texel.y);
                if (hx0 < 0) hx0 = 0;
                if (hz0 < 0) hz0 = 0;
                if (hx0 > stride - 2) hx0 = stride - 2;
                if (hz0 > stride - 2) hz0 = stride - 2;
                float tx = texel.x - (float)hx0;
                float tz = texel.y - (float)hz0;

                float4 p00 = terrainHeightData[texelBase + hz0 * stride + hx0];
                float4 p10 = terrainHeightData[texelBase + hz0 * stride + hx0 + 1];
                float4 p01 = terrainHeightData[texelBase + (hz0 + 1) * stride + hx0];
                float4 p11 = terrainHeightData[texelBase + (hz0 + 1) * stride + hx0 + 1];
                float4 terrainSample = TerrainRendering.Hermite4(p00, p10, p01, p11, tx, tz,
                    texelWorldSize, texelWorldSize);
                terrainSample.x *= fade;
                terrainSample.y *= fade;
                terrainSample.z *= fade;

                found = 1;
                return terrainSample;
            }

            if (meta.worldSpace == 0)
            {
                found = 0;
                return new float4(0f, 0f, 0f, 0f);
            }

            found = 1;
            return new float4(0f, 0f, 0f, 0.5f);
        }

        private float ObjectSDF(float3 pt, SDFObjectDTO obj)
        {
            float3 curPoint = pt - obj.position;
            curPoint = ShaderMath.InverseRotateVector(curPoint, obj.rotation);
            curPoint *= obj.scaling;

            if (obj.type == 9) // Terrain heightfield
            {
                if (obj.terrainDataIndex < 0 || obj.terrainDataIndex >= terrainMetadata.Length)
                    return 1e8f;

                TerrainDTO meta = terrainMetadata[obj.terrainDataIndex];
                float2 sampleXZ = curPoint.xz;
                float2 outside = new float2(0f, 0f);
                if (meta.worldSpace == 0)
                {
                    float limit = math.max(meta.size - 0.01f, 0f);
                    sampleXZ = new float2(math.clamp(curPoint.x, -limit, limit),
                        math.clamp(curPoint.z, -limit, limit));
                    outside = math.abs(curPoint.xz) - new float2(meta.size, meta.size);
                }

                float4 terrainSample = SampleTerrainData(meta, sampleXZ, out int found);
                if (found == 0) return 1e8f;

                float distance = TerrainRendering.HeightfieldDistance(curPoint.y, terrainSample);

                if (meta.worldSpace == 0)
                {
                    distance = math.max(distance, math.max(outside.x, outside.y));
                    distance = math.max(distance, -meta.heightScale * 3.0f - curPoint.y);
                }

                return distance;
            }
            float dist = math.min(obj.scaling.x, math.min(obj.scaling.y, obj.scaling.z));
            dist *= EvaluatePrimitiveDistanceFast(obj.type, obj.parameters, curPoint);
            return dist * obj.stepBias;
        }

        private static float EvaluatePrimitiveDistanceFast(int type, float4 parameters, float3 localPt)
        {
            if (type == 0) return SDFPrimitives.Sphere(localPt, parameters.x);
            else if (type == 1) return SDFPrimitives.Box(localPt, parameters.xyz);
            else if (type == 2) return SDFPrimitives.RoundedBox(localPt, parameters.xyz, parameters.w);
            else if (type == 3) return SDFPrimitives.Torus(localPt, parameters.xy);
            else if (type == 4) return SDFPrimitives.Pyramid(localPt, parameters.x);
            else if (type == 5) return SDFPrimitives.Plane(localPt, parameters.xyz, parameters.w);
            else if (type == 6) return SDFPrimitives.Cylinder(localPt, parameters.x, parameters.y);
            else if (type == 7) return SDFPrimitives.Capsule(localPt, parameters.x, parameters.y);
            else if (type == 8) return SDFPrimitives.Cone(localPt, parameters.xy, parameters.z);
            else if (type == 10) return SDFPrimitives.Mandelbulb(localPt,
                math.clamp(parameters.x, 2f, 16f), (int)math.clamp(parameters.y, 1f, 12f), math.max(parameters.z, 1.1f));
            else if (type == 11) return SDFPrimitives.MengerSponge(localPt,
                math.max(parameters.x, 0.05f), math.max(parameters.y, 2f), (int)math.clamp(parameters.z, 0f, 6f));
            else if (type == 12) return SDFPrimitives.Mandelbox(localPt,
                parameters.x, parameters.y, parameters.z, (int)math.clamp(parameters.w, 1f, 16f));
            else return SDFPrimitives.Sphere(localPt, parameters.x);
        }

        private float WorldSDF(float3 pt, out int closest)
        {
            float minDist = MIN_TRAVERSE_DIST;
            float cullDist = 1e9f;
            closest = -1;
            for (int i = 0; i < sdfObjects.Length; i++)
            {
                SDFObjectDTO obj = sdfObjects[i];
                if (math.length(pt - obj.bounds.xyz) - obj.bounds.w > cullDist) continue;

                float dist = ObjectSDF(pt, obj);
                float absDist = math.abs(dist);
                if (absDist < cullDist) cullDist = absDist;
                if (absDist < minDist)
                {
                    closest = i;
                    minDist = dist;
                }
            }
            return minDist;
        }
    }
}
