
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
    /// Computes A-Trous denoising on an image.
    /// </summary>
    /// <param name="width">Width of the image</param>
    /// <param name="height">Height of the image</param>
    /// <param name="stepSize">Step size for the denoiser</param>
    /// <param name="inputTexture">Input render</param>
    /// <param name="outputTexture">Output denoised render</param>
    /// <param name="depthNormals">Depth normal texture</param>
    /// <param name="sdfObjects">SDF objects list</param>
    /// <param name="objectIdBuffer">Object ID Buffer</param>
    [Shader]
    public struct ATrousDenoiseShader
    {
        /// <summary>Width of the image.</summary>
        public float width;

        /// <summary>Height of the image.</summary>
        public float height;

        /// <summary>Step size for the denoiser.</summary>
        public int stepSize;

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

        /// <summary>A-Trous kernel weights.</summary>
        [ShaderResource] public Buffer<float> kernelBuffer;

        /// <summary>Denoises one texel per thread.</summary>
        [Kernel(8, 8, 1)]
        public void Execute(uint3 id)
        {
            int2 pixel = new int2((int)id.x, (int)id.y);
            int centerObjId = (int)objectIdBuffer[pixel.x + pixel.y * (int)width].x;
            if (centerObjId < 0)
            {
                outputTexture[pixel] = inputTexture[pixel];
                return;
            }

            // Check reflections enabled
            float roughness = sdfObjects[centerObjId].roughness;
            if (sdfObjects[centerObjId].hasReflection == 0 || roughness < 0.05f)
            {
                outputTexture[pixel] = inputTexture[pixel];
                return;
            }

            float4 centerColor = inputTexture[pixel];
            float4 centerDepthNormal = depthNormals[pixel];
            float centerDepth = centerDepthNormal.x;
            float3 centerNormal = centerDepthNormal.yzw;

            float3 sumColor = new float3(0f);
            float sumWeight = 0f;

            // A-trous 5x5 filter with adaptive step size
            for (int yy = -2; yy <= 2; yy++)
            {
                for (int xx = -2; xx <= 2; xx++)
                {
                    int2 offset = new int2(xx * stepSize, yy * stepSize);
                    int2 samplePixel = pixel + offset;
                    if (samplePixel.x < 0 || samplePixel.x >= (int)width ||
                        samplePixel.y < 0 || samplePixel.y >= (int)height)
                        continue;

                    float4 sampleDepthNormal = depthNormals[samplePixel];

                    // Edge-stopping functions
                    float depthDiff = math.abs(centerDepth - sampleDepthNormal.x);
                    float normalDiff = math.max(0.0f, math.dot(centerNormal, sampleDepthNormal.yzw));

                    // Weights
                    float spatialWeight = kernelBuffer[xx + 2] * kernelBuffer[yy + 2];
                    float depthWeight = math.exp(-depthDiff * depthDiff / (2f * 0.1f * 0.1f));
                    float normalWeight = math.pow(normalDiff, 32.0f / roughness);
                    float weight = spatialWeight * depthWeight * normalWeight;

                    sumColor += inputTexture[samplePixel].xyz * weight;
                    sumWeight += weight;
                }
            }

            float3 finalColor = sumWeight > 0f ? sumColor / sumWeight : centerColor.xyz;
            outputTexture[pixel] = new float4(finalColor, 1f);
        }
    }
}