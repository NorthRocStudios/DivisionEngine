
//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//

using DivisionEngine.Graphics;
using DivisionEngine.MathLib;
using F4 = DivisionEngine.MathLib.float4;
using I2 = DivisionEngine.MathLib.int2;
using U3 = DivisionEngine.MathLib.uint3;

namespace DivisionEngine.Rendering.Effects
{
    /// <summary>
    /// Gaussian blur post-processing effect.
    /// </summary>
    [Shader]
    public struct BlurShader
    {
        /// <summary>Width of the input texture.</summary>
        public float width;

        /// <summary>Height of the input texture.</summary>
        public float height;

        /// <summary>Radius of the blur effect.</summary>
        public float blurRadius;

        /// <summary>Number of blur passes.</summary>
        public int blurPasses;

        /// <summary>Input render texture.</summary>
        [ShaderResource] public RWTexture2D<F4> inputTexture;

        /// <summary>Output texture with the blurred result.</summary>
        [ShaderResource] public RWTexture2D<F4> outputTexture;

        /// <summary>Scratch texture, unused by the current implementation.</summary>
        [ShaderResource] public RWTexture2D<F4> tempTexture;

        /// <summary>
        /// Gaussian blur kernel weights for 5x5 kernel.
        /// </summary>
        private static float GaussianWeight(float x, float sigma)
        {
            float sigma2 = sigma * sigma;
            return 1f / math.sqrt(2f * 3.141592654f * sigma2) * math.exp(-(x * x) / (2f * sigma2));
        }

        /// <summary>
        /// Applies a 1D Gaussian blur along the X axis.
        /// </summary>
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
                result = result + inputTexture[samplePos] * weight;
                totalWeight = totalWeight + weight;
            }

            return totalWeight > 0f ? result / totalWeight : inputTexture[pixel];
        }

        /// <summary>
        /// Applies a 1D Gaussian blur along the Y axis.
        /// </summary>
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
                result = result + inputTexture[samplePos] * weight;
                totalWeight = totalWeight + weight;
            }

            return totalWeight > 0f ? result / totalWeight : inputTexture[pixel];
        }

        /// <summary>Blurs one texel per thread.</summary>
        [Kernel(8, 8, 1)]
        public void Execute(U3 id)
        {
            I2 pixel = new I2((int)id.x, (int)id.y);
            if (pixel.x >= width || pixel.y >= height) return;

            float sigma = math.max(blurRadius * 0.5f, 0.5f);
            F4 finalColor = inputTexture[pixel];

            // Apply multiple passes for stronger blur
            for (int passIndex = 0; passIndex < blurPasses; passIndex++)
            {
                // Horizontal pass
                F4 horizontal = BlurHorizontal(pixel, sigma);

                // Vertical pass (using the horizontal result)
                if (passIndex < blurPasses - 1) tempTexture[pixel] = horizontal;

                // For the final pass, compute vertical directly
                if (passIndex == blurPasses - 1) finalColor = BlurVertical(pixel, sigma);
                else tempTexture[pixel] = horizontal;
            }

            outputTexture[pixel] = finalColor;
        }
    }
}
