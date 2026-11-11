
//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//

using DivisionEngine.Graphics;
using DivisionEngine.MathLib;
using F2 = DivisionEngine.MathLib.float2;
using F3 = DivisionEngine.MathLib.float3;
using F4 = DivisionEngine.MathLib.float4;
using I2 = DivisionEngine.MathLib.int2;
using U3 = DivisionEngine.MathLib.uint3;

namespace DivisionEngine.Rendering.AntiAliasing
{
    /// <summary>
    /// Computes fast approximate anti-aliasing on an image.
    /// </summary>
    [Shader]
    public struct FXAAShader
    {
        /// <summary>Width of image.</summary>
        public int width;

        /// <summary>Height of image.</summary>
        public int height;

        /// <summary>Threshold for edge detection.</summary>
        public float edgeThreshold;

        /// <summary>Strength of the anti-aliasing blend.</summary>
        public float strength;

        /// <summary>Neighbourhood radius that is averaged for the edge blur.</summary>
        public int area;

        /// <summary>Debug visualization mode (0 = off).</summary>
        public int debug;

        /// <summary>Input image.</summary>
        [ShaderResource] public RWTexture2D<F4> inputTexture;

        /// <summary>Anti-aliased output image.</summary>
        [ShaderResource] public RWTexture2D<F4> outputTexture;

        private static float Luminance(F4 color)
        {
            return color.x * 0.299f + color.y * 0.587f + color.z * 0.114f;
        }

        /// <summary>Anti-aliases one texel per thread.</summary>
        [Kernel(8, 8, 1)]
        public void Execute(U3 id)
        {
            I2 pixel = new I2((int)id.x, (int)id.y);
            if (pixel.x >= width || pixel.y >= height) return;
            F4 center = inputTexture[pixel];

            // Sample neighbors
            int count = 0;
            F4 finalColor = new F4(0f);
            for (int x = -area; x <= area; x++)
            {
                for (int y = -area; y <= area; y++)
                {
                    // Bounds-test the resulting TEXEL, not the offset: testing the offset threw
                    // away every negative tap and never rejected right/bottom edge reads.
                    int sx = pixel.x + x;
                    int sy = pixel.y + y;
                    if (sx >= 0 && sx < width && sy >= 0 && sy < height)
                    {
                        I2 curPix = new I2(sx, sy);
                        finalColor = finalColor + inputTexture[curPix];
                        count = count + 1;
                    }
                }
            }

            finalColor = finalColor / count;
            I2 leftPix = new I2(math.max(0, pixel.x - 1), pixel.y);
            I2 rightPix = new I2(math.min((int)width - 1, pixel.x + 1), pixel.y);
            I2 upPix = new I2(pixel.x, math.max(0, pixel.y - 1));
            I2 downPix = new I2(pixel.x, math.min((int)height - 1, pixel.y + 1));
            F4 left = inputTexture[leftPix];
            F4 right = inputTexture[rightPix];
            F4 up = inputTexture[upPix];
            F4 down = inputTexture[downPix];

            float lumaLeft = Luminance(left);
            float lumaRight = Luminance(right);
            float lumaUp = Luminance(up);
            float lumaDown = Luminance(down);

            // Detect edge
            float lumaMin = math.min(math.min(lumaLeft, lumaRight), math.min(lumaUp, lumaDown));
            float lumaMax = math.max(math.max(lumaLeft, lumaRight), math.max(lumaUp, lumaDown));

            // If no edge, output original
            if (lumaMax - lumaMin < edgeThreshold)
            {
                outputTexture[pixel] = center;
                return;
            }

            // Blend based on edge strength
            float edgeStrength = math.saturate((lumaMax - lumaMin) / edgeThreshold);
            F4 result = math.lerp(center, finalColor, edgeStrength * strength);

            if (debug == 0)
            {
                outputTexture[pixel] = result;
            }
            else if (debug == 1)
            {
                outputTexture[pixel] = new F4(1f - result.x, 1f - result.y, result.z, result.w);
            }
            else if (debug == 2)
            {
                // Show edge direction (color coded)
                float lumaX = lumaLeft - lumaRight;
                float lumaY = lumaUp - lumaDown;
                F2 normDir = math.normalize(new F2(lumaX, lumaY));
                F3 dirColor = new F3(math.abs(normDir.x), math.abs(normDir.y), 0f);
                dirColor = math.saturate(dirColor * 2f);
                outputTexture[pixel] = new F4(dirColor, 1f);
            }
            else
            {
                outputTexture[pixel] = result;
            }
        }
    }
}
