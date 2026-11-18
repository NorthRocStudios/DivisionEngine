//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.Components.FieldAttributes;

namespace DivisionEngine.Components
{
    /// <summary>
    /// Represents a camera in the world.
    /// </summary>
    public class Camera : IComponent
    {
        /// <summary>
        /// Anti-aliasing method used for this camera.
        /// </summary>
        public enum AntiAliasingMode
        {
            None = 0,
            FXAA = 1,
            CTSS = 2,
        }

        /// <summary>
        /// Debug visualization for the selected anti-aliasing method.
        /// </summary>
        public enum AntiAliasingDebugMode
        {
            Disabled = 0,
            FxaaThreshold = 1,
            FxaaDirection = 2,
            CtssSamples = 3,
            CtssCoverage = 4,
        }

        /// <summary>
        /// Whether this camera is currently active for rendering.
        /// </summary>
        [Tooltip("Whether this camera is currently active for rendering")]
        public bool isActive = true;

        /// <summary>
        /// The viewing angle of the camera.
        /// </summary>
        [Header("Camera Setup")]
        [Tooltip("The viewing angle of the camera")]
        public float fieldOfView = 75f;
        /// <summary>
        /// Near clip plane that the camera starts rendering at.
        /// </summary>
        [Tooltip("Near clip plane that the camera starts rendering at")]
        public float nearClip = 0.01f;
        /// <summary>
        /// Far clip plane that the camera stops rendering at.
        /// </summary>
        [Tooltip("Far clip plane that the camera stops rendering at")]
        public float farClip = 10000f;

        /// <summary>
        /// Max number of trace steps.
        /// </summary>
        [Header("SDF Rendering")]
        [Tooltip("Max number of trace steps")]
        public int maxRaySteps = 256;
        /// <summary>
        /// Max trace steps for shadows.
        /// </summary>
        [Tooltip("Max trace steps for shadows")]
        public int maxShadowRaySteps = 256;

        /// <summary>
        /// Enable built-in reflections denoiser.
        /// </summary>
        [Header("Denoising")]
        public bool enableDivisionDenoise = true;
        /// <summary>
        /// Enable A-Trous wavelet denoising.
        /// </summary>
        [Tooltip("Enable A-Trous wavelet denoising")]
        public bool enableATrousDenoise = true;
        /// <summary>
        /// 0 - 1 denoising threshold for built in method.
        /// </summary>
        [Range(0f, 1f)]
        public float divisionDenoiseThreshold = 0.24f;
        /// <summary>
        /// Size of radius in render image to take blur samples from.
        /// </summary>
        [Range(1, 4)][Tooltip("Size of radius in render image to take blur samples from")]
        public int divisionDenoiseDomain = 2;
        /// <summary>
        /// Step count for a-trous denoiser.
        /// </summary>
        [Range(1, 5)]
        public int aTrousStepCount = 2;

        /// <summary>
        /// Anti-aliasing method: none, FXAA, or SDF-specific cone-traced supersampling.
        /// </summary>
        [Header("Anti-Aliasing")]
        [Tooltip("Anti-aliasing method (FXAA or cone-traced supersampling)")]
        public AntiAliasingMode antiAliasing = AntiAliasingMode.FXAA;
        /// <summary>
        /// FXAA edge detection luminance threshold.
        /// </summary>
        [Tooltip("FXAA edge detection luminance threshold")]
        public float fxaaThreshold = 0.07f;
        /// <summary>
        /// Strength of the blur in the FXAA filter.
        /// </summary>
        public float fxaaStrength = 0.5f;
        /// <summary>
        /// The size of the area edge blur.
        /// </summary>
        [Tooltip("The size of the area edge blur")]
        public int fxaaKernelSize = 2;
        /// <summary>
        /// Radius of the cone in pixels. 1 covers the pixel corners.
        /// </summary>
        [Range(0.25f, 4f)]
        [Tooltip("CTSS cone coverage in pixels (1 = pixel corners)")]
        public float ctssConeCoverage = 1f;

        /// <summary>
        /// Combined debug view for the selected anti-aliasing method. Shared by FXAA
        /// (threshold/direction) and CTSS (samples/coverage), so it is the last option.
        /// </summary>
        [Space(6f)]
        [DisplayName("AA Debug")]
        [Tooltip("Anti-aliasing debug view (FXAA threshold/direction or CTSS samples/coverage)")]
        public AntiAliasingDebugMode debugCTSS = AntiAliasingDebugMode.Disabled;


        public IComponent Clone() => new Camera
        {
            isActive = isActive,

            fieldOfView = fieldOfView,
            nearClip = nearClip,
            farClip = farClip,

            maxRaySteps = maxRaySteps,
            maxShadowRaySteps = maxShadowRaySteps,

            enableDivisionDenoise = enableDivisionDenoise,
            enableATrousDenoise = enableATrousDenoise,
            divisionDenoiseThreshold = divisionDenoiseThreshold,
            divisionDenoiseDomain = divisionDenoiseDomain,
            aTrousStepCount = aTrousStepCount,

            antiAliasing = antiAliasing,
            fxaaThreshold = fxaaThreshold,
            fxaaStrength = fxaaStrength,
            fxaaKernelSize = fxaaKernelSize,
            debugCTSS = debugCTSS,

            ctssConeCoverage = ctssConeCoverage,
        };
    }
}
