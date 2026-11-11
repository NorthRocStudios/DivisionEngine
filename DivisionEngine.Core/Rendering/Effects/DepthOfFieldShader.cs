
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
    /// Inaccurate to physical world, but visually pleasing Depth of Field shader.
    /// </summary>
    [Shader]
    public struct FastDepthOfFieldShader
    {
        /// <summary>Width of the input texture.</summary>
        public float width;

        /// <summary>Height of the input texture.</summary>
        public float height;

        /// <summary>Camera near clip plane.</summary>
        public float nearPlane;

        /// <summary>Camera far clip plane.</summary>
        public float farPlane;

        /// <summary>Distance at which the image is in focus.</summary>
        public float focusDistance;

        /// <summary>Falloff distance for the blur.</summary>
        public float focalLength;

        /// <summary>Input render texture.</summary>
        [ShaderResource] public RWTexture2D<F4> inputTexture;

        /// <summary>Output texture with depth of field applied.</summary>
        [ShaderResource] public RWTexture2D<F4> outputTexture;

        /// <summary>Depth (r) and world normals (gba) of the frame.</summary>
        [ShaderResource] public RWTexture2D<F4> depthNormals;

        /// <summary>Blurs one texel per thread based on its depth.</summary>
        [Kernel(8, 8, 1)]
        public void Execute(U3 id)
        {
            I2 pixel = new I2((int)id.x, (int)id.y);
            float normalizedDepth = depthNormals[pixel].x;
            float worldDepth = math.lerp(nearPlane, farPlane, normalizedDepth);
            float distanceFromFocal = math.abs(worldDepth - focusDistance);
            float blurAmount = math.saturate(distanceFromFocal / focalLength);
            float blurRadius = blurAmount * blurAmount * 16f;

            if (blurRadius < 0.5f)
            {
                outputTexture[pixel] = inputTexture[pixel];
                return;
            }

            int radius = (int)math.ceil(blurRadius);
            F3 sumColor = new F3(0f);
            float totalWeight = 0f;
            for (int yy = -radius; yy <= radius; yy++)
            {
                for (int xx = -radius; xx <= radius; xx++)
                {
                    I2 samplePixel = pixel + new I2(xx, yy);
                    if (samplePixel.x < 0 || samplePixel.x >= (int)width ||
                        samplePixel.y < 0 || samplePixel.y >= (int)height) continue;
                    float dist = math.sqrt(xx * xx + yy * yy);
                    if (dist > blurRadius) continue;

                    // Simple inverse distance weight
                    float weight = 1f / (1f + dist);
                    sumColor = sumColor + inputTexture[samplePixel].xyz * weight;
                    totalWeight = totalWeight + weight;
                }
            }

            F3 finalColor = totalWeight > 0f ? sumColor / totalWeight : inputTexture[pixel].xyz;
            outputTexture[pixel] = new F4(finalColor, 1f);
        }
    }
}
