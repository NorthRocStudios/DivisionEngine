//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
#pragma warning disable CA1416 // Validate platform compatibility

using ComputeSharp;
using DivisionEngine.Rendering;
using DivisionEngine.Rendering.ShaderUtilities;

namespace DivisionEngine
{
    [GeneratedComputeShaderDescriptor]
    [ThreadGroupSize(DefaultThreadGroupSizes.XY)]
    public readonly partial struct SDFShader3D(
        float width,
        float height,
        float aspect,
        int frameCount,
        int debugMode,
        int enableCheckerboard,
        ReadOnlyBuffer<SDFWorldDTO> worldData,
        ReadWriteTexture2D<float4> texture,
        ReadWriteTexture2D<float4> depthNormals,
        ReadWriteBuffer<uint2> entityIdBuffer,
        ReadOnlyBuffer<SDFObjectDTO> sdfObjects,
        ReadOnlyBuffer<SDFLightDTO> lights,
        ReadOnlyBuffer<uint> textureData,
        ReadOnlyBuffer<TextureMetadata> textureMetadata,
        ReadOnlyBuffer<float> terrainHeightData,       // NEW
        ReadOnlyBuffer<TerrainDTO> terrainMetadata) : IComputeShader
    {
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
            return SampleTexture(textureId, uv, fallback * float4.One).R;
        }

        public float4 SampleTexture(int textureId, float2 uv, float4 fallbackColor)
        {
            if (textureId < 0 || textureId >= textureMetadata.Length) return fallbackColor;
            float2 newUV = new float2(Hlsl.Abs(uv.X % 1), Hlsl.Abs(uv.Y % 1));

            TextureMetadata meta = textureMetadata[textureId];
            int x = (int)(newUV.X * (meta.resolution.X - 1));
            int y = (int)(newUV.Y * (meta.resolution.Y - 1));
            int index = meta.bufferOffset + y * meta.resolution.X + x;
            return ShaderMath.UnpackRGBA(textureData[index]);
        }

        public float SampleTextureBilinear(int textureId, float2 uv, float fallback)
        {
            return SampleTextureBilinear(textureId, uv, fallback * float4.One).R;
        }

        public float4 SampleTextureBilinear(int textureId, float2 uv, float4 fallbackColor)
        {
            if (textureId < 0 || textureId >= textureMetadata.Length) return fallbackColor;
            float2 newUV = new float2(Hlsl.Abs(uv.X % 1), Hlsl.Abs(uv.Y % 1));

            TextureMetadata meta = textureMetadata[textureId];
            float u = newUV.X * (meta.resolution.X - 1);
            float v = newUV.Y * (meta.resolution.Y - 1);

            int x0 = (int)u;
            int y0 = (int)v;
            int x1 = Hlsl.Min(x0 + 1, meta.resolution.X - 1);
            int y1 = Hlsl.Min(y0 + 1, meta.resolution.Y - 1);

            float u_frac = u - x0;
            float v_frac = v - y0;

            int idx00 = meta.bufferOffset + y0 * meta.resolution.X + x0;
            int idx10 = meta.bufferOffset + y0 * meta.resolution.X + x1;
            int idx01 = meta.bufferOffset + y1 * meta.resolution.X + x0;
            int idx11 = meta.bufferOffset + y1 * meta.resolution.X + x1;

            float4 c00 = ShaderMath.UnpackRGBA(textureData[idx00]);
            float4 c10 = ShaderMath.UnpackRGBA(textureData[idx10]);
            float4 c01 = ShaderMath.UnpackRGBA(textureData[idx01]);
            float4 c11 = ShaderMath.UnpackRGBA(textureData[idx11]);

            float4 c0 = Hlsl.Lerp(c00, c10, u_frac);
            float4 c1 = Hlsl.Lerp(c01, c11, u_frac);
            return Hlsl.Lerp(c0, c1, v_frac);
        }

        /// <summary>
        /// Faster version using precomputed TBN matrix (reduces operations).
        /// </summary>
        public float3 SampleNormalMapFast(int textureId, float2 uv, float3 normal, float3 tangent, float strength)
        {
            float4 normalMap = SampleTextureBilinear(textureId, uv, new float4(0.5f, 0.5f, 1.0f, 1.0f));

            // Unpack normal
            float3 tangentNormal = new float3(
                (normalMap.R * 2.0f - 1.0f) * strength,
                (normalMap.G * 2.0f - 1.0f) * strength,
                normalMap.B * 2.0f - 1.0f
            );
            tangentNormal = Hlsl.Normalize(tangentNormal);

            // Transform to world space using TBN matrix
            return Hlsl.Normalize(
                tangent * tangentNormal.X +
                ShaderMath.CalcBitangent(normal, tangent) * tangentNormal.Y +
                normal * tangentNormal.Z
            );
        }

        private static void GetMipInfo(TextureMetadata meta, int level, out int offset, out int2 res)
        {
            level = Hlsl.Clamp(level, 0, meta.mipCount - 1);
            offset = meta.bufferOffset;
            res = meta.resolution;
            for (int i = 0; i < level; i++)
            {
                offset += res.X * res.Y;
                res = new int2(Hlsl.Max(1, res.X / 2), Hlsl.Max(1, res.Y / 2));
            }
        }

        private float4 SampleTextureBilinearMip(int textureId, float2 uv, int mipLevel, float4 fallbackColor)
        {
            if (textureId < 0 || textureId >= textureMetadata.Length) return fallbackColor;
            TextureMetadata meta = textureMetadata[textureId];
            GetMipInfo(meta, mipLevel, out int offset, out int2 res);

            float2 newUV = new float2(Hlsl.Abs(uv.X % 1), Hlsl.Abs(uv.Y % 1));
            float u = newUV.X * (res.X - 1);
            float v = newUV.Y * (res.Y - 1);

            int x0 = (int)u; int y0 = (int)v;
            int x1 = Hlsl.Min(x0 + 1, res.X - 1);
            int y1 = Hlsl.Min(y0 + 1, res.Y - 1);
            float u_frac = u - x0; float v_frac = v - y0;

            float4 c00 = ShaderMath.UnpackRGBA(textureData[offset + y0 * res.X + x0]);
            float4 c10 = ShaderMath.UnpackRGBA(textureData[offset + y0 * res.X + x1]);
            float4 c01 = ShaderMath.UnpackRGBA(textureData[offset + y1 * res.X + x0]);
            float4 c11 = ShaderMath.UnpackRGBA(textureData[offset + y1 * res.X + x1]);

            float4 c0 = Hlsl.Lerp(c00, c10, u_frac);
            float4 c1 = Hlsl.Lerp(c01, c11, u_frac);
            return Hlsl.Lerp(c0, c1, v_frac);
        }

        private float4 SampleTextureTrilinear(int textureId, float2 uv, float mipLevel, float4 fallbackColor)
        {
            if (textureId < 0 || textureId >= textureMetadata.Length) return fallbackColor;
            int mipCount = textureMetadata[textureId].mipCount;
            float clampedLevel = Hlsl.Clamp(mipLevel, 0f, mipCount - 1);
            int level0 = (int)clampedLevel;
            int level1 = Hlsl.Min(level0 + 1, mipCount - 1);
            float frac = clampedLevel - level0;

            float4 c0 = SampleTextureBilinearMip(textureId, uv, level0, fallbackColor);
            float4 c1 = SampleTextureBilinearMip(textureId, uv, level1, fallbackColor);
            return Hlsl.Lerp(c0, c1, frac);
        }

        private float EstimateMipLevel(float depth, float scale, int2 textureResolution)
        {
            float texelWorldSize = Hlsl.Rcp(Hlsl.Max(scale, EPSILON)) / Hlsl.Max(textureResolution.X, 1f);
            float pixelWorldSize = Hlsl.Max(depth * worldData[0].camScreenDist / height, depth * worldData[0].camScreenDist / width);
            return Hlsl.Max(0f, Hlsl.Log2(Hlsl.Max(pixelWorldSize / Hlsl.Max(texelWorldSize, EPSILON), 1f)));
        }

        #endregion textures
        #region triplanar

        /// <summary>
        /// Blend weights for triplanar projection based on a local-space surface normal.
        /// Higher sharpness = narrower blend seams, more distinct axis-aligned faces.
        /// </summary>
        private static float3 TriplanarWeights(float3 localNormal, float sharpness)
        {
            float3 blend = Hlsl.Pow(Hlsl.Abs(localNormal), sharpness);
            return blend / Hlsl.Max(blend.X + blend.Y + blend.Z, EPSILON);
        }

        /// <summary>
        /// Triplanar sample of a data texture (albedo, roughness, metallic).
        /// </summary>
        private float4 SampleTriplanar(int textureId, float3 localPos, float3 blend, float scale, float4 fallback)
        {
            float4 colX = SampleTextureBilinear(textureId, localPos.YZ * scale, fallback);
            float4 colY = SampleTextureBilinear(textureId, localPos.XZ * scale, fallback);
            float4 colZ = SampleTextureBilinear(textureId, localPos.XY * scale, fallback);
            return colX * blend.X + colY * blend.Y + colZ * blend.Z;
        }

        private float SampleTriplanarScalar(int textureId, float3 localPos, float3 blend, float scale, float fallback)
        {
            return SampleTriplanar(textureId, localPos, blend, scale, fallback * float4.One).R;
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
            float4 mapX = SampleTextureBilinear(textureId, localPos.YZ * scale, flat);
            float4 mapY = SampleTextureBilinear(textureId, localPos.XZ * scale, flat);
            float4 mapZ = SampleTextureBilinear(textureId, localPos.XY * scale, flat);

            float3 tX = new float3((mapX.R * 2f - 1f) * strength, (mapX.G * 2f - 1f) * strength, mapX.B);
            float3 tY = new float3((mapY.R * 2f - 1f) * strength, (mapY.G * 2f - 1f) * strength, mapY.B);
            float3 tZ = new float3((mapZ.R * 2f - 1f) * strength, (mapZ.G * 2f - 1f) * strength, mapZ.B);

            // Keep the map's "outward" axis pointing the same way as the geometric normal
            float sx = Hlsl.Sign(localNormal.X);
            float sy = Hlsl.Sign(localNormal.Y);
            float sz = Hlsl.Sign(localNormal.Z);

            // X-projection: U=localY, V=localZ, tangent-Z = localX
            float3 nX = new float3(tX.Z * sx, tX.X, tX.Y);
            // Y-projection: U=localX, V=localZ, tangent-Z = localY
            float3 nY = new float3(tY.X, tY.Z * sy, tY.Y);
            // Z-projection: U=localX, V=localY, tangent-Z = localZ
            float3 nZ = new float3(tZ.X, tZ.Y, tZ.Z * sz);

            return Hlsl.Normalize(nX * blend.X + nY * blend.Y + nZ * blend.Z);
        }

        private float4 SampleTriplanarMip(int textureId, float3 localPos, float3 blend, float scale, float mipLevel, float4 fallback)
        {
            float4 colX = SampleTextureTrilinear(textureId, localPos.YZ * scale, mipLevel, fallback);
            float4 colY = SampleTextureTrilinear(textureId, localPos.XZ * scale, mipLevel, fallback);
            float4 colZ = SampleTextureTrilinear(textureId, localPos.XY * scale, mipLevel, fallback);
            return colX * blend.X + colY * blend.Y + colZ * blend.Z;
        }

        private float SampleTriplanarScalarMip(int textureId, float3 localPos, float3 blend, float scale, float mipLevel, float fallback)
        {
            return SampleTriplanarMip(textureId, localPos, blend, scale, mipLevel, fallback * float4.One).R;
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
            float4 mapX = SampleTextureTrilinear(textureId, localPos.YZ * scale, mipLevel, flat);
            float4 mapY = SampleTextureTrilinear(textureId, localPos.XZ * scale, mipLevel, flat);
            float4 mapZ = SampleTextureTrilinear(textureId, localPos.XY * scale, mipLevel, flat);

            float3 tX = new float3((mapX.R * 2f - 1f) * strength, (mapX.G * 2f - 1f) * strength, mapX.B);
            float3 tY = new float3((mapY.R * 2f - 1f) * strength, (mapY.G * 2f - 1f) * strength, mapY.B);
            float3 tZ = new float3((mapZ.R * 2f - 1f) * strength, (mapZ.G * 2f - 1f) * strength, mapZ.B);

            // Keep the map's "outward" axis pointing the same way as the geometric normal
            float sx = Hlsl.Sign(localNormal.X);
            float sy = Hlsl.Sign(localNormal.Y);
            float sz = Hlsl.Sign(localNormal.Z);

            // X-projection: U=localY, V=localZ, tangent-Z = localX
            float3 nX = new float3(tX.Z * sx, tX.X, tX.Y);
            // Y-projection: U=localX, V=localZ, tangent-Z = localY
            float3 nY = new float3(tY.X, tY.Z * sy, tY.Y);
            // Z-projection: U=localX, V=localY, tangent-Z = localZ
            float3 nZ = new float3(tZ.X, tZ.Y, tZ.Z * sz);

            return Hlsl.Normalize(nX * blend.X + nY * blend.Y + nZ * blend.Z);
        }

        #endregion triplanar
        #region sdf_sampling

        private float SampleTerrainHeight(TerrainDTO meta, float2 worldXZ)
        {
            // Calculate UV coordinates in the heightmap
            float2 uv = (worldXZ / meta.size) * 0.5f + 0.5f;
            uv = Hlsl.Saturate(uv);

            // Get pixel coordinates with bilinear interpolation
            float u = uv.X * (meta.resolution.X - 1);
            float v = uv.Y * (meta.resolution.Y - 1);
            int x0 = (int)u, y0 = (int)v;
            int x1 = Hlsl.Min(x0 + 1, meta.resolution.X - 1);
            int y1 = Hlsl.Min(y0 + 1, meta.resolution.Y - 1);
            float fx = u - x0, fy = v - y0;

            // Sample from the height buffer
            int idx00 = meta.bufferOffset + y0 * meta.resolution.X + x0;
            int idx10 = meta.bufferOffset + y0 * meta.resolution.X + x1;
            int idx01 = meta.bufferOffset + y1 * meta.resolution.X + x0;
            int idx11 = meta.bufferOffset + y1 * meta.resolution.X + x1;

            float h00 = terrainHeightData[idx00];
            float h10 = terrainHeightData[idx10];
            float h01 = terrainHeightData[idx01];
            float h11 = terrainHeightData[idx11];

            float h0 = Hlsl.Lerp(h00, h10, fx);
            float h1 = Hlsl.Lerp(h01, h11, fx);
            float height = Hlsl.Lerp(h0, h1, fy);

            return height;
        }

        private float ObjectSDF(float3 point, SDFObjectDTO obj)
        {
            float3 curPoint = point - obj.position;
            curPoint = ShaderMath.RotateVector(curPoint, obj.rotation);
            curPoint *= obj.scaling;

            if (obj.type == 9) // Get terrain height at this point
            {
                float height = 0f;
                TerrainDTO meta = new TerrainDTO();
                meta.heightScale = 20f;
                if (obj.terrainDataIndex >= 0 && obj.terrainDataIndex < terrainMetadata.Length)
                {
                    meta = terrainMetadata[obj.terrainDataIndex];
                    height = Hlsl.Abs(SampleTerrainHeight(meta, curPoint.XZ));
                }
                float halfSize = obj.parameters.X * 0.5f;
                return SDFPrimitives.Box(new float3(curPoint.X, curPoint.Y - height, curPoint.Z), 
                    new float3(halfSize, meta.heightScale + height, halfSize));
            }
            else
            {
                float dist = Hlsl.Min(obj.scaling.X, Hlsl.Min(obj.scaling.Y, obj.scaling.Z));
                dist *= EvaluatePrimitiveDistanceFast(obj.type, obj.parameters, curPoint);
                return dist * obj.stepBias;
            }
        }

        /// <summary>
        /// Single dispatch for a primitive's own local-space distance, excluding terrain.
        /// Takes only what it needs (type + one float4), not the full DTO, to keep
        /// per-call codegen light — this gets called multiple times per normal.
        /// </summary>
        private static float EvaluatePrimitiveDistanceFast(int type, float4 parameters, float3 localPt)
        {
            if (type == 0) return SDFPrimitives.Sphere(localPt, parameters.X);
            else if (type == 1) return SDFPrimitives.Box(localPt, parameters.XYZ);
            else if (type == 2) return SDFPrimitives.RoundedBox(localPt, parameters.XYZ, parameters.W);
            else if (type == 3) return SDFPrimitives.Torus(localPt, parameters.XY);
            else if (type == 4) return SDFPrimitives.Pyramid(localPt, parameters.X);
            else if (type == 5) return SDFPrimitives.Plane(localPt, parameters.XYZ, parameters.W);
            else if (type == 6) return SDFPrimitives.Cylinder(localPt, parameters.X, parameters.Y);
            else if (type == 7) return SDFPrimitives.Capsule(localPt, parameters.X, parameters.Y);
            else if (type == 8) return SDFPrimitives.Cone(localPt, parameters.XY, parameters.Z);
            else return SDFPrimitives.Sphere(localPt, parameters.X);
        }

        /// <summary>
        /// Calculates the SDF distance for the world at a point.
        /// </summary>
        /// <param name="point">World position to evaluate</param>
        /// <returns>Float2 representing the min distance, and closest object</returns>
        private float WorldSDF(float3 point, out int closest)
        {
            float minDist = MIN_TRAVERSE_DIST;
            closest = -1;
            for (int i = 0; i < sdfObjects.Length; i++)
            {
                float dist = ObjectSDF(point, sdfObjects[i]);
                if (Hlsl.Abs(dist) < minDist)
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
            float2 k = new float2(1f, -1f);
            return Hlsl.Normalize(k.XYY * WorldSDF(pos + k.XYY * h, out _) +
                              k.YYX * WorldSDF(pos + k.YYX * h, out _) +
                              k.YXY * WorldSDF(pos + k.YXY * h, out _) +
                              k.XXX * WorldSDF(pos + k.XXX * h, out _));
        }

        /// <summary>
        /// Computes a surface normal using only the known hit object's own primitive
        /// field, instead of the full multi-object WorldSDF. Cuts normal calculation
        /// from O(4 * totalObjectCount) world samples down to O(4) — since WorldSDF is
        /// a hard-min union (no smooth blending), the gradient at a given hit point is
        /// exactly this object's own gradient there, so this isn't an approximation
        /// except at the measure-zero seam where two objects are exactly tied.
        /// Not used for terrain (type 9) — see ComputeSurfaceNormal below.
        /// </summary>
        private float3 FastNormalSingleObject(float3 pos, SDFObjectDTO sdf)
        {
            float h = EPSILON * 50;
            float2 k = new float2(1f, -1f);
            return Hlsl.Normalize(k.XYY * ObjectSDF(pos + k.XYY * h, sdf) +
                              k.YYX * ObjectSDF(pos + k.YYX * h, sdf) +
                              k.YXY * ObjectSDF(pos + k.YXY * h, sdf) +
                              k.XXX * ObjectSDF(pos + k.XXX * h, sdf));
        }

        #endregion normals
        #region lighting

        // --------------------
        // Lighting Calculation
        // --------------------

        // New soft-shadow technique:
        // Reference: https://iquilezles.org/articles/rmshadows/
        // New Version: https://www.shadertoy.com/view/tscSRS
        private float3 SoftShadow(float3 point, float3 dir)
        {
            float depth, dist, shadow = 1;
            int maxShadowRaySteps = worldData[0].maxShadowRaySteps;
            for (int k = 0; k < sdfObjects.Length; k++)
            {
                SDFObjectDTO sdf = sdfObjects[k];
                depth = sdf.shadowDistances.X;
                if (!sdf.shadowEffects.X) continue;
                for (int i = 0; i < maxShadowRaySteps; ++i)
                {
                    dist = ObjectSDF(point + depth * dir, sdf);
                    if (depth > sdf.shadowDistances.Y) break;
                    shadow = Hlsl.Min(shadow, sdf.shadowDistances.Z * dist / depth);
                    depth += Hlsl.Clamp(dist, 0.025f, 100f); // Larger minimum step than 0.01
                }
            }
            shadow = Hlsl.Max(shadow, -1f);
            return Hlsl.SmoothStep(-1f, 0f, shadow) * float3.One;
        }

        private float3 SoftShadowColored(float3 point, float3 dir)
        {
            float shadow = 1f;
            float3 tint = float3.One;
            float tintShadow = 1f;
            int maxShadowRaySteps = worldData[0].maxShadowRaySteps;
            for (int k = 0; k < sdfObjects.Length; k++)
            {
                SDFObjectDTO sdf = sdfObjects[k];
                if (!sdf.shadowEffects.X) continue;
                bool isGlass = sdf.hasRefraction == 1;
                float depth = sdf.shadowDistances.X;
                float3 absorptionCoefficient = isGlass ? Hlsl.Log(Hlsl.Max(sdf.absorptionColor.RGB, 0.001f)) : float3.Zero;
                for (int i = 0; i < maxShadowRaySteps; ++i)
                {
                    if (depth > sdf.shadowDistances.Y) break;
                    float dist = ObjectSDF(point + depth * dir, sdf);
                    float stepSize = Hlsl.Clamp(dist, 0.025f, 100f);
                    if (isGlass)
                    {
                        tintShadow = Hlsl.Min(tintShadow, sdf.shadowDistances.Z * dist / depth);
                        float coverage = Hlsl.Saturate(0.5f - dist * sdf.shadowDistances.Z);
                        tint *= Hlsl.Exp(absorptionCoefficient * stepSize * coverage * sdf.absorptionColor.A * 5f);
                    }
                    else shadow = Hlsl.Min(shadow, sdf.shadowDistances.Z * dist / depth);
                    depth += stepSize;
                }
            }

            shadow = Hlsl.Max(shadow, -1f);
            float occlusion = Hlsl.SmoothStep(-1f, 0f, shadow);
            float occlusionTint = Hlsl.SmoothStep(-1f, 0f, tintShadow);
            return occlusion * Hlsl.Lerp(tint, float3.One, occlusionTint);
        }

        /// <summary>
        /// Hard shadow - returns 1.0 for fully lit, 0.0 for fully shadowed.
        /// Fast and sharp, no penumbra.
        /// </summary>
        private float3 HardShadow(float3 point, float3 dir)
        {
            float depth, dist;
            int maxShadowRaySteps = worldData[0].maxShadowRaySteps;
            for (int k = 0; k < sdfObjects.Length; k++)
            {
                SDFObjectDTO sdf = sdfObjects[k];
                depth = sdf.shadowDistances.X;
                if (!sdf.shadowEffects.X) continue;
                for (int i = 0; i < maxShadowRaySteps; ++i)
                {
                    dist = ObjectSDF(point + depth * dir, sdf);
                    if (dist < EPSILON) return 0f;
                    if (depth >= sdf.shadowDistances.Y) break;
                    depth += dist;
                }
            }
            return 1f; // Max iterations reached, assume no shadow
        }

        /// <summary>
        /// Unified shadow function that selects the appropriate shadow type.
        /// </summary>
        private float3 CalculateShadow(float3 hitPoint, float3 normal, float3 lightDir, int shadowType)
        {
            float3 shadowOrigin = hitPoint + normal * EPSILON * REFLECTION_BIAS;
            switch (shadowType)
            {
                case 0: // Hard shadows
                    return HardShadow(shadowOrigin, lightDir);
                case 1: // Soft shadows
                    return SoftShadow(shadowOrigin, lightDir);
                case 2: // Colored shadows
                    return SoftShadowColored(shadowOrigin, lightDir);
                default:
                    return SoftShadow(shadowOrigin, lightDir);
            }
        }

        /// <summary>
        /// Calculate lighting contribution from all lights
        /// </summary>
        private float3 CalculateLighting(float3 hitPoint, float3 geoNormal, float3 finalNormal, float3 viewDir,
            bool2 shadowEffects, int shadowType, float specular, float reflectance,
            float3 baseCol, float metallic, float roughAlpha)
        {
            float3 totalLight = float3.Zero;
            float3 lightShadow = 1f;

            for (int i = 0; i < lights.Length; i++)
            {
                SDFLightDTO light = lights[i];
                if (light.type == 0) // Directional
                {
                    float3 lightDir = Hlsl.Normalize(ShaderMath.RotateVector(new float3(0, 0, -1), light.rotation));
                    float3 lightColor = light.color.RGB * light.intensity;

                    float NoL = Hlsl.Max(Hlsl.Dot(finalNormal, lightDir), 0f);
                    if (NoL <= 0f) continue;

                    // Shadows
                    if (shadowEffects.Y) lightShadow = CalculateShadow(hitPoint, geoNormal, lightDir, shadowType);
                    float3 brdf = PBR.BRDFMicrofacetFunction(lightDir, viewDir,
                        finalNormal, baseCol, metallic, roughAlpha, specular, reflectance, RECIPROCAL_PI, EPSILON);

                    // Apply shadow to the entire lighting contribution
                    totalLight += brdf * lightColor * NoL * lightShadow;
                }
                else if (light.type == 1) // Point
                {
                    float3 lightVec = light.position - hitPoint;
                    float distance = Hlsl.Length(lightVec);
                    float3 lightDir = lightVec / distance;
                    float attenuation = Hlsl.Rcp(distance * distance);
                    float radiusFactor = Hlsl.Saturate(1f - (distance / light.radius));
                    attenuation *= radiusFactor;

                    float NoL = Hlsl.Max(Hlsl.Dot(finalNormal, lightDir), 0f);
                    if (NoL <= 0f || attenuation <= 0f) continue;
                    float3 lightColor = light.color.RGB * light.intensity * attenuation;

                    // Calculate shadow for point light
                    if (shadowEffects.Y && distance < light.radius * 2f)
                        lightShadow = CalculateShadow(hitPoint, geoNormal, lightDir, shadowType);

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
                if (Hlsl.Abs(dist) < worldDist) worldDist = dist;
            }

            float occlusionRadius = aoValues.Y;
            if (worldDist >= occlusionRadius) return 1f; // No object found within radius
            float occlusion = 1f - Hlsl.Saturate(worldDist / occlusionRadius);
            occlusion = Hlsl.Pow(occlusion, aoValues.Z);
            return 1f - occlusion;
        }

        /// <summary>
        /// Bends a shadow ray through a single refractive object (entry + exit refraction),
        /// returning the transmittance and the ray's new origin/direction on the far side.
        /// This is a single-bounce approximation, not a full recursive trace - it's meant
        /// to be cheap enough to call from inside a shadow loop.
        /// </summary>
        private float3 CausticTransmit(float3 point, float3 dir, SDFObjectDTO sdf,
            out float3 exitPoint, out float3 exitDir)
        {
            exitPoint = point;
            exitDir = dir;

            float3 entryNormal = FastNormalSingleObject(point, sdf);
            if (Hlsl.Dot(entryNormal, dir) > 0f) entryNormal = -entryNormal; // oppose incident, like TraceRay's convention

            float entryEta = Hlsl.Rcp(Hlsl.Max(sdf.ior, EPSILON));
            if (!Refract(dir, entryNormal, entryEta, out float3 innerDir))
                return float3.Zero; // Total internal reflection right at entry - treat as opaque for shadow purposes

            float3 p = point - entryNormal * EPSILON * 2f;
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
                float stepSize = Hlsl.Max(Hlsl.Abs(d), EPSILON * 4f);
                p += innerDir * stepSize;
                travel += stepSize;
            }

            float3 absorptionCoefficient = Hlsl.Log(Hlsl.Max(sdf.absorptionColor.RGB, 0.001f));
            float3 transmittance = Hlsl.Exp(absorptionCoefficient * travel * sdf.absorptionColor.A * 5f);

            if (!foundExit) return float3.Zero; // Ran out of budget inside the object - treat as opaque

            float3 exitNormal = FastNormalSingleObject(exitPoint, sdf);
            if (Hlsl.Dot(exitNormal, innerDir) > 0f) exitNormal = -exitNormal;

            if (Refract(innerDir, exitNormal, sdf.ior, out float3 bentDir))
                exitDir = bentDir;
            else
                exitDir = Hlsl.Reflect(innerDir, exitNormal); // TIR at exit, fall back to reflection

            return transmittance;
        }

        /// <summary>
        /// Shadow ray that bends through refractive occluders instead of just stopping at them.
        /// Recursion budget (how many glass objects it'll pass through) shrinks with traceDepth,
        /// so this stays cheap when called from reflection/refraction bounces.
        /// </summary>
        private float3 CalculateCausticShadow(float3 hitPoint, float3 normal, float3 lightDir, int traceDepth)
        {
            float3 p = hitPoint + normal * EPSILON * REFLECTION_BIAS;
            float3 dir = lightDir;
            float3 attenuation = float3.One;

            int maxRefractiveHits = Hlsl.Max(0, 2 - traceDepth); // depth 0: up to 2 panes of glass, depth 2+: none
            int maxShadowRaySteps = worldData[0].maxShadowRaySteps;
            for (int pass = 0; pass <= maxRefractiveHits; pass++)
            {
                int hitObj = -1;
                float hitDepth = 0f;

                for (int k = 0; k < sdfObjects.Length; k++)
                {
                    SDFObjectDTO sdf = sdfObjects[k];
                    if (!sdf.shadowEffects.X) continue;

                    float td = sdf.shadowDistances.X;
                    for (int i = 0; i < maxShadowRaySteps; i++)
                    {
                        if (td > sdf.shadowDistances.Y) break;
                        float dist = ObjectSDF(p + td * dir, sdf);
                        if (dist < EPSILON)
                        {
                            hitObj = k;
                            hitDepth = td;
                            break;
                        }
                        td += Hlsl.Clamp(dist, 0.025f, 100f);
                    }
                    if (hitObj >= 0) break;
                }

                if (hitObj < 0) return attenuation; // Reached the light

                SDFObjectDTO hitSdf = sdfObjects[hitObj];
                if (hitSdf.hasRefraction == 0 || pass == maxRefractiveHits)
                    return float3.Zero; // Opaque occluder, or out of refractive budget

                float3 entryPoint = p + dir * hitDepth;
                float3 segAtten = CausticTransmit(entryPoint, dir, hitSdf, out float3 exitPoint, out float3 exitDir);
                attenuation *= segAtten;
                if (attenuation.X + attenuation.Y + attenuation.Z < MIN_THROUGHPUT) return float3.Zero;

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
            localPos = ShaderMath.RotateVector(hitPoint - sdf.position, sdf.rotation);
            float3 localGeoNormal = Hlsl.Normalize(ShaderMath.RotateVector(geoNormal, sdf.rotation));
            blend = TriplanarWeights(localGeoNormal, sdf.triplanarBlend);
            float scale = Hlsl.Rcp(Hlsl.Max(sdf.texTilingOffset.X, EPSILON));

            float mipLevel = 0f;
            if (sdf.albedoTexMetaID >= 0)
                mipLevel = EstimateMipLevel(depth, scale, textureMetadata[sdf.albedoTexMetaID].resolution);

            albedo = SampleTriplanarMip(sdf.albedoTexMetaID, localPos, blend, scale, mipLevel, sdf.color).RGB;
            metallic = SampleTriplanarScalarMip(sdf.metalTexMetaID, localPos, blend, scale, mipLevel, sdf.metallic);
            roughness = Hlsl.Max(SampleTriplanarScalarMip(sdf.roughTexMetaID, localPos, blend, scale, mipLevel, sdf.roughness), 0.045f);

            float3 localFinalNormal = SampleNormalTriplanarMip(sdf.normalTexMetaID, localPos, localGeoNormal, blend, scale, sdf.normalStrength, mipLevel);
            finalNormal = Hlsl.Normalize(ShaderMath.InverseRotateVector(localFinalNormal, sdf.rotation));
        }

        #endregion pbr_workflow
        #region sky

        /// <summary>
        /// Gets the sky color based on the configured sky mode.
        /// </summary>
        private float3 GetSkyColor(float3 viewDir)
        {
            SDFWorldDTO world = worldData[0];
            if (world.skyType == 0) return AddAtmosphericScattering(viewDir, world.skyColor.RGB * world.skyIntensity, world.mainLightDir);
            else if (world.skyType == 1) return AddAtmosphericScattering(viewDir, GetGradientSkyColor(viewDir, world), world.mainLightDir);
            else if (world.skyType == 2) return GetHDRISkyColor(viewDir, world);
            return world.skyColor.RGB * world.skyIntensity;
        }

        /// <summary>
        /// Computes gradient sky color based on view direction.
        /// </summary>
        private static float3 GetGradientSkyColor(float3 viewDir, SDFWorldDTO world)
        {
            float3 dir = Hlsl.Normalize(viewDir);
            float y = dir.Y;

            // Map to 0-1 range
            float t = y * 0.5f + 0.5f;
            t = Hlsl.Saturate(t);

            // Sample the gradient
            float3 bottom = world.bottomSkyColor.RGB;
            float3 middle = world.middleSkyColor.RGB;
            float3 top = world.topSkyColor.RGB;

            float3 color;
            if (t < 0.5f)
            {
                // Bottom to middle
                float u = t / 0.5f;
                color = Hlsl.Lerp(bottom, middle, u);
            }
            else
            {
                // Middle to top
                float u = (t - 0.5f) / 0.5f;
                color = Hlsl.Lerp(middle, top, u);
            }

            // Optional: Add horizon glow
            float horizonGlow = 1f - Hlsl.Pow(Hlsl.Abs(y), 4f);
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
                return world.skyColor.RGB * world.skyIntensity; // fallback

            int layout = textureMetadata[texId].cubemapLayout;
            float3 color = SampleCubemap(texId, viewDir, layout);
            return color * world.skyIntensity;
        }

        private float3 SampleCubemap(int texId, float3 direction, int layout)
        {
            if (layout == 1) // For Equirectangular (panorama)
            {
                float theta = Hlsl.Atan2(direction.Z, direction.X); // -PI to PI
                float phi = Hlsl.Acos(direction.Y); // 0 to PI
                float u = (theta + PI) / (2f * PI);
                float v = phi / PI;
                return SampleTextureBilinear(texId, new float2(u, v), float4.Zero).RGB;
            }
            else if (layout == 2) // Horizontal cross (4 wide x 3 tall grid)
            {
                float3 absDir = Hlsl.Abs(direction);
                float maxComp = Hlsl.Max(absDir.X, Hlsl.Max(absDir.Y, absDir.Z));

                int faceCol, faceRow;
                float2 faceUV;

                if (maxComp == absDir.X && direction.X > 0) { faceUV = new float2(-direction.Z, -direction.Y) / absDir.X; faceCol = 2; faceRow = 1; } // +X
                else if (maxComp == absDir.X) { faceUV = new float2(direction.Z, -direction.Y) / absDir.X; faceCol = 0; faceRow = 1; } // -X
                else if (maxComp == absDir.Y && direction.Y > 0) { faceUV = new float2(direction.X, direction.Z) / absDir.Y; faceCol = 1; faceRow = 0; } // +Y
                else if (maxComp == absDir.Y) { faceUV = new float2(direction.X, -direction.Z) / absDir.Y; faceCol = 1; faceRow = 2; } // -Y
                else if (direction.Z > 0) { faceUV = new float2(direction.X, -direction.Y) / absDir.Z; faceCol = 1; faceRow = 1; } // +Z
                else { faceUV = new float2(-direction.X, -direction.Y) / absDir.Z; faceCol = 3; faceRow = 1; } // -Z

                faceUV = faceUV * 0.5f + 0.5f;
                float2 globalUV = new float2((faceCol + faceUV.X) / 4f, (faceRow + faceUV.Y) / 3f);
                return SampleTextureBilinear(texId, globalUV, float4.Zero).RGB;
            }
            else if (layout == 3) // Vertical cross (3 wide x 4 tall grid)
            {
                float3 absDir = Hlsl.Abs(direction);
                float maxComp = Hlsl.Max(absDir.X, Hlsl.Max(absDir.Y, absDir.Z));

                int faceCol, faceRow;
                float2 faceUV;

                if (maxComp == absDir.X && direction.X > 0) { faceUV = new float2(-direction.Z, -direction.Y) / absDir.X; faceCol = 2; faceRow = 1; } // +X
                else if (maxComp == absDir.X) { faceUV = new float2(direction.Z, -direction.Y) / absDir.X; faceCol = 0; faceRow = 1; } // -X
                else if (maxComp == absDir.Y && direction.Y > 0) { faceUV = new float2(direction.X, direction.Z) / absDir.Y; faceCol = 1; faceRow = 0; } // +Y
                else if (maxComp == absDir.Y) { faceUV = new float2(direction.X, -direction.Z) / absDir.Y; faceCol = 1; faceRow = 2; } // -Y
                else if (direction.Z > 0) { faceUV = new float2(direction.X, -direction.Y) / absDir.Z; faceCol = 1; faceRow = 1; } // +Z
                else { faceUV = new float2(-direction.X, direction.Y) / absDir.Z; faceCol = 1; faceRow = 3; } // -Z, bottom cell

                faceUV = faceUV * 0.5f + 0.5f;
                float2 globalUV = new float2((faceCol + faceUV.X) / 3f, (faceRow + faceUV.Y) / 4f);
                return SampleTextureBilinear(texId, globalUV, float4.Zero).RGB;
            }
            else return SampleTextureBilinear(texId, new float2(0.5f, 0.5f), float4.Zero).RGB; // Fallback
        }

        /// <summary>
        /// Adds atmospheric scattering to the sky (optional enhancement).
        /// </summary>
        private static float3 AddAtmosphericScattering(float3 viewDir, float3 skyColor, float3 sunDir)
        {
            // Simple Rayleigh scattering approximation
            float sunDot = Hlsl.Max(Hlsl.Dot(viewDir, sunDir), 0.0f);
            float scattering = Hlsl.Pow(sunDot, 3.0f) * 0.5f;

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

                        if (Hlsl.Abs(refineDist) < BISECT_EPS)
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
            float NdotI = Hlsl.Dot(normal, incident);
            float k = 1.0f - eta * eta * (1.0f - NdotI * NdotI);
            if (k < 0f)
            {
                refracted = float3.Zero;
                return false; // Total internal reflection
            }
            refracted = eta * incident - (eta * NdotI + Hlsl.Sqrt(k)) * normal;
            refracted = Hlsl.Normalize(refracted);
            return true;
        }

        private float3 TraceRefractionRay(float3 startDir, float3 startOrigin, float3 ambientBase, float3 backgroundCol,
            float3 normal, SDFObjectDTO startMat, out int steps)
        {
            float3 totalTransmittance = float3.One;
            float3 accumulatedColor = float3.Zero;

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
                    float3 exitPt = float3.Zero;
                    float3 exitNorm = float3.Zero;
                    float3 absorptionCoefficient = Hlsl.Log(Hlsl.Max(currentObj.absorptionColor.RGB, 0.001f));

                    for (int i = 0; i < currentObj.refractionMaxSteps; i++)
                    {
                        // Use ONLY the current refractive object's SDF
                        float d = ObjectSDF(p, currentObj);
                        bool nowInside = d < 0f;

                        if (currentlyInsideObject != nowInside)
                        {
                            // Binary search for the exact surface of currentObj
                            float3 tMin = p - refractDir * Hlsl.Max(Hlsl.Abs(d),
                                ShaderMath.AdaptiveEpsilon(travelDistance, width, height, camScreenDist, EPSILON));
                            float3 tMax = p;
                            const float REFRACT_BISECT_EPS = EPSILON * 20f;

                            for (int b = 0; b < 12; b++)
                            {
                                float3 mid = (tMin + tMax) * 0.5f;
                                float dMid = ObjectSDF(mid, currentObj);   // use currentObj only

                                if (Hlsl.Abs(dMid) < REFRACT_BISECT_EPS)
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
                            if (Hlsl.Dot(exitNorm, refractDir) > 0f) exitNorm = -exitNorm;
                            foundExit = true;

                            float3 segmentTransmittance = Hlsl.Exp(
                                absorptionCoefficient * travelDistance * currentObj.absorptionColor.A * 5f);
                            totalTransmittance *= segmentTransmittance;
                            break;
                        }

                        float stepSize = Hlsl.Max(Hlsl.Abs(d),
                            ShaderMath.AdaptiveEpsilon(travelDistance, width, height, camScreenDist, EPSILON));
                        p += refractDir * stepSize;
                        travelDistance += stepSize;
                    }

                    if (!foundExit)
                    {
                        // Still inside? Apply final absorption and return background.
                        float3 segmentTransmittance = Hlsl.Exp(absorptionCoefficient * travelDistance * currentObj.absorptionColor.A * 5f);
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
                        if (Hlsl.Dot(currentNormal, currentDir) > 0) currentNormal = -currentNormal;
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
            float3 finalColor = float3.Zero;
            normal = float3.Zero;

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
            if (sdf.aoValues.X > 0f) aoValue = Hlsl.Lerp(1f, CalculateAO(hitPoint, normal, sdf.entityID, sdf.aoValues), sdf.aoValues.X);

            float3 directLight = CalculateLighting(hitPoint, normal, finalNormal, viewDir,
                new bool2(sdf.shadowEffects.X, false), sdf.shadowType, sdf.specular, sdf.reflectance, albedo, metallic, roughAlpha);
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
            float3 finalColor = float3.Zero;
            float3 contribution = float3.One;
            float3 ambientBase = float3.Zero;
            float cosTheta = 0f;

            float3 refractedLight = float3.Zero;
            SDFObjectDTO mainMat = default;
            outputNormal = float3.Zero;
            totalDist = 0f;
            float farPlane = worldData[0].farPlane;
            bool firstHit = true;
            steps = 0;

            // Refraction coloring
            float3 surfaceColor = float3.Zero;
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
                if (Hlsl.Dot(normal, rayDir) > 0f) normal = -normal;
                float3 viewDir = -rayDir;

                SampleSurfaceMaterial(hitPoint, normal, sdf, depth,
                    out _, out _, out float3 albedo, out float metallic, out float roughness, out float3 finalNormal);
                float roughAlpha = roughness * roughness;

                float aoStrength = sdf.aoValues.X;
                cosTheta = Hlsl.Max(Hlsl.Dot(normal, viewDir), 0f);

                if (firstHit)
                {
                    outputNormal = normal;
                    totalDist = depth;
                    entityIdBuffer[pixel.X + pixel.Y * (int)width] = new uint2((uint)closestObjIndex, sdf.entityID);
                    mainMat = sdf;

                    // AO
                    ambientBase = Hlsl.Lerp(albedo, worldData[0].skyColor.RGB, worldData[0].ambientStrength) * worldData[0].ambientStrength;
                    if (aoStrength > 0f) aoValue = Hlsl.Lerp(1f, CalculateAO(hitPoint, normal, sdf.entityID, sdf.aoValues), aoStrength);

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
                    sdf.shadowEffects, sdf.shadowType, sdf.specular, sdf.reflectance, albedo, metallic, roughAlpha);
                if (Hlsl.Length(directLight) > 0.5f) aoValue = Hlsl.Length(directLight);
                float3 ambientLight = ambientBase * aoValue;

                if (bounce == 0) surfaceColor = directLight;
                finalColor += contribution * (ambientLight + directLight);

                if (sdf.hasReflection == 0) break;
                if (bounce == sdf.reflectionMaxBounces - 1) break;

                maxRaySteps = (int)(maxRaySteps / sdf.reflectRayStepFalloff);

                // Metallic/roughness maps now actually drive this:
                float3 f0Refl = Hlsl.Lerp(sdf.f0_reflectance, albedo, new float3(metallic, metallic, metallic));
                float3 F = PBR.FresnelSchlick(Hlsl.Max(Hlsl.Dot(finalNormal, viewDir), 0f), f0Refl, sdf.reflectance, 1f);
                float reflectionChance = Hlsl.Lerp(F.X, 1f, metallic);

                if (bounce == 0 && isRefractive) reflectionChance = fresnelFactor;
                if (reflectionChance < MIN_REFLECTION_CHANCE) break;

                contribution *= F * (1f - roughness * 0.5f);
                contribution = Hlsl.Min(contribution, 10f);
                if (contribution.X + contribution.Y + contribution.Z < MIN_THROUGHPUT) break;

                int reflectionSampleIndex = pixel.X * 73 + pixel.Y * 9277 + frameCount * 1973 + sampleIndexInPixel * 3271 + bounce * 997;
                float2 u = PBR.Halton2D(reflectionSampleIndex);

                // Perturb with the bumped normal, not the geometric one -> normal maps now visible in reflections
                float3 halfVector = PBR.ImportanceSampleGGX(u, finalNormal, roughAlpha, PI);
                float3 reflectDir = Hlsl.Normalize(Hlsl.Reflect(rayDir, halfVector));
                if (Hlsl.Dot(reflectDir, normal) < 0.01f) break; // test vs geometric normal, avoids self-intersection

                rayOrigin = hitPoint + normal * EPSILON * REFLECTION_BIAS; // offset along geometric normal, stays safe
                rayDir = reflectDir;
            }

            float3 outputColor;
            if (isRefractive)
            {
                // Proper Fresnel using Schlick
                float3 f0 = float3.One * mainMat.f0_dielectric;
                float3 fresnel = PBR.FresnelSchlick(cosTheta, f0, mainMat.reflectance, 1f).X;
                outputColor = finalColor * fresnel + refractedLight * (1f - fresnel);
            }
            else outputColor = finalColor + surfaceColor;

            return outputColor;
        }

        #endregion raymarching
        #region debug

        private float3 DebugBRDF(int2 pixel, float3 rayDir, float3 hitPoint, float depth)
        {
            float3 outputVec = float3.Zero;
            uint2 idData = entityIdBuffer[pixel.X + pixel.Y * (int)width];
            if (idData.X != uint.MaxValue)
            {
                SDFObjectDTO sdf = sdfObjects[(int)idData.X];
                float3 normal = depthNormals[pixel].GBA;
                float3 viewDir = -rayDir;

                if (Hlsl.Length(worldData[0].mainLightDir) > 0f)
                {
                    SampleSurfaceMaterial(hitPoint, normal, sdf, depth,
                        out _, out _, out float3 albedo, out float metallic, out float roughness, out float3 finalNormal);
                    float roughAlpha = roughness * roughness;

                    float NoL = Hlsl.Max(Hlsl.Dot(finalNormal, worldData[0].mainLightDir), 0f);
                    if (NoL > 0f)
                    {
                        if (debugMode == 6)
                            outputVec = PBR.BRDFMicrofacetFunction(worldData[0].mainLightDir, viewDir,
                                finalNormal, albedo, metallic, roughAlpha, sdf.specular, sdf.reflectance, RECIPROCAL_PI, EPSILON);
                        else if (debugMode == 7)
                        {
                            float3 halfwayDir = Hlsl.Normalize(viewDir + worldData[0].mainLightDir);
                            float NoV = Hlsl.Saturate(Hlsl.Dot(finalNormal, viewDir));
                            float NoH = Hlsl.Saturate(Hlsl.Dot(finalNormal, halfwayDir));
                            float VoH = Hlsl.Saturate(Hlsl.Dot(viewDir, halfwayDir));
                            float3 f0 = Hlsl.Lerp(float3.One * 0.16f * sdf.specular * sdf.specular, albedo, new float3(metallic, metallic, metallic));
                            float3 F = PBR.FresnelSchlick(VoH, f0, sdf.reflectance, 1f);
                            float D = PBR.D_GGX(NoH, roughAlpha, RECIPROCAL_PI);
                            float G = PBR.GSmith(NoV, NoL, roughAlpha, EPSILON);
                            outputVec = F * D * G / (4f * Hlsl.Max(NoV * NoL, EPSILON));
                        }
                        else if (debugMode == 8)
                        {
                            float NoV = Hlsl.Saturate(Hlsl.Dot(finalNormal, viewDir));
                            float VoH = Hlsl.Saturate(Hlsl.Dot(viewDir, Hlsl.Normalize(viewDir + worldData[0].mainLightDir)));
                            outputVec = albedo * PBR.DisneyDiffuseFactor(NoV, NoL, VoH, roughAlpha) * RECIPROCAL_PI;
                        }
                    }
                }
            }
            return outputVec;
            //float3 H = Hlsl.Normalize(V + L);
            //float NdotV = Hlsl.Max(Hlsl.Dot(N, V), 0.0f);
            //float NdotL = Hlsl.Max(Hlsl.Dot(N, L), 0.0f);
            //float NdotH = Hlsl.Max(Hlsl.Dot(N, H), 0.0f);

            //float D = D_GGX(NdotH, roughness);
            //float G = G1_GGX_Schlick(NdotV, roughness) * G1_GGX_Schlick(NdotL, roughness);

            //float3 f0 = float3.One * 0.16f * reflectance * reflectance;
            //float3 F = FresnelSchlick(Hlsl.Max(Hlsl.Dot(V, H), 0.0f), f0);

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
            int level = (int)Hlsl.Floor(mipLevel + 0.5f); // round to nearest whole mip for clean bands
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
        public void Execute()
        {
            int2 localPixel = ThreadIds.XY;
            int2 pixel;
            if (enableCheckerboard == 1) // Checkerboard pattern
            {
                int framePass = frameCount & 1;
                pixel = new int2(
                    localPixel.X * 2 + (framePass ^ (localPixel.Y & 1)),
                    localPixel.Y
                );
            }
            else pixel = localPixel; // Full resolution rendering
            if (pixel.X >= width || pixel.Y >= height) return;

            texture[pixel] = new float4(0, 0, 0, 0);
            depthNormals[pixel] = new float4(0, 0, 0, 0);
            entityIdBuffer[pixel.X + pixel.Y * (int)width] = new uint2(uint.MaxValue, uint.MaxValue);

            float2 uv = (float2)pixel / new float2(width, height) * 2f - 1f; // UV math
            float3 rayOrigin = worldData[0].cameraOrigin;

            float3 accumulatedColor = float3.Zero;
            float3 accumulatedNormal = float3.Zero;
            float3 rayDir = float3.Zero;
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
                finalColor = Hlsl.Saturate(finalColor * (2.51f * finalColor + 0.03f) / (finalColor * (2.43f * finalColor + 0.59f) + 0.14f));

            // Final
            texture[pixel] = new float4(finalColor, 1f);
            depthNormals[pixel] = new float4(finalDist / maxPossibleDistance, finalNormal);

            // Debugging:
            if (debugMode > 0)
            {
                if (debugMode == 1) // Depth buffer
                {
                    float depth = depthNormals[pixel].R;
                    texture[pixel] = new float4(depth, depth, depth, 1);
                }
                else if (debugMode == 2) // World normal buffer
                    texture[pixel] = new float4(depthNormals[pixel].GBA, 1);
                else if (debugMode == 3) // Object ID buffer
                {
                    float3 objColor = ShaderMath.IntToColor(entityIdBuffer[pixel.X + pixel.Y * (int)width].X);
                    texture[pixel] = new float4(objColor, 1);
                }
                else if (debugMode == 4) // Ray steps
                {
                    float stepValue1 = Hlsl.Saturate((float)steps / worldData[0].maxRaySteps);
                    float stepValue2 = Hlsl.Saturate((float)(steps - worldData[0].maxRaySteps) / worldData[0].maxRaySteps);
                    float stepValue3 = Hlsl.Saturate((float)(steps - worldData[0].maxRaySteps * 2) / worldData[0].maxRaySteps);
                    texture[pixel] = new float4(stepValue1, stepValue2, stepValue3, 1);
                }
                else if (debugMode == 5) // Shadows
                {
                    float3 shadowColor = new float3(1, 0, 0);
                    uint2 idData = entityIdBuffer[pixel.X + pixel.Y * (int)width];
                    if (idData.X != uint.MaxValue)
                    {
                        float3 hitPoint = rayOrigin + rayDir * depthNormals[pixel].R * (worldData[0].farPlane - worldData[0].nearPlane);
                        float3 normal = depthNormals[pixel].GBA;
                        if (Hlsl.Length(worldData[0].mainLightDir) > 0f)
                        {
                            float3 shadowOrigin = hitPoint + normal * EPSILON * REFLECTION_BIAS;
                            shadowColor = SoftShadow(shadowOrigin, worldData[0].mainLightDir);
                        }
                    }
                    texture[pixel] = new float4(shadowColor, 1);
                }
                else if (debugMode == 6 || debugMode == 7 || debugMode == 8) // BRDF, Specular, Diffuse
                {
                    float3 objColor = new float3(1, 0, 1);
                    uint2 idData = entityIdBuffer[pixel.X + pixel.Y * (int)width];
                    if (idData.X != uint.MaxValue)
                    {
                        float depth = depthNormals[pixel].R;
                        float3 hitPoint = rayOrigin + rayDir * depth * (worldData[0].farPlane - worldData[0].nearPlane);
                        objColor = DebugBRDF(pixel, rayDir, hitPoint, depth);
                    }
                    texture[pixel] = new float4(objColor, 1);
                }
                else if (debugMode == 9) // Mip cascades
                {
                    float3 objColor = new float3(0, 0, 0);
                    uint2 idData = entityIdBuffer[pixel.X + pixel.Y * (int)width];
                    if (idData.X != uint.MaxValue)
                    {
                        SDFObjectDTO sdf = sdfObjects[(int)idData.X];
                        if (sdf.albedoTexMetaID >= 0)
                        {
                            float depth = depthNormals[pixel].R * (worldData[0].farPlane - worldData[0].nearPlane);
                            float scale = Hlsl.Rcp(Hlsl.Max(sdf.texTilingOffset.X, EPSILON));
                            float mipLevel = EstimateMipLevel(depth, scale, textureMetadata[sdf.albedoTexMetaID].resolution);
                            objColor = MipLevelColor(mipLevel);
                        }
                    }
                    texture[pixel] = new float4(objColor, 1);
                }
            }
        }

        #endregion main
    }
}
