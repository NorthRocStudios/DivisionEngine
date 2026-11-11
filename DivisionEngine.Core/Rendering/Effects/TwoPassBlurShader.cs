
//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.

using DivisionEngine.Graphics;
using DivisionEngine.MathLib;
using F4 = DivisionEngine.MathLib.float4;
using I2 = DivisionEngine.MathLib.int2;
using U3 = DivisionEngine.MathLib.uint3;

namespace DivisionEngine.Rendering.Effects
{
    /// <summary>
    /// Two-pass separable Gaussian blur for better performance.
    /// </summary>
    /// <remarks>Pass 0 = Horizontal, Pass 1 = Vertical</remarks>
    [Shader]
    public struct TwoPassBlurShader
    {
        /// <summary>Width of the input texture.</summary>
        public float width;

        /// <summary>Height of the input texture.</summary>
        public float height;

        /// <summary>Radius of the blur effect.</summary>
        public float blurRadius;

        /// <summary>0 = horizontal pass, 1 = vertical pass.</summary>
        public int passIndex;

        /// <summary>Input render texture.</summary>
        [ShaderResource] public RWTexture2D<F4> inputTexture;

        /// <summary>Output texture with the blurred result.</summary>
        [ShaderResource] public RWTexture2D<F4> outputTexture;

        private static float GaussianWeight(float x, float sigma)
        {
            float sigma2 = sigma * sigma;
            return 1f / math.sqrt(2f * 3.141592654f * sigma2) * math.exp(-(x * x) / (2f * sigma2));
        }

        /// <summary>Blurs one texel per thread along the pass axis.</summary>
        [Kernel(8, 8, 1)]
        public void Execute(U3 id)
        {
            I2 pixel = new I2((int)id.x, (int)id.y);
            if (pixel.x >= width || pixel.y >= height) return;

            float sigma = math.max(blurRadius * 0.5f, 0.5f);
            int kernelSize = (int)math.ceil(sigma * 3f);
            kernelSize = math.max(kernelSize, 1);

            F4 result = new F4(0f);
            float totalWeight = 0f;

            // Horizontal or vertical pass
            for (int i = -kernelSize; i <= kernelSize; i++)
            {
                I2 samplePos = passIndex == 0
                    ? pixel + new I2(i, 0)
                    : pixel + new I2(0, i);

                if (samplePos.x < 0 || samplePos.x >= (int)width ||
                    samplePos.y < 0 || samplePos.y >= (int)height)
                    continue;

                float weight = GaussianWeight((float)i, sigma);
                result = result + inputTexture[samplePos] * weight;
                totalWeight = totalWeight + weight;
            }

            outputTexture[pixel] = totalWeight > 0f ? result / totalWeight : inputTexture[pixel];
        }
    }
}
