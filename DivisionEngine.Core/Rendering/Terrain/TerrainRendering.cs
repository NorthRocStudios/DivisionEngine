//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine.
//
// MPL-2.0 NOTICE: the Phacelle Noise and erosion filter portions of this file
// are derived from Rune Skovbo Johansen's "Advanced Terrain Erosion Filter"
// (https://www.shadertoy.com/view/wXcfWn), copyright (c) 2025 Rune Skovbo
// Johansen, and remain licensed under the Mozilla Public License, v. 2.0
// (https://mozilla.org/MPL/2.0/). All other code in this file is MIT.
// See THIRD_PARTY_NOTICES.md for details.
//
// The remainder of Division Engine is licensed under the MIT License. See the
// LICENSE.txt file in the project root for full license terms.
//
using DivisionEngine.MathLib;

namespace DivisionEngine.Rendering.Terrain
{
    /// <summary>
    /// Terrain height-field math shared by the CPU and the GPU.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The base terrain is the height function from Rune Skovbo Johansen's
    /// "Advanced Terrain Erosion Filter" Shadertoy (https://www.shadertoy.com/view/wXcfWn).
    /// The erosion filter is a port of that shader:
    /// </para>
    /// <para>
    /// Phacelle Noise and the erosion filter are copyright (c) 2025 Rune Skovbo
    /// Johansen and are used under the Mozilla Public License 2.0; those
    /// portions remain MPL-2.0 while all other code in this file is MIT. The
    /// original technique derives from Clay John (https://www.shadertoy.com/view/MtGcWh)
    /// and Fewes (https://www.shadertoy.com/view/7ljcRW).
    /// </para>
    /// <para>
    /// Everything here is written in the subset of C# that DivisionCompute can
    /// translate to HLSL, so the same code generates the heightmap on the GPU when
    /// a terrain is baked and on the CPU when LOD chunks stream in on worker
    /// threads.
    /// </para>
    /// </remarks>
    public static class TerrainRendering
    {
        /// <summary>Two pi.</summary>
        public const float TAU = 6.28318530717959f;

        /// <summary>Reference amplitude of the demo height function. The demo's
        /// heights live in [0,1] with a base amplitude of <c>heightAmp</c>; this
        /// constant converts that unit height into world units.</summary>
        public const float BASE_UNIT_AMPLITUDE = 0.5f;

        // ---------------------------------------------------------------------
        // Hashing and noise
        // ---------------------------------------------------------------------

        // Hash required by Phacelle noise (and gradient noise).
        public static float2 Hash(float2 x)
        {
            float2 k = new float2(0.3183099f, 0.3678794f);
            x = x * k + new float2(k.y, k.x);
            return -1.0f + 2.0f * math.frac(16.0f * k * math.frac(x.x * x.y * (x.x + x.y)));
        }

        // Returns gradient noise (x) and its derivatives (yz), quintic interpolation.
        public static float3 Noised(float2 p)
        {
            float2 i = math.floor(p);
            float2 f = math.frac(p);

            float2 u = f * f * f * (f * (f * 6.0f - 15.0f) + 10.0f);
            float2 du = 30.0f * f * f * (f * (f - 2.0f) + 1.0f);

            float2 ga = Hash(i);
            float2 gb = Hash(i + new float2(1.0f, 0.0f));
            float2 gc = Hash(i + new float2(0.0f, 1.0f));
            float2 gd = Hash(i + new float2(1.0f, 1.0f));

            float va = math.dot(ga, f);
            float vb = math.dot(gb, f - new float2(1.0f, 0.0f));
            float vc = math.dot(gc, f - new float2(0.0f, 1.0f));
            float vd = math.dot(gd, f - new float2(1.0f, 1.0f));

            float value = va + u.x * (vb - va) + u.y * (vc - va) + u.x * u.y * (va - vb - vc + vd);
            float2 derivative = ga + u.x * (gb - ga) + u.y * (gc - ga) + u.x * u.y * (ga - gb - gc + gd)
                + du * (new float2(u.y, u.x) * (va - vb - vc + vd) + new float2(vb, vc) - va);

            return new float3(value, derivative.x, derivative.y);
        }

        // Fractal noise returning (height, dHeight/dx, dHeight/dy). The derivatives
        // are scaled by each octave's frequency so they remain correct in p-space.
        public static float3 FractalNoise(float2 p, float freq, int octaves, float lacunarity, float gain)
        {
            float3 n = new float3(0f);
            float nf = freq;
            float na = 1.0f;

            for (int i = 0; i < octaves; i++)
            {
                n += Noised(p * nf) * na * new float3(1.0f, nf, nf);
                na *= gain;
                nf *= lacunarity;
            }
            return n;
        }

        // ---------------------------------------------------------------------
        // Utility functions used by the erosion filter
        // ---------------------------------------------------------------------

        public static float PowInv(float t, float power)
        {
            // Flip, raise to the specified power, and flip back.
            return 1.0f - math.pow(1.0f - math.saturate(t), power);
        }

        public static float EaseOut(float t)
        {
            float v = 1.0f - math.saturate(t);
            return 1.0f - v * v;
        }

        public static float SmoothStart(float t, float smoothing)
        {
            if (t >= smoothing)
                return t - 0.5f * smoothing;
            return 0.5f * t * t / math.max(smoothing, 1e-6f);
        }

        public static float2 SafeNormalize(float2 n)
        {
            float l = math.length(n);
            return (math.abs(l) > 1e-10f) ? (n / l) : n;
        }

        // ---------------------------------------------------------------------
        // Phacelle noise
        // ---------------------------------------------------------------------

        /// <summary>
        /// Directional stripe noise. <paramref name="p"/> is the evaluation point,
        /// <paramref name="normDir"/> the (normalized) stripe direction,
        /// <paramref name="freq"/> the stripes per cell, <paramref name="offset"/>
        /// the phase offset in cycles and <paramref name="normalization"/> how
        /// aggressively the blended magnitude is restored to one. Returns
        /// (cosine, sine, derivative direction * freq * TAU).
        /// </summary>
        public static float4 PhacelleNoise(float2 p, float2 normDir, float freq, float offset, float normalization)
        {
            float2 sideDir = new float2(-normDir.y, normDir.x) * freq * TAU;
            offset *= TAU;

            float2 pInt = math.floor(p);
            float2 pFrac = math.frac(p);
            float2 phaseDir = new float2(0f);
            float weightSum = 0.0f;

            for (int i = -1; i <= 2; i++)
            {
                for (int j = -1; j <= 2; j++)
                {
                    float2 gridOffset = new float2(i, j);
                    float2 gridPoint = pInt + gridOffset;
                    float2 randomOffset = Hash(gridPoint) * 0.5f;

                    float2 vectorFromCellPoint = pFrac - gridOffset - randomOffset;

                    float sqrDist = math.dot(vectorFromCellPoint, vectorFromCellPoint);
                    float weight = math.max(0.0f, math.exp(-sqrDist * 2.0f) - 0.01111f);
                    weightSum += weight;

                    float waveInput = math.dot(vectorFromCellPoint, sideDir) + offset;
                    phaseDir += new float2(math.cos(waveInput), math.sin(waveInput)) * weight;
                }
            }

            float2 interpolated = phaseDir / math.max(weightSum, 1e-10f);
            float magnitude = math.sqrt(math.dot(interpolated, interpolated));
            magnitude = math.max(1.0f - normalization, magnitude);
            return new float4(interpolated / magnitude, sideDir);
        }

        // ---------------------------------------------------------------------
        // Erosion filter
        // ---------------------------------------------------------------------

        /// <summary>
        /// Applies stacked faded gullies to a height-and-slope triple. Returns
        /// (height delta, slope delta x, slope delta y, total magnitude) and writes
        /// the ridge map (-1 at creases, 1 at ridges) plus a debug value.
        /// </summary>
        public static float4 ErosionFilter(
            float2 p, float3 heightAndSlope, float fadeTarget,
            float strength, float gullyWeight, float detail,
            float4 rounding, float4 onset, float2 assumedSlope,
            float scale, int octaves, float lacunarity,
            float gain, float cellScale, float normalization,
            out float ridgeMap, out float debug)
        {
            strength *= scale;
            fadeTarget = math.clamp(fadeTarget, -1.0f, 1.0f);

            float3 inputHeightAndSlope = heightAndSlope;
            float freq = 1.0f / math.max(scale * cellScale, 1e-6f);
            float slopeLength = math.max(math.length(new float2(heightAndSlope.y, heightAndSlope.z)), 1e-10f);
            float magnitude = 0.0f;
            float roundingMult = 1.0f;

            float roundingForInput = math.lerp(rounding.y, rounding.x, math.saturate(fadeTarget + 0.5f)) * rounding.z;
            float combiMask = EaseOut(SmoothStart(slopeLength * onset.x, roundingForInput * onset.x));

            float ridgeMapCombiMask = EaseOut(slopeLength * onset.z);
            float ridgeMapFadeTarget = fadeTarget;

            float2 gullySlope = math.lerp(new float2(heightAndSlope.y, heightAndSlope.z),
                new float2(heightAndSlope.y, heightAndSlope.z) / slopeLength * assumedSlope.x,
                assumedSlope.y);

            for (int i = 0; i < octaves; i++)
            {
                float4 phacelle = PhacelleNoise(p * freq, SafeNormalize(gullySlope), cellScale, 0.25f, normalization);

                // p was scaled by freq and slope directions point down, so the
                // derivative carries a -freq factor.
                float2 phacelleDeriv = new float2(phacelle.z, phacelle.w) * -freq;
                float sloping = math.abs(phacelle.y);

                gullySlope += math.sign(phacelle.y) * phacelleDeriv * strength * gullyWeight;

                float2 gulliesDerivative = phacelle.y * phacelleDeriv;
                float3 gullies = new float3(phacelle.x, gulliesDerivative.x, gulliesDerivative.y);

                float3 fadedGullies = math.lerp(new float3(fadeTarget, 0.0f, 0.0f), gullies * gullyWeight, combiMask);

                heightAndSlope += fadedGullies * strength;
                magnitude += strength;

                fadeTarget = fadedGullies.x;

                float roundingForOctave = math.lerp(rounding.y, rounding.x, math.saturate(phacelle.x + 0.5f)) * roundingMult;
                float newMask = EaseOut(SmoothStart(sloping * onset.y, roundingForOctave * onset.y));
                combiMask = PowInv(combiMask, detail) * newMask;

                ridgeMapFadeTarget = math.lerp(ridgeMapFadeTarget, gullies.x, ridgeMapCombiMask);
                float newRidgeMapMask = EaseOut(sloping * onset.w);
                ridgeMapCombiMask *= newRidgeMapMask;

                strength *= gain;
                freq *= lacunarity;
                roundingMult *= rounding.w;
            }

            ridgeMap = ridgeMapFadeTarget * (1.0f - ridgeMapCombiMask);
            debug = fadeTarget;

            float3 delta = heightAndSlope - inputHeightAndSlope;
            return new float4(delta, magnitude);
        }

        // ---------------------------------------------------------------------
        // Height function
        // ---------------------------------------------------------------------

        /// <summary>
        /// Constant vertical offset that the reference erosion filter applies to the
        /// whole terrain (its "height offset" term). The engine adds the inverse so
        /// the terrain sits around its transform position instead of sinking below
        /// it; the surface shape is unchanged.
        /// </summary>
        public static float ErosionCenterOffset(TerrainDTO meta)
        {
            if (meta.hasErosion == 0) return 0.0f;

            float strength = meta.erosionStrength * meta.erosionScale;
            float total = 0.0f;
            for (int i = 0; i < meta.erosionOctaves; i++)
            {
                total += strength;
                strength *= meta.erosionGain;
            }

            float unitToWorld = meta.heightScale / (meta.heightAmp * BASE_UNIT_AMPLITUDE);
            return -meta.terrainHeightOffset.x * total * unitToWorld;
        }

        /// <summary>
        /// Base (un-eroded) terrain height and slope at a world XZ position. The
        /// returned height is relative to the terrain origin; y and z hold the
        /// world-space slope d(height)/dx and d(height)/dz.
        /// </summary>
        public static float3 BaseHeight(float2 worldXZ, TerrainDTO meta)
        {
            float2 p = worldXZ * meta.frequency;
            float3 n = FractalNoise(p, meta.heightFrequency, meta.heightOctaves,
                meta.heightLacunarity, meta.heightGain) * meta.heightAmp;

            float unitToWorld = meta.heightScale / (meta.heightAmp * BASE_UNIT_AMPLITUDE);
            float height = n.x * BASE_UNIT_AMPLITUDE * unitToWorld + meta.heightOffset
                + ErosionCenterOffset(meta);
            float2 slope = new float2(n.y, n.z) * BASE_UNIT_AMPLITUDE * unitToWorld * meta.frequency;

            return new float3(height, slope.x, slope.y);
        }

        /// <summary>
        /// Full terrain sample at a world XZ position: (height, dHeight/dx,
        /// dHeight/dz, ridge01). The height and slopes are relative to the terrain
        /// origin. The ridge map is a normalized 0..1 value (1 on ridges, 0 in
        /// creases), and <paramref name="erosionAmount"/> is a 0..1 measure of how
        /// much the erosion raised (above 0.5) or lowered (below 0.5) the surface.
        /// </summary>
        public static float4 Heightmap(float2 worldXZ, TerrainDTO meta, out float ridgeMap, out float erosionAmount)
        {
            float2 p = worldXZ * meta.frequency;
            float3 n = FractalNoise(p, meta.heightFrequency, meta.heightOctaves,
                meta.heightLacunarity, meta.heightGain) * meta.heightAmp;

            float unitToWorld = meta.heightScale / (meta.heightAmp * BASE_UNIT_AMPLITUDE);

            float fadeTarget = math.clamp(n.x / math.max(meta.heightAmp * 0.6f, 1e-6f), -1.0f, 1.0f);
            float3 n01 = new float3(n.x * BASE_UNIT_AMPLITUDE + 0.5f,
                n.y * BASE_UNIT_AMPLITUDE, n.z * BASE_UNIT_AMPLITUDE);

            float ridge = 1.0f;
            float unitHeight = n01.x;
            float2 unitSlope = new float2(n01.y, n01.z);

            if (meta.hasErosion == 1)
            {
                float debug;
                float4 h = ErosionFilter(p, n01, fadeTarget,
                    meta.erosionStrength, meta.erosionGullyWeight, meta.erosionDetail,
                    meta.erosionRounding, meta.erosionOnset, meta.erosionAssumedSlope,
                    meta.erosionScale, meta.erosionOctaves, meta.erosionLacunarity,
                    meta.erosionGain, meta.erosionCellScale, meta.erosionNormalization,
                    out ridge, out debug);

                float offset = math.lerp(meta.terrainHeightOffset.x, -fadeTarget,
                    meta.terrainHeightOffset.y) * h.w;

                unitHeight = n01.x + h.x + offset;
                unitSlope += new float2(h.y, h.z);

                erosionAmount = h.w > 1e-6f ? math.saturate(h.x / h.w * 0.5f + 0.5f) : 0.5f;
            }
            else
            {
                erosionAmount = 0.5f;
            }

            float height = (unitHeight - 0.5f) * unitToWorld + meta.heightOffset
                + ErosionCenterOffset(meta);
            float2 slope = unitSlope * unitToWorld * meta.frequency;

            ridgeMap = ridge * 0.5f + 0.5f;
            return new float4(height, slope.x, slope.y, ridgeMap);
        }

        // ---------------------------------------------------------------------
        // Height-field surface helpers
        // ---------------------------------------------------------------------

        /// <summary>
        /// Signed distance to the height-field surface for a point at
        /// <paramref name="localY"/> above the terrain origin. Uses the slope to
        /// turn the vertical separation into a conservative Euclidean estimate
        /// (the distance to the local tangent plane).
        /// </summary>
        public static float HeightfieldDistance(float localY, float4 terrainSample)
        {
            float2 slope = new float2(terrainSample.y, terrainSample.z);
            float k = 1.0f / math.sqrt(1.0f + math.dot(slope, slope));
            return (localY - terrainSample.x) * math.max(k, 0.2f);
        }

        /// <summary>
        /// Terrain surface normal from a height-field sample, in terrain local space.
        /// </summary>
        public static float3 HeightfieldNormal(float4 terrainSample)
        {
            return math.normalize(new float3(-terrainSample.y, 1.0f, -terrainSample.z));
        }

        // ---------------------------------------------------------------------
        // Interpolation
        // ---------------------------------------------------------------------

        /// <summary>
        /// Uniform Catmull-Rom sample of four values at <paramref name="t"/> in [0,1].
        /// C1-continuous and interpolating, which is what removes the polygonal
        /// "pixelated" look of bilinear heightmap sampling.
        /// </summary>
        public static float CatmullRom(float p0, float p1, float p2, float p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * ((-t3 + 2.0f * t2 - t) * p0
                + (3.0f * t3 - 5.0f * t2 + 2.0f) * p1
                + (-3.0f * t3 + 4.0f * t2 + t) * p2
                + (t3 - t2) * p3);
        }

        /// <summary>
        /// Cubic Hermite reconstruction from four texels of packed terrain data
        /// (height, dHdx, dHdz, ridge). The stored analytic slopes are used as the
        /// Hermite tangents, so the surface is C1 while only four texels are read.
        /// Returns the interpolated (height, dHdx, dHdz, ridge) with the slopes in
        /// world units per world unit.
        /// </summary>
        /// <remarks>
        /// This is the tensor-product Hermite patch from Bán &amp; Valasek,
        /// "Hermite interpolation of heightmaps" (Eurographics 2022 poster):
        /// <code>
        /// f(x,z) = sum_i sum_j [ f_ij H_i0(x) H_j0(z)
        ///                      + fx_ij H_i1(x) H_j0(z)
        ///                      + fz_ij H_i0(x) H_j1(z) ]
        /// </code>
        /// The z-tangent field is therefore interpolated along X with the Hermite
        /// <b>value</b> basis only (H_i0), not linearly: that is what keeps the patch
        /// C1 across neighbouring cells. The returned derivatives are the exact
        /// analytic derivatives of that patch, which is what the shading normals use.
        /// </remarks>
        /// <param name="p00">Texel at (x0, z0).</param>
        /// <param name="p10">Texel at (x0 + 1, z0).</param>
        /// <param name="p01">Texel at (x0, z0 + 1).</param>
        /// <param name="p11">Texel at (x0 + 1, z0 + 1).</param>
        /// <param name="tx">Fraction along X in [0,1].</param>
        /// <param name="tz">Fraction along Z in [0,1].</param>
        /// <param name="dx">World distance between adjacent texels along X.</param>
        /// <param name="dz">World distance between adjacent texels along Z.</param>
        public static float4 Hermite4(float4 p00, float4 p10, float4 p01, float4 p11,
            float tx, float tz, float dx, float dz)
        {
            float tx2 = tx * tx;
            float tx3 = tx2 * tx;
            float h00x = 2.0f * tx3 - 3.0f * tx2 + 1.0f;
            float h10x = -2.0f * tx3 + 3.0f * tx2;
            float h01x = tx3 - 2.0f * tx2 + tx;
            float h11x = tx3 - tx2;
            float d00x = 6.0f * tx2 - 6.0f * tx;
            float d10x = -6.0f * tx2 + 6.0f * tx;
            float d01x = 3.0f * tx2 - 4.0f * tx + 1.0f;
            float d11x = 3.0f * tx2 - 2.0f * tx;

            // Height value at each Z edge, Hermite-interpolated along X (A_j in the paper).
            float a0 = p00.x * h00x + p10.x * h10x + dx * (p00.y * h01x + p10.y * h11x);
            float a1 = p01.x * h00x + p11.x * h10x + dx * (p01.y * h01x + p11.y * h11x);
            float da0 = p00.x * d00x + p10.x * d10x + dx * (p00.y * d01x + p10.y * d11x);
            float da1 = p01.x * d00x + p11.x * d10x + dx * (p01.y * d01x + p11.y * d11x);

            // Z tangents, interpolated along X with the Hermite value basis (B_j in the paper).
            float b0 = p00.z * h00x + p10.z * h10x;
            float b1 = p01.z * h00x + p11.z * h10x;
            float db0 = p00.z * d00x + p10.z * d10x;
            float db1 = p01.z * d00x + p11.z * d10x;

            float tz2 = tz * tz;
            float tz3 = tz2 * tz;
            float h00z = 2.0f * tz3 - 3.0f * tz2 + 1.0f;
            float h10z = -2.0f * tz3 + 3.0f * tz2;
            float h01z = tz3 - 2.0f * tz2 + tz;
            float h11z = tz3 - tz2;
            float d00z = 6.0f * tz2 - 6.0f * tz;
            float d10z = -6.0f * tz2 + 6.0f * tz;
            float d01z = 3.0f * tz2 - 4.0f * tz + 1.0f;
            float d11z = 3.0f * tz2 - 2.0f * tz;

            float height = a0 * h00z + a1 * h10z + dz * (b0 * h01z + b1 * h11z);
            float dHeightDx = (da0 * h00z + da1 * h10z + dz * (db0 * h01z + db1 * h11z))
                / math.max(dx, 1e-6f);
            float dHeightDz = (a0 * d00z + a1 * d10z + dz * (b0 * d01z + b1 * d11z))
                / math.max(dz, 1e-6f);
            float ridge = p00.w + (p10.w - p00.w) * tx + (p01.w - p00.w) * tz
                + (p00.w - p10.w - p01.w + p11.w) * tx * tz;

            return new float4(height, dHeightDx, dHeightDz, ridge);
        }

        /// <summary>
        /// Catmull-Rom sample of a float4 (used for the packed terrain data).
        /// </summary>
        public static float4 CatmullRom4(float4 p0, float4 p1, float4 p2, float4 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            float w0 = 0.5f * (-t3 + 2.0f * t2 - t);
            float w1 = 0.5f * (3.0f * t3 - 5.0f * t2 + 2.0f);
            float w2 = 0.5f * (-3.0f * t3 + 4.0f * t2 + t);
            float w3 = 0.5f * (t3 - t2);
            return p0 * w0 + p1 * w1 + p2 * w2 + p3 * w3;
        }

        /// <summary>
        /// Bilinear sample of a float4 (all four channels). Terrain sampling uses
        /// <see cref="BilinearTerrain4"/> instead so the returned slopes match the
        /// bilinear height field rather than the stored source slopes.
        /// </summary>
        public static float4 Bilinear4(float4 p00, float4 p10, float4 p01, float4 p11, float tx, float ty)
        {
            float4 a = math.lerp(p00, p10, tx);
            float4 b = math.lerp(p01, p11, tx);
            return math.lerp(a, b, ty);
        }

        /// <summary>
        /// Bilinear terrain sample: bilinear height and ridge, but with the <b>exact</b>
        /// gradient of that bilinear height field. The stored slopes describe the
        /// pre-filtered source surface, not the bilinear reconstruction, so reusing them
        /// for shading makes a deliberately faceted bilinear terrain look incorrectly
        /// smooth (and produces shading that does not match the geometry).
        /// </summary>
        public static float4 BilinearTerrain4(float4 p00, float4 p10, float4 p01, float4 p11,
            float tx, float tz, float dx, float dz)
        {
            float4 a = math.lerp(p00, p10, tx);
            float4 b = math.lerp(p01, p11, tx);
            float4 value = math.lerp(a, b, tz);

            // d/dx of the bilinear height = lerp of the two row finite differences.
            value.y = ((p10.x - p00.x) * (1.0f - tz) + (p11.x - p01.x) * tz)
                / math.max(dx, 1e-6f);
            // d/dz of the bilinear height = lerp of the two column finite differences.
            value.z = ((p01.x - p00.x) * (1.0f - tx) + (p11.x - p10.x) * tx)
                / math.max(dz, 1e-6f);
            return value;
        }

        /// <summary>
        /// Maps a distance from the camera to a LOD level. Level 0 covers
        /// <paramref name="baseRadius"/>; every subsequent level doubles the radius.
        /// </summary>
        public static int LodLevel(float distance, float baseRadius, int levels)
        {
            float r = math.max(baseRadius, 1e-3f);
            float level = math.log2(math.max(distance / r, 1.0f));
            int l = (int)math.floor(level);
            return math.clamp(l, 0, levels - 1);
        }
    }
}
