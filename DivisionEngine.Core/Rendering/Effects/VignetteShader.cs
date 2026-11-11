
//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.

using DivisionEngine.Graphics;
using DivisionEngine.MathLib;
using F2 = DivisionEngine.MathLib.float2;
using F3 = DivisionEngine.MathLib.float3;
using F4 = DivisionEngine.MathLib.float4;
using I2 = DivisionEngine.MathLib.int2;
using U3 = DivisionEngine.MathLib.uint3;

namespace DivisionEngine.Rendering.Effects
{
    /// <summary>
    /// Darkens the edges of the screen with configurable radius.
    /// </summary>
    [Shader]
    public struct VignetteShader
    {
        /// <summary>Width of the input texture.</summary>
        public float width;

        /// <summary>Height of the input texture.</summary>
        public float height;

        /// <summary>Intensity of the vignette effect.</summary>
        public float vignetteIntensity;

        /// <summary>How quickly the vignette falls off.</summary>
        public float vignetteSmoothness;

        /// <summary>How round vs square the vignette is.</summary>
        public float vignetteRoundness;

        /// <summary>How far the vignette starts from the center.</summary>
        public float vignetteRadius;

        /// <summary>Color of the vignette.</summary>
        public F3 vignetteColor;

        /// <summary>Input render texture.</summary>
        [ShaderResource] public RWTexture2D<F4> inputTexture;

        /// <summary>Output texture with the vignette applied.</summary>
        [ShaderResource] public RWTexture2D<F4> outputTexture;

        /// <summary>Applies the vignette to one texel per thread.</summary>
        [Kernel(8, 8, 1)]
        public void Execute(U3 id)
        {
            I2 pixel = new I2((int)id.x, (int)id.y);
            if (pixel.x >= width || pixel.y >= height) return;

            F2 uv = new F2(pixel.x / width, pixel.y / height);
            F2 center = new F2(0.5f, 0.5f);
            F2 distVec = uv - center;

            // Calculate distance with roundness control
            float radialDistance;
            if (vignetteRoundness >= 0.999f)
            {
                radialDistance = math.length(distVec); // Perfect circle
            }
            else if (vignetteRoundness <= 0.001f)
            {
                radialDistance = math.max(math.abs(distVec.x), math.abs(distVec.y)); // Perfect rectangle
            }
            else
            {
                float p = math.lerp(8f, 2f, vignetteRoundness); // Higher p = more rectangular
                F2 absDist = math.abs(distVec);
                radialDistance = math.pow(math.pow(absDist.x, p) + math.pow(absDist.y, p), 1f / p);
            }

            // Max distance from center is ~0.707 for corners
            float maxDistance = 0.7071f;
            float scaledDistance = radialDistance / (vignetteRadius * maxDistance);

            // Calculate vignette factor (1 at center, 0 at edges)
            float smoothness = math.lerp(1f, 4f, vignetteSmoothness);
            float vignetteFactor = 1f - math.pow(math.saturate(scaledDistance), smoothness);
            vignetteFactor = math.lerp(1f, vignetteFactor, vignetteIntensity);
            vignetteFactor = math.clamp(vignetteFactor, 0f, 1f);

            // Apply vignette
            F3 color = inputTexture[pixel].xyz;
            F3 darkenedColor = color * vignetteFactor;
            F3 finalColor = math.lerp(darkenedColor, color * vignetteColor, (1f - vignetteFactor) * 0.5f);

            outputTexture[pixel] = new F4(finalColor, 1f);
        }
    }
}
