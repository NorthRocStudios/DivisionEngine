
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
using DivisionEngine.Rendering.Terrain;

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

        /// <summary>1 when cone-traced supersampling replaces the single-sample primary trace.</summary>
        public int enableCtss;

        /// <summary>1 to put the primary ray through the pixel centre (test/comparison mode).</summary>
        public int sampleCenter;

        /// <summary>CTSS cone coverage in pixels; 1 traces a cone through the pixel corners.</summary>
        public float ctssConeCoverage;

        /// <summary>CTSS debug visualization (0 = off, 1 = samples, 2 = coverage).</summary>
        public int ctssDebug;

        /// <summary>
        /// Primary samples per pixel when CTSS is off. The default is the engine's
        /// normal 2x sampling; reference renders raise it to converge the stochastic
        /// reflection estimate that the denoiser is measured against.
        /// </summary>
        public int samplesPerPixel;

        /// <summary>World/scene uniforms.</summary>
        [ShaderResource] public StructuredBuffer<SDFWorldDTO> worldData;

        /// <summary>Colour target. Named outputTexture because 'texture' is reserved in HLSL.</summary>
        [ShaderResource] public RWTexture2D<float4> outputTexture;

        /// <summary>Per-pixel cone-edge mask written by the ExecuteEdge kernel (1 = supersample).</summary>
        [ShaderResource] public RWTexture2D<float4> edgeMaskWrite;

        /// <summary>Read-side copy of the cone-edge mask (separate texture for the SRV).</summary>
        [ShaderResource] public Texture2D<float4> edgeMaskRead;

        /// <summary>Depth (r) and world normals (gba) target.</summary>
        [ShaderResource] public RWTexture2D<float4> depthNormals;

        /// <summary>Per-pixel object ID (x = index into <see cref="sdfObjects"/>, y = entity id).</summary>
        [ShaderResource] public RWBuffer<uint2> entityIdBuffer;

        /// <summary>
        /// Per-pixel primary-surface data for the denoiser:
        /// x = roughness, y = metallic, z = flags (1 reflection, 2 refraction,
        /// 4 exact-path split channels), w = reflection weight (Fresnel magnitude).
        /// </summary>
        [ShaderResource] public RWTexture2D<float4> materialBuffer;

        /// <summary>
        /// First-surface reflection radiance, separated from the base shading so the
        /// denoiser filters only the stochastic part and leaves albedo/texture detail alone.
        /// </summary>
        [ShaderResource] public RWTexture2D<float4> reflectionTexture;

        /// <summary>
        /// First-surface refracted radiance. Rough glass blurs this channel in screen
        /// space, which is what gives frosted glass without extra refraction rays.
        /// </summary>
        [ShaderResource] public RWTexture2D<float4> refractionTexture;

        /// <summary>All SDF objects in the scene.</summary>
        [ShaderResource] public StructuredBuffer<SDFObjectDTO> sdfObjects;

        /// <summary>All lights in the scene.</summary>
        [ShaderResource] public StructuredBuffer<SDFLightDTO> lights;

        /// <summary>Packed RGBA8 texture data.</summary>
        [ShaderResource] public Buffer<uint> textureData;

        /// <summary>Metadata for each texture.</summary>
        [ShaderResource] public StructuredBuffer<TextureMetadata> textureMetadata;

        /// <summary>All baked terrain heightmaps, packed (height, dHdX, dHdZ, ridge01).</summary>
        [ShaderResource] public Buffer<float4> terrainHeightData;

        /// <summary>Toroidal chunk identity records for streamed terrain quadtrees.</summary>
        [ShaderResource] public Buffer<int4> terrainChunkInfo;

        /// <summary>Metadata for each terrain.</summary>
        [ShaderResource] public StructuredBuffer<TerrainDTO> terrainMetadata;

        // Main constants
        const float EPSILON = 0.0001f;
        const float PI = 3.141592654f;
        const float RECIPROCAL_PI = 1f / PI;
        const float MIN_TRAVERSE_DIST = 100000000.0f;

        // Reflection constants
        const float MIN_REFLECTION_CHANCE = 0.01f;
        const float MIN_THROUGHPUT = 0.01f;
        const float REFLECTION_BIAS = 2f; // Multiplier for normal offset
        /// <summary>
        /// Maximum shadow-ray length passed for directional lights. Point lights pass the
        /// distance to the light instead, so their shadow rays stop at the light and do not
        /// get occluded by geometry behind it.
        /// </summary>
        const float SHADOW_RAY_UNLIMITED = 1e30f;

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
        /// Samples the terrain at a position relative to the terrain origin. Walks
        /// the quadtree from the camera-distance LOD down to coarser levels until a
        /// streamed chunk is found, then reconstructs the surface with bilinear or
        /// Catmull-Rom filtering. Outside every loaded chunk an infinite terrain
        /// falls back to the cheap procedural base height, so it is never empty.
        /// </summary>
        /// <returns>(height, dHdX, dHdZ, ridge01); <paramref name="found"/> is 0 when
        /// a bounded terrain has no surface at this position.</returns>
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

                // Fade the outermost ring to sea level so the clipmap does not end
                // in a visible cliff. This is deliberately cheap: adding the
                // procedural base height here would duplicate the whole FBM (three
                // noise octaves) at every terrain sample in the raymarcher. The fade
                // applies to every interpolation mode, otherwise bilinear mode would
                // end at a raw height-map cliff while Hermite fades out.
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

            // No streamed chunk: bounded terrains end here.
            if (meta.worldSpace == 0)
            {
                found = 0;
                return new float4(0f, 0f, 0f, 0f);
            }

            // Infinite terrain without a loaded chunk: a flat sea-level plane keeps
            // the world solid while the quadtree streams in. (Evaluating the base
            // FBM here would inline the whole noise function into every terrain
            // sample of the raymarcher and dominate shader compile time.)
            found = 1;
            return new float4(0f, 0f, 0f, 0.5f);
        }

        private float ObjectSDF(float3 pt, SDFObjectDTO obj)
        {
            float3 curPoint = pt - obj.position;
            // obj.rotation is local->world, so the world->local transform is its inverse.
            curPoint = ShaderMath.InverseRotateVector(curPoint, obj.rotation);
            curPoint *= obj.scaling;

            if (obj.type == 9) // Terrain heightfield
            {
                if (obj.terrainDataIndex < 0 || obj.terrainDataIndex >= terrainMetadata.Length)
                    return 1e8f;

                TerrainDTO meta = terrainMetadata[obj.terrainDataIndex];

                // Fixed terrains are a heightfield clipped to a square prism. Outside
                // the square the heightfield lookup would miss the chunk entirely, so
                // the sample point is clamped to the edge and the XZ rectangle
                // distance supplies the solid's exterior distance.
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
                    // Close the slab at the bottom so a bounded terrain reads as a
                    // floating block instead of an infinitely deep curtain.
                    distance = math.max(distance, -meta.heightScale * 3.0f - curPoint.y);
                }

                return distance;
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

        /// <summary>
        /// Terrain surface sample used for both shading and the analytic normal. It
        /// reads the height field exactly once per hit, which keeps the inlined
        /// shader graph (and its compile time) under control.
        /// </summary>
        private void TerrainSurface(SDFObjectDTO sdf, TerrainDTO meta, float3 localPos,
            out float3 albedo, out float3 normal, out float roughness, out float ambientOcclusion)
        {
            // The clipped sides of a bounded terrain are a layered rock section.
            if (meta.worldSpace == 0)
            {
                float sideX = math.abs(localPos.x) - meta.size;
                float sideZ = math.abs(localPos.z) - meta.size;
                if (sideX > -0.05f || sideZ > -0.05f)
                {
                    float3 wallNormal = math.abs(sideX) < math.abs(sideZ)
                        ? new float3(math.sign(localPos.x), 0f, 0f)
                        : new float3(0f, 0f, math.sign(localPos.z));
                    normal = math.normalize(ShaderMath.RotateVector(wallNormal, sdf.rotation));

                    // Wavy sedimentary bands: a slow horizontal warp on the layer
                    // frequency reads as tilted strata instead of ruler stripes.
                    float warp = math.sin(localPos.x * 0.011f + 1.3f) * 0.9f
                        + math.sin(localPos.z * 0.017f + 0.4f) * 0.7f;
                    float band = 0.5f + 0.5f * math.sin(localPos.y * 0.22f + warp);
                    albedo = math.lerp(new float3(0.17f, 0.17f, 0.18f),
                        new float3(0.38f, 0.36f, 0.33f), band);
                    float depth = math.saturate((localPos.y + meta.heightScale * 3f) / (meta.heightScale * 6f));
                    albedo *= 0.7f + 0.3f * depth;
                    roughness = 0.95f;
                    ambientOcclusion = 0.8f;
                    return;
                }
            }

            float4 terrainSample = SampleTerrainData(meta, localPos.xz, out int found);
            if (found == 0)
            {
                albedo = sdf.color.rgb;
                normal = new float3(0f, 1f, 0f);
                roughness = 1f;
                ambientOcclusion = 1f;
                return;
            }

            normal = ShaderMath.RotateVector(TerrainRendering.HeightfieldNormal(terrainSample), sdf.rotation);

            float2 slope = new float2(terrainSample.y, terrainSample.z);
            float normalY = 1.0f / math.sqrt(1.0f + math.dot(slope, slope));
            float altitude = math.saturate(terrainSample.x / math.max(meta.heightScale * 1.6f, 1e-3f) * 0.5f + 0.5f);

            // The ridge map doubles as an occlusion/provenance term: ridges are
            // exposed, creases collect detritus. Using it instead of re-evaluating
            // the un-eroded base keeps the shader small.
            float erosion = (terrainSample.w - 0.5f) * 2.0f;
            float occlusion = math.saturate(terrainSample.w * 1.2f);

            // Blend the material tint toward the reference's mossy olive so the
            // default forest-green material does not read as neon.
            float3 grassColor = math.lerp(sdf.color.rgb, new float3(0.26f, 0.30f, 0.12f), 0.55f);
            float grassMix = math.saturate((1.0f - terrainSample.w) * 1.4f + (0.5f - erosion) * 0.6f + occlusion * 0.2f);
            float3 grass = math.lerp(grassColor * 0.55f, grassColor * 1.25f, grassMix);

            float3 rock = math.lerp(new float3(0.14f, 0.16f, 0.20f), new float3(0.32f, 0.35f, 0.40f),
                math.saturate(terrainSample.w * 1.3f - 0.15f));
            float3 dirt = new float3(0.36f, 0.31f, 0.24f);
            float3 snow = new float3(0.93f, 0.95f, 0.98f);

            float steep = math.saturate((0.86f - normalY) * 3.2f);
            float3 color = math.lerp(grass, dirt, math.saturate(steep * 0.55f));
            color = math.lerp(color, rock, steep);

            float snowLine = 0.72f;
            float snowAmount = math.smoothstep(snowLine, snowLine + 0.1f, altitude + normalY * 0.05f - 0.02f);
            color = math.lerp(color, snow, snowAmount);

            float drainage = math.saturate((1.0f - math.saturate(terrainSample.w / 0.34f)) * 1.5f);
            color = math.lerp(color, new float3(0.045f, 0.07f, 0.06f), drainage * 0.65f);

            color *= 0.62f + 0.38f * occlusion;

            albedo = color;
            roughness = math.lerp(math.max(sdf.roughness, 0.6f), 0.95f, snowAmount);

            // Terrain self-occlusion. The engine's screen-space AO estimates the
            // second-closest object and therefore cannot occlude an object with
            // itself; the baked ridge map is exactly the missing terrain
            // self-occlusion term (creases shadowed, ridges exposed).
            ambientOcclusion = math.saturate(0.40f + 0.78f * terrainSample.w)
                * (0.78f + 0.22f * altitude);
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
        private float3 SoftShadow(float3 pt, float3 dir, uint excludeID, float maxDistance)
        {
            float depth, dist, shadow = 1;
            int maxShadowRaySteps = worldData[0].maxShadowRaySteps;
            bool limited = maxDistance < SHADOW_RAY_UNLIMITED;
            for (int k = 0; k < sdfObjects.Length; k++)
            {
                SDFObjectDTO sdf = sdfObjects[k];
                if (sdf.shadowEffects.x == 0) continue;
                if (sdf.entityID == excludeID && sdf.type != 9) continue;

                float rayEnd = limited
                    ? math.min(sdf.shadowDistances.y, maxDistance)
                    : sdf.shadowDistances.y;
                depth = sdf.shadowDistances.x;
                for (int i = 0; i < maxShadowRaySteps; ++i)
                {
                    // Point lights stop at the light itself. The extra check keeps the
                    // directional path bit-for-bit identical (it still samples at the
                    // far boundary, as it always did).
                    if (limited && depth > rayEnd) break;
                    dist = ObjectSDF(pt + depth * dir, sdf);
                    if (depth > rayEnd) break;
                    shadow = math.min(shadow, sdf.shadowDistances.z * dist / depth);
                    depth += math.clamp(dist, 0.025f, 100f); // Larger minimum step than 0.01
                }
            }
            shadow = math.max(shadow, -1f);
            return math.saturate(math.smoothstep(-1f, 0f, shadow)) * new float3(1f);
        }

        private float3 SoftShadowColored(float3 pt, float3 dir, uint excludeID, float maxDistance)
        {
            float shadow = 1f;
            float3 tint = new float3(1f);
            float tintShadow = 1f;
            int maxShadowRaySteps = worldData[0].maxShadowRaySteps;
            bool limited = maxDistance < SHADOW_RAY_UNLIMITED;
            for (int k = 0; k < sdfObjects.Length; k++)
            {
                SDFObjectDTO sdf = sdfObjects[k];
                if (sdf.shadowEffects.x == 0) continue;
                if (sdf.entityID == excludeID && sdf.type != 9) continue;

                bool isGlass = sdf.hasRefraction == 1;
                float depth = sdf.shadowDistances.x;
                float rayEnd = limited
                    ? math.min(sdf.shadowDistances.y, maxDistance)
                    : sdf.shadowDistances.y;
                float3 absorptionCoefficient = isGlass ? math.log(math.max(sdf.absorptionColor.rgb, 0.001f)) : new float3(0f);
                for (int i = 0; i < maxShadowRaySteps; ++i)
                {
                    if (depth > rayEnd) break;
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
        private float3 HardShadow(float3 pt, float3 dir, uint excludeID, float maxDistance)
        {
            float depth, dist;
            int maxShadowRaySteps = worldData[0].maxShadowRaySteps;
            bool limited = maxDistance < SHADOW_RAY_UNLIMITED;
            for (int k = 0; k < sdfObjects.Length; k++)
            {
                SDFObjectDTO sdf = sdfObjects[k];
                depth = sdf.shadowDistances.x;
                if (sdf.shadowEffects.x == 0) continue;
                if (sdf.entityID == excludeID && sdf.type != 9) continue;
                float rayEnd = limited
                    ? math.min(sdf.shadowDistances.y, maxDistance)
                    : sdf.shadowDistances.y;
                for (int i = 0; i < maxShadowRaySteps; ++i)
                {
                    // See SoftShadow: point lights stop before sampling past the light,
                    // directional lights keep the original far-boundary behaviour.
                    if (limited && depth >= rayEnd) break;
                    dist = ObjectSDF(pt + depth * dir, sdf);
                    if (dist < EPSILON) return 0f;
                    if (depth >= rayEnd) break;
                    depth += dist;
                }
            }
            return 1f; // Max iterations reached, assume no shadow
        }

        /// <summary>
        /// Unified shadow function that selects the appropriate shadow type.
        /// </summary>
        private float3 CalculateShadow(float3 hitPoint, float3 normal, float3 lightDir, int shadowType,
            uint excludeID, float shadowBias, float maxDistance)
        {
            // Terrain passes a larger bias: its height field can shadow itself, and
            // a sub-millimetre offset would produce self-intersection acne.
            float3 shadowOrigin = hitPoint + normal * shadowBias;
            switch (shadowType)
            {
                case 0: // Hard shadows
                    return HardShadow(shadowOrigin, lightDir, excludeID, maxDistance);
                case 1: // Soft shadows
                    return SoftShadow(shadowOrigin, lightDir, excludeID, maxDistance);
                case 2: // Colored shadows
                    return SoftShadowColored(shadowOrigin, lightDir, excludeID, maxDistance);
                case 3: // Caustic shadows (refracted transmission + focus)
                    return CalculateCausticShadow(hitPoint, normal, lightDir, 0, excludeID, maxDistance);
                default:
                    return SoftShadow(shadowOrigin, lightDir, excludeID, maxDistance);
            }
        }

        /// <summary>
        /// Calculate lighting contribution from all lights
        /// </summary>
        private float3 CalculateLighting(float3 hitPoint, float3 geoNormal, float3 finalNormal, float3 viewDir,
            int2 shadowEffects, int shadowType, float specular, float reflectance,
            float3 baseCol, float metallic, float roughAlpha, uint excludeID, float shadowBias,
            int2 pixel)
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
                    if (shadowEffects.y != 0) lightShadow = CalculateShadow(hitPoint, geoNormal, lightDir, shadowType, excludeID, shadowBias, SHADOW_RAY_UNLIMITED);
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

                    // Calculate shadow for point light. The ray must stop at the light:
                    // without this, any object behind the light is marched into and
                    // darkens the surface, which looks like the point light casting
                    // directional shadows from the far side of the scene.
                    if (shadowEffects.y != 0 && distance < light.radius * 2f)
                        lightShadow = CalculateShadow(hitPoint, geoNormal, lightDir, shadowType, excludeID, shadowBias, distance);

                    // Apply shadow to the entire BRDF result
                    float3 brdf = PBR.BRDFMicrofacetFunction(lightDir, viewDir,
                        finalNormal, baseCol, metallic, roughAlpha, specular, reflectance, RECIPROCAL_PI, EPSILON);
                    totalLight += brdf * lightColor * NoL * lightShadow;
                }
                else if (light.type == 2 || light.type == 3) // Area light (spherical / rectangular)
                {
                    totalLight += AreaLightContribution(light, hitPoint, viewDir, finalNormal, baseCol,
                        metallic, roughAlpha, specular, reflectance, shadowEffects.y != 0, shadowBias,
                        excludeID, i, pixel);
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
        private float3 CalculateCausticShadow(float3 hitPoint, float3 normal, float3 lightDir, int traceDepth,
            uint excludeID, float maxDistance)
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
                    if (sdf.entityID == excludeID && sdf.type != 9) continue;

                    float td = sdf.shadowDistances.x;
                    float rayEnd = math.min(sdf.shadowDistances.y, maxDistance);
                    for (int i = 0; i < maxShadowRaySteps; i++)
                    {
                        if (td > rayEnd) break;
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

        #region area_lights

        // -----------------------------------------------------------------------------
        // Area lights
        //
        // Reference: R. Ban, Cs. Balint and G. Valasek, "Area Lights in Signed Distance
        // Function Scenes", Eurographics 2019 short paper.
        //
        // Algorithm 2 of the paper (the minimal tangent cone search) is implemented 1:1 in
        // MinimalTangentCone below: a cone is built from the shaded point towards the
        // light's bounding sphere, and sphere tracing along its axis shrinks the cone
        // around the closest surface until the relative cone radius m stops decreasing.
        // m = 1 means nothing occludes the cone; m <= 0 means the surface crosses the axis.
        //
        // Spherical lights then use the paper's closed form (Section 4.1), which maps m
        // through smoothstep(0, 1, m/2 + 1/2) == ((m/2 + 1/2)^2 * (2 - m)). Rectangular
        // lights use the paper's numerical integration (Section 4.2): the largest occluder
        // found by the cone search becomes a planar proxy, so every light sample can be
        // tested with one dot product instead of a second shadow trace.
        //
        // Nothing here is shared with CalculateShadow: the penumbra and coloured shadow
        // styles of the point and directional lights keep their own code path untouched.
        // -----------------------------------------------------------------------------

        /// <summary>
        /// Distance to the closest shadow-casting surface, i.e. the paper's f. Objects that
        /// do not cast shadows are skipped, and so is the receiving object (except terrain,
        /// which has to be able to shadow itself).
        /// </summary>
        private float ShadowCasterSDF(float3 pt, uint excludeID, out int closest)
        {
            float minDist = MIN_TRAVERSE_DIST;
            closest = -1;
            for (int k = 0; k < sdfObjects.Length; k++)
            {
                SDFObjectDTO sdf = sdfObjects[k];
                if (sdf.shadowEffects.x == 0) continue;
                if (sdf.entityID == excludeID && sdf.type != 9) continue;

                float dist = ObjectSDF(pt, sdf);
                if (dist < minDist)
                {
                    minDist = dist;
                    closest = k;
                }
            }
            return minDist;
        }

        /// <summary>
        /// Algorithm 2 of the paper: the minimal relative cone radius seen along a shadow
        /// cone's axis. The cone is shrunk every time a surface enters it, so the march
        /// skips the parts of the scene the shrunken cone no longer covers.
        /// </summary>
        /// <param name="origin">Cone apex (the offset shaded point).</param>
        /// <param name="dir">Unit direction of the cone axis, towards the light.</param>
        /// <param name="tanAlpha0">Tangent of the cone's original half opening angle.</param>
        /// <param name="tMax">Axis length to march, where the cone reaches the light.</param>
        /// <param name="epsilon">The paper's accuracy parameter.</param>
        /// <param name="excludeID">Receiving object, excluded from the caster set.</param>
        /// <param name="occluderObject">Index of the largest occluder found, -1 when none.</param>
        /// <param name="occluderT">Axis parameter of the closest point on that occluder.</param>
        /// <returns>m clamped to [-1, 1]; 1 = unobstructed, 0 = the surface touches the axis.</returns>
        private float MinimalTangentCone(float3 origin, float3 dir, float tanAlpha0, float tMax,
            float epsilon, uint excludeID, out int occluderObject, out float occluderT)
        {
            occluderObject = -1;
            occluderT = 0f;

            float m = 1f;
            float r0 = 0f;              // cone radius at the apex: the cone starts as a point
            float tanAlpha = tanAlpha0; // current (shrunken) cone half angle
            int maxSteps = worldData[0].maxShadowRaySteps;
            float minStep = math.max(tMax * 1e-4f, EPSILON * 10f);
            // Never sample the apex itself: the surface passes through it and a zero distance
            // there would read as a fully occluded cone.
            float t = minStep;
            // The paper's epsilon budgets how far the march may skip past a local minimum, so
            // it doubles as the over-relaxation of the accelerated sphere tracing step below.
            float relaxation = 1f + math.max(epsilon, 0f);

            for (int i = 0; i < maxSteps; i++)
            {
                if (t >= tMax) break;

                float f = ShadowCasterSDF(origin + dir * t, excludeID, out int closest);
                float coneRadius = t * tanAlpha0;

                // The occluder proxy is the first place the cone touches a surface: the plane
                // through that contact blocks exactly the radiance behind it, which is the
                // paper's edge-like assumption. (The deepest point would sit inside the
                // occluder once the cone is fully blocked.)
                if (occluderObject < 0 && f < coneRadius && closest >= 0)
                {
                    occluderObject = closest;
                    occluderT = t;
                }

                // m(t) = f / (r0 + t*tan(a0)): the relative cone radius. The paper's search is
                // the minimum of m along the axis, so every sample is a candidate.
                float mNew = math.clamp(f / math.max(coneRadius, EPSILON), -1f, 1f);
                if (mNew < m)
                {
                    m = mNew;
                    // The surface fully crosses the cone: no shading term can recover from it.
                    if (m <= -1f) break;
                }

                // Pocket the current minimum: shrinking the cone so it no longer intersects
                // the surface here bounds how much of a later minimum the march can skip.
                float coneDist = f - r0 - t * tanAlpha;
                if (coneDist < epsilon * coneRadius)
                {
                    float shrink = math.max(m - epsilon, 0f);
                    tanAlpha = shrink * tanAlpha0;
                }

                // Accelerating Sphere Tracing (Balint & Valasek 2018) steps by the cone
                // condition divided by its Lipschitz constant; plain sphere tracing (|f|)
                // keeps narrow cones from crawling, and |f| penetrates an occluder once the
                // cone is blocked, which is what lets m go negative (a fully hidden light).
                float surfaceStep = f >= 0f ? f : -f;
                float stepSize = math.max(coneDist / (1f + tanAlpha), surfaceStep) * relaxation;
                t += math.max(stepSize, minStep);
            }

            return m;
        }

        /// <summary>
        /// Direct illumination from an area light: one minimal tangent cone search plus the
        /// paper's shading for the light's shape. Section 4.1 for spherical emitters (closed
        /// form visibility), Section 4.2 for rectangular ones (numerical integration of
        /// Equation 1 against a planar proxy of the largest occluder).
        /// </summary>
        /// <remarks>
        /// Both shapes share this single entry point on purpose: the cone search contains the
        /// inlined scene SDF, so giving each shape its own copy of it roughly doubles the
        /// front-end cost of the already very large raymarch shader.
        /// The paper samples directions inside the cone towards the bounding sphere and tests
        /// whether they hit the rectangle; sampling the rectangle's area instead integrates
        /// the same estimate with far less variance, which is what keeps the penumbra clean at
        /// the low sample counts an editor frame can afford.
        /// </remarks>
        private float3 AreaLightContribution(SDFLightDTO light, float3 hitPoint, float3 viewDir,
            float3 finalNormal, float3 baseCol, float metallic, float roughAlpha, float specular,
            float reflectance, bool traceShadows, float shadowBias, uint excludeID, int lightIndex,
            int2 pixel)
        {
            float3 forward = math.normalize(ShaderMath.RotateVector(new float3(0f, 0f, -1f), light.rotation));
            float3 lightNormal = -forward; // emission direction: away from the lit side, into the scene
            float3 axisX = ShaderMath.RotateVector(new float3(1f, 0f, 0f), light.rotation);
            float3 axisY = ShaderMath.RotateVector(new float3(0f, 1f, 0f), light.rotation);

            bool isRect = light.type == 3;
            float2 halfExtent = new float2(math.max(light.size.x, 1e-4f), math.max(light.size.y, 1e-4f));
            float3 toCenter = light.position - hitPoint;
            float centerDistance = math.length(toCenter);
            if (centerDistance <= 1e-4f) return new float3(0f);
            float3 axisDir = toCenter / centerDistance;

            // Cone tangent to the emitter's bounding sphere: the sphere's radius for a
            // spherical light, the rectangle's half diagonal for a rectangular one.
            float emitterRadius = isRect ? math.length(halfExtent) : math.max(light.radius, EPSILON);
            float3 origin = hitPoint + finalNormal * shadowBias;
            float tMax = math.sqrt(math.max(centerDistance * centerDistance - emitterRadius * emitterRadius, EPSILON));

            float m = 1f;
            int occluderObject = -1;
            float occluderT = 0f;
            if (traceShadows && centerDistance > emitterRadius * 1.01f)
                m = MinimalTangentCone(origin, axisDir, emitterRadius / tMax, tMax, light.epsilon,
                    excludeID, out occluderObject, out occluderT);

            if (!isRect)
            {
                // Section 4.1: smoothstep(0, 1, m/2 + 1/2) is identically
                // ((m/2 + 1/2)^2 * (2 - m)), the closed form the paper uses in place of the
                // exact circular integral of the visibility.
                float visibility = traceShadows ? math.smoothstep(0f, 1f, 0.5f + 0.5f * m) : 1f;
                float attenuation = math.rcp(math.max(centerDistance * centerDistance, EPSILON));
                if (light.range > 0f) attenuation *= math.saturate(1f - centerDistance / light.range);
                float NoL = math.max(math.dot(finalNormal, axisDir), 0f);
                if (NoL <= 0f || attenuation <= 0f) return new float3(0f);
                float3 brdf = PBR.BRDFMicrofacetFunction(axisDir, viewDir, finalNormal, baseCol,
                    metallic, roughAlpha, specular, reflectance, RECIPROCAL_PI, EPSILON);
                return brdf * (light.color.rgb * (light.intensity * attenuation * NoL * visibility));
            }

            // Section 4.2: the largest occluder found by the cone search becomes a plane, so
            // each light sample costs one dot product instead of another shadow trace.
            bool hasOccluder = occluderObject >= 0 && m < 1f;
            float3 occluderPoint = origin;
            float3 occluderNormal = new float3(0f, 1f, 0f);
            if (hasOccluder)
            {
                occluderPoint = origin + axisDir * occluderT;
                occluderNormal = FastNormalSingleObject(occluderPoint, sdfObjects[occluderObject]);
            }

            int samples = (int)math.clamp((float)light.samples, 1f, 64f);
            float3 sum = new float3(0f);
            for (int s = 0; s < samples; s++)
            {
                // Low-discrepancy sample over the rectangle, offset per pixel and per light
                // so neighbouring pixels do not use the same set of points.
                int sampleIndex = pixel.x * 73 + pixel.y * 9277 + lightIndex * 3571 + s * 1237 + 17;
                float2 u = PBR.Halton2D(sampleIndex);
                float3 samplePoint = light.position
                    + axisX * ((u.x * 2f - 1f) * halfExtent.x)
                    + axisY * ((u.y * 2f - 1f) * halfExtent.y);

                float3 toSample = samplePoint - hitPoint;
                float distanceSquared = math.dot(toSample, toSample);
                if (distanceSquared <= 1e-8f) continue;
                float3 lightDir = toSample / math.sqrt(distanceSquared);

                float cosLight = -math.dot(lightNormal, lightDir);
                if (cosLight <= 0f) continue;
                float NoL = math.dot(finalNormal, lightDir);
                if (NoL <= 0f) continue;

                float visibility = 1f;
                if (hasOccluder)
                {
                    // Planar/edge proxy of the largest occluder: a light sample is hidden
                    // when it sits behind the plane through the closest approach point.
                    visibility = math.dot(samplePoint - occluderPoint, occluderNormal) > 0f ? 1f : 0f;
                }

                float falloff = distanceSquared;
                if (light.range > 0f)
                {
                    float rangeFactor = math.saturate(1f - math.sqrt(distanceSquared) / light.range);
                    if (rangeFactor <= 0f) continue;
                    falloff = distanceSquared / rangeFactor;
                }

                float3 brdf = PBR.BRDFMicrofacetFunction(lightDir, viewDir, finalNormal, baseCol,
                    metallic, roughAlpha, specular, reflectance, RECIPROCAL_PI, EPSILON);
                sum += brdf * (visibility * cosLight * NoL / falloff);
            }

            // intensity is a power-like scale (see AreaLight), so it replaces the emitter
            // radiance times its area: the light keeps the brightness of a point light of
            // the same intensity while its extent only softens the shadow.
            return sum * (light.color.rgb * (light.intensity / (float)samples));
        }

        #endregion area_lights

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

            // Refracted pixels receive no shadows here (the second shadow flag is 0), so the
            // area light sample pattern is deliberately pixel independent: one coherent set
            // of rectangle samples behind glass instead of per-pixel noise.
            float3 directLight = CalculateLighting(hitPoint, normal, finalNormal, viewDir,
                new int2(sdf.shadowEffects.x, 0), sdf.shadowType, sdf.specular, sdf.reflectance, albedo, metallic, roughAlpha,
                sdf.entityID, EPSILON * REFLECTION_BIAS, new int2(0, 0));
            float3 ambientLight = ambientBase * aoValue;
            finalColor += ambientLight + directLight;
            return finalColor;
        }

        /// <summary>
        /// Traces a primary ray and returns the base shading, the stochastic reflection
        /// contribution and the refraction contribution as separate linear radiance
        /// values. The denoiser filters only the two secondary channels and adds them
        /// back to the base, which keeps albedo, textures and direct lighting sharp.
        /// </summary>
        private float3 TraceRay(int2 pixel, float3 rayOrigin, float3 rayDir, int sampleIndexInPixel,
            out float3 outputNormal, out float totalDist, out int steps,
            out float3 baseColor, out float3 reflectionColor, out float3 refractionColor,
            out float primaryRoughness, out float primaryMetallic, out int primaryFlags,
            out float reflectionWeight)
        {
            float3 firstHitLighting = new float3(0f);
            float3 secondaryLighting = new float3(0f);
            float3 contribution = new float3(1f);
            float3 ambientBase = new float3(0f);
            float cosTheta = 0f;
            float primaryCosTheta = 0f;

            float3 refractedLight = new float3(0f);
            float hitTerrainAO = 1f;
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

            baseColor = new float3(0f);
            reflectionColor = new float3(0f);
            refractionColor = new float3(0f);
            primaryRoughness = 0f;
            primaryMetallic = 0f;
            primaryFlags = 0;
            reflectionWeight = 0f;

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
                    if (firstHit)
                    {
                        // Primary ray escaped: the whole pixel is sky.
                        firstHitLighting += contribution * sky;
                        totalDist = depth;
                    }
                    else secondaryLighting += contribution * sky;
                    break;
                }

                SDFObjectDTO sdf = sdfObjects[closestObjIndex];
                bool isPrimaryHit = firstHit;
                float3 normal;
                float3 albedo;
                float metallic;
                float roughness;
                float3 finalNormal;
                float3 viewDir = -rayDir;

                if (sdf.type == 9 && sdf.terrainDataIndex >= 0 && sdf.terrainDataIndex < terrainMetadata.Length)
                {
                    // Terrain reads its height field once and derives both the normal
                    // and the material from that sample.
                    TerrainDTO terrainMeta = terrainMetadata[sdf.terrainDataIndex];
                    float3 localPoint = ShaderMath.InverseRotateVector(hitPoint - sdf.position, sdf.rotation);
                    TerrainSurface(sdf, terrainMeta, localPoint, out albedo, out normal, out roughness, out float terrainAO);
                    metallic = 0f;
                    hitTerrainAO = terrainAO;
                    finalNormal = normal;
                    if (math.dot(normal, rayDir) > 0f) normal = -normal;
                    finalNormal = normal;
                }
                else
                {
                    normal = FastNormalSingleObject(hitPoint, sdf);
                    if (math.dot(normal, rayDir) > 0f) normal = -normal;
                    SampleSurfaceMaterial(hitPoint, normal, sdf, depth,
                        out _, out _, out albedo, out metallic, out roughness, out finalNormal);
                }
                float roughAlpha = roughness * roughness;

                float aoStrength = sdf.aoValues.x;
                cosTheta = math.max(math.dot(normal, viewDir), 0f);

                if (isPrimaryHit)
                {
                    outputNormal = normal;
                    totalDist = depth;
                    entityIdBuffer[pixel.x + pixel.y * (int)width] = new uint2((uint)closestObjIndex, sdf.entityID);
                    mainMat = sdf;
                    primaryCosTheta = cosTheta;

                    // Per-pixel material data for the denoiser. Roughness comes from the
                    // roughness texture, so filtering tracks the smooth areas of a textured
                    // material instead of a single value for the whole object.
                    primaryRoughness = roughness;
                    primaryMetallic = metallic;
                    primaryFlags = (sdf.hasReflection == 1 ? 1 : 0)
                        | (sdf.hasRefraction == 1 ? 2 : 0)
                        | 4; // exact path: reflection/refraction channels are valid

                    // AO
                    ambientBase = math.lerp(albedo, worldData[0].skyColor.rgb, worldData[0].ambientStrength) * worldData[0].ambientStrength;
                    if (aoStrength > 0f) aoValue = math.lerp(1f, CalculateAO(hitPoint, normal, sdf.entityID, sdf.aoValues), aoStrength);
                    if (sdf.type == 9) aoValue = math.min(aoValue, hitTerrainAO);

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

                float shadowBias = sdf.type == 9 ? 0.15f : EPSILON * REFLECTION_BIAS;
                float3 directLight = CalculateLighting(hitPoint, normal, finalNormal, viewDir,
                    sdf.shadowEffects, sdf.shadowType, sdf.specular, sdf.reflectance, albedo, metallic, roughAlpha,
                    sdf.entityID, shadowBias, pixel);
                // Ambient is scaled by AO only. The previous line overwrote aoValue with the
                // *direct* light magnitude whenever it exceeded 0.5, which made ambient jump back
                // up exactly where the shadow term pulled directLight down: a bright band hugging
                // the penumbra ("lighter area outside the shadow"). It also let ambient exceed 1.
                float3 ambientLight = ambientBase * aoValue;

                if (bounce == 0) surfaceColor = directLight;

                // First hit: stable shading that the denoiser leaves untouched. Later
                // bounces: stochastic reflection radiance that the denoiser filters.
                if (isPrimaryHit) firstHitLighting += contribution * (ambientLight + directLight);
                else secondaryLighting += contribution * (ambientLight + directLight);

                if (sdf.hasReflection == 0) break;
                if (bounce == sdf.reflectionMaxBounces - 1) break;

                maxRaySteps = (int)(maxRaySteps / sdf.reflectRayStepFalloff);

                // Metallic/roughness maps now actually drive this:
                float3 f0Refl = math.lerp(sdf.f0_reflectance, albedo, new float3(metallic, metallic, metallic));
                float3 F = PBR.FresnelSchlick(math.max(math.dot(finalNormal, viewDir), 0f), f0Refl, sdf.reflectance, 1f);
                float reflectionChance = math.lerp(F.x, 1f, metallic);

                if (bounce == 0 && isRefractive) reflectionChance = fresnelFactor;
                if (reflectionChance < MIN_REFLECTION_CHANCE) break;

                if (bounce == 0) reflectionWeight = math.max(F.x, math.max(F.y, F.z)) * (1f - roughness * 0.5f);

                contribution *= F * (1f - roughness * 0.5f);
                contribution = math.min(contribution, 10f);
                if (contribution.x + contribution.y + contribution.z < MIN_THROUGHPUT) break;

                // Reflection budget: above this material's cutoff the lobe is too wide for
                // one ray to sample usefully, so spend no raymarch steps and add nothing.
                // The base ambient/direct shading already carries the energy; sampling the
                // sky here previously made rough surfaces look like a bright skybox and
                // double-counted the ambient term.
                if (roughness > sdf.reflectionCutoff) break;
                if (bounce == 0) primaryFlags |= 8; // reflection was actually ray traced

                int reflectionSampleIndex = pixel.x * 73 + pixel.y * 9277 + frameCount * 1973 + sampleIndexInPixel * 3271 + bounce * 997;
                float2 u = PBR.Halton2D(reflectionSampleIndex);

                // Perturb with the bumped normal, not the geometric one -> normal maps now visible in reflections
                float3 halfVector = PBR.ImportanceSampleGGX(u, finalNormal, roughAlpha, PI);
                float3 reflectDir = math.normalize(math.reflect(rayDir, halfVector));
                if (math.dot(reflectDir, normal) < 0.01f) break; // test vs geometric normal, avoids self-intersection

                rayOrigin = hitPoint + normal * EPSILON * REFLECTION_BIAS; // offset along geometric normal, stays safe
                rayDir = reflectDir;
            }

            if (isRefractive)
            {
                // Proper Fresnel using Schlick. This must use the primary hit's angle:
                // cosTheta is overwritten on every reflection bounce, and using the last
                // bounce's angle made the glass split (and therefore its base channel)
                // depend on the stochastic reflection direction.
                float3 f0 = new float3(1f) * mainMat.f0_dielectric;
                float3 fresnel = PBR.FresnelSchlick(primaryCosTheta, f0, mainMat.reflectance, 1f).x;
                baseColor = firstHitLighting * fresnel;
                reflectionColor = secondaryLighting * fresnel;
                refractionColor = refractedLight * (1f - fresnel);
            }
            else
            {
                // `surfaceColor` is the direct light at the first hit; the engine has always
                // added it a second time at the end for non-refractive surfaces.
                baseColor = firstHitLighting + surfaceColor;
                reflectionColor = secondaryLighting;
                refractionColor = new float3(0f);
            }

            return baseColor + reflectionColor + refractionColor;
        }

        // --------------------
        // Cone-traced supersampling (CTSS)
        // --------------------

        /// <summary>
        /// CTSS assumes f is a true signed distance field (‖∇f‖ = 1). The iterated
        /// fractals only provide conservative distance estimates; tracing a cone against
        /// them finds false partial hits and shades the wrong surface (the Menger sponge
        /// loses its holes entirely at one-pixel cone coverage). Those objects fall back
        /// to the ordinary single-sample trace. Terrain and ordinary primitives are fine.
        /// </summary>
        private bool IsApproximateObject(int objectIndex)
        {
            if (objectIndex < 0 || objectIndex >= sdfObjects.Length) return true;

            SDFObjectDTO obj = sdfObjects[objectIndex];
            if (obj.type == 10 || obj.type == 11 || obj.type == 12) return true;

            // Non-uniform object scaling turns the local distance into a conservative
            // estimate too. Uniform scaling (including the terrain's unit scale) is exact.
            float3 s = obj.scaling;
            float smallest = math.min(s.x, math.min(s.y, s.z));
            float largest = math.max(s.x, math.max(s.y, s.z));
            return largest > smallest * 1.02f;
        }

        /// <summary>
        /// Classifies a pixel by tracing its cone. Returns true when the cone only
        /// partially covers geometry: a soft-only hit group (the outside fringe of a
        /// silhouette) or a hard hit that exits without fully occluding the cone (a
        /// grazing chord along a large curved surface). Fully occluded interior pixels
        /// return false. Objects whose distance estimators are not Eikonal are never
        /// classified as edges, so their detail is left to the ordinary trace.
        /// </summary>
        private bool ConeEdgePixel(float3 rayOrigin, float3 rayDir)
        {
            float tanTheta = math.max(ctssConeCoverage, 0.05f) * 1.41421356f
                * worldData[0].camScreenDist / math.max(height, 1f);
            float farPlane = worldData[0].farPlane;
            int maxSteps = worldData[0].maxRaySteps;
            float t = math.max(worldData[0].nearPlane, EPSILON);
            bool inGroup = false;
            bool groupHasHard = false;

            for (int step = 0; step < maxSteps && t < farPlane; step++)
            {
                float d = WorldSDF(rayOrigin + rayDir * t, out int obj);
                float coneRadius = math.max(t * tanTheta, 1e-6f);
                float omega = (1f - d / coneRadius) * 0.5f;
                bool softHit = omega > 0f;
                bool fullOcclusion = omega >= 1f - 1e-4f;

                if (softHit)
                {
                    if (!inGroup)
                    {
                        inGroup = true;
                        groupHasHard = false;
                        if (IsApproximateObject(obj)) return false;
                    }
                    if (omega > 0.5f) groupHasHard = true;
                }

                if (inGroup && (!softHit || fullOcclusion))
                {
                    if (!groupHasHard || !fullOcclusion) return true;
                    // A hard hit that fully occludes the cone is an interior pixel;
                    // no later surface in this cone can be visible.
                    return false;
                }

                // The paper's adaptive minimum step, so a hard hit cannot stall tracing.
                float advance = math.max(d, coneRadius * 0.5f);
                if (advance < EPSILON) advance = EPSILON;
                t += advance;
            }

            return false;
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
        /// Writes the per-pixel cone-edge mask. This is a separate kernel so the large
        /// shading kernel below does not pay its register cost when CTSS is off.
        /// </summary>
        [Kernel(8, 8, 1)]
        public void ExecuteEdge(uint3 id)
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

            float2 uv = (new float2((float)pixel.x + 0.5f, (float)pixel.y + 0.5f)
                / new float2(width, height)) * 2f - 1f;
            float3 rayDir = ShaderMath.GetCameraRayDirNew(aspect, uv,
                worldData[0].camScreenDist, worldData[0].camForward, worldData[0].camRight, worldData[0].camUp);
            bool edge = ConeEdgePixel(worldData[0].cameraOrigin, rayDir);
            edgeMaskWrite[pixel] = new float4(edge ? 1f : 0f, 0f, 0f, 1f);
        }

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

            float3 accumulatedBase = new float3(0f);
            float3 accumulatedReflection = new float3(0f);
            float3 accumulatedRefraction = new float3(0f);
            float accumulatedRoughness = 0f;
            float accumulatedMetallic = 0f;
            float accumulatedReflectionWeight = 0f;
            int accumulatedFlags = 0;
            float3 accumulatedNormal = new float3(0f);
            float3 rayDir = new float3(0f);
            float accumulatedDistance = 0f;
            int steps = 0;
            int ctssSamples = 0;
            float ctssCoverage = 0f;

            // The primary renderer samples on pixel corners. Cone-traced pixels sample
            // through the pixel centre so the 2x2 subpixel grid is aligned with the
            // pixel footprint.
            float2 sampleUv = uv;
            if (enableCtss == 1 || sampleCenter == 1)
                sampleUv = (new float2((float)pixel.x + 0.5f, (float)pixel.y + 0.5f) / new float2(width, height)) * 2f - 1f;

            rayDir = ShaderMath.GetCameraRayDirNew(aspect, sampleUv,
                worldData[0].camScreenDist, worldData[0].camForward, worldData[0].camRight, worldData[0].camUp);

            // Cone tracing only classifies the pixel: partially covered pixels get the
            // 2x2 supersampling grid the paper pairs with CTSS, everything else keeps a
            // single sample. Keeping one TraceRay call site is important: this shader's
            // inlined shading graph is what made naive CTSS variants take an hour to
            // compile.
            bool ctssEdge = enableCtss == 1 && edgeMaskRead[pixel].r > 0.5f;
            bool subpixelGrid = enableCtss == 1 && ctssEdge;
            int sampleCount = enableCtss == 1 ? (ctssEdge ? 4 : 1) : math.max(samplesPerPixel, 1);
            ctssSamples = enableCtss == 1 ? sampleCount : 0;
            ctssCoverage = ctssEdge ? 1f : 0f;

            for (int sample = 0; sample < sampleCount; sample++)
            {
                float2 subUv = sampleUv;
                if (subpixelGrid)
                {
                    int sx = sample & 1;
                    int sy = sample >> 1;
                    subUv += new float2(((float)sx - 0.5f) / width, ((float)sy - 0.5f) / height);
                }

                float3 sampleDir = ShaderMath.GetCameraRayDirNew(aspect, subUv,
                    worldData[0].camScreenDist, worldData[0].camForward, worldData[0].camRight, worldData[0].camUp);

                // Automatically skip reflection bounces for non-reflective materials
                TraceRay(pixel, rayOrigin, sampleDir, sample,
                    out float3 outputNormal, out float dist, out int tracedSteps,
                    out float3 baseColor, out float3 reflectionColor, out float3 refractionColor,
                    out float roughness, out float metallic, out int flags, out float reflectionWeight);
                steps += tracedSteps;
                accumulatedBase += baseColor;
                accumulatedReflection += reflectionColor;
                accumulatedRefraction += refractionColor;
                accumulatedRoughness += roughness;
                accumulatedMetallic += metallic;
                accumulatedReflectionWeight += reflectionWeight;
                accumulatedFlags |= flags;
                accumulatedNormal += outputNormal;
                accumulatedDistance += dist;
            }

            float maxPossibleDistance = worldData[0].farPlane - worldData[0].nearPlane;
            float invSamples = 1f / (float)sampleCount;
            float3 baseRadiance = accumulatedBase * invSamples;
            float3 finalNormal = accumulatedNormal * invSamples;
            float finalDist = accumulatedDistance * invSamples;

            // The colour target holds only the stable base shading; the resolve pass adds
            // the denoised reflection/refraction channels back and applies the tonemapper.
            outputTexture[pixel] = new float4(baseRadiance, 1f);
            depthNormals[pixel] = new float4(finalDist / maxPossibleDistance, finalNormal);

            // Denoiser inputs. Roughness comes from the roughness texture at this pixel.
            materialBuffer[pixel] = new float4(
                accumulatedRoughness * invSamples,
                accumulatedMetallic * invSamples,
                (float)accumulatedFlags,
                accumulatedReflectionWeight * invSamples);
            reflectionTexture[pixel] = new float4(accumulatedReflection * invSamples, 1f);
            refractionTexture[pixel] = new float4(accumulatedRefraction * invSamples, 1f);

            // CTSS debug views are shown linearly, so they replace the base shading and the
            // resolve pass is skipped for them.
            if (enableCtss == 1 && ctssDebug == 1)
                outputTexture[pixel] = new float4(math.saturate((float)ctssSamples / 8f), math.saturate(ctssCoverage), 0f, 1f);
            else if (enableCtss == 1 && ctssDebug == 2)
                outputTexture[pixel] = new float4(ctssCoverage, ctssCoverage, ctssCoverage, 1f);

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
                            shadowColor = SoftShadow(shadowOrigin, worldData[0].mainLightDir, uint.MaxValue, SHADOW_RAY_UNLIMITED);
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
                else if (debugMode == 11) // Per-pixel roughness (from the material's roughness texture)
                {
                    float roughness = materialBuffer[pixel].x;
                    outputTexture[pixel] = new float4(roughness, roughness, roughness, 1);
                }
                else if (debugMode == 12) // Material flags: R = reflection, G = refraction, B = reflection was traced
                {
                    int flags = (int)materialBuffer[pixel].z;
                    outputTexture[pixel] = new float4(
                        (flags & 1) != 0 ? 1f : 0f,
                        (flags & 2) != 0 ? 1f : 0f,
                        (flags & 8) != 0 ? 1f : 0f,
                        1f);
                }
                else if (debugMode == 13) // Reflection cutoff mask: white = approximated, green = ray traced
                {
                    float3 objColor = new float3(0f);
                    uint2 idData = entityIdBuffer[pixel.x + pixel.y * (int)width];
                    if (idData.x != uint.MaxValue && (int)idData.x < sdfObjects.Length)
                    {
                        SDFObjectDTO sdf = sdfObjects[(int)idData.x];
                        float roughness = materialBuffer[pixel].x;
                        int flags = (int)materialBuffer[pixel].z;
                        if ((flags & 1) != 0)
                        {
                            objColor = roughness > sdf.reflectionCutoff
                                ? new float3(1f)                    // approximated: no reflection rays
                                : new float3(0f, 0.35f, 0f);        // traced
                        }
                    }
                    outputTexture[pixel] = new float4(objColor, 1);
                }
            }
        }

        #endregion main
    }
}







