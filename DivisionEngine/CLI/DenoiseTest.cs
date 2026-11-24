
//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using DivisionEngine.Components;
using DivisionEngine.Components.Lights;
using DivisionEngine.Components.SDFs;
using DivisionEngine.Components.SDFs.Effects;
using DivisionEngine.Components.SDFs.Primitives;
using DivisionEngine.MathLib;
using DivisionEngine.MathUtilities;
using DivisionEngine.Projects.Assets;
using DivisionEngine.Rendering;
using DivisionEngine.Systems;
using SkiaSharp;
using Environment = DivisionEngine.Components.Environment;

namespace DivisionEngine.Editor
{
    /// <summary>
    /// Headless quality test for the unified reflection/refraction denoiser.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Builds a deterministic scene with a row of semi-rough metal spheres, a row of
    /// rough glass spheres (one of them refraction-only), a metal slab carrying a
    /// checkerboard roughness texture and a diffuse floor/backdrop that the denoiser
    /// must not touch. It then renders:
    /// </para>
    /// <list type="bullet">
    /// <item><c>noisy</c> - two primary samples, denoiser off;</item>
    /// <item><c>denoised</c> - two primary samples, unified denoiser on;</item>
    /// <item><c>reference</c> - many primary samples, denoiser off;</item>
    /// <item>debug views for material flags, per-pixel roughness and the reflection
    /// cutoff mask, used to build the measurement masks.</item>
    /// </list>
    /// <para>
    /// All configurations use a frozen frame counter so the denoised frame filters
    /// exactly the noisy samples it is compared against. Gates check error against the
    /// reference, frosted-glass blur, that non-reflective surfaces are bit-identical,
    /// that rough texels are not filtered while smooth ones are, and that the
    /// reflection cutoff actually removes raymarch steps. Exits non-zero on failure.
    /// </para>
    /// </remarks>
    internal static class DenoiseTest
    {
        /// <summary>Synthetic asset id for the roughness checkerboard texture.</summary>
        private const string RoughnessTextureId = "denoisetest:roughness-checklist";

        /// <summary>Frozen frame counter so every configuration sees the same samples.</summary>
        private const int FrozenFrame = 8;

        private static readonly float3 LumaWeights = new(0.2126f, 0.7152f, 0.0722f);

        /// <summary>Runs the <c>--cli denoisetest</c> command.</summary>
        public static bool Run(string[] args)
        {
            int width = GetInt(args, "--width", 640);
            int height = GetInt(args, "--height", 360);
            int frames = GetInt(args, "--fixed-frame", 2);
            int noisySpp = GetInt(args, "--spp", 2);
            int referenceSpp = GetInt(args, "--reference-spp", 32);
            float glassCutoff = GetFloat(args, "--glass-cutoff", 0.65f);
            bool temporal = !HasFlag(args, "--no-temporal");
            string outputDirectory = GetString(args, "--out", "artifacts/denoisetest")!;
            bool visualOnly = HasFlag(args, "--no-gates");
            if (width < 64 || height < 64 || frames < 1)
                throw new ArgumentException("--width/--height must be at least 64 and --fixed-frame at least 1.");
            if (referenceSpp < 4 || referenceSpp > 64)
                throw new ArgumentException("--reference-spp must be between 4 and 64.");

            Console.WriteLine(
                $"denoisetest: {width}x{height}, noisy {noisySpp} spp, reference {referenceSpp} spp, {frames} frame(s)");

            RenderPipeline.DeterministicCapture = true;
            RenderPipeline.FrameCountOverride = FrozenFrame;
            RenderPipeline.SampleCountOverride = noisySpp;
            MathUtilities.Random.Seed(12345);

            InstallRoughnessTexture();

            WorldManager.CreateDefaultWorld(makeCurrent: true);
            World world = WorldManager.CurrentWorld!;
            StripDefaultScene(world);
            Camera camera = ConfigureCamera(world);
            camera.enableTemporalDenoise = temporal;
            PostProcessing postProcess = world.QueryData<PostProcessing>().First().Item2;
            ConfigurePostProcessing(postProcess);
            (uint slabId, float3 slabCenter, float3 slabSize) = BuildScene(world, glassCutoff);

            RenderPipeline pipeline = new();
            if (!pipeline.BindCurrentWorld())
                throw new InvalidOperationException("Failed to bind the denoisetest world.");

            // Masks for everything below.
            DebugMasks masks = RenderDebugMasks(pipeline, camera, width, height, frames);

            // Denoiser on/off comparison at the same sample sequence.
            camera.enableDenoise = false;
            pipeline.ResetDenoiseHistory();
            float4[] noisy = RenderSnapshot(pipeline, width, height, frames);
            camera.enableDenoise = true;
            pipeline.ResetDenoiseHistory();
            float4[] denoised = RenderSnapshot(pipeline, width, height, frames);

            // Channel views make it obvious whether residual sparkle belongs to the
            // stochastic reflection or to the deterministic refraction.
            RenderPipeline.DenoiseChannelDebug = 1;
            pipeline.ResetDenoiseHistory();
            float4[] reflectionChannel = RenderSnapshot(pipeline, width, height, frames);
            RenderPipeline.DenoiseChannelDebug = 2;
            pipeline.ResetDenoiseHistory();
            float4[] refractionChannel = RenderSnapshot(pipeline, width, height, frames);
            RenderPipeline.DenoiseChannelDebug = 3;
            camera.enableDenoise = false;
            pipeline.ResetDenoiseHistory();
            float4[] baseChannel = RenderSnapshot(pipeline, width, height, frames);
            camera.enableDenoise = true;
            RenderPipeline.DenoiseChannelDebug = 0;

            RenderPipeline.SampleCountOverride = referenceSpp;
            camera.enableDenoise = false;
            pipeline.ResetDenoiseHistory();
            float4[] reference = RenderSnapshot(pipeline, width, height, frames);
            RenderPipeline.SampleCountOverride = noisySpp;
            camera.enableDenoise = true;
            pipeline.ResetDenoiseHistory();

            // ---- metrics ---------------------------------------------------------
            bool[] traced = masks.TracedReflection;
            // Metal-only reflection mask: rough glass deliberately frosts its refracted
            // background, which a sharp reference cannot represent, so glass error is
            // judged by the frost gates instead.
            bool[] tracedMetal = And(traced, Not(masks.Refraction));
            bool[] semiRough = And(tracedMetal, Between(masks.Roughness, 0.12f, 0.6f));
            bool[] smooth = And(tracedMetal, LessThan(masks.Roughness, 0.12f));
            bool[] roughGlass = And(masks.Refraction, MoreThan(masks.Roughness, 0.25f));
            bool[] refractionOnly = And(And(masks.Refraction, Not(masks.Reflection)), MoreThan(masks.Roughness, 0.25f));
            bool[] smoothGlass = And(masks.Refraction, LessThan(masks.Roughness, 0.1f));
            bool[] untouched = Not(Or(masks.Reflection, masks.Refraction));
            bool[] approximated = masks.ApproximatedReflection;
            // Ignore the two-pixel roughness transition at checker-block boundaries: those
            // pixels have intermediate roughness and are legitimately partially filtered.
            bool[] approximatedCore = Erode(approximated, width, height, 2);

            double noisyError = Rmse(noisy, reference, tracedMetal);
            double denoisedError = Rmse(denoised, reference, tracedMetal);
            double noisySemiRough = Rmse(noisy, reference, semiRough);
            double denoisedSemiRough = Rmse(denoised, reference, semiRough);
            double noisySmooth = Rmse(noisy, reference, smooth);
            double denoisedSmooth = Rmse(denoised, reference, smooth);

            // Per-roughness bands: a spatially narrow filter helps where the GGX lobe is
            // already small, while a wide lobe benefits from a larger kernel. Printing
            // each band makes it obvious when the filter starts trading noise for bias.
            (string Label, float Low, float High)[] bands =
            [
                ("roughness 0.00-0.10", 0.0f, 0.10f),
                ("roughness 0.10-0.25", 0.10f, 0.25f),
                ("roughness 0.25-0.45", 0.25f, 0.45f),
                ("roughness 0.45-0.70", 0.45f, 0.70f),
            ];
            List<string> bandLines = [];
            foreach ((string label, float low, float high) in bands)
            {
                bool[] band = And(tracedMetal, Between(masks.Roughness, low, high));
                if (Count(band) == 0) continue;
                bandLines.Add(Report(label + " (metal)", Rmse(noisy, reference, band), Rmse(denoised, reference, band), percent: true));
            }

            double glassNoisyHf = HighFrequency(noisy, width, height, roughGlass);
            double glassDenoisedHf = HighFrequency(denoised, width, height, roughGlass);
            double refractionOnlyNoisyHf = HighFrequency(noisy, width, height, refractionOnly);
            double refractionOnlyDenoisedHf = HighFrequency(denoised, width, height, refractionOnly);
            double smoothGlassNoisyHf = HighFrequency(noisy, width, height, smoothGlass);
            double smoothGlassDenoisedHf = HighFrequency(denoised, width, height, smoothGlass);

            double untouchedMaxDiff = MaxDifference(noisy, denoised, untouched);
            int untouchedPixels = Count(untouched);
            double approxMaxDiff = MaxDifference(noisy, denoised, approximatedCore);
            double approxMeanDiff = MeanDifference(noisy, denoised, approximatedCore);
            int approxChanged = CountChanged(noisy, denoised, approximatedCore, 1e-4f);
            double smoothTexelsChange = MeanDifference(noisy, denoised, smooth);
            double roughTexelsChange = approxMeanDiff;

            // Cutoff budget: same debug view with every reflection cutoff wide open and
            // then tight. Material cutoffs are restored before the visual captures.
            float[]? storedCutoffs = null;
            double stepsOpen = RenderMeanStepCount(pipeline, world, camera, width, height, frames, 1f, out storedCutoffs);
            double stepsCut = RenderMeanStepCount(pipeline, world, camera, width, height, frames, 0.4f, out _);
            RestoreCutoffs(world, storedCutoffs);

            // Temporal stability: consecutive frames use different stochastic samples, so
            // without accumulation the residual flickers. Live frame counts (not the frozen
            // test counter) are required for this measurement.
            TemporalSeries temporalStats = MeasureTemporalStability(pipeline, camera, width, height, masks);
            double noisyTemporalStd = temporalStats.NoisyStd;
            double denoisedTemporalStd = temporalStats.DenoisedStd;
            double noisyTemporalMotion = temporalStats.NoisyStep;
            double denoisedTemporalMotion = temporalStats.DenoisedStep;

            // Optional moving-camera capture to eyeball temporal lag/ghosting.
            if (HasFlag(args, "--motion"))
            {
                string motionDir = Path.Combine(outputDirectory, "motion");
                Directory.CreateDirectory(motionDir);
                (uint cameraEntityId, Camera _) = world.QueryData<Camera>().First();
                Transform cameraTransform = world.GetComponent<Transform>(cameraEntityId)!;
                float3 start = cameraTransform.position;
                RenderPipeline.FrameCountOverride = null;
                camera.enableDenoise = true;
                camera.enableTemporalDenoise = true;
                pipeline.ResetDenoiseHistory();
                for (int i = 0; i < 12; i++)
                {
                    cameraTransform.position = start + new float3(i * 0.35f, 0f, 0f);
                    float4[] motion = RenderSnapshot(pipeline, width, height, 1);
                    SavePng(Path.Combine(motionDir, $"frame-{i:00}.png"), motion, width, height);
                }
                cameraTransform.position = start;
                camera.enableTemporalDenoise = temporal;
                RenderPipeline.FrameCountOverride = FrozenFrame;
                pipeline.ResetDenoiseHistory();
                Console.WriteLine($"denoisetest: wrote camera-motion frames to {motionDir}");
            }

            int nanPixels = CountNaN(denoised);

            // ---- report ----------------------------------------------------------
            string[] lines =
            [
                Report("reflection RMSE (traced, vs reference)", noisyError, denoisedError, percent: true),
                Report("reflection RMSE (roughness 0.12-0.6)", noisySemiRough, denoisedSemiRough, percent: true),
                Report("reflection RMSE (mirror, roughness < 0.12)", noisySmooth, denoisedSmooth, percent: true),
                Report("frosted glass high-frequency energy", glassNoisyHf, glassDenoisedHf, percent: true),
                Report("refraction-only glass high-frequency energy", refractionOnlyNoisyHf, refractionOnlyDenoisedHf, percent: true),
                Report("smooth glass high-frequency energy", smoothGlassNoisyHf, smoothGlassDenoisedHf, percent: true),
                Report("ray steps per reflective pixel (open -> cutoff 0.4)", stepsOpen, stepsCut, percent: true),
                Report("temporal std (secondary pixels, 8 live frames)", noisyTemporalStd, denoisedTemporalStd, percent: true),
                Report("temporal frame-to-frame change", noisyTemporalMotion, denoisedTemporalMotion, percent: true),
            ];
            foreach (string line in lines) Console.WriteLine("  " + line);
            foreach (string line in bandLines) Console.WriteLine("  " + line);
            Console.WriteLine($"  untouched pixels bit-identical : maxDiff={untouchedMaxDiff:E2} over {untouchedPixels} px");
            Console.WriteLine($"  approximated pixels unchanged  : mean={approxMeanDiff:E2}, max={approxMaxDiff:E2}, changed={approxChanged}/{Count(approximatedCore)} (2px eroded)");
            Console.WriteLine($"  denoise change, smooth texels  : mean={smoothTexelsChange:F6}");
            Console.WriteLine($"  denoise change, rough texels   : mean={roughTexelsChange:F6}");
            Console.WriteLine($"  NaN/Inf pixels                 : {nanPixels}");

            WriteArtifacts(outputDirectory, width, height,
                noisy, denoised, reference, masks, untouched,
                reflectionChannel, refractionChannel, baseChannel,
                temporalStats.NoisyStdMap, temporalStats.DenoisedStdMap,
                temporalStats.DenoisedLast);

            // ---- gates -----------------------------------------------------------
            List<string> failures = [];
            void Gate(string name, bool condition, string detail)
            {
                Console.WriteLine($"  {(condition ? "PASS" : "FAIL")} {name}: {detail}");
                if (!condition) failures.Add(name);
            }

            Gate("non-reflective pixels untouched", untouchedMaxDiff <= 1e-6 && untouchedPixels > width * height / 4,
                $"{untouchedPixels} px, maxDiff {untouchedMaxDiff:E2}");
            Gate("reflection denoising reduces error", denoisedError <= noisyError * 0.95,
                $"{noisyError:F5} -> {denoisedError:F5}");
            Gate("semi-rough reflections improve", denoisedSemiRough <= noisySemiRough * 0.90,
                $"{noisySemiRough:F5} -> {denoisedSemiRough:F5}");
            Gate("mirror reflections not blurred", denoisedSmooth <= noisySmooth * 1.05,
                $"{noisySmooth:F5} -> {denoisedSmooth:F5}");
            Gate("rough glass is frosted", glassDenoisedHf <= glassNoisyHf * 0.85,
                $"{glassNoisyHf:F5} -> {glassDenoisedHf:F5}");
            Gate("refraction-only glass is frosted", refractionOnlyDenoisedHf <= refractionOnlyNoisyHf * 0.80,
                $"{refractionOnlyNoisyHf:F5} -> {refractionOnlyDenoisedHf:F5}");
            Gate("smooth glass stays sharp", smoothGlassDenoisedHf >= smoothGlassNoisyHf * 0.75,
                $"{smoothGlassNoisyHf:F5} -> {smoothGlassDenoisedHf:F5}");
            Gate("rough texels are not denoised",
                approxMeanDiff <= 1e-5 && approxChanged <= Count(approximatedCore) / 50,
                $"{Count(approximatedCore)} px, mean {approxMeanDiff:E2}, {approxChanged} changed");
            Gate("smooth texels are denoised", smoothTexelsChange >= 1e-4,
                $"mean change {smoothTexelsChange:F6}");
            Gate("reflection cutoff removes raymarch steps", stepsCut <= stepsOpen * 0.92,
                $"{stepsOpen:F4} -> {stepsCut:F4}");
            if (temporal)
            {
                Gate("temporal accumulation stabilises reflections",
                    denoisedTemporalStd <= noisyTemporalStd * 0.25,
                    $"{noisyTemporalStd:F5} -> {denoisedTemporalStd:F5}");
                Gate("temporal residual is small", denoisedTemporalStd <= 0.01,
                    $"{denoisedTemporalStd:F5}");
            }
            Gate("no NaN/Inf", nanPixels == 0, $"{nanPixels} px");
            _ = slabCenter;
            _ = slabSize;
            _ = slabId;

            if (visualOnly)
            {
                Console.WriteLine("denoisetest: gates skipped (--no-gates)");
                DivisionCLI.SetExitCode(0);
                return true;
            }

            if (failures.Count > 0)
            {
                Console.Error.WriteLine($"denoisetest: {failures.Count} gate(s) failed: {string.Join(", ", failures)}");
                DivisionCLI.SetExitCode(1);
            }
            else
            {
                Console.WriteLine("denoisetest: all gates passed");
                DivisionCLI.SetExitCode(0);
            }
            return true;
        }

        #region scene

        /// <summary>
        /// Installs a 64x64 roughness checkerboard. Blocks are either nearly smooth
        /// (0.04) or fully rough (0.94); the slab's cutoff (0.35) puts the rough blocks
        /// above the reflection budget, so they must not be denoised at all.
        /// </summary>
        private static void InstallRoughnessTexture()
        {
            const int size = 64;
            const int block = 8;
            uint[] pixels = new uint[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool roughBlock = ((x / block) + (y / block)) % 2 == 0;
                    byte value = (byte)(roughBlock ? 240 : 10); // 0.94 / 0.04
                    // Packed as RGBA with R in the high byte, matching UnpackRGBA.
                    pixels[y * size + x] = (uint)((value << 24) | (value << 16) | (value << 8) | 0xFF);
                }
            }

            int index = TextureSystem.InstallTestTexture(RoughnessTextureId, pixels, size, size);
            if (index < 0) throw new InvalidOperationException("Could not install the synthetic roughness texture.");
        }

        /// <summary>Removes the stock default-scene objects, keeping camera, sun and environment.</summary>
        private static void StripDefaultScene(World world)
        {
            List<uint> remove = [];
            foreach ((uint id, Transform _) in world.QueryData<Transform>())
            {
                if (world.HasComponent<Camera>(id) ||
                    world.HasComponent<Environment>(id) ||
                    world.HasComponent<DirectionalLight>(id))
                    continue;
                remove.Add(id);
            }
            foreach (uint id in remove) world.DestroyEntity(id);
        }

        private static Camera ConfigureCamera(World world)
        {
            (uint cameraId, Camera camera) = world.QueryData<Camera>().First();
            Transform transform = world.GetComponent<Transform>(cameraId)!;
            transform.position = new float3(0f, 2.7f, 10.8f);
            transform.rotation = new float3(-0.055f, 0f, 0f).EulerToQuaternion();

            camera.fieldOfView = 62f;
            camera.nearClip = 0.05f;
            camera.farClip = 120f;
            camera.maxRaySteps = 256;
            camera.maxShadowRaySteps = 256;
            camera.antiAliasing = Camera.AntiAliasingMode.None;
            camera.enableDenoise = true;
            camera.denoiseStrength = 1f;
            camera.denoisePasses = 4;
            camera.refractionDenoisePasses = 3;
            return camera;
        }

        private static void ConfigurePostProcessing(PostProcessing postProcess)
        {
            postProcess.enableAcesTonemapper = true;
            postProcess.enableDepthOfField = false;
            postProcess.enableBlur = false;
            postProcess.enableBloom = false;
            postProcess.enableChromaticAberration = false;
            postProcess.enableVignette = false;
            postProcess.hueShift = 0f;
            postProcess.saturation = 1f;
            postProcess.lightness = 1f;
            postProcess.contrast = 1f;
        }

        /// <summary>
        /// Builds the test scene: a diffuse floor and backdrop (must stay untouched), a
        /// row of metal spheres from mirror to semi-rough, a row of glass spheres from
        /// polished to frosted (one refraction-only), and a textured metal slab.
        /// </summary>
        private static (uint SlabId, float3 Center, float3 Size) BuildScene(World world, float glassCutoff)
        {
            // Diffuse floor and backdrop. Neither carries a Reflections/Refractions
            // component, so the denoiser must leave every one of their pixels intact.
            AddBox(world, "Floor", new float3(0f, -0.45f, -2f), new float3(15f, 0.45f, 11f),
                new SDFMaterial
                {
                    albedoColor = new float4(0.24f, 0.25f, 0.27f, 1f),
                    metallic = 0.15f,
                    roughness = 0.92f,
                });
            AddBox(world, "Backdrop", new float3(0f, 4.5f, -7.5f), new float3(10f, 5.5f, 0.3f),
                new SDFMaterial
                {
                    albedoColor = new float4(0.62f, 0.34f, 0.22f, 1f),
                    metallic = 0f,
                    roughness = 0.8f,
                });

            // Metal spheres on the left, from mirror to rough. Kept side by side with the
            // glass row so every sphere is visible to the camera.
            float[] metalRoughness = [0.06f, 0.3f, 0.6f];
            for (int i = 0; i < metalRoughness.Length; i++)
            {
                AddSphere(world, $"Metal {metalRoughness[i]:F2}", new float3(-6.4f + i * 2.2f, 1.2f, -0.5f), 1.0f,
                    new SDFMaterial
                    {
                        albedoColor = new float4(0.82f, 0.84f, 0.88f, 1f),
                        metallic = 1f,
                        roughness = metalRoughness[i],
                        reflectionCutoff = 0.65f,
                    },
                    reflections: true,
                    refractions: false);
            }

            // Glass spheres on the right: polished, frosted, very frosted, and a
            // refraction-only frosted sphere that the old denoiser skipped entirely.
            float[] glassRoughness = [0.02f, 0.35f, 0.6f, 0.6f];
            for (int i = 0; i < glassRoughness.Length; i++)
            {
                bool refractionOnly = i == glassRoughness.Length - 1;
                AddSphere(world, refractionOnly ? "Frosted refraction-only glass" : $"Glass {glassRoughness[i]:F2}",
                    new float3(1.0f + i * 2.2f, 1.2f, -0.5f), 1.05f,
                    new SDFMaterial
                    {
                        albedoColor = new float4(0.55f, 0.82f, 0.9f, 1f),
                        metallic = 0f,
                        roughness = glassRoughness[i],
                        ior = 1.5f,
                        reflectance = 1.4f,
                        reflectionCutoff = glassCutoff,
                        ambientOcclusion = 0.4f,
                    },
                    reflections: !refractionOnly,
                    refractions: true);
            }

            // Metal slab with a checkerboard roughness texture: smooth blocks get real,
            // denoised reflections; rough blocks are above the cutoff and must not be
            // filtered (they use the cheap environment approximation instead).
            uint slab = world.CreateEntity("Roughness Texture Slab");
            world.AddComponent(slab, new Transform { position = new float3(0f, -0.05f, 3.4f) });
            world.AddComponent(slab, new SDFBox { size = new float3(5.5f, 0.15f, 1.6f) });
            SDFMaterial slabMaterial = new()
            {
                albedoColor = new float4(0.72f, 0.74f, 0.78f, 1f),
                metallic = 1f,
                roughness = 0.5f,
                reflectionCutoff = 0.35f,
                uvScale = new float2(5f, 5f),
                triplanarBlend = 1f,
            };
            slabMaterial.roughnessMap = new AssetRef<TextureAsset>(RoughnessTextureId);
            world.AddComponent(slab, slabMaterial);
            world.AddComponent(slab, new Shadows());
            world.AddComponent(slab, new Reflections());

            return (slab, new float3(0f, -0.05f, 3.4f), new float3(5.5f, 0.15f, 1.6f));
        }

        private static void AddSphere(World world, string name, float3 position, float radius, SDFMaterial material,
            bool reflections, bool refractions)
        {
            uint id = world.CreateEntity(name);
            world.AddComponent(id, new Transform { position = position });
            world.AddComponent(id, new SDFSphere { radius = radius });
            world.AddComponent(id, material);
            world.AddComponent(id, new Shadows());
            if (reflections) world.AddComponent(id, new Reflections());
            if (refractions)
            {
                world.AddComponent(id, new Refractions
                {
                    absorptionColor = new float4(0.55f, 0.8f, 0.9f, 0.22f),
                });
            }
        }

        private static void AddBox(World world, string name, float3 position, float3 size, SDFMaterial material)
        {
            uint id = world.CreateEntity(name);
            world.AddComponent(id, new Transform { position = position });
            world.AddComponent(id, new SDFBox { size = size });
            world.AddComponent(id, material);
            world.AddComponent(id, new Shadows());
        }

        #endregion scene

        #region rendering

        private static float4[] RenderSnapshot(RenderPipeline pipeline, int width, int height, int frames)
        {
            float4[] pixels = pipeline.RenderFixedFrames(width, height, frames, 1.0 / 60.0)
                ?? throw new InvalidOperationException("Render produced no pixels.");
            return (float4[])pixels.Clone();
        }

        private sealed class DebugMasks
        {
            public bool[] Reflection = [];
            public bool[] Refraction = [];
            public bool[] TracedReflection = [];
            public bool[] ApproximatedReflection = [];
            public float[] Roughness = [];
        }

        private static DebugMasks RenderDebugMasks(RenderPipeline pipeline, Camera camera, int width, int height, int frames)
        {
            bool wasEnabled = camera.enableDenoise;
            camera.enableDenoise = false;

            pipeline.CurrentDebugMode = (RenderPipeline.DebugMode)12;
            float4[] flags = RenderSnapshot(pipeline, width, height, frames);

            pipeline.CurrentDebugMode = (RenderPipeline.DebugMode)11;
            float4[] roughness = RenderSnapshot(pipeline, width, height, frames);

            pipeline.CurrentDebugMode = (RenderPipeline.DebugMode)13;
            float4[] cutoff = RenderSnapshot(pipeline, width, height, frames);

            pipeline.CurrentDebugMode = RenderPipeline.DebugMode.None;
            camera.enableDenoise = wasEnabled;

            DebugMasks masks = new()
            {
                Reflection = new bool[width * height],
                Refraction = new bool[width * height],
                TracedReflection = new bool[width * height],
                ApproximatedReflection = new bool[width * height],
                Roughness = new float[width * height],
            };
            for (int i = 0; i < masks.Reflection.Length; i++)
            {
                masks.Reflection[i] = flags[i].x > 0.5f;
                masks.Refraction[i] = flags[i].y > 0.5f;
                masks.TracedReflection[i] = cutoff[i].y > 0.2f;
                masks.ApproximatedReflection[i] = cutoff[i].x > 0.5f;
                masks.Roughness[i] = roughness[i].x;
            }
            return masks;
        }

        /// <summary>
        /// Renders the ray-step debug view with every material's reflection cutoff forced to
        /// one value and returns the mean step count over reflective pixels. The original
        /// cutoffs are returned through <paramref name="storedCutoffs"/> for restoration.
        /// </summary>
        private static double RenderMeanStepCount(RenderPipeline pipeline, World world, Camera camera,
            int width, int height, int frames, float cutoff, out float[]? storedCutoffs)
        {
            List<SDFMaterial> materials = [.. world.QueryData<SDFMaterial>().Select(entry => entry.Item2)];
            storedCutoffs = new float[materials.Count];
            for (int i = 0; i < materials.Count; i++)
            {
                storedCutoffs[i] = materials[i].reflectionCutoff;
                materials[i].reflectionCutoff = cutoff;
            }

            bool wasEnabled = camera.enableDenoise;
            camera.enableDenoise = false;
            pipeline.CurrentDebugMode = (RenderPipeline.DebugMode)12;
            float4[] flags = RenderSnapshot(pipeline, width, height, frames);
            pipeline.CurrentDebugMode = (RenderPipeline.DebugMode)4;
            float4[] steps = RenderSnapshot(pipeline, width, height, frames);
            pipeline.CurrentDebugMode = RenderPipeline.DebugMode.None;
            camera.enableDenoise = wasEnabled;

            double sum = 0;
            int count = 0;
            for (int i = 0; i < steps.Length; i++)
            {
                if (flags[i].x <= 0.5f) continue;
                sum += steps[i].x + steps[i].y + steps[i].z;
                count++;
            }
            return count > 0 ? sum / count : 0;
        }

        private static void RestoreCutoffs(World world, float[]? cutoffs)
        {
            if (cutoffs is null) return;
            List<SDFMaterial> materials = [.. world.QueryData<SDFMaterial>().Select(entry => entry.Item2)];
            for (int i = 0; i < materials.Count && i < cutoffs.Length; i++)
                materials[i].reflectionCutoff = cutoffs[i];
        }

        /// <summary>Per-pixel temporal statistics over a sequence of frames.</summary>
        private sealed class TemporalSeries
        {
            public double NoisyStd;
            public double DenoisedStd;
            public double NoisyStep;
            public double DenoisedStep;
            public float4[] NoisyStdMap = [];
            public float4[] DenoisedStdMap = [];
            public float4[] DenoisedLast = [];
        }

        /// <summary>
        /// Renders a short sequence with live frame counts and measures how much the image
        /// changes from frame to frame. This is the stability check the old denoiser's blur
        /// effectively passed: a spatial-only filter leaves a residual that flickers.
        /// </summary>
        private static TemporalSeries MeasureTemporalStability(RenderPipeline pipeline, Camera camera,
            int width, int height, DebugMasks masks)
        {
            int? savedFrameOverride = RenderPipeline.FrameCountOverride;
            bool savedDenoise = camera.enableDenoise;
            bool savedTemporal = camera.enableTemporalDenoise;
            RenderPipeline.FrameCountOverride = null; // live samples vary per frame

            const int warmup = 10;
            const int capture = 8;

            camera.enableDenoise = true;
            camera.enableTemporalDenoise = true;
            pipeline.ResetDenoiseHistory();
            for (int i = 0; i < warmup; i++)
                pipeline.RenderFixedFrames(width, height, 1, 1.0 / 60.0);
            float4[][] denoisedFrames = new float4[capture][];
            for (int i = 0; i < capture; i++)
                denoisedFrames[i] = RenderSnapshot(pipeline, width, height, 1);

            camera.enableDenoise = false;
            pipeline.ResetDenoiseHistory();
            float4[][] noisyFrames = new float4[capture][];
            for (int i = 0; i < capture; i++)
                noisyFrames[i] = RenderSnapshot(pipeline, width, height, 1);

            camera.enableDenoise = savedDenoise;
            camera.enableTemporalDenoise = savedTemporal;
            RenderPipeline.FrameCountOverride = savedFrameOverride;
            pipeline.ResetDenoiseHistory();

            bool[] secondary = Or(masks.Reflection, masks.Refraction);
            (double denoisedStd, float4[] denoisedMap) = TemporalStd(denoisedFrames, secondary, width, height);
            (double noisyStd, float4[] noisyMap) = TemporalStd(noisyFrames, secondary, width, height);
            return new TemporalSeries
            {
                NoisyStd = noisyStd,
                DenoisedStd = denoisedStd,
                NoisyStep = FrameDelta(noisyFrames, secondary, width, height),
                DenoisedStep = FrameDelta(denoisedFrames, secondary, width, height),
                NoisyStdMap = noisyMap,
                DenoisedStdMap = denoisedMap,
                DenoisedLast = denoisedFrames[capture - 1],
            };
        }

        private static (double Mean, float4[] Map) TemporalStd(float4[][] frames, bool[] mask, int width, int height)
        {
            double sum = 0;
            int count = 0;
            float4[] map = new float4[width * height];
            for (int i = 0; i < mask.Length; i++)
            {
                if (!mask[i]) continue;
                double mean = 0;
                for (int f = 0; f < frames.Length; f++) mean += Luminance(frames[f][i]);
                mean /= frames.Length;
                double variance = 0;
                for (int f = 0; f < frames.Length; f++)
                {
                    double delta = Luminance(frames[f][i]) - mean;
                    variance += delta * delta;
                }
                double std = System.Math.Sqrt(variance / frames.Length);
                sum += std;
                count++;
                float value = (float)System.Math.Min(1.0, std * 12.0);
                map[i] = new float4(value, value, value, 1f);
            }
            return (count > 0 ? sum / count : 0, map);
        }

        private static double FrameDelta(float4[][] frames, bool[] mask, int width, int height)
        {
            double sum = 0;
            int count = 0;
            for (int f = 1; f < frames.Length; f++)
            {
                for (int i = 0; i < mask.Length; i++)
                {
                    if (!mask[i]) continue;
                    sum += System.Math.Abs(Luminance(frames[f][i]) - Luminance(frames[f - 1][i]));
                    count++;
                }
            }
            return count > 0 ? sum / count : 0;
        }

        #endregion rendering

        #region metrics

        private static double Luminance(float4 color) =>
            color.x * LumaWeights.x + color.y * LumaWeights.y + color.z * LumaWeights.z;

        private static double Rmse(float4[] a, float4[] b, bool[]? mask)
        {
            double sum = 0;
            int count = 0;
            for (int i = 0; i < a.Length; i++)
            {
                if (mask is not null && !mask[i]) continue;
                double dx = a[i].x - b[i].x;
                double dy = a[i].y - b[i].y;
                double dz = a[i].z - b[i].z;
                sum += dx * dx + dy * dy + dz * dz;
                count += 3;
            }
            return count > 0 ? System.Math.Sqrt(sum / count) : 0;
        }

        /// <summary>Mean absolute Laplacian of luminance: a compact noise/frost metric.</summary>
        private static double HighFrequency(float4[] pixels, int width, int height, bool[] mask)
        {
            double sum = 0;
            int count = 0;
            for (int y = 1; y < height - 1; y++)
            {
                for (int x = 1; x < width - 1; x++)
                {
                    int i = y * width + x;
                    if (!mask[i]) continue;
                    double center = Luminance(pixels[i]);
                    double laplacian = System.Math.Abs(
                        Luminance(pixels[i - 1]) + Luminance(pixels[i + 1]) +
                        Luminance(pixels[i - width]) + Luminance(pixels[i + width]) - 4 * center);
                    sum += laplacian;
                    count++;
                }
            }
            return count > 0 ? sum / count : 0;
        }

        private static double MaxDifference(float4[] a, float4[] b, bool[] mask)
        {
            double max = 0;
            for (int i = 0; i < a.Length; i++)
            {
                if (mask.Length > 0 && !mask[i]) continue;
                max = System.Math.Max(max, System.Math.Abs(a[i].x - b[i].x));
                max = System.Math.Max(max, System.Math.Abs(a[i].y - b[i].y));
                max = System.Math.Max(max, System.Math.Abs(a[i].z - b[i].z));
            }
            return max;
        }

        private static double MeanDifference(float4[] a, float4[] b, bool[] mask)
        {
            double sum = 0;
            int count = 0;
            for (int i = 0; i < a.Length; i++)
            {
                if (!mask[i]) continue;
                sum += System.Math.Abs(a[i].x - b[i].x)
                    + System.Math.Abs(a[i].y - b[i].y)
                    + System.Math.Abs(a[i].z - b[i].z);
                count += 3;
            }
            return count > 0 ? sum / count : 0;
        }

        private static int CountNaN(float4[] pixels)
        {
            int count = 0;
            foreach (float4 p in pixels)
            {
                if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z) ||
                    float.IsInfinity(p.x) || float.IsInfinity(p.y) || float.IsInfinity(p.z))
                    count++;
            }
            return count;
        }

        private static int Count(bool[] mask)
        {
            int count = 0;
            foreach (bool value in mask) if (value) count++;
            return count;
        }

        private static int CountChanged(float4[] a, float4[] b, bool[] mask, float threshold)
        {
            int count = 0;
            for (int i = 0; i < a.Length; i++)
            {
                if (!mask[i]) continue;
                if (System.Math.Abs(a[i].x - b[i].x) > threshold ||
                    System.Math.Abs(a[i].y - b[i].y) > threshold ||
                    System.Math.Abs(a[i].z - b[i].z) > threshold)
                    count++;
            }
            return count;
        }

        private static bool[] Erode(bool[] mask, int width, int height, int radius)
        {
            bool[] result = new bool[mask.Length];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    if (!mask[index]) continue;
                    bool keep = true;
                    for (int oy = -radius; oy <= radius && keep; oy++)
                    {
                        int ny = y + oy;
                        if (ny < 0 || ny >= height) { keep = false; break; }
                        for (int ox = -radius; ox <= radius; ox++)
                        {
                            int nx = x + ox;
                            if (nx < 0 || nx >= width || !mask[ny * width + nx]) { keep = false; break; }
                        }
                    }
                    result[index] = keep;
                }
            }
            return result;
        }

        private static string Report(string label, double before, double after, bool percent)
        {
            double change = before > 1e-12 ? (after - before) / before * 100.0 : 0;
            return string.Format(CultureInfo.InvariantCulture,
                "{0,-48} {1,10:F6} -> {2,10:F6} ({3,7:F2}%)", label, before, after, change);
        }

        private static bool[] And(bool[] a, bool[] b)
        {
            bool[] result = new bool[a.Length];
            for (int i = 0; i < a.Length; i++) result[i] = a[i] && b[i];
            return result;
        }

        private static bool[] Or(bool[] a, bool[] b)
        {
            bool[] result = new bool[a.Length];
            for (int i = 0; i < a.Length; i++) result[i] = a[i] || b[i];
            return result;
        }

        private static bool[] Not(bool[] a)
        {
            bool[] result = new bool[a.Length];
            for (int i = 0; i < a.Length; i++) result[i] = !a[i];
            return result;
        }

        private static bool[] Between(float[] values, float low, float high)
        {
            bool[] result = new bool[values.Length];
            for (int i = 0; i < values.Length; i++) result[i] = values[i] >= low && values[i] <= high;
            return result;
        }

        private static bool[] LessThan(float[] values, float limit)
        {
            bool[] result = new bool[values.Length];
            for (int i = 0; i < values.Length; i++) result[i] = values[i] < limit;
            return result;
        }

        private static bool[] MoreThan(float[] values, float limit)
        {
            bool[] result = new bool[values.Length];
            for (int i = 0; i < values.Length; i++) result[i] = values[i] > limit;
            return result;
        }

        #endregion metrics

        #region output

        private static void WriteArtifacts(string outputDirectory, int width, int height,
            float4[] noisy, float4[] denoised, float4[] reference, DebugMasks masks, bool[] untouched,
            float4[] reflectionChannel, float4[] refractionChannel, float4[] baseChannel,
            float4[] temporalNoisyMap, float4[] temporalDenoisedMap, float4[] temporalDenoisedFrame)
        {
            Directory.CreateDirectory(outputDirectory);
            SavePng(Path.Combine(outputDirectory, "noisy.png"), noisy, width, height);
            SavePng(Path.Combine(outputDirectory, "denoised.png"), denoised, width, height);
            SavePng(Path.Combine(outputDirectory, "reference.png"), reference, width, height);
            SavePng(Path.Combine(outputDirectory, "channel-reflection.png"), reflectionChannel, width, height);
            SavePng(Path.Combine(outputDirectory, "channel-refraction.png"), refractionChannel, width, height);
            SavePng(Path.Combine(outputDirectory, "channel-base.png"), baseChannel, width, height);
            SavePng(Path.Combine(outputDirectory, "temporal-noisy-std.png"), temporalNoisyMap, width, height);
            SavePng(Path.Combine(outputDirectory, "temporal-denoised-std.png"), temporalDenoisedMap, width, height);
            SavePng(Path.Combine(outputDirectory, "denoised-temporal.png"), temporalDenoisedFrame, width, height);
            SaveDifference(Path.Combine(outputDirectory, "denoise-diff.png"), noisy, denoised, width, height);
            SaveDifference(Path.Combine(outputDirectory, "denoised-vs-reference.png"), denoised, reference, width, height);
            SaveMask(Path.Combine(outputDirectory, "untouched-mask.png"), untouched, width, height);
            SaveMask(Path.Combine(outputDirectory, "approximated-mask.png"), masks.ApproximatedReflection, width, height);
            SaveMask(Path.Combine(outputDirectory, "traced-mask.png"), masks.TracedReflection, width, height);
            SaveRoughness(Path.Combine(outputDirectory, "roughness.png"), masks.Roughness, width, height);
            Console.WriteLine($"denoisetest: wrote images to {outputDirectory}");
        }

        private static void SavePng(string path, float4[] pixels, int width, int height)
        {
            using SKBitmap bitmap = new(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
            byte[] bytes = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float4 p = pixels[(height - 1 - y) * width + x];
                    int i = (y * width + x) * 4;
                    bytes[i + 0] = (byte)System.Math.Clamp((int)(p.x * 255f), 0, 255);
                    bytes[i + 1] = (byte)System.Math.Clamp((int)(p.y * 255f), 0, 255);
                    bytes[i + 2] = (byte)System.Math.Clamp((int)(p.z * 255f), 0, 255);
                    bytes[i + 3] = 255;
                }
            }
            Marshal.Copy(bytes, 0, bitmap.GetPixels(), bytes.Length);
            using SKImage image = SKImage.FromBitmap(bitmap);
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
            using FileStream stream = File.Create(path);
            data.SaveTo(stream);
        }

        private static void SaveDifference(string path, float4[] a, float4[] b, int width, int height)
        {
            float4[] diff = new float4[width * height];
            for (int i = 0; i < diff.Length; i++)
            {
                float d = System.Math.Min(1f, (System.Math.Abs(a[i].x - b[i].x) +
                    System.Math.Abs(a[i].y - b[i].y) + System.Math.Abs(a[i].z - b[i].z)) * 6f);
                diff[i] = new float4(d, d, d, 1f);
            }
            SavePng(path, diff, width, height);
        }

        private static void SaveMask(string path, bool[] mask, int width, int height)
        {
            float4[] pixels = new float4[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                float value = mask[i] ? 1f : 0f;
                pixels[i] = new float4(value, value, value, 1f);
            }
            SavePng(path, pixels, width, height);
        }

        private static void SaveRoughness(string path, float[] roughness, int width, int height)
        {
            float4[] pixels = new float4[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                float value = roughness[i];
                pixels[i] = new float4(value, value, value, 1f);
            }
            SavePng(path, pixels, width, height);
        }

        #endregion output

        #region args

        private static int GetInt(string[] args, string name, int fallback)
        {
            int index = Array.IndexOf(args, name);
            if (index >= 0 && index + 1 < args.Length &&
                int.TryParse(args[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                return value;
            return fallback;
        }

        private static float GetFloat(string[] args, string name, float fallback)
        {
            int index = Array.IndexOf(args, name);
            if (index >= 0 && index + 1 < args.Length &&
                float.TryParse(args[index + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                return value;
            return fallback;
        }

        private static string? GetString(string[] args, string name, string? fallback)
        {
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
        }

        private static bool HasFlag(string[] args, string name) => Array.IndexOf(args, name) >= 0;

        #endregion args
    }
}
