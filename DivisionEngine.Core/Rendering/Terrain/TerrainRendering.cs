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
    /// <summary>
    /// Functions useful for terrain rendering (on the GPU only).
    /// </summary>
    public static class TerrainRendering
    {
        // Constants
        private const float TAU = 6.28318530717959f;

        // Hash function required for Phacelle noise
        private static float2 Hash(float2 x)
        {
            float2 k = new float2(0.3183099f, 0.3678794f);
            x = x * k + new float2(k.y, k.x);
            return -1.0f + 2.0f * math.frac(16.0f * k * math.frac(x.x * x.y * (x.x + x.y)));
        }

        public static float PowInv(float t, float power)
        {
            // Flip, raise to the specified power, and flip back
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
            return 0.5f * t * t / smoothing;
        }

        public static float2 SafeNormalize(float2 n)
        {
            float l = math.length(n);
            return (math.abs(l) > 1e-10f) ? (n / l) : n;
        }

        // Phacelle Noise function - produces stripe patterns aligned with input vector
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
                    float weight = math.exp(-sqrDist * 2.0f);
                    weight = math.max(0.0f, weight - 0.01111f);
                    weightSum += weight;

                    float waveInput = math.dot(vectorFromCellPoint, sideDir) + offset;
                    phaseDir += new float2(math.cos(waveInput), math.sin(waveInput)) * weight;
                }
            }

            float2 interpolated = phaseDir / weightSum;
            float magnitude = math.sqrt(math.dot(interpolated, interpolated));
            magnitude = math.max(1.0f - normalization, magnitude);
            return new float4(interpolated / magnitude, sideDir);
        }

        public static float3 FractalNoiseWithDerivatives(float2 p, float freq, int octaves, float lacunarity, float gain)
        {
            float3 n = new float3(0f);
            float nf = freq;
            float na = 1.0f;

            for (int i = 0; i < octaves; i++)
            {
                float3 noiseVal = GradientNoise.noised(p * nf);
                n += noiseVal * na * new float3(1.0f, nf, nf);
                na *= gain;
                nf *= lacunarity;
            }
            return n;
        }

        // Main erosion filter - FIXED Phacelle noise handling
        public static float4 ErosionFilter(
            float2 p,
            float3 heightAndSlope,
            float fadeTarget,
            float strength,
            float gullyWeight,
            float detail,
            float4 rounding,
            float4 onset,
            float2 assumedSlope,
            float scale,
            int octaves,
            float lacunarity,
            float gain,
            float cellScale,
            float normalization,
            out float ridgeMap,
            out float debug)
        {
            strength *= scale;
            fadeTarget = math.clamp(fadeTarget, -1.0f, 1.0f);

            float3 inputHeightAndSlope = heightAndSlope;
            float freq = 1.0f / (scale * cellScale);
            float slopeLength = math.max(math.length(new float2(heightAndSlope.y, heightAndSlope.z)), 1e-10f);
            float magnitude = 0.0f;
            float roundingMult = 1.0f;

            float roundingForInput = math.lerp(rounding.y, rounding.x, math.saturate(fadeTarget + 0.5f)) * rounding.z;
            float combiMask = EaseOut(SmoothStart(slopeLength * onset.x, roundingForInput * onset.x));

            float ridgeMapCombiMask = EaseOut(slopeLength * onset.z);
            float ridgeMapFadeTarget = fadeTarget;

            // Determine gully direction based on slope mix
            float2 gullySlope = math.lerp(new float2(heightAndSlope.y, heightAndSlope.z),
                                           new float2(heightAndSlope.y, heightAndSlope.z) / slopeLength * assumedSlope.x,
                                           assumedSlope.y);

            for (int i = 0; i < octaves; i++)
            {
                float4 phacelle = PhacelleNoise(p * freq, SafeNormalize(gullySlope), cellScale, 0.25f, normalization);

                // Matches GLSL's `phacelle.zw *= -freq` - p was scaled by freq, and slope
                // directions point down, hence the negation. Everything below this line
                // must use the transformed derivative, not the raw PhacelleNoise output.
                float2 phacelleDeriv = new float2(phacelle.z, phacelle.w) * -freq;

                // Fix: Update gullySlope correctly
                float sloping = math.abs(phacelle.y);
                gullySlope += math.sign(phacelle.y) * phacelleDeriv * strength * gullyWeight;

                // Fix: Create proper derivatives for gullies
                float2 gulliesDerivative = phacelle.y * phacelleDeriv;
                float3 gullies = new float3(phacelle.x, gulliesDerivative.x, gulliesDerivative.y);

                float3 fadedGullies = math.lerp(new float3(fadeTarget, 0.0f, 0.0f), gullies * gullyWeight, combiMask);

                heightAndSlope += fadedGullies * strength;
                magnitude += strength;

                // Update fadeTarget
                fadeTarget = fadedGullies.x;

                // Update mask
                float roundingForOctave = math.lerp(rounding.y, rounding.x, math.saturate(phacelle.x + 0.5f)) * roundingMult;
                float newMask = EaseOut(SmoothStart(sloping * onset.y, roundingForOctave * onset.y));
                combiMask = PowInv(combiMask, detail) * newMask;

                // Update ridgeMap
                ridgeMapFadeTarget = math.lerp(ridgeMapFadeTarget, gullies.x, ridgeMapCombiMask);
                float newRidgeMapMask = EaseOut(sloping * onset.w);
                ridgeMapCombiMask *= newRidgeMapMask;

                // Prepare next octave
                strength *= gain;
                freq *= lacunarity;
                roundingMult *= rounding.w;
            }

            ridgeMap = ridgeMapFadeTarget * (1.0f - ridgeMapCombiMask);
            debug = fadeTarget;

            float3 heightAndSlopeDelta = heightAndSlope - inputHeightAndSlope;
            return new float4(heightAndSlopeDelta, magnitude);
        }
    }
}
