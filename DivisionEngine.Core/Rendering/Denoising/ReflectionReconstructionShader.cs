
//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//

using DivisionEngine.Graphics;
using DivisionEngine.MathLib;

namespace DivisionEngine.Rendering.Denoising
{
    /// <summary>
    /// Smart reconstruction for incomplete ray-traced reflections
    /// Tracks incomplete rays and replaces them with nearby complete samples
    /// </summary>
    [Shader]
    public struct ReflectionReconstructionShader
    {
        /// <summary>Width of the image.</summary>
        public float width;

        /// <summary>Height of the image.</summary>
        public float height;

        /// <summary>Minimum bounces to consider a sample "complete".</summary>
        public float minBounceThreshold;

        /// <summary>Search radius used to reconstruct incomplete samples.</summary>
        public float reconstructionRadius;

        /// <summary>Input render.</summary>
        [ShaderResource] public RWTexture2D<float4> inputTexture;

        /// <summary>Output reconstructed render.</summary>
        [ShaderResource] public RWTexture2D<float4> outputTexture;

        /// <summary>Bounce count per pixel.</summary>
        [ShaderResource] public RWTexture2D<int> bounceCountTexture;

        /// <summary>Depth normal texture.</summary>
        [ShaderResource] public RWTexture2D<float4> depthNormals;

        // First, we need to modify the rendering shader to output bounce counts
        // Add this to SDFShader3D's TraceRayWithReflections:
        // - Store bounce count in a separate texture
        // OR compute "completeness" as: (actual bounces / max possible bounces)

        /// <summary>Reconstructs one texel per thread.</summary>
        [Kernel(8, 8, 1)]
        public void Execute(uint3 id)
        {
            int2 pixel = new int2((int)id.x, (int)id.y);

            // Get bounce count for this pixel
            int centerBounces = bounceCountTexture[pixel];
            float4 centerColor = inputTexture[pixel];
            float4 centerDepthNormal = depthNormals[pixel];
            float centerDepth = centerDepthNormal.x;
            float3 centerNormal = centerDepthNormal.yzw;

            // Check if this pixel has "complete" reflection data
            bool isComplete = centerBounces >= minBounceThreshold;

            // If complete, just pass through (or do minimal denoising)
            if (isComplete)
            {
                // Optionally do very light edge-aware filtering for complete pixels
                outputTexture[pixel] = centerColor;
                return;
            }

            // Incomplete pixel: Need to reconstruct from nearby complete pixels
            float3 colorSum = new float3(0f);
            float weightSum = 0f;
            int completeSamplesFound = 0;

            // Search radius for reconstruction (larger for more incomplete pixels)
            int searchRadius = (int)reconstructionRadius;

            for (int dy = -searchRadius; dy <= searchRadius; dy++)
            {
                for (int dx = -searchRadius; dx <= searchRadius; dx++)
                {
                    int2 samplePixel = pixel + new int2(dx, dy);

                    // Skip out of bounds
                    if (samplePixel.x < 0 || samplePixel.x >= (int)width ||
                        samplePixel.y < 0 || samplePixel.y >= (int)height)
                        continue;

                    // Skip the center pixel (we know it's incomplete)
                    if (dx == 0 && dy == 0) continue;

                    int sampleBounces = bounceCountTexture[samplePixel];

                    // Only use "complete" samples for reconstruction
                    if (sampleBounces < minBounceThreshold) continue;

                    completeSamplesFound++;

                    float4 sampleDepthNormal = depthNormals[samplePixel];

                    // Calculate reconstruction weights
                    // 1. Spatial distance weight
                    float spatialDist = math.length(new float2(dx, dy));
                    float spatialWeight = math.exp(-spatialDist * spatialDist / (2f * reconstructionRadius * reconstructionRadius));

                    // 2. Depth similarity
                    float depthDiff = math.abs(centerDepth - sampleDepthNormal.x);
                    float depthWeight = math.exp(-depthDiff * depthDiff / 0.01f);

                    // 3. Normal similarity
                    float3 sampleNormal = sampleDepthNormal.yzw;
                    float normalSim = math.max(0f, math.dot(centerNormal, sampleNormal));
                    float normalWeight = math.pow(normalSim, 8f);

                    // Combine weights
                    float weight = spatialWeight * depthWeight * normalWeight;

                    colorSum += inputTexture[samplePixel].xyz * weight;
                    weightSum += weight;
                }
            }

            // Reconstruction result
            if (completeSamplesFound > 0 && weightSum > 0f)
            {
                // We have complete neighbors to reconstruct from
                float3 reconstructedColor = colorSum / weightSum;

                // Blend based on how many complete samples we found
                float confidence = math.clamp(completeSamplesFound / (float)(searchRadius * searchRadius * 2f), 0f, 1f);
                float3 finalColor = math.lerp(centerColor.xyz, reconstructedColor, confidence);

                outputTexture[pixel] = new float4(finalColor, 1f);
            }
            else
            {
                // No complete neighbors found - fall back to standard bilateral filter
                // Or keep original (noisy) color
                outputTexture[pixel] = centerColor;
            }
        }
    }
}