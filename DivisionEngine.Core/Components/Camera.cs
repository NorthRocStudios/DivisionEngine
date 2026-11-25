//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE file in the project root
// for full license terms.
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
        /// Enable the unified reflection/refraction denoiser.
        /// </summary>
        [Header("Denoising")]
        [Tooltip("Enable the unified reflection/refraction denoiser")]
        public bool enableDenoise = true;
        /// <summary>
        /// Overall strength of the denoiser (0 keeps the noisy image).
        /// </summary>
        [Tooltip("Overall denoising strength (0 keeps the noisy image)")]
        [Range(0f, 1f)]
        public float denoiseStrength = 1f;
        /// <summary>
        /// A-trous wavelet passes used to clean rough reflection samples.
        /// </summary>
        [Tooltip("A-trous wavelet passes used to clean rough reflections (higher = smoother)")]
        [Range(1, 6)]
        public int denoisePasses = 4;
        /// <summary>
        /// Wide passes used to frost rough refraction. More passes blur rough glass further.
        /// </summary>
        [Tooltip("Wide passes used to frost rough refraction (more = frostier glass)")]
        [Range(1, 4)]
        public int refractionDenoisePasses = 3;
        /// <summary>
        /// Accumulate denoised reflection/refraction radiance over frames. This is what
        /// keeps reflections stable instead of flickering with the per-frame samples.
        /// </summary>
        [Tooltip("Accumulate denoised reflections/refractions over frames (temporal stability)")]
        public bool enableTemporalDenoise = true;
        /// <summary>
        /// Current-frame weight for temporal accumulation. Lower is more stable but lags motion.
        /// </summary>
        [Tooltip("Temporal current-frame weight: lower = more stable, higher = less lag")]
        [Range(0.02f, 0.5f)]
        public float denoiseTemporalWeight = 0.08f;

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

            enableDenoise = enableDenoise,
            denoiseStrength = denoiseStrength,
            denoisePasses = denoisePasses,
            refractionDenoisePasses = refractionDenoisePasses,
            enableTemporalDenoise = enableTemporalDenoise,
            denoiseTemporalWeight = denoiseTemporalWeight,

            antiAliasing = antiAliasing,
            fxaaThreshold = fxaaThreshold,
            fxaaStrength = fxaaStrength,
            fxaaKernelSize = fxaaKernelSize,
            debugCTSS = debugCTSS,

            ctssConeCoverage = ctssConeCoverage,
        };
    }
}
