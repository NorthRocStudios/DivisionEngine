//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.Components;
using DivisionEngine.Components.SDFs.Effects;
using DivisionEngine.MathLib;
using DivisionEngine.Rendering;

namespace DivisionEngine.Systems
{
    /// <summary>
    /// Automatically adjusts postEffects focus distance and focal length based on scene content.
    /// </summary>
    public class DOFAutofocusSystem : SystemBase
    {
        private int framesSinceUpdate = 0;

        /// <summary>
        /// Update interval in frames (update every N frames for performance).
        /// </summary>
        public int UpdateIntervalFrames { get; set; } = 4;

        /// <summary>
        /// Minimum focus distance.
        /// </summary>
        public float MinFocusDistance { get; set; } = 0.5f;

        /// <summary>
        /// Maximum focus distance.
        /// </summary>
        public float MaxFocusDistance { get; set; } = 1000f;

        public override void EditorUpdate()
        {
            if (!EngineCore.IsInPlayMode)
            {
                foreach (var (cameraId, camera, postProcess) in W.QueryData<Camera, PostProcessing>())
                    if (cameraId != EditorCamera.EditorCameraId)
                        ExecuteDOFIteration(camera, postProcess);
            }
        }

        public override void Update()
        {
            foreach (var (_, camera, postProcess) in W.QueryData<Camera, PostProcessing>())
                if (camera.isActive)
                    ExecuteDOFIteration(camera, postProcess);
        }

        private void ExecuteDOFIteration(Camera camera, PostProcessing postEffects)
        {
            // Only update every N frames for performance
            framesSinceUpdate++;
            if (framesSinceUpdate < UpdateIntervalFrames) return;
            framesSinceUpdate = 0;

            // Check the depth buffer from the render pipeline
            if (!postEffects.enableDepthOfField || !postEffects.enableAutofocus ||
                RenderPipeline.Instance?.DepthNormalPixels == null) return;

            // Sample the depth buffer
            var (focusDistance, focalLength) = AnalyzeDepthBuffer(
                RenderPipeline.Instance.DepthNormalPixels,
                RenderPipeline.Instance.RendererWindow?.Size.X ?? 1920,
                RenderPipeline.Instance.RendererWindow?.Size.Y ?? 1080,
                camera.nearClip,
                camera.farClip
            );

            postEffects.focusDistance = math.clamp(
                focusDistance,
                MinFocusDistance,
                MaxFocusDistance
            );
            postEffects.focalLength = math.max(focalLength, 1f);
        }

        /// <summary>
        /// Analyzes the depth buffer to find optimal focus settings.
        /// </summary>
        private static (float focusDistance, float focalLength) AnalyzeDepthBuffer(
            float4[] depthNormals,
            int width,
            int height,
            float nearPlane,
            float farPlane)
        {
            float centerX = width / 2f;
            float centerY = height / 2f;

            // Start with center pixel as focus distance
            int centerIndex = (int)centerX + (int)centerY * width;
            if (centerIndex >= depthNormals.Length) return (0f, farPlane);
            float focusDistance = depthNormals[centerIndex].X * farPlane;

            // Find the closest object within the center region (25% of screen)
            int regionRadius = (int)(math.min(width, height) * 0.125f); // 12.5% radius from center
            float closestDepth = farPlane;

            for (int y = (int)centerY - regionRadius; y <= (int)centerY + regionRadius; y++)
            {
                if (y < 0 || y >= height) continue;
                for (int x = (int)centerX - regionRadius; x <= (int)centerX + regionRadius; x++)
                {
                    if (x < 0 || x >= width) continue;
                    int index = x + y * width;
                    float depth = depthNormals[index].X * farPlane;

                    // Skip background
                    if (depth < farPlane - 1f && depth < closestDepth) closestDepth = depth;
                }
            }

            // If we found a closer object in the center region, use that
            if (closestDepth < farPlane - 1f) focusDistance = closestDepth;

            // Calculate focal length based on depth range in the scene
            float minDepth = farPlane;
            float maxDepth = nearPlane;

            // Sample every 8th pixel for performance
            for (int i = 0; i < depthNormals.Length; i += 8)
            {
                float depth = depthNormals[i].X * farPlane;
                if (depth < farPlane - 1f) // Skip background
                {
                    if (depth < minDepth) minDepth = depth;
                    if (depth > maxDepth) maxDepth = depth;
                }
            }

            // Calc depth range
            float depthRange = maxDepth - minDepth;
            float focalLength = math.max(depthRange * 0.3f, 5f);
            focalLength = math.min(focalLength, 100f);
            return (focusDistance, focalLength);
        }
    }
}