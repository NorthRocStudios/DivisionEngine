//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE file in the project root
// for full license terms.
//
using DivisionEngine.Components.FieldAttributes;

namespace DivisionEngine.Components.SDFs.Effects
{
    /// <summary>
    /// Component for changing the post-processing effects on a camera.
    /// </summary>
    public class PostProcessing : IComponent
    {
        /// <summary>
        /// How far the hue is shifted from -180 to 180 degrees.
        /// </summary>
        [Header("Color Grading")]
        [Tooltip("How far the hue is shifted from -180 to 180 degrees")]
        [Range(-180f, 180f)] public float hueShift = 0f;
        /// <summary>
        /// Saturation of the render result, from 0 to 2.
        /// </summary>
        /// <remarks>1 is default saturation</remarks>
        [Range(0f, 2f)] public float saturation = 1f;
        /// <summary>
        /// Lightness of the render result, from 0 to 2.
        /// </summary>
        /// <remarks>1 is default lightness</remarks>
        [Range(0f, 2f)] public float lightness = 1f;
        /// <summary>
        /// Contrast of the render result, from 0 to 2.
        /// </summary>
        /// <remarks>1 is default contrast</remarks>
        [Range(0f, 2f)] public float contrast = 1f;

        /// <summary>
        /// Enables or disables the vignette effect.
        /// </summary>
        [Header("Vignette")]
        [Tooltip("Enables or disables the vignette effect")]
        public bool enableVignette = false;
        /// <summary>
        /// Intensity of the vignette effect.
        /// </summary>
        [Tooltip("Intensity of the vignette effect")]
        [Range(0f, 1f)] public float vignetteIntensity = 0.5f;
        /// <summary>
        /// How far the vignette starts from the center of the screen.
        /// </summary>
        [Tooltip("How far the vignette starts from the center of the screen")]
        [Range(0f, 2f)] public float vignetteRadius = 1f;
        /// <summary>
        /// How quick the vignette falls off.
        /// </summary>
        [Tooltip("How quick the vignette falls off")]
        [Range(0f, 1f)] public float vignetteSmoothness = 0.7f;
        /// <summary>
        /// How round vs square the vignette is.
        /// </summary>
        [Tooltip("How round vs square the vignette is")]
        [Range(0f, 1f)] public float vignetteRoundness = 0.8f;
        /// <summary>
        /// Color of the vignette.
        /// </summary>
        [Tooltip("Color of the vignette")]
        [Color] public float3 vignetteColor = new float3(0f, 0f, 0f);

        /// <summary>
        /// Turns general blur on or off.
        /// </summary>
        [Header("Blur")]
        public bool enableBlur = false;
        /// <summary>
        /// Radius of the blur effect.
        /// </summary>
        [Tooltip("Radius of the blur effect")]
        [Range(0f, 50f)] public float blurRadius = 5f;

        /// <summary>
        /// If bloom is enabled.
        /// </summary>
        [Header("Bloom")]
        public bool enableBloom = false;
        /// <summary>
        /// Intensity of bloom pixels.
        /// </summary>
        [Tooltip("Intensity of bloom pixels")]
        [Range(0f, 10f)] public float bloomIntensity = 1f;
        /// <summary>
        /// Threshold for pixels to bloom.
        /// </summary>
        [Tooltip("Threshold for pixels to bloom")]
        [Range(0f, 2f)] public float bloomThreshold = 0.2f;
        /// <summary>
        /// Smoothness of the transition between bloom pixels and pixels that don't bloom.
        /// </summary>
        [Tooltip("Smoothness of the transition between bloom pixels and pixels that don't bloom")]
        [Range(0f, 1f)] public float bloomKnee = 0.2f;
        /// <summary>
        /// Radius of bloom effect.
        /// </summary>
        [Tooltip("Radius of bloom effect")]
        [Range(0f, 20f)] public float bloomRadius = 5f;
        /// <summary>
        /// Number of bloom blur passes.
        /// </summary>
        [Tooltip("Number of bloom blur passes")]
        public int bloomPasses = 3;

        /// <summary>
        /// Enable or disable depth of field.
        /// </summary>
        [Header("Depth of Field")]
        public bool enableDepthOfField = false;
        /// <summary>
        /// Attempts to calculate focal length and focus distance based on scene content.
        /// </summary>
        [Tooltip("Attempts to calculate focal length and focus distance based on scene content")]
        public bool enableAutofocus = false;
        /// <summary>
        /// Distance at which DOF = 0.
        /// </summary>
        [Tooltip("Distance at which DOF = 0")]
        public float focusDistance = 24f;
        /// <summary>
        /// Falloff distance for DOF.
        /// </summary>
        [Tooltip("Falloff distance for DOF")]
        public float focalLength = 26f;

        /// <summary>
        /// Turns the ACES tonemapper on or off.
        /// </summary>
        [Header("Tonemapping")]
        [Tooltip("Turns the ACES tonemapper on or off for this camera")]
        public bool enableAcesTonemapper = true;

        [Header("Chromatic Aberration")]
        public bool enableChromaticAberration = false;
        [Range(0f, 0.05f)] public float chromaticIntensity = 0.003f; // Subtle by default

        public IComponent Clone() => new PostProcessing
        {
            hueShift = hueShift,
            saturation = saturation,
            lightness = lightness,
            contrast = contrast,

            enableVignette = enableVignette,
            vignetteIntensity = vignetteIntensity,
            vignetteSmoothness = vignetteSmoothness,
            vignetteRoundness = vignetteRoundness,
            vignetteRadius = vignetteRadius,
            vignetteColor = vignetteColor,

            enableBlur = enableBlur,
            blurRadius = blurRadius,

            enableBloom = enableBloom,
            bloomIntensity = bloomIntensity,
            bloomThreshold = bloomThreshold,
            bloomKnee = bloomKnee,
            bloomRadius = bloomRadius,
            bloomPasses = bloomPasses,

            enableDepthOfField = enableDepthOfField,
            enableAutofocus = enableAutofocus,
            focusDistance = focusDistance,
            focalLength = focalLength,

            enableAcesTonemapper = enableAcesTonemapper,

            enableChromaticAberration = enableChromaticAberration,
            chromaticIntensity = chromaticIntensity,
        };
    }
}
