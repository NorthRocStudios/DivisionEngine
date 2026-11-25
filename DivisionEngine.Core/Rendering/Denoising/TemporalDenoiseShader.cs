
//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE.txt file in the project root
// for full license terms.
//

using DivisionEngine.Graphics;
using DivisionEngine.MathLib;

namespace DivisionEngine.Rendering.Denoising
{
    /// <summary>
    /// Temporal accumulation for the denoised reflection/refraction channels.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The spatial filter leaves a small residual that changes every frame because the
    /// stochastic reflection samples are decorrelated per frame; with no history that
    /// residual reads as flicker. This pass reprojects the previous frame's accumulated
    /// radiance through the camera and blends it with the current frame, which is what
    /// makes the image look stable without the heavy spatial blur the old denoiser used.
    /// </para>
    /// <para>
    /// There are no motion vectors in the engine: for a static scene, reprojecting the
    /// world position reconstructed from depth through the previous camera is exact.
    /// Depth, normal and object-ID validation reject disocclusions and moving surfaces,
    /// and the history colour is clamped to the current frame's local min/max so stale
    /// content (a moving reflection, a light change) cannot leave a trail.
    /// </para>
    /// </remarks>
    [Shader]
    public struct TemporalDenoiseShader
    {
        /// <summary>Width of the image.</summary>
        public float width;

        /// <summary>Height of the image.</summary>
        public float height;

        /// <summary>1 when the history textures hold a frame rendered with the same camera.</summary>
        public int historyValid;

        /// <summary>Current-frame weight when the camera is static (lower = more stable).</summary>
        public float temporalAlpha;

        /// <summary>Maximum current-frame weight used while the camera moves quickly.</summary>
        public float maxTemporalAlpha;

        /// <summary>Relative depth tolerance for accepting a history sample.</summary>
        public float depthTolerance;

        /// <summary>Minimum normal dot for accepting a history sample.</summary>
        public float normalTolerance;

        /// <summary>1 when the SDF pass rendered only half of the pixels this frame.</summary>
        public int checkerboard;

        /// <summary>Checkerboard phase used by the SDF pass this frame.</summary>
        public int framePass;

        /// <summary>Current camera position.</summary>
        public float3 cameraPosition;

        /// <summary>Current camera forward axis.</summary>
        public float3 cameraForward;

        /// <summary>Current camera right axis.</summary>
        public float3 cameraRight;

        /// <summary>Current camera up axis.</summary>
        public float3 cameraUp;

        /// <summary>Current camera screen distance.</summary>
        public float camScreenDist;

        /// <summary>Current camera aspect ratio.</summary>
        public float aspect;

        /// <summary>Current camera near plane.</summary>
        public float nearPlane;

        /// <summary>Current camera far plane.</summary>
        public float farPlane;

        /// <summary>Previous camera position.</summary>
        public float3 prevCameraPosition;

        /// <summary>Previous camera forward axis.</summary>
        public float3 prevCameraForward;

        /// <summary>Previous camera right axis.</summary>
        public float3 prevCameraRight;

        /// <summary>Previous camera up axis.</summary>
        public float3 prevCameraUp;

        /// <summary>Previous camera screen distance.</summary>
        public float prevCamScreenDist;

        /// <summary>Previous camera aspect ratio.</summary>
        public float prevAspect;

        /// <summary>Previous camera near plane.</summary>
        public float prevNearPlane;

        /// <summary>Previous camera far plane.</summary>
        public float prevFarPlane;

        /// <summary>Current spatially filtered reflection radiance.</summary>
        [ShaderResource] public Texture2D<float4> currentReflection;

        /// <summary>Current spatially filtered refraction radiance.</summary>
        [ShaderResource] public Texture2D<float4> currentRefraction;

        /// <summary>Previous accumulated reflection (alpha stores the previous object ID).</summary>
        [ShaderResource] public Texture2D<float4> historyReflection;

        /// <summary>Previous accumulated refraction.</summary>
        [ShaderResource] public Texture2D<float4> historyRefraction;

        /// <summary>Current depth (r) and world normals (gba).</summary>
        [ShaderResource] public Texture2D<float4> depthNormals;

        /// <summary>Previous frame's depth (r) and world normals (gba).</summary>
        [ShaderResource] public Texture2D<float4> prevDepthNormals;

        /// <summary>Current per-pixel object index.</summary>
        [ShaderResource] public Buffer<uint2> objectIdBuffer;

        /// <summary>Per-pixel roughness of the primary hit.</summary>
        [ShaderResource] public Texture2D<float4> materialBuffer;

        /// <summary>Accumulated reflection target.</summary>
        [ShaderResource] public RWTexture2D<float4> outputReflection;

        /// <summary>Accumulated refraction target.</summary>
        [ShaderResource] public RWTexture2D<float4> outputRefraction;

        /// <summary>Rec. 709 luminance.</summary>
        private static float Luminance(float3 color)
        {
            return color.x * 0.2126f + color.y * 0.7152f + color.z * 0.0722f;
        }

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

        /// <summary>Accumulates one texel per thread.</summary>
        [Kernel(8, 8, 1)]
        public void Execute(uint3 id)
        {
            int2 pixel = new int2((int)id.x, (int)id.y);
            if (pixel.x >= (int)width || pixel.y >= (int)height) return;

            // Checkerboard rendering refreshes only half of the pixels each frame. A pixel
            // that was not rendered has no fresh depth/material data, so its accumulated
            // result is carried forward unchanged; the spatial filter output for it is
            // stale and must not be blended in. This is what lets temporal accumulation
            // run with the editor's default checkerboard rendering.
            if (checkerboard == 1)
            {
                int expectedParity = framePass ^ (pixel.y & 1);
                if ((pixel.x & 1) != expectedParity)
                {
                    if (historyValid == 1)
                    {
                        float4 cbHistoryRefl = historyReflection[pixel];
                        float4 cbHistoryRefr = historyRefraction[pixel];
                        outputReflection[pixel] = new float4(Sanitize(cbHistoryRefl.rgb),
                            IsFinite(cbHistoryRefl.a) ? cbHistoryRefl.a : 0f);
                        outputRefraction[pixel] = new float4(Sanitize(cbHistoryRefr.rgb),
                            IsFinite(cbHistoryRefr.a) ? cbHistoryRefr.a : 0f);
                    }
                    else
                    {
                        outputReflection[pixel] = new float4(Sanitize(currentReflection[pixel].rgb), 0f);
                        outputRefraction[pixel] = new float4(Sanitize(currentRefraction[pixel].rgb), 0.5f);
                    }
                    return;
                }
            }

            int objectIndex = (int)objectIdBuffer[pixel.x + pixel.y * (int)width].x;
            float3 currentRefl = Sanitize(currentReflection[pixel].rgb);
            float3 currentRefr = Sanitize(currentRefraction[pixel].rgb);
            float currentRoughness = materialBuffer[pixel].x;
            if (!IsFinite(currentRoughness)) currentRoughness = 0.5f;
            float storedId = (float)(objectIndex + 1);

            // Sky and first-frame pixels have no usable history.
            if (historyValid == 0 || objectIndex < 0)
            {
                outputReflection[pixel] = new float4(currentRefl, storedId);
                outputRefraction[pixel] = new float4(currentRefr, currentRoughness);
                return;
            }

            float4 centerDn = depthNormals[pixel];
            if (!IsFinite(centerDn.x) || !IsFinite(centerDn.yzw))
            {
                outputReflection[pixel] = new float4(currentRefl, storedId);
                outputRefraction[pixel] = new float4(currentRefr, currentRoughness);
                return;
            }
            float linearDepth = centerDn.x * (farPlane - nearPlane);
            float3 normal = centerDn.yzw;

            // Reconstruct the world-space position of this pixel and project it into the
            // previous camera. For a static scene this is an exact reprojection.
            float2 uv = (new float2((float)pixel.x + 0.5f, (float)pixel.y + 0.5f)
                / new float2(width, height)) * 2f - 1f;
            float3 rayDir = math.normalize(cameraForward
                + cameraRight * (uv.x * aspect * camScreenDist)
                + cameraUp * (uv.y * camScreenDist));
            float3 worldPosition = cameraPosition + rayDir * linearDepth;
            float3 relative = worldPosition - prevCameraPosition;
            float prevZ = math.dot(relative, prevCameraForward);

            bool accepted = false;
            float3 historyRefl = currentRefl;
            float3 historyRefr = currentRefr;
            float alpha = temporalAlpha;
            float2 prevPixelF = new float2((float)pixel.x, (float)pixel.y);

            if (prevZ > prevNearPlane && prevZ < prevFarPlane)
            {
                float2 prevUv = new float2(
                    math.dot(relative, prevCameraRight) / (prevZ * prevAspect * prevCamScreenDist),
                    math.dot(relative, prevCameraUp) / (prevZ * prevCamScreenDist));
                prevPixelF = (prevUv * 0.5f + 0.5f) * new float2(width, height);
                int2 prevPixel = new int2(
                    (int)math.floor(prevPixelF.x),
                    (int)math.floor(prevPixelF.y));

                if (prevPixel.x >= 0 && prevPixel.x < (int)width &&
                    prevPixel.y >= 0 && prevPixel.y < (int)height)
                {
                    float4 prevDn = prevDepthNormals[prevPixel];
                    // depthNormals.x is the ray length from the camera, normalised by the
                    // depth range. Compare it against the ray length of the reprojected
                    // point, not against its forward depth: at the edge of the screen the
                    // two differ by 1/cos(angle) and the mismatch rejected almost every
                    // pixel outside the centre of the frame.
                    float prevLinear = prevDn.x * (prevFarPlane - prevNearPlane);
                    float currentDistance = math.length(relative);
                    float depthError = math.abs(prevLinear - currentDistance) /
                        math.max(currentDistance, 0.001f);
                    float normalDot = math.dot(normal, prevDn.yzw);
                    float4 previous = historyReflection[prevPixel];
                    float4 previousRefraction = historyRefraction[prevPixel];
                    int previousId = IsFinite(previous.a) ? (int)(previous.a + 0.5f) - 1 : -2;
                    float previousRoughness = previousRefraction.a;
                    bool roughnessMatches = IsFinite(previousRoughness) &&
                        math.abs(previousRoughness - currentRoughness) < 0.06f;
                    bool historyFinite = IsFinite(previous.rgb) && IsFinite(previousRefraction.rgb);

                    if (historyFinite && depthError < depthTolerance && normalDot > normalTolerance &&
                        previousId == objectIndex && roughnessMatches)
                    {
                        // Clamp the history to the current frame's local range. This is the
                        // main ghosting guard: stale or disoccluded content can never leave
                        // a trail because it is pulled back into what the current frame sees.
                        float3 minRefl = currentRefl;
                        float3 maxRefl = currentRefl;
                        float3 minRefr = currentRefr;
                        float3 maxRefr = currentRefr;
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int2 samplePixel = pixel + new int2(dx, dy);
                                if (samplePixel.x < 0 || samplePixel.x >= (int)width ||
                                    samplePixel.y < 0 || samplePixel.y >= (int)height)
                                    continue;
                                if ((int)objectIdBuffer[samplePixel.x + samplePixel.y * (int)width].x != objectIndex)
                                    continue;
                                float3 sampleRefl = currentReflection[samplePixel].rgb;
                                float3 sampleRefr = currentRefraction[samplePixel].rgb;
                                if (!IsFinite(sampleRefl) || !IsFinite(sampleRefr)) continue;
                                minRefl = math.min(minRefl, sampleRefl);
                                maxRefl = math.max(maxRefl, sampleRefl);
                                minRefr = math.min(minRefr, sampleRefr);
                                maxRefr = math.max(maxRefr, sampleRefr);
                            }
                        }

                        historyRefl = math.min(math.max(Sanitize(previous.rgb), minRefl), maxRefl);
                        historyRefr = math.min(math.max(Sanitize(previousRefraction.rgb), minRefr), maxRefr);

                        // Temporal firefly rejection: a rare single-sample sun hit must not
                        // spike through the history. A persistent feature ramps in within a
                        // few frames because the history follows it.
                        float historyLuma = Luminance(previous.rgb);
                        float currentLuma = Luminance(currentRefl);
                        float temporalLimit = historyLuma * 3f + 0.02f;
                        if (currentLuma > temporalLimit)
                            currentRefl *= temporalLimit / math.max(currentLuma, 1e-5f);

                        // Faster camera motion needs a larger current-frame weight so lag
                        // does not smear edges; a static camera keeps most of the history.
                        float motion = math.length(prevPixelF - new float2((float)pixel.x, (float)pixel.y));
                        alpha = math.lerp(temporalAlpha, maxTemporalAlpha, math.saturate(motion / 6f));
                        accepted = true;
                    }
                }
            }

            float3 resultRefl = Sanitize(accepted ? math.lerp(historyRefl, currentRefl, alpha) : currentRefl);
            float3 resultRefr = Sanitize(accepted ? math.lerp(historyRefr, currentRefr, alpha) : currentRefr);

            outputReflection[pixel] = new float4(resultRefl, storedId);
            outputRefraction[pixel] = new float4(resultRefr, currentRoughness);
        }
    }
}
