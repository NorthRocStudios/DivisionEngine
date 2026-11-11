
//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.

using DivisionEngine.Graphics;
using DivisionEngine.MathLib;
using F3 = DivisionEngine.MathLib.float3;
using F4 = DivisionEngine.MathLib.float4;
using I2 = DivisionEngine.MathLib.int2;
using U3 = DivisionEngine.MathLib.uint3;

namespace DivisionEngine.Rendering.Effects
{
    /// <summary>
    /// Bloom effect using bright pass extraction and Gaussian blur.
    /// </summary>
    [Shader]
    public struct BloomShader
    {
        /// <summary>Width of the input texture.</summary>
        public float width;

        /// <summary>Height of the input texture.</summary>
        public float height;

        /// <summary>Threshold for pixels to bloom.</summary>
        public float threshold;

        /// <summary>Smoothness of the transition between blooming and non-blooming pixels.</summary>
        public float knee;

        /// <summary>Intensity of bloom pixels.</summary>
        public float intensity;

        /// <summary>Radius of the bloom blur.</summary>
        public float blurRadius;

        /// <summary>Number of blur passes.</summary>
        public int blurPasses;

        /// <summary>Input render texture.</summary>
        [ShaderResource] public RWTexture2D<F4> inputTexture;

        /// <summary>Output texture with bloom applied.</summary>
        [ShaderResource] public RWTexture2D<F4> outputTexture;

        /// <summary>Scratch texture holding the bright pass.</summary>
        [ShaderResource] public RWTexture2D<F4> brightTexture;

        /// <summary>Scratch texture for the horizontal blur pass.</summary>
        [ShaderResource] public RWTexture2D<F4> blurTempTexture;

        private static float Luminance(F3 rgb)
        {
            return math.dot(rgb, new F3(0.2126f, 0.7152f, 0.0722f));
        }

        private F4 BrightPass(F4 color)
        {
            F3 rgb = color.xyz;
            float luminance = Luminance(rgb);

            // Soft knee threshold
            float knee2 = knee * 2f;
            float bright = luminance - threshold;
            bright = math.max(bright, 0f);
            bright = (bright * bright) / (bright + knee2);
            bright = math.clamp(bright, 0f, 1f);

            return new F4(rgb * bright, color.w);
        }

        private F4 BlurHorizontal(I2 pixel, float sigma)
        {
            int kernelSize = (int)math.ceil(sigma * 3f);
            kernelSize = math.max(kernelSize, 1);

            F4 result = new F4(0f);
            float totalWeight = 0f;
            for (int x = -kernelSize; x <= kernelSize; x++)
            {
                I2 samplePos = pixel + new I2(x, 0);
                if (samplePos.x < 0 || samplePos.x >= (int)width ||
                    samplePos.y < 0 || samplePos.y >= (int)height)
                    continue;

                float weight = GaussianWeight(x, sigma);
                result = result + brightTexture[samplePos] * weight;
                totalWeight = totalWeight + weight;
            }
            return totalWeight > 0f ? result / totalWeight : brightTexture[pixel];
        }

        private F4 BlurVertical(I2 pixel, float sigma)
        {
            int kernelSize = (int)math.ceil(sigma * 3f);
            kernelSize = math.max(kernelSize, 1);

            F4 result = new F4(0f);
            float totalWeight = 0f;
            for (int y = -kernelSize; y <= kernelSize; y++)
            {
                I2 samplePos = pixel + new I2(0, y);
                if (samplePos.x < 0 || samplePos.x >= (int)width ||
                    samplePos.y < 0 || samplePos.y >= (int)height)
                    continue;

                float weight = GaussianWeight(y, sigma);
                result = result + blurTempTexture[samplePos] * weight; // Use horizontal-blurred result from temp texture
                totalWeight = totalWeight + weight;
            }
            return totalWeight > 0f ? result / totalWeight : blurTempTexture[pixel];
        }

        private static float GaussianWeight(float x, float sigma)
        {
            float sigma2 = sigma * sigma;
            return 1f / math.sqrt(2f * 3.141592654f * sigma2) * math.exp(-(x * x) / (2f * sigma2));
        }

        /// <summary>Blooms one texel per thread.</summary>
        [Kernel(8, 8, 1)]
        public void Execute(U3 id)
        {
            I2 pixel = new I2((int)id.x, (int)id.y);
            if (pixel.x >= width || pixel.y >= height) return;

            F4 originalColor = inputTexture[pixel];
            F4 brightColor = BrightPass(originalColor); // Extract bright regions
            brightTexture[pixel] = brightColor;
            float sigma = math.max(blurRadius * 0.5f, 0.5f); // Blur the bright texture
            F4 blurred = brightColor;

            // Blur passes
            for (int blurPass = 0; blurPass < blurPasses; blurPass++)
            {
                F4 horizontal = BlurHorizontal(pixel, sigma);
                blurTempTexture[pixel] = horizontal;
                F4 vertical = BlurVertical(pixel, sigma);
                blurred = vertical;

                // Update brightTexture for next pass
                brightTexture[pixel] = blurred;
            }

            // Combine with original
            F3 combined = originalColor.xyz + (blurred.xyz * intensity);
            outputTexture[pixel] = new F4(combined, originalColor.w);
        }
    }
}
