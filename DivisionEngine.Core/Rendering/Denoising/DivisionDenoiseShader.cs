
//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//

using DivisionEngine.Graphics;
using DivisionEngine.MathLib;

namespace DivisionEngine.Rendering
{
    [Shader]
    public struct DivisionDenoiseShader
    {
        /// <summary>Width of the image.</summary>
        public float width;

        /// <summary>Height of the image.</summary>
        public float height;

        /// <summary>Denoising threshold.</summary>
        public float divisionDenoise;

        /// <summary>Radius of the blur sample domain.</summary>
        public int divisionDomain;

        /// <summary>Input render.</summary>
        [ShaderResource] public RWTexture2D<float4> inputTexture;

        /// <summary>Output denoised render.</summary>
        [ShaderResource] public RWTexture2D<float4> outputTexture;

        /// <summary>Depth normal texture.</summary>
        [ShaderResource] public RWTexture2D<float4> depthNormals;

        /// <summary>SDF objects list.</summary>
        [ShaderResource] public StructuredBuffer<SDFObjectDTO> sdfObjects;

        /// <summary>Object ID buffer.</summary>
        [ShaderResource] public RWBuffer<uint2> objectIdBuffer;

        // More forgiving thresholds for noisy reflections
        const float DEPTH_THRESHOLD = 0.2f;  // Increased from 0.1f
        const float NORMAL_THRESHOLD = 0.85f; // Relaxed from 0.9f
        const float MIN_ROUGHNESS_BLUR = 0.05f; // Start blurring earlier

        // Use a parabola that you find using a parabolic regression to change the threshold over time in the future!
        public float3 DivisionDenoise(float3 center, int2 pixel, float roughness)
        {
            float3 blurred = new float3(0f);
            int total = 0;
            for (int x = -divisionDomain; x <= divisionDomain; x++)
            {
                for (int y = -divisionDomain; y <= divisionDomain; y++)
                {
                    if (x == 0 && y == 0) continue;
                    blurred += inputTexture[pixel + new int2(x, y)].rgb;
                    total += 1;
                }
            }

            blurred /= total;
            if (math.distance(blurred, center) > math.max(1f - roughness - divisionDenoise, 0f)) return blurred;
            return center;
        }

        /// <summary>Denoises one texel per thread.</summary>
        [Kernel(8, 8, 1)]
        public void Execute(uint3 id)
        {
            int2 pixel = new int2((int)id.x, (int)id.y);

            // Get center pixel data
            float4 centerColor = inputTexture[pixel];
            float4 centerDepthNormal = depthNormals[pixel];
            float centerDepth = centerDepthNormal.x;
            float3 centerNormal = centerDepthNormal.yzw;
            int centerObjId = (int)objectIdBuffer[pixel.x + pixel.y * (int)width].x;

            // If no object hit, no blur needed
            if (centerObjId < 0)
            {
                outputTexture[pixel] = centerColor;
                return;
            }

            // Get roughness from material
            float roughness = sdfObjects[centerObjId].roughness;

            // Skip blur if not reflective or very smooth
            if (sdfObjects[centerObjId].hasReflection == 0 || roughness < MIN_ROUGHNESS_BLUR)
            {
                outputTexture[pixel] = centerColor;
                return;
            }

            // Perform custom Division Denoising. This used to write outputTexture here and then
            // write it again below with the bilateral result, which discarded the denoise pass
            // entirely; the two stages are now chained instead (see finalColor below).
            float3 denoisedColor = centerColor.rgb;
            if (pixel.x > divisionDomain - 1 && pixel.y > divisionDomain - 1 &&
                pixel.x < width - divisionDomain && pixel.y < height - divisionDomain)
                denoisedColor = DivisionDenoise(denoisedColor, pixel, roughness);

            // More aggressive blur radius for rough surfaces
            int blurRadius = (int)math.lerp(2.0f, 4.0f, roughness);

            float3 colorSum = new float3(0f);
            float weightSum = 0f;

            // Bilateral filter with adaptive parameters
            float spatialSigma = roughness * 3f + 0.5f; // Wider for rougher surfaces
            float depthSigma = DEPTH_THRESHOLD * (1f + roughness); // More forgiving for rough
            for (int dy = -blurRadius; dy <= blurRadius; dy++)
            {
                for (int dx = -blurRadius; dx <= blurRadius; dx++)
                {
                    int2 samplePixel = pixel + new int2(dx, dy);
                    if (samplePixel.x < 0 || samplePixel.x >= (int)width ||
                        samplePixel.y < 0 || samplePixel.y >= (int)height)
                        continue;
                    float4 sampleDepthNormal = depthNormals[samplePixel];

                    // Depth similarity with adaptive threshold
                    float depthDiff = math.abs(centerDepth - sampleDepthNormal.x);
                    float depthWeight = math.exp(-(depthDiff * depthDiff) / (2f * depthSigma * depthSigma));

                    // Skip if depth is too different (but more forgiving than before)
                    if (depthWeight < 0.1f) continue;

                    // Normal similarity with softer falloff
                    float normalSim = math.max(0f, math.dot(centerNormal, sampleDepthNormal.yzw));
                    float normalWeight = math.pow(normalSim, 3f); // Softer falloff than hard threshold

                    // Skip if normals are too different
                    if (normalSim < NORMAL_THRESHOLD) continue;
                    float spatialDist = math.sqrt(dx * dx + dy * dy); // Spatial weight (Gaussian)
                    float spatialWeight = math.exp(-(spatialDist * spatialDist) / (2f * spatialSigma * spatialSigma));

                    // Combine weights
                    float weight = spatialWeight * depthWeight * normalWeight;
                    colorSum += inputTexture[samplePixel].xyz * weight;
                    weightSum += weight;
                }
            }

            // Blend between original and blurred based on roughness
            float3 blurredColor = weightSum > 0f ? colorSum / weightSum : centerColor.xyz;

            // Less blending for smoother surfaces, more for rough. Blend from the denoised
            // colour so the Division Denoise stage keeps its effect.
            float blendFactor = math.smoothstep(MIN_ROUGHNESS_BLUR, 0.7f, roughness);
            float3 finalColor = math.lerp(denoisedColor, blurredColor, blendFactor);

            outputTexture[pixel] = new float4(finalColor, 1f);
        }
    }
}