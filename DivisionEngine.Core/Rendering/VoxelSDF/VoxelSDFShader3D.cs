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
using DivisionEngine.Rendering.VoxelSDF;

namespace DivisionEngine
{
    /// <summary>
    /// Renders the scene from a four-level, world-stable SDF clipmap. Each level is
    /// trilinearly interpolated and neighbouring levels are blended across a
    /// transition band. Outside every ready level the shader falls back to the exact
    /// scene SDF, which keeps infinite planes and distant terrain correct.
    /// </summary>
    [Shader]
    public struct VoxelSDFShader3D
    {
        public float width;
        public float height;
        public float aspect;
        public int frameCount;
        public int debugMode;
        public int enableCheckerboard;

        public int levelCount;
        public int voxelResolution;
        public float level0Radius;

        [ShaderResource] public StructuredBuffer<SDFWorldDTO> worldData;
        [ShaderResource] public StructuredBuffer<VoxelLevelDTO> voxelLevels;
        [ShaderResource] public RWTexture2D<float4> outputTexture;
        [ShaderResource] public RWTexture2D<float4> depthNormals;
        [ShaderResource] public RWBuffer<uint2> entityIdBuffer;
        [ShaderResource] public StructuredBuffer<SDFObjectDTO> sdfObjects;
        [ShaderResource] public StructuredBuffer<SDFLightDTO> lights;
        [ShaderResource] public Buffer<uint> textureData;
        [ShaderResource] public StructuredBuffer<TextureMetadata> textureMetadata;
        [ShaderResource] public Buffer<float> terrainHeightData;
        [ShaderResource] public StructuredBuffer<TerrainDTO> terrainMetadata;
        [ShaderResource] public Texture3D<float> voxelDistance;
        [ShaderResource] public Texture3D<uint> voxelObjectId;

        const float EPSILON = 0.0001f;
        const float PI = 3.141592654f;
        const float RECIPROCAL_PI = 1f / PI;
        const float MIN_TRAVERSE_DIST = 100000000.0f;
        const int MAX_REFLECTION_BOUNCES = 3;

        #region textures

        private float4 SampleTextureBilinear(int textureId, float2 uv, float4 fallbackColor)
        {
            if (textureId < 0 || textureId >= textureMetadata.Length) return fallbackColor;
            float2 newUV = new float2(math.abs(uv.x % 1), math.abs(uv.y % 1));

            TextureMetadata meta = textureMetadata[textureId];
            float u = newUV.x * (meta.resolution.x - 1);
            float v = newUV.y * (meta.resolution.y - 1);

            int x0 = (int)u;
            int y0 = (int)v;
            int x1 = math.min(x0 + 1, meta.resolution.x - 1);
            int y1 = math.min(y0 + 1, meta.resolution.y - 1);
            float uFrac = u - x0;
            float vFrac = v - y0;

            int idx00 = meta.bufferOffset + y0 * meta.resolution.x + x0;
            int idx10 = meta.bufferOffset + y0 * meta.resolution.x + x1;
            int idx01 = meta.bufferOffset + y1 * meta.resolution.x + x0;
            int idx11 = meta.bufferOffset + y1 * meta.resolution.x + x1;

            float4 c00 = ShaderMath.UnpackRGBA(textureData[idx00]);
            float4 c10 = ShaderMath.UnpackRGBA(textureData[idx10]);
            float4 c01 = ShaderMath.UnpackRGBA(textureData[idx01]);
            float4 c11 = ShaderMath.UnpackRGBA(textureData[idx11]);

            float4 c0 = math.lerp(c00, c10, uFrac);
            float4 c1 = math.lerp(c01, c11, uFrac);
            return math.lerp(c0, c1, vFrac);
        }

        private static float3 TriplanarWeights(float3 localNormal, float sharpness)
        {
            float3 blend = math.pow(math.abs(localNormal), sharpness);
            return blend / math.max(blend.x + blend.y + blend.z, EPSILON);
        }

        private float4 SampleTriplanar(int textureId, float3 localPos, float3 blend, float scale, float4 fallback)
        {
            float4 colX = SampleTextureBilinear(textureId, localPos.yz * scale, fallback);
            float4 colY = SampleTextureBilinear(textureId, localPos.xz * scale, fallback);
            float4 colZ = SampleTextureBilinear(textureId, localPos.xy * scale, fallback);
            return colX * blend.x + colY * blend.y + colZ * blend.z;
        }

        private float SampleTriplanarScalar(int textureId, float3 localPos, float3 blend, float scale, float fallback)
        {
            return SampleTriplanar(textureId, localPos, blend, scale, fallback * new float4(1f)).r;
        }

        #endregion textures
        #region scene_sdf

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

        private float ExactWorldSDF(float3 pt, out int closest)
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

        private int ResolveClosestObject(float3 pt)
        {
            float minDist = MIN_TRAVERSE_DIST;
            int closest = -1;
            for (int i = 0; i < sdfObjects.Length; i++)
            {
                SDFObjectDTO obj = sdfObjects[i];
                if (math.length(pt - obj.bounds.xyz) - obj.bounds.w > minDist) continue;
                float dist = math.abs(ObjectSDF(pt, obj));
                if (dist < minDist)
                {
                    minDist = dist;
                    closest = i;
                }
            }
            return closest;
        }

        private int WrapCell(int value)
        {
            int wrapped = value % voxelResolution;
            return wrapped < 0 ? wrapped + voxelResolution : wrapped;
        }

        private int3 WrapCell3(int3 value)
        {
            return new int3(WrapCell(value.x), WrapCell(value.y), WrapCell(value.z));
        }

        private float LevelCellSize(int level)
        {
            return voxelLevels[level].cellSize;
        }

        private bool LevelIsFull(int level)
        {
            return voxelLevels[level].full != 0;
        }

        private int3 LevelMinCell(int level)
        {
            return voxelLevels[level].minCell;
        }

        private int3 LevelMaxCell(int level)
        {
            return voxelLevels[level].maxCell;
        }

        private float LevelDistance(int level, int3 cell)
        {
            return voxelDistance[new int3(cell.x, cell.y, level * voxelResolution + cell.z)];
        }

        private uint LevelObjectId(int level, int3 cell)
        {
            return voxelObjectId[new int3(cell.x, cell.y, level * voxelResolution + cell.z)];
        }

        private bool LevelHasData(int level)
        {
            int3 minCell = LevelMinCell(level);
            int3 maxCell = LevelMaxCell(level);
            return minCell.x <= maxCell.x && minCell.y <= maxCell.y && minCell.z <= maxCell.z;
        }

        private static int3 CellAt(float3 p, float cellSize)
        {
            return new int3(
                (int)math.floor(p.x / cellSize),
                (int)math.floor(p.y / cellSize),
                (int)math.floor(p.z / cellSize));
        }

        private bool InsideLevel(int level, float3 p)
        {
            if (!LevelHasData(level)) return false;

            int3 cell = CellAt(p, LevelCellSize(level));
            int3 minCell = LevelMinCell(level);
            int3 maxCell = LevelMaxCell(level);
            return cell.x >= minCell.x && cell.x <= maxCell.x &&
                   cell.y >= minCell.y && cell.y <= maxCell.y &&
                   cell.z >= minCell.z && cell.z <= maxCell.z;
        }

        private float CameraDistance(float3 p)
        {
            return math.length(p - worldData[0].cameraOrigin);
        }

        private float LevelRadius(int level)
        {
            return level0Radius * (1 << level);
        }

        private int LevelFromDistanceValue(float dist)
        {
            int level = 0;
            float radius = level0Radius;
            if (dist > radius) { level = 1; radius *= 2f; }
            if (dist > radius) { level = 2; radius *= 2f; }
            if (dist > radius) { level = 3; radius *= 2f; }
            return math.min(level, levelCount - 1);
        }

        private int LevelFromDistance(float3 p)
        {
            return LevelFromDistanceValue(CameraDistance(p));
        }

        private int LevelAt(float3 p)
        {
            int level = LevelFromDistance(p);
            while (level < levelCount && !InsideLevel(level, p))
                level++;
            return level < levelCount ? level : -1;
        }

        private float LevelEdgeDistance(int level, float3 p)
        {
            float cellSize = LevelCellSize(level);
            int3 minCell = LevelMinCell(level);
            int3 maxCell = LevelMaxCell(level);
            float3 minWorld = new float3(minCell.x * cellSize, minCell.y * cellSize, minCell.z * cellSize);
            float3 maxWorld = new float3(
                (maxCell.x + 1) * cellSize,
                (maxCell.y + 1) * cellSize,
                (maxCell.z + 1) * cellSize);
            float3 q = math.min(p - minWorld, maxWorld - p);
            return math.min(q.x, math.min(q.y, q.z));
        }

        private float SampleLevel(int level, float3 p, out int objectId)
        {
            float cellSize = LevelCellSize(level);
            int3 minCell = LevelMinCell(level);
            int3 maxCell = LevelMaxCell(level);
            float3 g = p / cellSize;

            int3 baseCell = new int3(
                (int)math.floor(g.x),
                (int)math.floor(g.y),
                (int)math.floor(g.z));

            // Clamp the base so the two trilinear corners stay inside the ready
            // region. One-cell-wide regions reuse the same cell for both corners.
            int maxBaseX = minCell.x < maxCell.x ? maxCell.x - 1 : minCell.x;
            int maxBaseY = minCell.y < maxCell.y ? maxCell.y - 1 : minCell.y;
            int maxBaseZ = minCell.z < maxCell.z ? maxCell.z - 1 : minCell.z;
            baseCell = new int3(
                math.clamp(baseCell.x, minCell.x, maxBaseX),
                math.clamp(baseCell.y, minCell.y, maxBaseY),
                math.clamp(baseCell.z, minCell.z, maxBaseZ));

            float3 f = new float3(
                math.saturate(g.x - baseCell.x),
                math.saturate(g.y - baseCell.y),
                math.saturate(g.z - baseCell.z));

            int3 c1 = new int3(
                baseCell.x < maxCell.x ? baseCell.x + 1 : baseCell.x,
                baseCell.y < maxCell.y ? baseCell.y + 1 : baseCell.y,
                baseCell.z < maxCell.z ? baseCell.z + 1 : baseCell.z);

            // Wrap the two corner indices once each, then assemble the eight
            // samples. Wrapping every corner separately costs 24 integer modulos.
            int3 wb = WrapCell3(baseCell);
            int3 wc = WrapCell3(c1);
            int3 t000 = wb;
            int3 t100 = new int3(wc.x, wb.y, wb.z);
            int3 t010 = new int3(wb.x, wc.y, wb.z);
            int3 t110 = new int3(wc.x, wc.y, wb.z);
            int3 t001 = new int3(wb.x, wb.y, wc.z);
            int3 t101 = new int3(wc.x, wb.y, wc.z);
            int3 t011 = new int3(wb.x, wc.y, wc.z);
            int3 t111 = wc;

            float d000 = LevelDistance(level, t000);
            float d100 = LevelDistance(level, t100);
            float d010 = LevelDistance(level, t010);
            float d110 = LevelDistance(level, t110);
            float d001 = LevelDistance(level, t001);
            float d101 = LevelDistance(level, t101);
            float d011 = LevelDistance(level, t011);
            float d111 = LevelDistance(level, t111);

            float d00 = math.lerp(d000, d100, f.x);
            float d10 = math.lerp(d010, d110, f.x);
            float d01 = math.lerp(d001, d101, f.x);
            float d11 = math.lerp(d011, d111, f.x);
            float d0 = math.lerp(d00, d10, f.y);
            float d1 = math.lerp(d01, d11, f.y);

            int3 nearest = new int3(
                baseCell.x + (f.x > 0.5f ? 1 : 0),
                baseCell.y + (f.y > 0.5f ? 1 : 0),
                baseCell.z + (f.z > 0.5f ? 1 : 0));
            nearest = new int3(
                math.min(nearest.x, maxCell.x),
                math.min(nearest.y, maxCell.y),
                math.min(nearest.z, maxCell.z));
            uint id = LevelObjectId(level, WrapCell3(nearest));
            objectId = id == uint.MaxValue ? -1 : (int)id;

            return math.lerp(d0, d1, f.z);
        }

        private float3 SampleLevelNormal(int level, float3 p)
        {
            float cellSize = LevelCellSize(level);
            int3 minCell = LevelMinCell(level);
            int3 maxCell = LevelMaxCell(level);
            float3 g = p / cellSize;

            int3 baseCell = new int3(
                (int)math.floor(g.x),
                (int)math.floor(g.y),
                (int)math.floor(g.z));

            int maxBaseX = minCell.x < maxCell.x ? maxCell.x - 1 : minCell.x;
            int maxBaseY = minCell.y < maxCell.y ? maxCell.y - 1 : minCell.y;
            int maxBaseZ = minCell.z < maxCell.z ? maxCell.z - 1 : minCell.z;
            baseCell = new int3(
                math.clamp(baseCell.x, minCell.x, maxBaseX),
                math.clamp(baseCell.y, minCell.y, maxBaseY),
                math.clamp(baseCell.z, minCell.z, maxBaseZ));

            float3 f = new float3(
                math.saturate(g.x - baseCell.x),
                math.saturate(g.y - baseCell.y),
                math.saturate(g.z - baseCell.z));

            int3 c1 = new int3(
                baseCell.x < maxCell.x ? baseCell.x + 1 : baseCell.x,
                baseCell.y < maxCell.y ? baseCell.y + 1 : baseCell.y,
                baseCell.z < maxCell.z ? baseCell.z + 1 : baseCell.z);

            int3 wb = WrapCell3(baseCell);
            int3 wc = WrapCell3(c1);
            float d000 = LevelDistance(level, wb);
            float d100 = LevelDistance(level, new int3(wc.x, wb.y, wb.z));
            float d010 = LevelDistance(level, new int3(wb.x, wc.y, wb.z));
            float d110 = LevelDistance(level, new int3(wc.x, wc.y, wb.z));
            float d001 = LevelDistance(level, new int3(wb.x, wb.y, wc.z));
            float d101 = LevelDistance(level, new int3(wc.x, wb.y, wc.z));
            float d011 = LevelDistance(level, new int3(wb.x, wc.y, wc.z));
            float d111 = LevelDistance(level, wc);

            float dx = math.lerp(
                math.lerp(d100 - d000, d110 - d010, f.y),
                math.lerp(d101 - d001, d111 - d011, f.y), f.z);
            float dy = math.lerp(
                math.lerp(d010 - d000, d110 - d100, f.x),
                math.lerp(d011 - d001, d111 - d101, f.x), f.z);
            float dz = math.lerp(
                math.lerp(d001 - d000, d101 - d100, f.x),
                math.lerp(d011 - d010, d111 - d110, f.x), f.y);
            return new float3(dx, dy, dz);
        }

        private float WorldSDF(float3 pt, out int closest)
        {
            return WorldSDFAtDistance(pt, CameraDistance(pt), out closest);
        }

        private float WorldSDFAtDistance(float3 pt, float cameraDistance, out int closest)
        {
            int level = LevelFromDistanceValue(cameraDistance);

            // A full level's cube always contains the inscribed sphere selected by
            // LevelFromDistance, so the bounds check can be skipped in the common
            // case. While a shell is streaming, fall through to the safe search.
            if (level == levelCount - 1 && cameraDistance > LevelRadius(level))
                return ExactWorldSDF(pt, out closest);

            if (!LevelIsFull(level))
            {
                while (level < levelCount && !InsideLevel(level, pt))
                    level++;
                if (level >= levelCount)
                    return ExactWorldSDF(pt, out closest);
            }

            float dist = SampleLevel(level, pt, out int objectId);

            // Blend into the next coarser level over the outer two cells so
            // clipmap transitions do not produce a hard LOD seam.
            if (level + 1 < levelCount && InsideLevel(level + 1, pt))
            {
                float edgeDistance = LevelEdgeDistance(level, pt);
                float transition = LevelCellSize(level) * 2f;
                if (edgeDistance < transition)
                {
                    float coarseDist = SampleLevel(level + 1, pt, out _);
                    float t = math.saturate(edgeDistance / transition);
                    dist = math.lerp(coarseDist, dist, t);
                }
            }

            closest = objectId;
            return dist;
        }

        private float WorldSDFAtLevel(float3 pt, int level, out int closest)
        {
            if (level < 0 || level >= levelCount || !InsideLevel(level, pt))
                return WorldSDF(pt, out closest);

            float dist = SampleLevel(level, pt, out int objectId);
            closest = objectId;
            return dist;
        }

        private float3 ExactNormal(float3 pos)
        {
            float h = EPSILON * 50f;
            float3 k0 = new float3(1f, -1f, -1f);
            float3 k1 = new float3(-1f, -1f, 1f);
            float3 k2 = new float3(-1f, 1f, -1f);
            float3 k3 = new float3(1f, 1f, 1f);
            return math.normalize(k0 * ExactWorldSDF(pos + k0 * h, out _) +
                                  k1 * ExactWorldSDF(pos + k1 * h, out _) +
                                  k2 * ExactWorldSDF(pos + k2 * h, out _) +
                                  k3 * ExactWorldSDF(pos + k3 * h, out _));
        }

        private float3 SceneNormal(float3 pos)
        {
            int level = LevelAt(pos);
            if (level < 0) return ExactNormal(pos);

            float3 n = SampleLevelNormal(level, pos);
            float len = math.length(n);
            if (len < EPSILON) return ExactNormal(pos);
            return n / len;
        }

        private static float3 DebugLevelColor(int level)
        {
            if (level == 0) return new float3(1f, 0.25f, 0.12f);
            if (level == 1) return new float3(0.2f, 1f, 0.3f);
            if (level == 2) return new float3(0.2f, 0.55f, 1f);
            if (level == 3) return new float3(1f, 0.85f, 0.2f);
            return new float3(0.6f, 0.2f, 1f);
        }

        #endregion scene_sdf
        #region sky

        private float3 GetSkyColor(float3 viewDir)
        {
            SDFWorldDTO world = worldData[0];
            float3 sky;
            if (world.skyType == 0)
            {
                sky = world.skyColor.rgb * world.skyIntensity;
            }
            else if (world.skyType == 2)
            {
                sky = GetHDRISkyColor(viewDir, world);
            }
            else
            {
                // Gradient is the default; procedural atmosphere falls back to it
                // in the voxel path (the exact path still has the full sky model).
                sky = GetGradientSkyColor(viewDir, world);
            }

            float3 sunDir = math.normalize(world.mainLightDir + new float3(1e-5f, 1e-5f, 1e-5f));
            float sunAmount = math.saturate(math.dot(viewDir, sunDir));
            float disc = math.smoothstep(1f - world.sunDiscSize, 1f, sunAmount);
            float3 sunColor = world.sunColor.rgb * world.sunIntensity;
            sky += sunColor * disc * 4f;
            sky += sunColor * math.pow(sunAmount, 16f) * 0.08f;
            return sky;
        }

        private static float3 GetGradientSkyColor(float3 viewDir, SDFWorldDTO world)
        {
            float t = math.saturate(viewDir.y * 0.5f + 0.5f);
            float3 color;
            if (t < 0.5f)
                color = math.lerp(world.bottomSkyColor.rgb, world.middleSkyColor.rgb, t / 0.5f);
            else
                color = math.lerp(world.middleSkyColor.rgb, world.topSkyColor.rgb, (t - 0.5f) / 0.5f);

            float horizonGlow = 1f - math.pow(math.abs(viewDir.y), 4f);
            color += new float3(1f, 0.8f, 0.5f) * 0.3f * horizonGlow * 0.2f;
            return color * world.skyIntensity;
        }

        private float3 GetHDRISkyColor(float3 viewDir, SDFWorldDTO world)
        {
            int texId = world.hdriTexMetaID;
            if (texId < 0 || texId >= textureMetadata.Length)
                return world.skyColor.rgb * world.skyIntensity;

            int layout = textureMetadata[texId].cubemapLayout;
            if (layout == 1) // equirectangular
            {
                float theta = math.atan2(viewDir.z, viewDir.x);
                float phi = math.acos(viewDir.y);
                float u = (theta + PI) / (2f * PI);
                float v = phi / PI;
                return SampleTextureBilinear(texId, new float2(u, v), new float4(0f)).rgb * world.skyIntensity;
            }

            float3 absDir = math.abs(viewDir);
            float maxComp = math.max(absDir.x, math.max(absDir.y, absDir.z));
            int faceCol = 1, faceRow = 1;
            float2 faceUV = new float2(0f);
            if (maxComp == absDir.x && viewDir.x > 0) { faceUV = new float2(-viewDir.z, -viewDir.y) / absDir.x; faceCol = 2; faceRow = 1; }
            else if (maxComp == absDir.x) { faceUV = new float2(viewDir.z, -viewDir.y) / absDir.x; faceCol = 0; faceRow = 1; }
            else if (maxComp == absDir.y && viewDir.y > 0) { faceUV = new float2(viewDir.x, viewDir.z) / absDir.y; faceCol = 1; faceRow = 0; }
            else if (maxComp == absDir.y) { faceUV = new float2(viewDir.x, -viewDir.z) / absDir.y; faceCol = 1; faceRow = 2; }
            else if (viewDir.z > 0) { faceUV = new float2(viewDir.x, -viewDir.y) / absDir.z; faceCol = 1; faceRow = 1; }
            else { faceUV = new float2(-viewDir.x, viewDir.y) / absDir.z; faceCol = 1; faceRow = 3; }

            faceUV = faceUV * 0.5f + 0.5f;
            float2 globalUV = layout == 2
                ? new float2((faceCol + faceUV.x) / 4f, (faceRow + faceUV.y) / 3f)
                : new float2((faceCol + faceUV.x) / 3f, (faceRow + faceUV.y) / 4f);
            return SampleTextureBilinear(texId, globalUV, new float4(0f)).rgb * world.skyIntensity;
        }

        #endregion sky
        #region lighting

        private float CalculateShadow(float3 pt, float3 normal, float3 dir)
        {
            int lodHint = LevelAt(pt);
            float t = 0.02f;
            float shadow = 1f;
            for (int i = 0; i < worldData[0].maxShadowRaySteps; i++)
            {
                float3 q = pt + dir * t;
                int shadowDiscard;
                float d;
                if (lodHint >= 0)
                {
                    d = WorldSDFAtLevel(q, lodHint, out shadowDiscard);
                }
                else
                {
                    d = WorldSDF(q, out shadowDiscard);
                }
                shadow = math.min(shadow, 8f * d / t);
                if (d < EPSILON * 4f)
                {
                    shadow = -1f;
                    break;
                }
                t += math.clamp(d, 0.03f, 2f);
                if (t > 64f) break;
            }
            // Same penumbra shaping as the exact path: the ratio is negative on a
            // hit, so smoothstep gives a soft edge instead of a hard voxel stair.
            return math.saturate(math.smoothstep(-1f, 0f, shadow));
        }

        private float3 CalculateLighting(float3 hitPoint, float3 geoNormal, float3 finalNormal, float3 viewDir,
            int2 shadowEffects, float specular, float reflectance, float3 baseCol, float metallic, float roughAlpha)
        {
            float3 totalLight = new float3(0f);
            for (int i = 0; i < lights.Length; i++)
            {
                SDFLightDTO light = lights[i];
                if (light.type == 0)
                {
                    float3 lightDir = math.normalize(ShaderMath.RotateVector(new float3(0, 0, -1), light.rotation));
                    float3 lightColor = light.color.rgb * light.intensity;
                    float NoL = math.max(math.dot(finalNormal, lightDir), 0f);
                    if (NoL <= 0f) continue;

                    float shadow = shadowEffects.y != 0 ? CalculateShadow(hitPoint, geoNormal, lightDir) : 1f;
                    float3 brdf = PBR.BRDFMicrofacetFunction(lightDir, viewDir, finalNormal, baseCol,
                        metallic, roughAlpha, specular, reflectance, RECIPROCAL_PI, EPSILON);
                    totalLight += brdf * lightColor * NoL * shadow;
                }
                else if (light.type == 1)
                {
                    float3 lightVec = light.position - hitPoint;
                    float distance = math.length(lightVec);
                    float3 lightDir = lightVec / math.max(distance, EPSILON);
                    float attenuation = math.rcp(math.max(distance * distance, EPSILON));
                    attenuation *= math.saturate(1f - distance / math.max(light.radius, EPSILON));
                    float NoL = math.max(math.dot(finalNormal, lightDir), 0f);
                    if (NoL <= 0f || attenuation <= 0f) continue;

                    float shadow = shadowEffects.y != 0 ? CalculateShadow(hitPoint, geoNormal, lightDir) : 1f;
                    float3 brdf = PBR.BRDFMicrofacetFunction(lightDir, viewDir, finalNormal, baseCol,
                        metallic, roughAlpha, specular, reflectance, RECIPROCAL_PI, EPSILON);
                    totalLight += brdf * light.color.rgb * light.intensity * attenuation * NoL * shadow;
                }
            }
            return totalLight;
        }

        private void SampleSurfaceMaterial(float3 hitPoint, float3 geoNormal, SDFObjectDTO sdf,
            out float3 albedo, out float metallic, out float roughness, out float3 finalNormal)
        {
            float3 localPos = ShaderMath.InverseRotateVector(hitPoint - sdf.position, sdf.rotation);
            float3 localGeoNormal = math.normalize(ShaderMath.InverseRotateVector(geoNormal, sdf.rotation));
            float3 blend = TriplanarWeights(localGeoNormal, sdf.triplanarBlend);
            float scale = math.rcp(math.max(sdf.texTilingOffset.x, EPSILON));

            albedo = SampleTriplanar(sdf.albedoTexMetaID, localPos, blend, scale, sdf.color).rgb;
            metallic = SampleTriplanarScalar(sdf.metalTexMetaID, localPos, blend, scale, sdf.metallic);
            roughness = math.max(SampleTriplanarScalar(sdf.roughTexMetaID, localPos, blend, scale, sdf.roughness), 0.045f);
            finalNormal = geoNormal;
        }

        private static bool Refract(float3 incident, float3 normal, float eta, out float3 refracted)
        {
            float NdotI = math.dot(normal, incident);
            float k = 1.0f - eta * eta * (1.0f - NdotI * NdotI);
            if (k < 0f)
            {
                refracted = new float3(0f);
                return false;
            }
            refracted = eta * incident - (eta * NdotI + math.sqrt(k)) * normal;
            refracted = math.normalize(refracted);
            return true;
        }

        private float3 FastNormalSingleObject(float3 pos, SDFObjectDTO sdf)
        {
            float h = EPSILON * 50f;
            float3 k0 = new float3(1f, -1f, -1f);
            float3 k1 = new float3(-1f, -1f, 1f);
            float3 k2 = new float3(-1f, 1f, -1f);
            float3 k3 = new float3(1f, 1f, 1f);
            return math.normalize(k0 * ObjectSDF(pos + k0 * h, sdf) +
                                  k1 * ObjectSDF(pos + k1 * h, sdf) +
                                  k2 * ObjectSDF(pos + k2 * h, sdf) +
                                  k3 * ObjectSDF(pos + k3 * h, sdf));
        }

        #endregion lighting
        #region raymarching

        private float3 Raymarch(float3 rayOrigin, float3 rayDir, int maxSteps, float farClipPlane,
            out int closestObj, out float depth, out int steps, int lodHint)
        {
            depth = worldData[0].nearPlane;
            closestObj = -1;
            float3 hitPoint = rayOrigin;
            float lastSafeDepth = depth;
            float camScreenDist = worldData[0].camScreenDist;

            for (steps = 0; steps < maxSteps && depth < farClipPlane; steps++)
            {
                hitPoint = rayOrigin + rayDir * depth;
                float worldDist;
                if (lodHint == -2)
                    worldDist = WorldSDFAtDistance(hitPoint, depth, out closestObj);
                else if (lodHint >= 0)
                    worldDist = WorldSDFAtLevel(hitPoint, lodHint, out closestObj);
                else
                    worldDist = WorldSDF(hitPoint, out closestObj);

                if (worldDist > 0f)
                {
                    lastSafeDepth = depth;
                }
                else if (worldDist < 0f)
                {
                    float tMin = lastSafeDepth;
                    float tMax = depth;
                    const float BISECT_EPS = EPSILON * 10f;
                    int maxRefine = depth < 100f ? 8 : 4;
                    for (int refine = 0; refine < maxRefine; refine++)
                    {
                        float tMid = (tMin + tMax) * 0.5f;
                        float3 refinePoint = rayOrigin + rayDir * tMid;
                        float refineDist;
                        if (lodHint == -2)
                        {
                            refineDist = WorldSDFAtDistance(refinePoint, tMid, out _);
                        }
                        else if (lodHint >= 0)
                        {
                            refineDist = WorldSDFAtLevel(refinePoint, lodHint, out _);
                        }
                        else
                        {
                            refineDist = WorldSDF(refinePoint, out _);
                        }
                        if (math.abs(refineDist) < BISECT_EPS)
                        {
                            depth = tMid;
                            hitPoint = rayOrigin + rayDir * (tMid - BISECT_EPS);
                            closestObj = ResolveClosestObject(hitPoint);
                            return hitPoint;
                        }
                        if (refineDist > 0f) tMin = tMid;
                        else tMax = tMid;
                    }
                    depth = tMin;
                    hitPoint = rayOrigin + rayDir * tMin;
                    closestObj = ResolveClosestObject(hitPoint);
                    return hitPoint;
                }

                float hitEps = depth < 10f ? EPSILON : ShaderMath.AdaptiveEpsilon(depth, width, height, camScreenDist, EPSILON);
                if (worldDist < hitEps)
                {
                    closestObj = ResolveClosestObject(hitPoint);
                    break;
                }

                // Trilinear reconstruction can slightly overestimate the true
                // distance, so clipmap steps are shortened to stay conservative.
                float step = math.max(worldDist, EPSILON * 4f) * 0.72f;
                depth += step;
            }
            return hitPoint;
        }

        private float3 ShadeRefractedHit(float3 hitPoint, float3 rayDir, SDFObjectDTO sdf)
        {
            float3 normal = SceneNormal(hitPoint);
            if (math.dot(normal, rayDir) > 0f) normal = -normal;

            SampleSurfaceMaterial(hitPoint, normal, sdf,
                out float3 albedo, out float metallic, out float roughness, out float3 finalNormal);
            float roughAlpha = roughness * roughness;
            float3 directLight = CalculateLighting(hitPoint, normal, finalNormal, -rayDir,
                sdf.shadowEffects, sdf.specular, sdf.reflectance, albedo, metallic, roughAlpha);
            float3 ambientBase = math.lerp(albedo, worldData[0].skyColor.rgb, worldData[0].ambientStrength) * worldData[0].ambientStrength;
            return directLight + ambientBase;
        }

        private float3 TraceRefractionRay(float3 startDir, float3 startOrigin,
            float3 normal, SDFObjectDTO startMat, out int steps)
        {
            steps = 0;
            float3 refracted;
            if (!Refract(startDir, normal, 1.0f / math.max(startMat.ior, EPSILON), out refracted))
                return GetSkyColor(startDir);

            float3 p = startOrigin - normal * EPSILON * 2f;
            float3 exitPoint = new float3(0f);
            float3 exitNormal = new float3(0f);
            float travel = 0f;
            bool foundExit = false;

            for (int i = 0; i < startMat.refractionMaxSteps; i++)
            {
                float d = ObjectSDF(p, startMat);
                if (d >= 0f)
                {
                    exitPoint = p;
                    exitNormal = FastNormalSingleObject(p, startMat);
                    foundExit = true;
                    break;
                }
                float stepSize = math.max(math.abs(d), EPSILON * 4f);
                p += refracted * stepSize;
                travel += stepSize;
            }

            float3 absorb = math.exp(math.log(math.max(startMat.absorptionColor.rgb, 0.001f)) *
                travel * startMat.absorptionColor.a * 5f);

            if (!foundExit) return GetSkyColor(refracted) * absorb;
            if (math.dot(exitNormal, refracted) > 0f) exitNormal = -exitNormal;

            float3 exitDir;
            if (!Refract(refracted, exitNormal, math.max(startMat.ior, EPSILON), out exitDir))
                exitDir = math.reflect(refracted, exitNormal);

            float3 rayStart = exitPoint + exitNormal * EPSILON * 2f;
            float3 hitPoint = Raymarch(rayStart, exitDir, worldData[0].maxRaySteps, worldData[0].farPlane,
                out int nextObjIndex, out _, out int tracedSteps, LevelAt(rayStart));
            steps += tracedSteps;

            float3 color = nextObjIndex >= 0 && nextObjIndex < sdfObjects.Length
                ? ShadeRefractedHit(hitPoint, exitDir, sdfObjects[nextObjIndex])
                : GetSkyColor(exitDir);
            return color * absorb;
        }

        private float3 TraceRay(int2 pixel, float3 rayOrigin, float3 rayDir,
            out float3 outputNormal, out float totalDist, out int steps)
        {
            float3 finalColor = new float3(0f);
            float3 contribution = new float3(1f);
            outputNormal = new float3(0f);
            totalDist = 0f;
            steps = 0;
            float farPlane = worldData[0].farPlane;
            int maxRaySteps = worldData[0].maxRaySteps;
            bool firstHit = true;

            float3 refractedLight = new float3(0f);
            SDFObjectDTO mainMat = default;
            float cosTheta = 0f;
            bool isRefractive = false;

            for (int bounce = 0; bounce < MAX_REFLECTION_BOUNCES; bounce++)
            {
                int lodHint = bounce == 0 ? -2 : LevelAt(rayOrigin);
                float3 hitPoint = Raymarch(rayOrigin, rayDir, maxRaySteps, farPlane,
                    out int closestObjIndex, out float depth, out int tracedSteps, lodHint);
                steps += tracedSteps;
                float3 sky = GetSkyColor(rayDir);

                if (closestObjIndex < 0 || depth > farPlane)
                {
                    finalColor += contribution * sky;
                    if (firstHit) totalDist = depth;
                    break;
                }

                SDFObjectDTO sdf = sdfObjects[closestObjIndex];
                float3 normal = SceneNormal(hitPoint);
                if (math.dot(normal, rayDir) > 0f) normal = -normal;
                float3 viewDir = -rayDir;

                SampleSurfaceMaterial(hitPoint, normal, sdf,
                    out float3 albedo, out float metallic, out float roughness, out float3 finalNormal);
                float roughAlpha = roughness * roughness;
                float3 directLight = CalculateLighting(hitPoint, normal, finalNormal, viewDir,
                    sdf.shadowEffects, sdf.specular, sdf.reflectance, albedo, metallic, roughAlpha);
                float3 ambientBase = math.lerp(albedo, worldData[0].skyColor.rgb, worldData[0].ambientStrength) * worldData[0].ambientStrength;

                if (firstHit)
                {
                    outputNormal = normal;
                    totalDist = depth;
                    entityIdBuffer[pixel.x + pixel.y * (int)width] = new uint2((uint)closestObjIndex, sdf.entityID);
                    finalColor += directLight + ambientBase;
                    mainMat = sdf;
                    cosTheta = math.max(math.dot(normal, viewDir), 0f);

                    if (sdf.hasRefraction == 1)
                    {
                        isRefractive = true;
                        refractedLight = TraceRefractionRay(rayDir, hitPoint, normal, sdf, out int refractionSteps);
                        steps += refractionSteps;
                    }
                    firstHit = false;
                }
                else
                {
                    finalColor += contribution * (directLight + ambientBase);
                }

                if (sdf.hasReflection == 0) break;
                if (bounce == MAX_REFLECTION_BOUNCES - 1) break;

                float3 f0 = math.lerp(sdf.f0_reflectance, albedo, new float3(metallic, metallic, metallic));
                float3 F = PBR.FresnelSchlick(math.max(math.dot(finalNormal, viewDir), 0f), f0, sdf.reflectance, 1f);
                contribution *= F;
                if (contribution.x + contribution.y + contribution.z < 0.02f) break;

                rayOrigin = hitPoint + normal * EPSILON * 2f;
                rayDir = math.normalize(math.reflect(rayDir, normal));
            }

            if (isRefractive)
            {
                float3 f0 = new float3(1f) * mainMat.f0_dielectric;
                float3 fresnel = PBR.FresnelSchlick(cosTheta, f0, mainMat.reflectance, 1f);
                return finalColor * fresnel + refractedLight * (1f - fresnel);
            }
            return finalColor;
        }

        #endregion raymarching
        #region main

        [Kernel(8, 8, 1)]
        public void Execute(uint3 id)
        {
            int2 localPixel = new int2((int)id.x, (int)id.y);
            int2 pixel;
            if (enableCheckerboard == 1)
            {
                int framePass = frameCount & 1;
                pixel = new int2(localPixel.x * 2 + (framePass ^ (localPixel.y & 1)), localPixel.y);
            }
            else pixel = localPixel;
            if (pixel.x >= width || pixel.y >= height) return;

            outputTexture[pixel] = new float4(0, 0, 0, 0);
            depthNormals[pixel] = new float4(0, 0, 0, 0);
            entityIdBuffer[pixel.x + pixel.y * (int)width] = new uint2(uint.MaxValue, uint.MaxValue);

            float2 uv = new float2(pixel.x, pixel.y) / new float2(width, height) * 2f - 1f;
            float3 rayOrigin = worldData[0].cameraOrigin;
            float3 rayDir = ShaderMath.GetCameraRayDirNew(aspect, uv,
                worldData[0].camScreenDist, worldData[0].camForward, worldData[0].camRight, worldData[0].camUp);

            float3 color = TraceRay(pixel, rayOrigin, rayDir,
                out float3 outputNormal, out float distance, out int steps);

            float3 finalColor = color;
            if (worldData[0].enableAces == 1)
                finalColor = math.saturate(finalColor * (2.51f * finalColor + 0.03f) / (finalColor * (2.43f * finalColor + 0.59f) + 0.14f));

            outputTexture[pixel] = new float4(finalColor, 1f);
            float maxPossibleDistance = worldData[0].farPlane - worldData[0].nearPlane;
            depthNormals[pixel] = new float4(distance / maxPossibleDistance, outputNormal);

            if (debugMode == 1)
            {
                float d = depthNormals[pixel].r;
                outputTexture[pixel] = new float4(d, d, d, 1);
            }
            else if (debugMode == 2)
            {
                outputTexture[pixel] = new float4(depthNormals[pixel].gba, 1);
            }
            else if (debugMode == 3)
            {
                float3 objColor = ShaderMath.IntToColor(entityIdBuffer[pixel.x + pixel.y * (int)width].x);
                outputTexture[pixel] = new float4(objColor, 1);
            }
            else if (debugMode == 4)
            {
                float s0 = math.saturate((float)steps / worldData[0].maxRaySteps);
                float s1 = math.saturate((float)(steps - worldData[0].maxRaySteps) / worldData[0].maxRaySteps);
                float s2 = math.saturate((float)(steps - worldData[0].maxRaySteps * 2) / worldData[0].maxRaySteps);
                outputTexture[pixel] = new float4(s0, s1, s2, 1);
            }
            else if (debugMode == 5)
            {
                uint2 idData = entityIdBuffer[pixel.x + pixel.y * (int)width];
                float3 shadowColor = new float3(1, 0, 0);
                if (idData.x != uint.MaxValue)
                {
                    float3 hitPoint = rayOrigin + rayDir * distance;
                    float3 normal = outputNormal;
                    shadowColor = CalculateShadow(hitPoint, normal, worldData[0].mainLightDir) * new float3(1f);
                }
                outputTexture[pixel] = new float4(shadowColor, 1);
            }
            else if (debugMode == 10)
            {
                int level = LevelAt(rayOrigin + rayDir * distance);
                float3 levelColor = level < 0 ? new float3(0.04f) : DebugLevelColor(level);
                outputTexture[pixel] = new float4(levelColor, 1);
            }
        }

        #endregion main
    }
}
