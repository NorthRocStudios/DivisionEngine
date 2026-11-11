
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
    /// Hue, Saturation, and Lightness color grading effect.
    /// </summary>
    [Shader]
    public struct HSLShader
    {
        /// <summary>Width of the input texture.</summary>
        public float width;

        /// <summary>Height of the input texture.</summary>
        public float height;

        /// <summary>Hue shift in degrees, -180 to 180.</summary>
        public float hueShift;

        /// <summary>Saturation multiplier, 0 to 2 (1 = normal).</summary>
        public float saturation;

        /// <summary>Lightness multiplier, 0 to 2 (1 = normal).</summary>
        public float lightness;

        /// <summary>Contrast multiplier, 0 to 1 (1 = normal).</summary>
        public float contrast;

        /// <summary>Input render texture.</summary>
        [ShaderResource] public RWTexture2D<F4> inputTexture;

        /// <summary>Graded output texture.</summary>
        [ShaderResource] public RWTexture2D<F4> outputTexture;

        /// <summary>
        /// Converts RGB to HSL.
        /// </summary>
        private static F3 RgbToHsl(F3 rgb)
        {
            float high = math.max(rgb.x, math.max(rgb.y, rgb.z));
            float low = math.min(rgb.x, math.min(rgb.y, rgb.z));
            float delta = high - low;

            float h = 0f;
            float s = 0f;
            float l = (high + low) * 0.5f;

            if (delta > 0.001f)
            {
                s = l < 0.5f ? delta / (high + low) : delta / (2f - high - low);

                if (high == rgb.x)
                    h = (rgb.y - rgb.z) / delta + (rgb.y < rgb.z ? 6f : 0f);
                else if (high == rgb.y)
                    h = (rgb.z - rgb.x) / delta + 2f;
                else // high == rgb.z
                    h = (rgb.x - rgb.y) / delta + 4f;

                h = h / 6f;
            }

            return new F3(h, s, l);
        }

        private static float Hue2RGB(float p, float q, float t)
        {
            if (t < 0f) t = t + 1f;
            if (t > 1f) t = t - 1f;
            if (t < 1f / 6f) return p + (q - p) * 6f * t;
            if (t < 0.5f) return q;
            if (t < 2f / 3f) return p + (q - p) * (2f / 3f - t) * 6f;
            return p;
        }

        /// <summary>
        /// Converts HSL to RGB.
        /// </summary>
        private static F3 HslToRgb(F3 hsl)
        {
            float h = hsl.x;
            float s = hsl.y;
            float l = hsl.z;

            if (s < 0.001f) return new F3(l, l, l);

            float q = l < 0.5f ? l * (1f + s) : l + s - l * s;
            float p = 2f * l - q;

            return new F3(
                Hue2RGB(p, q, h + 1f / 3f),
                Hue2RGB(p, q, h),
                Hue2RGB(p, q, h - 1f / 3f));
        }

        /// <summary>Grades one texel per thread.</summary>
        [Kernel(8, 8, 1)]
        public void Execute(U3 id)
        {
            I2 pixel = new I2((int)id.x, (int)id.y);
            if (pixel.x >= width || pixel.y >= height) return;

            F4 color = inputTexture[pixel];
            F3 hsl = RgbToHsl(color.xyz); // Convert to HSL

            // Apply adjustments
            hsl.x = math.frac(hsl.x + hueShift / 360f); // Hue shift in degrees
            hsl.y = math.clamp(hsl.y * saturation, 0f, 1f);
            hsl.z = math.clamp(hsl.z * lightness, 0f, 1f);
            F3 rgb = HslToRgb(hsl); // Convert back to RGB
            F3 contrastColor = (rgb - 0.5f) * contrast + 0.5f; // Apply contrast
            contrastColor = math.clamp(contrastColor, 0f, 1f);

            outputTexture[pixel] = new F4(contrastColor, color.w);
        }
    }
}
