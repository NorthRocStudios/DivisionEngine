
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
    [Shader]
    public struct SDFShader3D
    {
        // Uniforms: plain fields are packed into the b0 constant buffer.

        /// <summary>Render width in pixels.</summary>
        public float width;

        /// <summary>Render height in pixels.</summary>
        public float height;

        /// <summary>Width / height.</summary>
        public float aspect;

        /// <summary>Frame counter used to decorrelate the reflection samples.</summary>
        public int frameCount;

        /// <summary>Debug visualization mode.</summary>
        public int debugMode;

        /// <summary>1 when the checkerboard half-resolution path is active.</summary>
        public int enableCheckerboard;

        /// <summary>World/scene uniforms.</summary>
        [ShaderResource] public StructuredBuffer<SDFWorldDTO> worldData;

        /// <summary>Colour target. Named outputTexture because 'texture' is reserved in HLSL.</summary>
        [ShaderResource] public RWTexture2D<float4> outputTexture;

        /// <summary>Depth (r) and world normals (gba) target.</summary>
        [ShaderResource] public RWTexture2D<float4> depthNormals;

        /// <summary>Per-pixel object ID (x = index into <see cref="sdfObjects"/>, y = entity id).</summary>
        [ShaderResource] public RWBuffer<uint2> entityIdBuffer;

        /// <summary>All SDF objects in the scene.</summary>
        [ShaderResource] public StructuredBuffer<SDFObjectDTO> sdfObjects;

        /// <summary>All lights in the scene.</summary>
        [ShaderResource] public StructuredBuffer<SDFLightDTO> lights;

        /// <summary>Packed RGBA8 texture data.</summary>
        [ShaderResource] public Buffer<uint> textureData;

        /// <summary>Metadata for each texture.</summary>
        [ShaderResource] public StructuredBuffer<TextureMetadata> textureMetadata;

        /// <summary>All baked terrain heightmaps.</summary>
        [ShaderResource] public Buffer<float> terrainHeightData;

        /// <summary>Metadata for each terrain.</summary>
        [ShaderResource] public StructuredBuffer<TerrainDTO> terrainMetadata;

        // Main constants
        const float EPSILON = 0.0001f;
        const float PI = 3.141592654f;
        const float RECIPROCAL_PI = 1f / PI;
        const float MIN_TRAVERSE_DIST = 100000000.0f;

        // Reflection constants
        const int SAMPLES_PER_PIXEL = 2;
        const float MIN_REFLECTION_CHANCE = 0.01f;
        const float MIN_THROUGHPUT = 0.01f;
        const float REFLECTION_BIAS = 2f; // Multiplier for normal offset

        #region textures

        public float SampleTexture(int textureId, float2 uv, float fallback)
        {
            return SampleTexture(textureId, uv, fallback * new float4(1f)).r;
        }

        public float4 SampleTexture(int textureId, float2 uv, float4 fallbackColor)
        {
            if (textureId < 0 || textureId >= textureMetadata.Length) return fallbackColor;
            float2 newUV = new float2(math.abs(uv.x % 1), math.abs(uv.y % 1));

            TextureMetadata meta = textureMetadata[textureId];
            int x = (int)(newUV.x * (meta.resolution.x - 1));
            int y = (int)(newUV.y * (meta.resolution.y - 1));
            int index = meta.bufferOffset + y * meta.resolution.x + x;
            return ShaderMath.UnpackRGBA(textureData[index]);
        }

        public float SampleTextureBilinear(int textureId, float2 uv, float fallback)
        {
            return SampleTextureBilinear(textureId, uv, fallback * new float4(1f)).r;
        }

        public float4 SampleTextureBilinear(int textureId, float2 uv, float4 fallbackColor)
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

            float u_frac = u - x0;
            float v_frac = v - y0;

            int idx00 = meta.bufferOffset + y0 * meta.resolution.x + x0;
            int idx10 = meta.bufferOffset + y0 * meta.resolution.x + x1;
            int idx01 = meta.bufferOffset + y1 * meta.resolution.x + x0;
            int idx11 = meta.bufferOffset + y1 * meta.resolution.x + x1;

            float4 c00 = ShaderMath.UnpackRGBA(textureData[idx00]);
            float4 c10 = ShaderMath.UnpackRGBA(textureData[idx10]);
            float4 c01 = ShaderMath.UnpackRGBA(textureData[idx01]);
            float4 c11 = ShaderMath.UnpackRGBA(textureData[idx11]);

            float4 c0 = math.lerp(c00, c10, u_frac);
            float4 c1 = math.lerp(c01, c11, u_frac);
            return math.lerp(c0, c1, v_frac);
        }

        /// <summary>
        /// Faster version using precomputed TBN matrix (reduces operations).
        /// </summary>
        public float3 SampleNormalMapFast(int textureId, float2 uv, float3 normal, float3 tangent, float strength)
        {
            float4 normalMap = SampleTextureBilinear(textureId, uv, new float4(0.5f, 0.5f, 1.0f, 1.0f));

            // Unpack normal
            float3 tangentNormal = new float3(
                (normalMap.r * 2.0f - 1.0f) * strength,
                (normalMap.g * 2.0f - 1.0f) * strength,
                normalMap.b * 2.0f - 1.0f
            );
            tangentNormal = math.normalize(tangentNormal);

            // Transform to world space using TBN matrix
            return math.normalize(
                tangent * tangentNormal.x +
                ShaderMath.CalcBitangent(normal, tangent) * tangentNormal.y +
                normal * tangentNormal.z
            );
        }

        private static void GetMipInfo(TextureMetadata meta, int level, out int offset, out int2 res)
        {
            level = math.clamp(level, 0, meta.mipCount - 1);
            offset = meta.bufferOffset;
            res = meta.resolution;
            for (int i = 0; i < level; i++)
            {
                offset += res.x * res.y;
                res = new int2(math.max(1, res.x / 2), math.max(1, res.y / 2));
            }
        }

        private float4 SampleTextureBilinearMip(int textureId, float2 uv, int mipLevel, float4 fallbackColor)
        {
            if (textureId < 0 || textureId >= textureMetadata.Length) return fallbackColor;
            TextureMetadata meta = textureMetadata[textureId];
            GetMipInfo(meta, mipLevel, out int offset, out int2 res);

            float2 newUV = new float2(math.abs(uv.x % 1), math.abs(uv.y % 1));
            float u = newUV.x * (res.x - 1);
            float v = newUV.y * (res.y - 1);

            int x0 = (int)u; int y0 = (int)v;
            int x1 = math.min(x0 + 1, res.x - 1);
            int y1 = math.min(y0 + 1, res.y - 1);
            float u_frac = u - x0; float v_frac = v - y0;

            float4 c00 = ShaderMath.UnpackRGBA(textureData[offset + y0 * res.x + x0]);
            float4 c10 = ShaderMath.UnpackRGBA(textureData[offset + y0 * res.x + x1]);
            float4 c01 = ShaderMath.UnpackRGBA(textureData[offset + y1 * res.x + x0]);
            float4 c11 = ShaderMath.UnpackRGBA(textureData[offset + y1 * res.x + x1]);

            float4 c0 = math.lerp(c00, c10, u_frac);
            float4 c1 = math.lerp(c01, c11, u_frac);
            return math.lerp(c0, c1, v_frac);
        }

        private float4 SampleTextureTrilinear(int textureId, float2 uv, float mipLevel, float4 fallbackColor)
        {
            if (textureId < 0 || textureId >= textureMetadata.Length) return fallbackColor;
            int mipCount = textureMetadata[textureId].mipCount;
            float clampedLevel = math.clamp(mipLevel, 0f, mipCount - 1);
            int level0 = (int)clampedLevel;
            int level1 = math.min(level0 + 1, mipCount - 1);
            float frac = clampedLevel - level0;

            float4 c0 = SampleTextureBilinearMip(textureId, uv, level0, fallbackColor);
            float4 c1 = SampleTextureBilinearMip(textureId, uv, level1, fallbackColor);
            return math.lerp(c0, c1, frac);
        }

        private float EstimateMipLevel(float depth, float scale, int2 textureResolution)
        {
            float texelWorldSize = math.rcp(math.max(scale, EPSILON)) / math.max(textureResolution.x, 1f);
            float pixelWorldSize = math.max(depth * worldData[0].camScreenDist / height, depth * worldData[0].camScreenDist / width);
            return math.max(0f, math.log2(math.max(pixelWorldSize / math.max(texelWorldSize, EPSILON), 1f)));
        }

        #endregion textures
        #region triplanar

        /// <summary>
        /// Blend weights for triplanar projection based on a local-space surface normal.
        /// Higher sharpness = narrower blend seams, more distinct axis-aligned faces.
        /// </summary>
        private static float3 TriplanarWeights(float3 localNormal, float sharpness)
        {
            float3 blend = math.pow(math.abs(localNormal), sharpness);
            return blend / math.max(blend.x + blend.y + blend.z, EPSILON);
        }

        /// <summary>
        /// Triplanar sample of a data texture (albedo, roughness, metallic).
        /// </summary>
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

        /// <summary>
        /// Triplanar normal map sample, swizzled directly into local object space.
        /// No arbitrary tangent basis is needed: each axis projection's tangent space
        /// IS just the two local axes it's projected onto, so the swizzle is exact
        /// rather than approximated.
        /// </summary>
        private float3 SampleNormalTriplanarLocal(int textureId, float3 localPos, float3 localNormal, float3 blend, float scale, float strength)
        {
            if (textureId < 0) return localNormal;

            float4 flat = new float4(0.5f, 0.5f, 1f, 1f);
            float4 mapX = SampleTextureBilinear(textureId, localPos.yz * scale, flat);
            float4 mapY = SampleTextureBilinear(textureId, localPos.xz * scale, flat);
            float4 mapZ = SampleTextureBilinear(textureId, localPos.xy * scale, flat);

            float3 tX = new float3((mapX.r * 2f - 1f) * strength, (mapX.g * 2f - 1f) * strength, mapX.b);
            float3 tY = new float3((mapY.r * 2f - 1f) * strength, (mapY.g * 2f - 1f) * strength, mapY.b);
            float3 tZ = new float3((mapZ.r * 2f - 1f) * strength, (mapZ.g * 2f - 1f) * strength, mapZ.b);

            // Keep the map's "outward" axis pointing the same way as the geometric normal
            float sx = math.sign(localNormal.x);
            float sy = math.sign(localNormal.y);
            float sz = math.sign(localNormal.z);

            // X-projection: U=localY, V=localZ, tangent-Z = localX
            float3 nX = new float3(tX.z * sx, tX.x, tX.y);
            // Y-projection: U=localX, V=localZ, tangent-Z = localY
            float3 nY = new float3(tY.x, tY.z * sy, tY.y);
            // Z-projection: U=localX, V=localY, tangent-Z = localZ
            float3 nZ = new float3(tZ.x, tZ.y, tZ.z * sz);

            return math.normalize(nX * blend.x + nY * blend.y + nZ * blend.z);
        }

        private float4 SampleTriplanarMip(int textureId, float3 localPos, float3 blend, float scale, float mipLevel, float4 fallback)
        {
            float4 colX = SampleTextureTrilinear(textureId, localPos.yz * scale, mipLevel, fallback);
            float4 colY = SampleTextureTrilinear(textureId, localPos.xz * scale, mipLevel, fallback);
            float4 colZ = SampleTextureTrilinear(textureId, localPos.xy * scale, mipLevel, fallback);
            return colX * blend.x + colY * blend.y + colZ * blend.z;
        }

        private float SampleTriplanarScalarMip(int textureId, float3 localPos, float3 blend, float scale, float mipLevel, float fallback)
        {
            return SampleTriplanarMip(textureId, localPos, blend, scale, mipLevel, fallback * new float4(1f)).r;
        }

        /// <summary>
        /// Triplanar normal map sample with mipmapping.
        /// </summary>
        private float3 SampleNormalTriplanarMip(int textureId, float3 localPos, float3 localNormal, float3 blend,
            float scale, float strength, float mipLevel)
        {
            if (textureId < 0) return localNormal;

            float4 flat = new float4(0.5f, 0.5f, 1f, 1f);

            // Use trilinear/mipmapped sampling for each axis
            float4 mapX = SampleTextureTrilinear(textureId, localPos.yz * scale, mipLevel, flat);
            float4 mapY = SampleTextureTrilinear(textureId, localPos.xz * scale, mipLevel, flat);
            float4 mapZ = SampleTextureTrilinear(textureId, localPos.xy * scale, mipLevel, flat);

            float3 tX = new float3((mapX.r * 2f - 1f) * strength, (mapX.g * 2f - 1f) * strength, mapX.b);
            float3 tY = new float3((mapY.r * 2f - 1f) * strength, (mapY.g * 2f - 1f) * strength, mapY.b);
            float3 tZ = new float3((mapZ.r * 2f - 1f) * strength, (mapZ.g * 2f - 1f) * strength, mapZ.b);

            // Keep the map's "outward" axis pointing the same way as the geometric normal
            float sx = math.sign(localNormal.x);
            float sy = math.sign(localNormal.y);
            float sz = math.sign(localNormal.z);

            // X-projection: U=localY, V=localZ, tangent-Z = localX
            float3 nX = new float3(tX.z * sx, tX.x, tX.y);
            // Y-projection: U=localX, V=localZ, tangent-Z = localY
            float3 nY = new float3(tY.x, tY.z * sy, tY.y);
            // Z-projection: U=localX, V=localY, tangent-Z = localZ
            float3 nZ = new float3(tZ.x, tZ.y, tZ.z * sz);

            return math.normalize(nX * blend.x + nY * blend.y + nZ * blend.z);
        }

        #endregion triplanar
        #region sdf_sampling

        private float SampleTerrainHeight(TerrainDTO meta, float2 worldXZ)
        {
            // Calculate UV coordinates in the heightmap
            float2 uv = (worldXZ / meta.size) * 0.5f + 0.5f;
            uv = math.saturate(uv);

            // Get pixel coordinates with bilinear interpolation
            float u = uv.x * (meta.resolution.x - 1);
            float v = uv.y * (meta.resolution.y - 1);
            int x0 = (int)u, y0 = (int)v;
            int x1 = math.min(x0 + 1, meta.resolution.x - 1);
            int y1 = math.min(y0 + 1, meta.resolution.y - 1);
            float fx = u - x0, fy = v - y0;

            // Sample from the height buffer
            int idx00 = meta.bufferOffset + y0 * meta.resolution.x + x0;
            int idx10 = meta.bufferOffset + y0 * meta.resolution.x + x1;
            int idx01 = meta.bufferOffset + y1 * meta.resolution.x + x0;
            int idx11 = meta.bufferOffset + y1 * meta.resolution.x + x1;

            float h00 = terrainHeightData[idx00];
            float h10 = terrainHeightData[idx10];
            float h01 = terrainHeightData[idx01];
            float h11 = terrainHeightData[idx11];

            float h0 = math.lerp(h00, h10, fx);
            float h1 = math.lerp(h01, h11, fx);
            float height = math.lerp(h0, h1, fy);

            return height;
        }

        private float ObjectSDF(float3 pt, SDFObjectDTO obj)
        {
            float3 curPoint = pt - obj.position;
            // obj.rotation is local->world, so the world->local transform is its inverse.
            curPoint = ShaderMath.InverseRotateVector(curPoint, obj.rotation);
            curPoint *= obj.scaling;

            if (obj.type == 9) // Get terrain height at this point
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
            else
            {
                // Ported 1:1 from the ComputeSharp build. This local-space scale is what the
                // shadow march steps with, so the penumbra width depends on it. Correcting it to
                // a true world-space distance (dividing instead of multiplying) shrinks the
                // penetration depth and visibly lightens every shadow, so it is intentionally
                // left as it was.
                float dist = math.min(obj.scaling.x, math.min(obj.scaling.y, obj.scaling.z));
                dist *= EvaluatePrimitiveDistanceFast(obj.type, obj.parameters, curPoint);
                return dist * obj.stepBias;
            }
        }

        /// <summary>
        /// Single dispatch for a primitive's own local-space distance, excluding terrain.
        /// Takes only what it needs (type + one float4), not the full DTO, to keep
        /// per-call codegen light - this gets called multiple times per normal.
        /// </summary>
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

        /// <summary>
        /// Calculates the SDF distance for the world at a point.
        /// </summary>
        /// <param name="point">World position to evaluate</param>
        /// <returns>Float2 representing the min distance, and closest object</returns>
        private float WorldSDF(float3 pt, out int closest)
        {
            float minDist = MIN_TRAVERSE_DIST;
            float cullDist = 1e9f;
            closest = -1;
            for (int i = 0; i < sdfObjects.Length; i++)
            {
                SDFObjectDTO obj = sdfObjects[i];

                // Skipping spheres: if this object's bounding sphere is farther away
                // than the closest surface found so far, its exact SDF cannot change
                // the result, so skip the (potentially very expensive) evaluation.
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

        #endregion sdf_sampling
        #region normals

        // --------------------
        // Normals and Tangents
        // --------------------

        /// <summary>
        /// Very fast high quality normal calculation (4 samples).
        /// </summary>
        /// <param name="pos">Hit position</param>
        /// <returns>World normal vector</returns>
        private float3 FastNormal(float3 pos) // for function f(p)
        {
            float h = EPSILON * 50; // replace by an appropriate value
            // Tetrahedral normal offsets. DivisionMath's float2 has no 3-component
            // swizzles, so the sign patterns are spelled out as float3s.
            float3 k0 = new float3(1f, -1f, -1f);
            float3 k1 = new float3(-1f, -1f, 1f);
            float3 k2 = new float3(-1f, 1f, -1f);
            float3 k3 = new float3(1f, 1f, 1f);
            return math.normalize(k0 * WorldSDF(pos + k0 * h, out _) +
                              k1 * WorldSDF(pos + k1 * h, out _) +
                              k2 * WorldSDF(pos + k2 * h, out _) +
                              k3 * WorldSDF(pos + k3 * h, out _));
        }

        /// <summary>
        /// Computes a surface normal using only the known hit object's own primitive
        /// field, instead of the full multi-object WorldSDF. Cuts normal calculation
        /// from O(4 * totalObjectCount) world samples down to O(4) - since WorldSDF is
        /// a hard-min union (no smooth blending), the gradient at a given hit point is
        /// exactly this object's own gradient there, so this isn't an approximation
        /// except at the measure-zero seam where two objects are exactly tied.
        /// Not used for terrain (type 9) - see ComputeSurfaceNormal below.
        /// </summary>
        private float3 FastNormalSingleObject(float3 pos, SDFObjectDTO sdf)
        {
            float h = EPSILON * 50;
            float3 k0 = new float3(1f, -1f, -1f);
            float3 k1 = new float3(-1f, -1f, 1f);
            float3 k2 = new float3(-1f, 1f, -1f);
            float3 k3 = new float3(1f, 1f, 1f);
            return math.normalize(k0 * ObjectSDF(pos + k0 * h, sdf) +
                              k1 * ObjectSDF(pos + k1 * h, sdf) +
                              k2 * ObjectSDF(pos + k2 * h, sdf) +
                              k3 * ObjectSDF(pos + k3 * h, sdf));
        }

        #endregion normals
        #region lighting

        // --------------------
        // Lighting Calculation
        // --------------------

        // New soft-shadow technique:
        // Reference: https://iquilezles.org/articles/rmshadows/
        // New Version: https://www.shadertoy.com/view/tscSRS
        // New soft-shadow technique:
        // Reference: https://iquilezles.org/articles/rmshadows/
        // New Version: https://www.shadertoy.com/view/tscSRS
        //
        // Ported 1:1 from the original ComputeSharp implementation (Hlsl.* -> math.*,
        // .X/.Y/.Z -> .x/.y/.z). Do not "correct" the smoothstep(-1, 0, ...) mapping: the
        // penumbra shape is defined by it, together with the local-space step sizes produced by
        // ObjectSDF. Only additions are the receiver's own entity being skipped (self-shadow)
        // and the DivisionCompute spelling.
        private float3 SoftShadow(float3 pt, float3 dir, uint excludeID)
        {
            float depth, dist, shadow = 1;
            int maxShadowRaySteps = worldData[0].maxShadowRaySteps;
            for (int k = 0; k < sdfObjects.Length; k++)
            {
                SDFObjectDTO sdf = sdfObjects[k];
                if (sdf.shadowEffects.x == 0) continue;
                if (sdf.entityID == excludeID) continue;

                depth = sdf.shadowDistances.x;
                for (int i = 0; i < maxShadowRaySteps; ++i)
                {
                    dist = ObjectSDF(pt + depth * dir, sdf);
                    if (depth > sdf.shadowDistances.y) break;
                    shadow = math.min(shadow, sdf.shadowDistances.z * dist / depth);
                    depth += math.clamp(dist, 0.025f, 100f); // Larger minimum step than 0.01
                }
            }
            shadow = math.max(shadow, -1f);
            return math.saturate(math.smoothstep(-1f, 0f, shadow)) * new float3(1f);
        }

        private float3 SoftShadowColored(float3 pt, float3 dir, uint excludeID)
        {
            float shadow = 1f;
            float3 tint = new float3(1f);
            float tintShadow = 1f;
            int maxShadowRaySteps = worldData[0].maxShadowRaySteps;
            for (int k = 0; k < sdfObjects.Length; k++)
            {
                SDFObjectDTO sdf = sdfObjects[k];
                if (sdf.shadowEffects.x == 0) continue;
                if (sdf.entityID == excludeID) continue;

                bool isGlass = sdf.hasRefraction == 1;
                float depth = sdf.shadowDistances.x;
                float3 absorptionCoefficient = isGlass ? math.log(math.max(sdf.absorptionColor.rgb, 0.001f)) : new float3(0f);
                for (int i = 0; i < maxShadowRaySteps; ++i)
                {
                    if (depth > sdf.shadowDistances.y) break;
                    float dist = ObjectSDF(pt + depth * dir, sdf);
                    float stepSize = math.clamp(dist, 0.025f, 100f);
                    if (isGlass)
                    {
                        tintShadow = math.min(tintShadow, sdf.shadowDistances.z * dist / depth);
                        float coverage = math.saturate(0.5f - dist * sdf.shadowDistances.z);
                        tint *= math.exp(absorptionCoefficient * stepSize * coverage * sdf.absorptionColor.a * 5f);
                    }
                    else shadow = math.min(shadow, sdf.shadowDistances.z * dist / depth);
                    depth += stepSize;
                }
            }

            shadow = math.max(shadow, -1f);
            float occlusion = math.saturate(math.smoothstep(-1f, 0f, shadow));
            float occlusionTint = math.smoothstep(-1f, 0f, tintShadow);
            return occlusion * math.lerp(tint, new float3(1f), occlusionTint);
        }

        /// <summary>
        /// Hard shadow - returns 1.0 for fully lit, 0.0 for fully shadowed.
        /// Fast and sharp, no penumbra.
        /// </summary>
        private float3 HardShadow(float3 pt, float3 dir, uint excludeID)
        {
            float depth, dist;
            int maxShadowRaySteps = worldData[0].maxShadowRaySteps;
            for (int k = 0; k < sdfObjects.Length; k++)
            {
                SDFObjectDTO sdf = sdfObjects[k];
                depth = sdf.shadowDistances.x;
                if (sdf.shadowEffects.x == 0) continue;
                if (sdf.entityID == excludeID) continue;
                for (int i = 0; i < maxShadowRaySteps; ++i)
                {
                    dist = ObjectSDF(pt + depth * dir, sdf);
                    if (dist < EPSILON) return 0f;
                    if (depth >= sdf.shadowDistances.y) break;
                    depth += dist;
                }
            }
            return 1f; // Max iterations reached, assume no shadow
        }

        /// <summary>
        /// Unified shadow function that selects the appropriate shadow type.
        /// </summary>
        private float3 CalculateShadow(float3 hitPoint, float3 normal, float3 lightDir, int shadowType, uint excludeID)
        {
            float3 shadowOrigin = hitPoint + normal * EPSILON * REFLECTION_BIAS;
            switch (shadowType)
            {
                case 0: // Hard shadows
                    return HardShadow(shadowOrigin, lightDir, excludeID);
                case 1: // Soft shadows
                    return SoftShadow(shadowOrigin, lightDir, excludeID);
                case 2: // Colored shadows
                    return SoftShadowColored(shadowOrigin, lightDir, excludeID);
                case 3: // Caustic shadows (refracted transmission + focus)
                    return CalculateCausticShadow(hitPoint, normal, lightDir, 0, excludeID);
                default:
                    return SoftShadow(shadowOrigin, lightDir, excludeID);
            }
        }

        /// <summary>
        /// Calculate lighting contribution from all lights
        /// </summary>
        private float3 CalculateLighting(float3 hitPoint, float3 geoNormal, float3 finalNormal, float3 viewDir,
            int2 shadowEffects, int shadowType, float specular, float reflectance,
            float3 baseCol, float metallic, float roughAlpha, uint excludeID)
        {
            float3 totalLight = new float3(0f);

            for (int i = 0; i < lights.Length; i++)
            {
                // Reset per light: a light that does not trace a shadow must not inherit the
                // previous light's shadow factor.
                float3 lightShadow = 1f;
                SDFLightDTO light = lights[i];
                if (light.type == 0) // Directional
                {
                    float3 lightDir = math.normalize(ShaderMath.RotateVector(new float3(0, 0, -1), light.rotation));
                    float3 lightColor = light.color.rgb * light.intensity;

                    float NoL = math.max(math.dot(finalNormal, lightDir), 0f);
                    if (NoL <= 0f) continue;

                    // Shadows
                    if (shadowEffects.y != 0) lightShadow = CalculateShadow(hitPoint, geoNormal, lightDir, shadowType, excludeID);
                    float3 brdf = PBR.BRDFMicrofacetFunction(lightDir, viewDir,
                        finalNormal, baseCol, metallic, roughAlpha, specular, reflectance, RECIPROCAL_PI, EPSILON);

                    // Apply shadow to the entire lighting contribution
                    totalLight += brdf * lightColor * NoL * lightShadow;
                }
                else if (light.type == 1) // Point
                {
                    float3 lightVec = light.position - hitPoint;
                    float distance = math.length(lightVec);
                    float3 lightDir = lightVec / distance;
                    float attenuation = math.rcp(distance * distance);
                    float radiusFactor = math.saturate(1f - (distance / light.radius));
                    attenuation *= radiusFactor;

                    float NoL = math.max(math.dot(finalNormal, lightDir), 0f);
                    if (NoL <= 0f || attenuation <= 0f) continue;
                    float3 lightColor = light.color.rgb * light.intensity * attenuation;

                    // Calculate shadow for point light
                    if (shadowEffects.y != 0 && distance < light.radius * 2f)
                        lightShadow = CalculateShadow(hitPoint, geoNormal, lightDir, shadowType, excludeID);

                    // Apply shadow to the entire BRDF result
                    float3 brdf = PBR.BRDFMicrofacetFunction(lightDir, viewDir,
                        finalNormal, baseCol, metallic, roughAlpha, specular, reflectance, RECIPROCAL_PI, EPSILON);
                    totalLight += brdf * lightColor * NoL * lightShadow;
                }
            }

            return totalLight;
        }

        /// <summary>
        /// Calculates ambient occlusion by getting the distance to the second-closest SDF object.
        /// </summary>
        /// <param name="hitPoint">Initial hit point</param>
        /// <param name="normal">Initial hit normal</param>
        /// <returns>Occlusion value at hit point</returns>
        private float CalculateAO(float3 hitPoint, float3 normal, uint excludeID, float3 aoValues)
        {
            float3 samplePoint = hitPoint + normal * EPSILON; // Normal vector offset
            float worldDist = MIN_TRAVERSE_DIST;
            for (int i = 0; i < sdfObjects.Length; i++)
            {
                SDFObjectDTO curSDF = sdfObjects[i];
                if (sdfObjects[i].entityID == excludeID) continue; // Exclude to get second-closest object

                float dist = ObjectSDF(samplePoint, curSDF);
                if (math.abs(dist) < worldDist) worldDist = dist;
            }

            float occlusionRadius = aoValues.y;
            if (worldDist >= occlusionRadius) return 1f; // No object found within radius
            float occlusion = 1f - math.saturate(worldDist / occlusionRadius);
            occlusion = math.pow(occlusion, aoValues.z);
            return 1f - occlusion;
        }

        /// <summary>
        /// Bends a shadow ray through a single refractive object (entry + exit refraction),
        /// returning the transmittance and the ray's new origin/direction on the far side.
        /// This is a single-bounce approximation, not a full recursive trace - it's meant
        /// to be cheap enough to call from inside a shadow loop.
        /// </summary>
        private float3 CausticTransmit(float3 pt, float3 dir, SDFObjectDTO sdf,
            out float3 exitPoint, out float3 exitDir)
        {
            exitPoint = pt;
            exitDir = dir;

            float3 entryNormal = FastNormalSingleObject(pt, sdf);
            if (math.dot(entryNormal, dir) > 0f) entryNormal = -entryNormal; // oppose incident, like TraceRay's convention

            float entryEta = math.rcp(math.max(sdf.ior, EPSILON));
            if (!Refract(dir, entryNormal, entryEta, out float3 innerDir))
                return new float3(0f); // Total internal reflection right at entry - treat as opaque for shadow purposes

            float3 p = pt - entryNormal * EPSILON * 2f;
            float travel = 0f;
            bool foundExit = false;

            for (int i = 0; i < 256; i++)
            {
                float d = ObjectSDF(p, sdf);
                if (d >= 0f) // Stepped back outside -> this is our exit
                {
                    foundExit = true;
                    exitPoint = p;
                    break;
                }
                float stepSize = math.max(math.abs(d), EPSILON * 4f);
                p += innerDir * stepSize;
                travel += stepSize;
            }

            float3 absorptionCoefficient = math.log(math.max(sdf.absorptionColor.rgb, 0.001f));
            float3 transmittance = math.exp(absorptionCoefficient * travel * sdf.absorptionColor.a * 5f);

            if (!foundExit) return new float3(0f); // Ran out of budget inside the object - treat as opaque

            float3 exitNormal = FastNormalSingleObject(exitPoint, sdf);
            if (math.dot(exitNormal, innerDir) > 0f) exitNormal = -exitNormal;

            if (Refract(innerDir, exitNormal, sdf.ior, out float3 bentDir))
                exitDir = bentDir;
            else
                exitDir = math.reflect(innerDir, exitNormal); // TIR at exit, fall back to reflection

            return transmittance;
        }

        /// <summary>
        /// Shadow ray that bends through refractive occluders instead of just stopping at them.
        /// Recursion budget (how many glass objects it'll pass through) shrinks with traceDepth,
        /// so this stays cheap when called from reflection/refraction bounces.
        /// </summary>
        private float3 CalculateCausticShadow(float3 hitPoint, float3 normal, float3 lightDir, int traceDepth, uint excludeID)
        {
            float3 p = hitPoint + normal * EPSILON * REFLECTION_BIAS;
            float3 dir = lightDir;
            float3 attenuation = new float3(1f);

            int maxRefractiveHits = math.max(0, 2 - traceDepth); // depth 0: up to 2 panes of glass, depth 2+: none
            int maxShadowRaySteps = worldData[0].maxShadowRaySteps;
            for (int passIndex = 0; passIndex <= maxRefractiveHits; passIndex++)
            {
                int hitObj = -1;
                float hitDepth = 0f;

                for (int k = 0; k < sdfObjects.Length; k++)
                {
                    SDFObjectDTO sdf = sdfObjects[k];
                    if (sdf.shadowEffects.x == 0) continue;
                    if (sdf.entityID == excludeID) continue;

                    float td = sdf.shadowDistances.x;
                    for (int i = 0; i < maxShadowRaySteps; i++)
                    {
                        if (td > sdf.shadowDistances.y) break;
                        float dist = ObjectSDF(p + td * dir, sdf);
                        if (dist < EPSILON)
                        {
                            hitObj = k;
                            hitDepth = td;
                            break;
                        }
                        td += math.clamp(dist, 0.025f, 100f);
                    }
                    if (hitObj >= 0) break;
                }

                if (hitObj < 0) return attenuation; // Reached the light

                SDFObjectDTO hitSdf = sdfObjects[hitObj];
                if (hitSdf.hasRefraction == 0 || passIndex == maxRefractiveHits)
                    return new float3(0f); // Opaque occluder, or out of refractive budget

                float3 entryPoint = p + dir * hitDepth;
                float3 segAtten = CausticTransmit(entryPoint, dir, hitSdf, out float3 exitPoint, out float3 exitDir);
                attenuation *= segAtten;

                // Caustics: light that the glass bends appreciably is being focused, so boost the
                // transmitted radiance by the amount of bending. Cheap stand-in for a photon
                // caustic map - one multiply per refracted segment.
                float bend = math.saturate(1f - math.dot(dir, exitDir));
                attenuation *= 1f + bend * 2f;

                if (attenuation.x + attenuation.y + attenuation.z < MIN_THROUGHPUT) return new float3(0f);

                p = exitPoint + exitDir * EPSILON * 4f;
                dir = exitDir;
            }

            return attenuation;
        }

        #endregion lighting
        #region pbr_workflow

        private void SampleSurfaceMaterial(
            float3 hitPoint, float3 geoNormal, SDFObjectDTO sdf, float depth,
            out float3 localPos, out float3 blend, out float3 albedo,
            out float metallic, out float roughness, out float3 finalNormal)
        {
            localPos = ShaderMath.InverseRotateVector(hitPoint - sdf.position, sdf.rotation);
            float3 localGeoNormal = math.normalize(ShaderMath.InverseRotateVector(geoNormal, sdf.rotation));
            blend = TriplanarWeights(localGeoNormal, sdf.triplanarBlend);
            float scale = math.rcp(math.max(sdf.texTilingOffset.x, EPSILON));

            float mipLevel = 0f;
            if (sdf.albedoTexMetaID >= 0)
                mipLevel = EstimateMipLevel(depth, scale, textureMetadata[sdf.albedoTexMetaID].resolution);

            albedo = SampleTriplanarMip(sdf.albedoTexMetaID, localPos, blend, scale, mipLevel, sdf.color).rgb;
            metallic = SampleTriplanarScalarMip(sdf.metalTexMetaID, localPos, blend, scale, mipLevel, sdf.metallic);
            roughness = math.max(SampleTriplanarScalarMip(sdf.roughTexMetaID, localPos, blend, scale, mipLevel, sdf.roughness), 0.045f);

            float3 localFinalNormal = SampleNormalTriplanarMip(sdf.normalTexMetaID, localPos, localGeoNormal, blend, scale, sdf.normalStrength, mipLevel);
            finalNormal = math.normalize(ShaderMath.RotateVector(localFinalNormal, sdf.rotation));
        }

        #endregion pbr_workflow
        #region sky

        /// <summary>
        /// Gets the sky color based on the configured sky mode.
        /// </summary>
        private float3 GetSkyColor(float3 viewDir)
        {
            SDFWorldDTO world = worldData[0];
            if (world.skyType == 3)
                return AtmosphereMath.ProceduralSky(viewDir, world.mainLightDir, world.sunColor.rgb,
                    world.planetRadius, world.atmosphereHeight, world.atmosphereDensity, world.mieAnisotropy,
                    1e9f, world.sunDiscSize, world.sunFalloff, world.sunIntensity, world.atmosphereExposure,
                    world.cloudCoverage, world.cloudDensity, world.cloudBaseHeight, world.cloudThickness,
                    world.cloudScale, world.cloudSpeed, world.time);

            float3 sky;
            if (world.skyType == 0) sky = AddAtmosphericScattering(viewDir, world.skyColor.rgb * world.skyIntensity, world.mainLightDir);
            else if (world.skyType == 1) sky = AddAtmosphericScattering(viewDir, GetGradientSkyColor(viewDir, world), world.mainLightDir);
            else if (world.skyType == 2) sky = GetHDRISkyColor(viewDir, world);
            else sky = world.skyColor.rgb * world.skyIntensity;

            return AddSun(sky, viewDir, world);
        }

        /// <summary>
        /// Adds a limb-darkened sun disc plus a soft halo on top of any sky type.
        /// </summary>
        private static float3 AddSun(float3 sky, float3 viewDir, SDFWorldDTO world)
        {
            float3 sunDir = math.normalize(world.mainLightDir + new float3(1e-5f, 1e-5f, 1e-5f));
            float3 sunColor = world.sunColor.rgb * world.sunIntensity;

            float disc = AtmosphereMath.LimbDarkenedSun(viewDir, sunDir, world.sunDiscSize, world.sunFalloff);
            float sunAmount = math.saturate(math.dot(viewDir, sunDir));

            // Disc plus a broad glow so the sun reads on bright HDRI skies too
            sky += sunColor * disc * 40f;
            sky += sunColor * math.pow(sunAmount, 16f) * 0.08f;
            return sky;
        }

        /// <summary>
        /// Computes gradient sky color based on view direction.
        /// </summary>
        private static float3 GetGradientSkyColor(float3 viewDir, SDFWorldDTO world)
        {
            float3 dir = math.normalize(viewDir);
            float y = dir.y;

            // Map to 0-1 range
            float t = y * 0.5f + 0.5f;
            t = math.saturate(t);

            // Sample the gradient
            float3 bottom = world.bottomSkyColor.rgb;
            float3 middle = world.middleSkyColor.rgb;
            float3 top = world.topSkyColor.rgb;

            float3 color;
            if (t < 0.5f)
            {
                // Bottom to middle
                float u = t / 0.5f;
                color = math.lerp(bottom, middle, u);
            }
            else
            {
                // Middle to top
                float u = (t - 0.5f) / 0.5f;
                color = math.lerp(middle, top, u);
            }

            // Optional: Add horizon glow
            float horizonGlow = 1f - math.pow(math.abs(y), 4f);
            float3 horizonColor = new float3(1f, 0.8f, 0.5f) * 0.3f;
            color += horizonColor * horizonGlow * 0.2f;

            // Apply intensity
            return color * world.skyIntensity;
        }

        /// <summary>
        /// Gets HDRI sky color (placeholder for now).
        /// </summary>
        private float3 GetHDRISkyColor(float3 viewDir, SDFWorldDTO world)
        {
            int texId = world.hdriTexMetaID;
            if (texId < 0 || texId >= textureMetadata.Length)
                return world.skyColor.rgb * world.skyIntensity; // fallback

            int layout = textureMetadata[texId].cubemapLayout;
            float3 color = SampleCubemap(texId, viewDir, layout);
            return color * world.skyIntensity;
        }

        private float3 SampleCubemap(int texId, float3 direction, int layout)
        {
            if (layout == 1) // For Equirectangular (panorama)
            {
                float theta = math.atan2(direction.z, direction.x); // -PI to PI
                float phi = math.acos(direction.y); // 0 to PI
                float u = (theta + PI) / (2f * PI);
                float v = phi / PI;
                return SampleTextureBilinear(texId, new float2(u, v), new float4(0f)).rgb;
            }
            else if (layout == 2) // Horizontal cross (4 wide x 3 tall grid)
            {
                float3 absDir = math.abs(direction);
                float maxComp = math.max(absDir.x, math.max(absDir.y, absDir.z));

                int faceCol, faceRow;
                float2 faceUV;

                if (maxComp == absDir.x && direction.x > 0) { faceUV = new float2(-direction.z, -direction.y) / absDir.x; faceCol = 2; faceRow = 1; } // +X
                else if (maxComp == absDir.x) { faceUV = new float2(direction.z, -direction.y) / absDir.x; faceCol = 0; faceRow = 1; } // -X
                else if (maxComp == absDir.y && direction.y > 0) { faceUV = new float2(direction.x, direction.z) / absDir.y; faceCol = 1; faceRow = 0; } // +Y
                else if (maxComp == absDir.y) { faceUV = new float2(direction.x, -direction.z) / absDir.y; faceCol = 1; faceRow = 2; } // -Y
                else if (direction.z > 0) { faceUV = new float2(direction.x, -direction.y) / absDir.z; faceCol = 1; faceRow = 1; } // +Z
                else { faceUV = new float2(-direction.x, -direction.y) / absDir.z; faceCol = 3; faceRow = 1; } // -Z

                faceUV = faceUV * 0.5f + 0.5f;
                float2 globalUV = new float2((faceCol + faceUV.x) / 4f, (faceRow + faceUV.y) / 3f);
                return SampleTextureBilinear(texId, globalUV, new float4(0f)).rgb;
            }
            else if (layout == 3) // Vertical cross (3 wide x 4 tall grid)
            {
                float3 absDir = math.abs(direction);
                float maxComp = math.max(absDir.x, math.max(absDir.y, absDir.z));

                int faceCol, faceRow;
                float2 faceUV;

                if (maxComp == absDir.x && direction.x > 0) { faceUV = new float2(-direction.z, -direction.y) / absDir.x; faceCol = 2; faceRow = 1; } // +X
                else if (maxComp == absDir.x) { faceUV = new float2(direction.z, -direction.y) / absDir.x; faceCol = 0; faceRow = 1; } // -X
                else if (maxComp == absDir.y && direction.y > 0) { faceUV = new float2(direction.x, direction.z) / absDir.y; faceCol = 1; faceRow = 0; } // +Y
                else if (maxComp == absDir.y) { faceUV = new float2(direction.x, -direction.z) / absDir.y; faceCol = 1; faceRow = 2; } // -Y
                else if (direction.z > 0) { faceUV = new float2(direction.x, -direction.y) / absDir.z; faceCol = 1; faceRow = 1; } // +Z
                else { faceUV = new float2(-direction.x, direction.y) / absDir.z; faceCol = 1; faceRow = 3; } // -Z, bottom cell

                faceUV = faceUV * 0.5f + 0.5f;
                float2 globalUV = new float2((faceCol + faceUV.x) / 3f, (faceRow + faceUV.y) / 4f);
                return SampleTextureBilinear(texId, globalUV, new float4(0f)).rgb;
            }
            else return SampleTextureBilinear(texId, new float2(0.5f, 0.5f), new float4(0f)).rgb; // Fallback
        }

        /// <summary>
        /// Adds atmospheric scattering to the sky (optional enhancement).
        /// </summary>
        private static float3 AddAtmosphericScattering(float3 viewDir, float3 skyColor, float3 sunDir)
        {
            // Simple Rayleigh scattering approximation
            float sunDot = math.max(math.dot(viewDir, sunDir), 0.0f);
            float scattering = math.pow(sunDot, 3.0f) * 0.5f;

            // Add warm sun color to scattering
            float3 sunColor = new float3(1.0f, 0.7f, 0.3f);
            return skyColor + sunColor * scattering * 0.3f;
        }

        #endregion sky
        #region raymarching

        // -----------
        // Raymarching
        // -----------

        //private float3 Raymarch(float3 rayOrigin, float3 rayDir, int maxSteps, float farClipPlane, out int closestObj, out float depth, out int steps)
        //{
        //    depth = worldData.nearPlane;
        //    closestObj = -1;
        //    float3 hitPoint = rayOrigin;

        //    for (steps = 0; steps < maxSteps && depth < farClipPlane; steps++)
        //    {
        //        hitPoint = rayOrigin + rayDir * depth;
        //        float worldDist = WorldSDF(hitPoint, false, uint.MaxValue, out closestObj);

        //        if (worldDist < AdaptiveEpsilon(depth)) break;
        //        depth += worldDist;
        //    }
        //    return hitPoint;
        //}

        private float3 Raymarch(float3 rayOrigin, float3 rayDir, int maxSteps,
            float farClipPlane, out int closestObj, out float depth, out int steps)
        {
            depth = worldData[0].nearPlane;
            closestObj = -1;
            float3 hitPoint = rayOrigin;
            float lastSafeDepth = depth;
            float camScreenDist = worldData[0].camScreenDist;

            for (steps = 0; steps < maxSteps && depth < farClipPlane; steps++)
            {
                hitPoint = rayOrigin + rayDir * depth;
                float worldDist = WorldSDF(hitPoint, out closestObj);

                if (worldDist > 0f) lastSafeDepth = depth;
                else if (worldDist < 0f)
                {
                    // Binary search, use fixed small epsilon, not adaptive
                    float tMin = lastSafeDepth;
                    float tMax = depth;
                    const float BISECT_EPS = EPSILON * 10f;

                    int maxRefine = (depth < 100f) ? 8 : 4;
                    for (int refine = 0; refine < maxRefine; refine++)
                    {
                        float tMid = (tMin + tMax) * 0.5f;
                        float3 refinePoint = rayOrigin + rayDir * tMid;
                        float refineDist = WorldSDF(refinePoint, out int refinedObj);

                        if (math.abs(refineDist) < BISECT_EPS)
                        {
                            depth = tMid;
                            closestObj = refinedObj;
                            return rayOrigin + rayDir * (tMid - BISECT_EPS); // Step back slightly to guarantee we're outside
                        }
                        if (refineDist > 0f)
                        {
                            tMin = tMid;
                            closestObj = refinedObj;
                        }
                        else tMax = tMid;
                    }

                    depth = tMin; // Always return the OUTSIDE point
                    return rayOrigin + rayDir * tMin;
                }

                // Use fixed epsilon near surface, adaptive only for early termination
                float hitEps = (depth < 10f) ? EPSILON : ShaderMath.AdaptiveEpsilon(depth, width, height, camScreenDist, EPSILON);
                if (worldDist < hitEps) break;

                depth += worldDist;
            }
            return hitPoint;
        }

        private static bool Refract(float3 incident, float3 normal, float eta, out float3 refracted)
        {
            float NdotI = math.dot(normal, incident);
            float k = 1.0f - eta * eta * (1.0f - NdotI * NdotI);
            if (k < 0f)
            {
                refracted = new float3(0f);
                return false; // Total internal reflection
            }
            refracted = eta * incident - (eta * NdotI + math.sqrt(k)) * normal;
            refracted = math.normalize(refracted);
            return true;
        }

        private float3 TraceRefractionRay(float3 startDir, float3 startOrigin, float3 ambientBase, float3 backgroundCol,
            float3 normal, SDFObjectDTO startMat, out int steps)
        {
            float3 totalTransmittance = new float3(1f);
            float3 accumulatedColor = new float3(0f);

            float3 currentOrigin = startOrigin;
            float3 currentDir = startDir;
            float3 currentNormal = normal;
            SDFObjectDTO currentObj = startMat;
            bool currentlyInsideObject = true;
            steps = 0;
            float camScreenDist = worldData[0].camScreenDist;
            float farPlane = worldData[0].farPlane;
            int maxRaySteps = worldData[0].maxRaySteps;

            for (int transmit = 0; transmit < startMat.refractMaxRecursion; transmit++)
            {
                float currentEta = currentlyInsideObject ? 1.0f / currentObj.ior : currentObj.ior;

                if (Refract(currentDir, currentNormal, currentEta, out float3 refractDir))
                {
                    float3 p = currentOrigin - currentNormal * EPSILON * 2f;
                    float travelDistance = 0f;
                    bool foundExit = false;
                    float3 exitPt = new float3(0f);
                    float3 exitNorm = new float3(0f);
                    float3 absorptionCoefficient = math.log(math.max(currentObj.absorptionColor.rgb, 0.001f));

                    for (int i = 0; i < currentObj.refractionMaxSteps; i++)
                    {
                        // Use ONLY the current refractive object's SDF
                        float d = ObjectSDF(p, currentObj);
                        bool nowInside = d < 0f;

                        if (currentlyInsideObject != nowInside)
                        {
                            // Binary search for the exact surface of currentObj
                            float3 tMin = p - refractDir * math.max(math.abs(d),
                                ShaderMath.AdaptiveEpsilon(travelDistance, width, height, camScreenDist, EPSILON));
                            float3 tMax = p;
                            const float REFRACT_BISECT_EPS = EPSILON * 20f;

                            for (int b = 0; b < 12; b++)
                            {
                                float3 mid = (tMin + tMax) * 0.5f;
                                float dMid = ObjectSDF(mid, currentObj);   // use currentObj only

                                if (math.abs(dMid) < REFRACT_BISECT_EPS)
                                {
                                    tMin = mid;
                                    break;
                                }
                                if ((dMid < 0f) == currentlyInsideObject) tMin = mid;
                                else tMax = mid;
                            }

                            exitPt = tMin;
                            // Compute normal from currentObj only
                            exitNorm = FastNormalSingleObject(exitPt, currentObj);
                            if (math.dot(exitNorm, refractDir) > 0f) exitNorm = -exitNorm;
                            foundExit = true;

                            float3 segmentTransmittance = math.exp(
                                absorptionCoefficient * travelDistance * currentObj.absorptionColor.a * 5f);
                            totalTransmittance *= segmentTransmittance;
                            break;
                        }

                        float stepSize = math.max(math.abs(d),
                            ShaderMath.AdaptiveEpsilon(travelDistance, width, height, camScreenDist, EPSILON));
                        p += refractDir * stepSize;
                        travelDistance += stepSize;
                    }

                    if (!foundExit)
                    {
                        // Still inside? Apply final absorption and return background.
                        float3 segmentTransmittance = math.exp(absorptionCoefficient * travelDistance * currentObj.absorptionColor.a * 5f);
                        totalTransmittance *= segmentTransmittance;
                        float3 bgColor = TraceRefractionExitRay(p, refractDir, ambientBase, backgroundCol, out _, out _, out int tracedSteps);
                        accumulatedColor = bgColor;
                        steps += tracedSteps;
                        break;
                    }

                    // Found exit, now outside currentObj.
                    currentDir = refractDir;
                    currentNormal = exitNorm;

                    // Now raymarch from the exit point to find the next surface (if any)
                    float3 rayStart = exitPt + currentNormal * EPSILON;
                    float3 hitPoint = Raymarch(rayStart, currentDir, maxRaySteps, farPlane,
                        out int nextObjIndex, out _, out int tracedSteps2);
                    steps += tracedSteps2;

                    if (nextObjIndex >= 0 && nextObjIndex < sdfObjects.Length)
                    {
                        currentOrigin = hitPoint;
                        currentNormal = FastNormalSingleObject(hitPoint, sdfObjects[nextObjIndex]);
                        if (math.dot(currentNormal, currentDir) > 0) currentNormal = -currentNormal;
                        currentObj = sdfObjects[nextObjIndex];
                        currentlyInsideObject = true;
                    }
                    else
                    {
                        // Nothing hit, accumulate background
                        float3 bgColor = TraceRefractionExitRay(rayStart, currentDir, ambientBase, backgroundCol, out _, out _, out int tracedSteps3);
                        accumulatedColor = bgColor;
                        steps += tracedSteps3;
                        break;
                    }
                }
                else break; // TIR
            }

            return accumulatedColor * totalTransmittance;
        }

        private float3 TraceRefractionExitRay(float3 rayOrigin, float3 rayDir, float3 ambientBase, float3 backgroundCol,
            out float3 normal, out float totalDist, out int steps)
        {
            float3 finalColor = new float3(0f);
            normal = new float3(0f);

            // Trace
            float farPlane = worldData[0].farPlane;
            float3 hitPoint = Raymarch(rayOrigin, rayDir, worldData[0].maxRaySteps, farPlane, out int closestObjIndex, out totalDist, out steps);

            if (closestObjIndex == -1 || totalDist > farPlane)
            {
                finalColor += backgroundCol;
                return finalColor;
            }
            
            SDFObjectDTO sdf = sdfObjects[closestObjIndex];
            normal = FastNormalSingleObject(hitPoint, sdf);
            float3 viewDir = -rayDir;

            // Sample the surface material for the sdf that was hit
            SampleSurfaceMaterial(hitPoint, normal, sdf, totalDist,
                out _, out _, out float3 albedo, out float metallic, out float roughness, out float3 finalNormal);
            float roughAlpha = roughness * roughness;

            // Calculate ambient occlusion for the hit point
            float aoValue = 1f;
            if (sdf.aoValues.x > 0f) aoValue = math.lerp(1f, CalculateAO(hitPoint, normal, sdf.entityID, sdf.aoValues), sdf.aoValues.x);

            float3 directLight = CalculateLighting(hitPoint, normal, finalNormal, viewDir,
                new int2(sdf.shadowEffects.x, 0), sdf.shadowType, sdf.specular, sdf.reflectance, albedo, metallic, roughAlpha, sdf.entityID);
            float3 ambientLight = ambientBase * aoValue;
            finalColor += ambientLight + directLight;
            return finalColor;
        }

        /// <summary>
        /// Actually performs the main raymarching calculations.
        /// </summary>
        /// <param name="rayOrigin">Ray origin to start at</param>
        /// <param name="rayDir">Ray direction to travel</param>
        /// <param name="outputNormal">Outputs surface normal</param>
        /// <param name="totalDist">Total distance traversed</param> 
        /// <returns>Output raymarch color, with effects</returns>
        private float3 TraceRay(int2 pixel, float3 rayOrigin, float3 rayDir, int sampleIndexInPixel,
            out float3 outputNormal, out float totalDist, out int steps)
        {
            float3 finalColor = new float3(0f);
            float3 contribution = new float3(1f);
            float3 ambientBase = new float3(0f);
            float cosTheta = 0f;

            float3 refractedLight = new float3(0f);
            SDFObjectDTO mainMat = default;
            outputNormal = new float3(0f);
            totalDist = 0f;
            float farPlane = worldData[0].farPlane;
            bool firstHit = true;
            steps = 0;

            // Refraction coloring
            float3 surfaceColor = new float3(0f);
            float fresnelFactor = 0f;
            bool isRefractive = false;
            float aoValue = 1f;

            // Adaptive reflection step sizes
            int maxRaySteps = worldData[0].maxRaySteps;
            for (int bounce = 0; bounce < 32; bounce++) // Max 32 bounces for reflections
            {
                float3 hitPoint = Raymarch(rayOrigin, rayDir, maxRaySteps, farPlane,
                    out int closestObjIndex, out float depth, out int tracedSteps);
                steps += tracedSteps;

                // Calc sky color
                float3 sky = GetSkyColor(rayDir);

                if (closestObjIndex == -1 || depth > farPlane)
                {
                    finalColor += contribution * sky;
                    if (firstHit) totalDist = depth;
                    break;
                }

                SDFObjectDTO sdf = sdfObjects[closestObjIndex];
                float3 normal = FastNormalSingleObject(hitPoint, sdf);
                if (math.dot(normal, rayDir) > 0f) normal = -normal;
                float3 viewDir = -rayDir;

                SampleSurfaceMaterial(hitPoint, normal, sdf, depth,
                    out _, out _, out float3 albedo, out float metallic, out float roughness, out float3 finalNormal);
                float roughAlpha = roughness * roughness;

                float aoStrength = sdf.aoValues.x;
                cosTheta = math.max(math.dot(normal, viewDir), 0f);

                if (firstHit)
                {
                    outputNormal = normal;
                    totalDist = depth;
                    entityIdBuffer[pixel.x + pixel.y * (int)width] = new uint2((uint)closestObjIndex, sdf.entityID);
                    mainMat = sdf;

                    // AO
                    ambientBase = math.lerp(albedo, worldData[0].skyColor.rgb, worldData[0].ambientStrength) * worldData[0].ambientStrength;
                    if (aoStrength > 0f) aoValue = math.lerp(1f, CalculateAO(hitPoint, normal, sdf.entityID, sdf.aoValues), aoStrength);

                    // Refraction
                    if (sdf.hasRefraction == 1)
                    {
                        isRefractive = true;
                        fresnelFactor = PBR.FresnelWithReflectance(cosTheta, mainMat.f0_dielectric, mainMat.reflectance, 1f);
                        refractedLight = TraceRefractionRay(rayDir, hitPoint, ambientBase, sky, normal, sdf, out tracedSteps);
                        steps += tracedSteps;
                    }
                    firstHit = false;
                }

                float3 directLight = CalculateLighting(hitPoint, normal, finalNormal, viewDir,
                    sdf.shadowEffects, sdf.shadowType, sdf.specular, sdf.reflectance, albedo, metallic, roughAlpha, sdf.entityID);
                // Ambient is scaled by AO only. The previous line overwrote aoValue with the
                // *direct* light magnitude whenever it exceeded 0.5, which made ambient jump back
                // up exactly where the shadow term pulled directLight down: a bright band hugging
                // the penumbra ("lighter area outside the shadow"). It also let ambient exceed 1.
                float3 ambientLight = ambientBase * aoValue;

                if (bounce == 0) surfaceColor = directLight;
                finalColor += contribution * (ambientLight + directLight);

                if (sdf.hasReflection == 0) break;
                if (bounce == sdf.reflectionMaxBounces - 1) break;

                maxRaySteps = (int)(maxRaySteps / sdf.reflectRayStepFalloff);

                // Metallic/roughness maps now actually drive this:
                float3 f0Refl = math.lerp(sdf.f0_reflectance, albedo, new float3(metallic, metallic, metallic));
                float3 F = PBR.FresnelSchlick(math.max(math.dot(finalNormal, viewDir), 0f), f0Refl, sdf.reflectance, 1f);
                float reflectionChance = math.lerp(F.x, 1f, metallic);

                if (bounce == 0 && isRefractive) reflectionChance = fresnelFactor;
                if (reflectionChance < MIN_REFLECTION_CHANCE) break;

                contribution *= F * (1f - roughness * 0.5f);
                contribution = math.min(contribution, 10f);
                if (contribution.x + contribution.y + contribution.z < MIN_THROUGHPUT) break;

                int reflectionSampleIndex = pixel.x * 73 + pixel.y * 9277 + frameCount * 1973 + sampleIndexInPixel * 3271 + bounce * 997;
                float2 u = PBR.Halton2D(reflectionSampleIndex);

                // Perturb with the bumped normal, not the geometric one -> normal maps now visible in reflections
                float3 halfVector = PBR.ImportanceSampleGGX(u, finalNormal, roughAlpha, PI);
                float3 reflectDir = math.normalize(math.reflect(rayDir, halfVector));
                if (math.dot(reflectDir, normal) < 0.01f) break; // test vs geometric normal, avoids self-intersection

                rayOrigin = hitPoint + normal * EPSILON * REFLECTION_BIAS; // offset along geometric normal, stays safe
                rayDir = reflectDir;
            }

            float3 outputColor;
            if (isRefractive)
            {
                // Proper Fresnel using Schlick
                float3 f0 = new float3(1f) * mainMat.f0_dielectric;
                float3 fresnel = PBR.FresnelSchlick(cosTheta, f0, mainMat.reflectance, 1f).x;
                outputColor = finalColor * fresnel + refractedLight * (1f - fresnel);
            }
            else outputColor = finalColor + surfaceColor;

            return outputColor;
        }

        #endregion raymarching
        #region debug

        private float3 DebugBRDF(int2 pixel, float3 rayDir, float3 hitPoint, float depth)
        {
            float3 outputVec = new float3(0f);
            uint2 idData = entityIdBuffer[pixel.x + pixel.y * (int)width];
            if (idData.x != uint.MaxValue)
            {
                SDFObjectDTO sdf = sdfObjects[(int)idData.x];
                float3 normal = depthNormals[pixel].gba;
                float3 viewDir = -rayDir;

                if (math.length(worldData[0].mainLightDir) > 0f)
                {
                    SampleSurfaceMaterial(hitPoint, normal, sdf, depth,
                        out _, out _, out float3 albedo, out float metallic, out float roughness, out float3 finalNormal);
                    float roughAlpha = roughness * roughness;

                    float NoL = math.max(math.dot(finalNormal, worldData[0].mainLightDir), 0f);
                    if (NoL > 0f)
                    {
                        if (debugMode == 6)
                            outputVec = PBR.BRDFMicrofacetFunction(worldData[0].mainLightDir, viewDir,
                                finalNormal, albedo, metallic, roughAlpha, sdf.specular, sdf.reflectance, RECIPROCAL_PI, EPSILON);
                        else if (debugMode == 7)
                        {
                            float3 halfwayDir = math.normalize(viewDir + worldData[0].mainLightDir);
                            float NoV = math.saturate(math.dot(finalNormal, viewDir));
                            float NoH = math.saturate(math.dot(finalNormal, halfwayDir));
                            float VoH = math.saturate(math.dot(viewDir, halfwayDir));
                            float3 f0 = math.lerp(new float3(1f) * 0.16f * sdf.specular * sdf.specular, albedo, new float3(metallic, metallic, metallic));
                            float3 F = PBR.FresnelSchlick(VoH, f0, sdf.reflectance, 1f);
                            float D = PBR.D_GGX(NoH, roughAlpha, RECIPROCAL_PI);
                            float G = PBR.GSmith(NoV, NoL, roughAlpha, EPSILON);
                            outputVec = F * D * G / (4f * math.max(NoV * NoL, EPSILON));
                        }
                        else if (debugMode == 8)
                        {
                            float NoV = math.saturate(math.dot(finalNormal, viewDir));
                            float VoH = math.saturate(math.dot(viewDir, math.normalize(viewDir + worldData[0].mainLightDir)));
                            outputVec = albedo * PBR.DisneyDiffuseFactor(NoV, NoL, VoH, roughAlpha) * RECIPROCAL_PI;
                        }
                    }
                }
            }
            return outputVec;
            //float3 H = math.normalize(V + L);
            //float NdotV = math.max(math.dot(N, V), 0.0f);
            //float NdotL = math.max(math.dot(N, L), 0.0f);
            //float NdotH = math.max(math.dot(N, H), 0.0f);

            //float D = D_GGX(NdotH, roughness);
            //float G = G1_GGX_Schlick(NdotV, roughness) * G1_GGX_Schlick(NdotL, roughness);

            //float3 f0 = new float3(1f) * 0.16f * reflectance * reflectance;
            //float3 F = FresnelSchlick(math.max(math.dot(V, H), 0.0f), f0);

            //// Return RGB with R = D, G = G, B = average(F)
            //return new float3(D, G, (F.X + F.Y + F.Z) / 3.0f);
        }

        /// <summary>
        /// Maps a continuous mip level to a distinct discrete color band for debug visualization.
        /// White = mip 0 (full res), progressing through the spectrum as mip level rises,
        /// dark gray for anything past what's realistically expected.
        /// </summary>
        private static float3 MipLevelColor(float mipLevel)
        {
            int level = (int)math.floor(mipLevel + 0.5f); // round to nearest whole mip for clean bands
            if (level <= 0) return new float3(1.0f, 1.0f, 1.0f);       // mip 0: white
            else if (level == 1) return new float3(0.0f, 1.0f, 0.0f);  // mip 1: green
            else if (level == 2) return new float3(0.0f, 1.0f, 1.0f);  // mip 2: cyan
            else if (level == 3) return new float3(0.0f, 0.0f, 1.0f);  // mip 3: blue
            else if (level == 4) return new float3(1.0f, 0.0f, 1.0f);  // mip 4: magenta
            else if (level == 5) return new float3(1.0f, 0.0f, 0.0f);  // mip 5: red
            else if (level == 6) return new float3(1.0f, 0.5f, 0.0f);  // mip 6: orange
            else if (level == 7) return new float3(1.0f, 1.0f, 0.0f);  // mip 7: yellow
            else return new float3(0.3f, 0.3f, 0.3f);                  // mip 8+: dark gray
        }

        #endregion debug
        #region main

        /// <summary>
        /// Executes the raymarching sequence.
        /// </summary>
        /// <summary>Raymarches one pixel per thread.</summary>
        [Kernel(8, 8, 1)]
        public void Execute(uint3 id)
        {
            int2 localPixel = new int2((int)id.x, (int)id.y);
            int2 pixel;
            if (enableCheckerboard == 1) // Checkerboard pattern
            {
                int framePass = frameCount & 1;
                pixel = new int2(
                    localPixel.x * 2 + (framePass ^ (localPixel.y & 1)),
                    localPixel.y
                );
            }
            else pixel = localPixel; // Full resolution rendering
            if (pixel.x >= width || pixel.y >= height) return;

            outputTexture[pixel] = new float4(0, 0, 0, 0);
            depthNormals[pixel] = new float4(0, 0, 0, 0);
            entityIdBuffer[pixel.x + pixel.y * (int)width] = new uint2(uint.MaxValue, uint.MaxValue);

            float2 uv = new float2(pixel.x, pixel.y) / new float2(width, height) * 2f - 1f; // UV math
            float3 rayOrigin = worldData[0].cameraOrigin;

            float3 accumulatedColor = new float3(0f);
            float3 accumulatedNormal = new float3(0f);
            float3 rayDir = new float3(0f);
            float accumulatedDistance = 0f;
            int steps = 0;

            for (int sample = 0; sample < SAMPLES_PER_PIXEL; sample++)
            {
                rayDir = ShaderMath.GetCameraRayDirNew(aspect, uv,
                    worldData[0].camScreenDist, worldData[0].camForward, worldData[0].camRight, worldData[0].camUp);

                // Automatically skip reflection bounces for non-reflective materials
                float3 color = TraceRay(pixel, rayOrigin, rayDir, sample,
                    out float3 outputNormal, out float dist, out int tracedSteps);
                steps += tracedSteps;
                accumulatedColor += color;
                accumulatedNormal += outputNormal;
                accumulatedDistance += dist;
            }

            float maxPossibleDistance = worldData[0].farPlane - worldData[0].nearPlane;
            float3 finalColor = accumulatedColor / SAMPLES_PER_PIXEL;
            float3 finalNormal = accumulatedNormal / SAMPLES_PER_PIXEL;
            float finalDist = accumulatedDistance / SAMPLES_PER_PIXEL;

            // ACES tonemapping
            if (worldData[0].enableAces == 1)
                finalColor = math.saturate(finalColor * (2.51f * finalColor + 0.03f) / (finalColor * (2.43f * finalColor + 0.59f) + 0.14f));

            // Final
            outputTexture[pixel] = new float4(finalColor, 1f);
            depthNormals[pixel] = new float4(finalDist / maxPossibleDistance, finalNormal);

            // Debugging:
            if (debugMode > 0)
            {
                if (debugMode == 1) // Depth buffer
                {
                    float depth = depthNormals[pixel].r;
                    outputTexture[pixel] = new float4(depth, depth, depth, 1);
                }
                else if (debugMode == 2) // World normal buffer
                    outputTexture[pixel] = new float4(depthNormals[pixel].gba, 1);
                else if (debugMode == 3) // Object ID buffer
                {
                    float3 objColor = ShaderMath.IntToColor(entityIdBuffer[pixel.x + pixel.y * (int)width].x);
                    outputTexture[pixel] = new float4(objColor, 1);
                }
                else if (debugMode == 4) // Ray steps
                {
                    float stepValue1 = math.saturate((float)steps / worldData[0].maxRaySteps);
                    float stepValue2 = math.saturate((float)(steps - worldData[0].maxRaySteps) / worldData[0].maxRaySteps);
                    float stepValue3 = math.saturate((float)(steps - worldData[0].maxRaySteps * 2) / worldData[0].maxRaySteps);
                    outputTexture[pixel] = new float4(stepValue1, stepValue2, stepValue3, 1);
                }
                else if (debugMode == 5) // Shadows
                {
                    float3 shadowColor = new float3(1, 0, 0);
                    uint2 idData = entityIdBuffer[pixel.x + pixel.y * (int)width];
                    if (idData.x != uint.MaxValue)
                    {
                        float3 hitPoint = rayOrigin + rayDir * depthNormals[pixel].r * (worldData[0].farPlane - worldData[0].nearPlane);
                        float3 normal = depthNormals[pixel].gba;
                        if (math.length(worldData[0].mainLightDir) > 0f)
                        {
                            float3 shadowOrigin = hitPoint + normal * EPSILON * REFLECTION_BIAS;
                            shadowColor = SoftShadow(shadowOrigin, worldData[0].mainLightDir, uint.MaxValue);
                        }
                    }
                    outputTexture[pixel] = new float4(shadowColor, 1);
                }
                else if (debugMode == 6 || debugMode == 7 || debugMode == 8) // BRDF, Specular, Diffuse
                {
                    float3 objColor = new float3(1, 0, 1);
                    uint2 idData = entityIdBuffer[pixel.x + pixel.y * (int)width];
                    if (idData.x != uint.MaxValue)
                    {
                        float depth = depthNormals[pixel].r;
                        float3 hitPoint = rayOrigin + rayDir * depth * (worldData[0].farPlane - worldData[0].nearPlane);
                        objColor = DebugBRDF(pixel, rayDir, hitPoint, depth);
                    }
                    outputTexture[pixel] = new float4(objColor, 1);
                }
                else if (debugMode == 9) // Mip cascades
                {
                    float3 objColor = new float3(0, 0, 0);
                    uint2 idData = entityIdBuffer[pixel.x + pixel.y * (int)width];
                    if (idData.x != uint.MaxValue)
                    {
                        SDFObjectDTO sdf = sdfObjects[(int)idData.x];
                        if (sdf.albedoTexMetaID >= 0)
                        {
                            float depth = depthNormals[pixel].r * (worldData[0].farPlane - worldData[0].nearPlane);
                            float scale = math.rcp(math.max(sdf.texTilingOffset.x, EPSILON));
                            float mipLevel = EstimateMipLevel(depth, scale, textureMetadata[sdf.albedoTexMetaID].resolution);
                            objColor = MipLevelColor(mipLevel);
                        }
                    }
                    outputTexture[pixel] = new float4(objColor, 1);
                }
            }
        }

        #endregion main
    }
}







