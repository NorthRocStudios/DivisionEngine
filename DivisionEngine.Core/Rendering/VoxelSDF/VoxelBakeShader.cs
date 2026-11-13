//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.Graphics;
using DivisionEngine.MathLib;
using DivisionEngine.Rendering;
using DivisionEngine.Rendering.ShaderUtilities;

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
        [ShaderResource] public Buffer<float> terrainHeightData;
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

        private float SampleTerrainHeight(TerrainDTO meta, float2 worldXZ)
        {
            float2 uv = (worldXZ / meta.size) * 0.5f + 0.5f;
            uv = math.saturate(uv);

            float u = uv.x * (meta.resolution.x - 1);
            float v = uv.y * (meta.resolution.y - 1);
            int x0 = (int)u, y0 = (int)v;
            int x1 = math.min(x0 + 1, meta.resolution.x - 1);
            int y1 = math.min(y0 + 1, meta.resolution.y - 1);
            float fx = u - x0, fy = v - y0;

            int idx00 = meta.bufferOffset + y0 * meta.resolution.x + x0;
            int idx10 = meta.bufferOffset + y0 * meta.resolution.x + x1;
            int idx01 = meta.bufferOffset + y1 * meta.resolution.x + x0;
            int idx11 = meta.bufferOffset + y1 * meta.resolution.x + x1;

            float h0 = math.lerp(terrainHeightData[idx00], terrainHeightData[idx10], fx);
            float h1 = math.lerp(terrainHeightData[idx01], terrainHeightData[idx11], fx);
            return math.lerp(h0, h1, fy);
        }

        private float ObjectSDF(float3 pt, SDFObjectDTO obj)
        {
            float3 curPoint = pt - obj.position;
            curPoint = ShaderMath.InverseRotateVector(curPoint, obj.rotation);
            curPoint *= obj.scaling;

            if (obj.type == 9) // terrain
            {
                float height = 0f;
                TerrainDTO meta = new TerrainDTO();
                meta.heightScale = 20f;
                if (obj.terrainDataIndex >= 0 && obj.terrainDataIndex < terrainMetadata.Length)
                {
                    meta = terrainMetadata[obj.terrainDataIndex];
                    height = math.abs(SampleTerrainHeight(meta, curPoint.xz));
                }
                float halfSize = obj.parameters.x * 0.5f;
                return SDFPrimitives.Box(new float3(curPoint.x, curPoint.y - height, curPoint.z),
                    new float3(halfSize, meta.heightScale + height, halfSize));
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
