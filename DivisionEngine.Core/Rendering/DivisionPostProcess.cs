//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.Components;
using DivisionEngine.Components.SDFs.Effects;
using DivisionEngine.Graphics;
using DivisionEngine.MathLib;
using DivisionEngine.Rendering.AntiAliasing;
using DivisionEngine.Rendering.Effects;

namespace DivisionEngine.Rendering
{
    /// <summary>
    /// Runs the post-processing stage on DivisionCompute: depth of field, FXAA and
    /// the effect chain (blur, HSL grading, bloom, chromatic aberration, vignette).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The chain order matches what the ComputeSharp pipeline used: DOF, then FXAA,
    /// then the colour effects. Shaders are compiled on first use and the intermediate
    /// textures ping-pong.
    /// </para>
    /// <para>
    /// The stage shares the render pipeline's device and textures, so a frame is
    /// processed GPU-to-GPU: the SDF colour texture is bound as the input of the
    /// first pass and the final texture is returned. Nothing is read back or
    /// re-uploaded.
    /// </para>
    /// </remarks>
    internal sealed class DivisionPostProcess : IDisposable
    {
        private readonly DivisionComputeContext context;
        private readonly Dictionary<Type, DivisionShader> shaders = [];

        private RWTexture2D<float4>? texA;
        private RWTexture2D<float4>? texB;
        private RWTexture2D<float4>? blurTemp;
        private RWTexture2D<float4>? bloomBright;
        private RWTexture2D<float4>? bloomTemp;
        private int width;
        private int height;

        /// <summary>
        /// Creates the post-processing stage on an existing device.
        /// </summary>
        /// <param name="context">The pipeline's device; this class does not own it.</param>
        public DivisionPostProcess(DivisionComputeContext context)
        {
            this.context = context;
        }

        /// <summary>
        /// True when the camera and its post-processing settings enable at least one
        /// stage that this class owns.
        /// </summary>
        public static bool HasAnyEffect(PostProcessing settings, Camera camera)
        {
            if (settings is null || camera is null) return false;

            return camera.enableFxaa
                || settings.enableDepthOfField
                || settings.enableBlur
                || settings.enableBloom
                || settings.enableChromaticAberration
                || settings.enableVignette
                || settings.hueShift != 0f
                || settings.saturation != 1f
                || settings.lightness != 1f
                || settings.contrast != 1f;
        }

        /// <summary>
        /// Runs the enabled stages over <paramref name="source"/> and returns the
        /// texture holding the result.
        /// </summary>
        /// <param name="source">The rendered frame.</param>
        /// <param name="depthNormals">
        /// Depth (r) and world normals (gba), or null when no depth buffer is available.
        /// </param>
        /// <param name="hasDepth">False when depth of field must be skipped.</param>
        /// <param name="frameWidth">Frame width in pixels.</param>
        /// <param name="frameHeight">Frame height in pixels.</param>
        /// <param name="settings">The camera's post-processing settings.</param>
        /// <param name="camera">The camera being rendered.</param>
        public RWTexture2D<float4> Process(
            RWTexture2D<float4> source,
            RWTexture2D<float4>? depthNormals,
            bool hasDepth,
            int frameWidth,
            int frameHeight,
            PostProcessing settings,
            Camera camera)
        {
            if (width != frameWidth || height != frameHeight)
            {
                DisposeTextures();
                width = frameWidth;
                height = frameHeight;
            }

            EnsureTexture(ref texA);
            EnsureTexture(ref texB);

            // The input is the pipeline's own texture; every pass alternates between
            // the two scratch targets so a pass never reads the texture it writes.
            RWTexture2D<float4> current = source;
            RWTexture2D<float4> Target() => ReferenceEquals(current, texA) ? texB! : texA!;

            if (settings.enableDepthOfField && hasDepth && depthNormals != null)
            {
                RWTexture2D<float4> target = Target();
                DivisionShader shader = GetShader(typeof(FastDepthOfFieldShader));
                shader.SetFloat("width", frameWidth);
                shader.SetFloat("height", frameHeight);
                shader.SetFloat("nearPlane", camera.nearClip);
                shader.SetFloat("farPlane", camera.farClip);
                shader.SetFloat("focusDistance", settings.focusDistance);
                shader.SetFloat("focalLength", settings.focalLength);
                shader.SetRWTexture2D("inputTexture", current);
                shader.SetRWTexture2D("outputTexture", target);
                shader.SetRWTexture2D("depthNormals", depthNormals);
                shader.Dispatch2D(shader.FindKernel("Execute"), frameWidth, frameHeight);
                current = target;
            }

            if (camera.enableFxaa)
            {
                RWTexture2D<float4> target = Target();
                DivisionShader shader = GetShader(typeof(FXAAShader));
                shader.SetInt("width", frameWidth);
                shader.SetInt("height", frameHeight);
                shader.SetFloat("edgeThreshold", camera.fxaaThreshold);
                shader.SetFloat("strength", camera.fxaaStrength);
                shader.SetInt("area", camera.fxaaKernelSize);
                shader.SetInt("debug", (int)camera.debugFxaa);
                shader.SetRWTexture2D("inputTexture", current);
                shader.SetRWTexture2D("outputTexture", target);
                shader.Dispatch2D(shader.FindKernel("Execute"), frameWidth, frameHeight);
                current = target;
            }

            if (settings.enableBlur)
            {
                EnsureTexture(ref blurTemp);
                RWTexture2D<float4> target = Target();
                DivisionShader shader = GetShader(typeof(TwoPassBlurShader));
                shader.SetFloat("width", frameWidth);
                shader.SetFloat("height", frameHeight);
                shader.SetFloat("blurRadius", settings.blurRadius);
                int kernel = shader.FindKernel("Execute");

                shader.SetInt("passIndex", 0);
                shader.SetRWTexture2D("inputTexture", current);
                shader.SetRWTexture2D("outputTexture", blurTemp!);
                shader.Dispatch2D(kernel, frameWidth, frameHeight);

                shader.SetInt("passIndex", 1);
                shader.SetRWTexture2D("inputTexture", blurTemp!);
                shader.SetRWTexture2D("outputTexture", target);
                shader.Dispatch2D(kernel, frameWidth, frameHeight);

                current = target;
            }

            if (settings.hueShift != 0f || settings.saturation != 1f ||
                settings.lightness != 1f || settings.contrast != 1f)
            {
                RWTexture2D<float4> target = Target();
                DivisionShader shader = GetShader(typeof(HSLShader));
                shader.SetFloat("width", frameWidth);
                shader.SetFloat("height", frameHeight);
                shader.SetFloat("hueShift", settings.hueShift);
                shader.SetFloat("saturation", settings.saturation);
                shader.SetFloat("lightness", settings.lightness);
                shader.SetFloat("contrast", settings.contrast);
                shader.SetRWTexture2D("inputTexture", current);
                shader.SetRWTexture2D("outputTexture", target);
                shader.Dispatch2D(shader.FindKernel("Execute"), frameWidth, frameHeight);
                current = target;
            }

            if (settings.enableBloom)
            {
                EnsureTexture(ref bloomBright);
                EnsureTexture(ref bloomTemp);
                RWTexture2D<float4> target = Target();
                DivisionShader shader = GetShader(typeof(BloomShader));
                shader.SetFloat("width", frameWidth);
                shader.SetFloat("height", frameHeight);
                shader.SetFloat("threshold", settings.bloomThreshold);
                shader.SetFloat("knee", settings.bloomKnee);
                shader.SetFloat("intensity", settings.bloomIntensity);
                shader.SetFloat("blurRadius", settings.bloomRadius);
                shader.SetInt("blurPasses", math.max(1, settings.bloomPasses));
                shader.SetRWTexture2D("inputTexture", current);
                shader.SetRWTexture2D("outputTexture", target);
                shader.SetRWTexture2D("brightTexture", bloomBright!);
                shader.SetRWTexture2D("blurTempTexture", bloomTemp!);
                shader.Dispatch2D(shader.FindKernel("Execute"), frameWidth, frameHeight);
                current = target;
            }

            if (settings.enableChromaticAberration)
            {
                RWTexture2D<float4> target = Target();
                DivisionShader shader = GetShader(typeof(ChromaticAberrationShader));
                shader.SetFloat("width", frameWidth);
                shader.SetFloat("height", frameHeight);
                shader.SetFloat("intensity", settings.chromaticIntensity);
                shader.SetRWTexture2D("inputTexture", current);
                shader.SetRWTexture2D("outputTexture", target);
                shader.Dispatch2D(shader.FindKernel("Execute"), frameWidth, frameHeight);
                current = target;
            }

            if (settings.enableVignette)
            {
                RWTexture2D<float4> target = Target();
                DivisionShader shader = GetShader(typeof(VignetteShader));
                shader.SetFloat("width", frameWidth);
                shader.SetFloat("height", frameHeight);
                shader.SetFloat("vignetteIntensity", settings.vignetteIntensity);
                shader.SetFloat("vignetteSmoothness", settings.vignetteSmoothness);
                shader.SetFloat("vignetteRoundness", settings.vignetteRoundness);
                shader.SetFloat("vignetteRadius", settings.vignetteRadius);
                shader.SetVector("vignetteColor", settings.vignetteColor);
                shader.SetRWTexture2D("inputTexture", current);
                shader.SetRWTexture2D("outputTexture", target);
                shader.Dispatch2D(shader.FindKernel("Execute"), frameWidth, frameHeight);
                current = target;
            }

            return current;
        }

        private DivisionShader GetShader(Type shaderType)
        {
            if (shaders.TryGetValue(shaderType, out DivisionShader? shader))
                return shader;

            shader = context.CompileShader(shaderType);
            shaders[shaderType] = shader;
            return shader;
        }

        private void EnsureTexture(ref RWTexture2D<float4>? texture)
        {
            texture ??= context.CreateRWTexture2D<float4>(width, height);
        }

        private void DisposeTextures()
        {
            texA?.Dispose();
            texA = null;
            texB?.Dispose();
            texB = null;
            blurTemp?.Dispose();
            blurTemp = null;
            bloomBright?.Dispose();
            bloomBright = null;
            bloomTemp?.Dispose();
            bloomTemp = null;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            foreach (DivisionShader shader in shaders.Values)
                shader.Dispose();
            shaders.Clear();

            DisposeTextures();

            // The context is owned by the render pipeline, not by this stage.
        }
    }
}
