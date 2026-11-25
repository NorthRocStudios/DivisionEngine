
//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE file in the project root
// for full license terms.
//

using DivisionEngine.Graphics;
using DivisionEngine.MathLib;

namespace DivisionEngine.Rendering.Denoising
{
    /// <summary>
    /// Resolves the denoised frame: recombines the base shading with the filtered
    /// reflection and refraction channels and applies the ACES tonemapper.
    /// </summary>
    [Shader]
    public struct DenoiseResolveShader
    {
        /// <summary>Width of the image.</summary>
        public float width;

        /// <summary>Height of the image.</summary>
        public float height;

        /// <summary>1 to apply the ACES tonemapper.</summary>
        public int enableAces;

        /// <summary>
        /// Test/debug channel: 0 = final image, 1 = reflection only, 2 = refraction only,
        /// 3 = base shading only. Non-zero values bypass the tonemapper.
        /// </summary>
        public int channel;

        /// <summary>Base shading (linear).</summary>
        [ShaderResource] public Texture2D<float4> inputColor;

        /// <summary>Filtered (or raw) reflection radiance.</summary>
        [ShaderResource] public Texture2D<float4> reflection;

        /// <summary>Filtered (or raw) refracted radiance.</summary>
        [ShaderResource] public Texture2D<float4> refraction;

        /// <summary>Final display-referred colour.</summary>
        [ShaderResource] public RWTexture2D<float4> outputTexture;

        /// <summary>Resolves one texel per thread.</summary>
        private static bool IsFinite(float value)
        {
            return value == value && math.abs(value) < 1e30f;
        }

        private static bool IsFinite(float3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static float3 Sanitize(float3 value)
        {
            if (!IsFinite(value)) return new float3(0f);
            return value;
        }

        /// <summary>Combines the base and denoised secondary channels and applies ACES.</summary>
        [Kernel(8, 8, 1)]
        public void Execute(uint3 id)
        {
            int2 pixel = new int2((int)id.x, (int)id.y);
            if (pixel.x >= (int)width || pixel.y >= (int)height) return;

            float3 baseColor = Sanitize(inputColor[pixel].rgb);
            float3 reflectionColor = Sanitize(reflection[pixel].rgb);
            float3 refractionColor = Sanitize(refraction[pixel].rgb);

            if (channel == 1)
            {
                outputTexture[pixel] = new float4(math.saturate(reflectionColor), 1f);
                return;
            }
            if (channel == 2)
            {
                outputTexture[pixel] = new float4(math.saturate(refractionColor), 1f);
                return;
            }
            if (channel == 3)
            {
                outputTexture[pixel] = new float4(math.saturate(baseColor), 1f);
                return;
            }

            float3 color = Sanitize(baseColor + reflectionColor + refractionColor);

            if (enableAces == 1)
                color = math.saturate(color * (2.51f * color + 0.03f) / (color * (2.43f * color + 0.59f) + 0.14f));

            outputTexture[pixel] = new float4(Sanitize(color), 1f);
        }
    }
}
