//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.Components;
using DivisionEngine.Components.Lights;
using DivisionEngine.Components.SDFs;
using DivisionEngine.Components.SDFs.Effects;
using DivisionEngine.Components.SDFs.Primitives;
using DivisionEngine.MathUtilities;
using DivisionEngine.Rendering;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using EngineEnvironment = DivisionEngine.Components.Environment;
using Math = System.Math;

namespace DivisionEngine.Editor
{
    /// <summary>
    /// Renders a controlled shadow scene and checks the shadow terms numerically.
    /// </summary>
    /// <remarks>
    /// <c>--scene soft</c> (default): sphere over a flat ground; verifies the penumbra exists
    /// and that nothing outside it is brighter than the lit ground.
    /// <c>--scene colored</c>: red and teal glass boxes over the ground; verifies each tint,
    /// that overlapping tints <b>combine</b> (multiplied absorption), and that the opaque
    /// sphere's soft shadow still renders. <c>--caustics</c> renders the same scene with the
    /// caustic shadow style and compares the transmitted/focused light.
    /// </remarks>
    internal static class ShadowTest
    {
        private static int width;
        private static int height;

        internal static bool Run(string[] args)
        {
            width = GetInt(args, "--width", 480);
            height = GetInt(args, "--height", 270);
            string scene = GetString(args, "--scene", "soft").ToLowerInvariant();
            bool caustics = HasFlag(args, "--caustics");
            string outPath = GetString(args, "--out",
                Path.Combine(System.Environment.CurrentDirectory, "shadowtest.png"));

            if (width < 32 || height < 32)
                throw new ArgumentException("--width and --height must be at least 32.");
            if (scene is not ("soft" or "colored"))
                throw new ArgumentException("--scene must be 'soft' or 'colored'.");

            DivisionCLI.WarnIfShaderCacheCold();

            diagnosticArgs = args;
            (World world, uint groundEntity) = scene == "colored" ? BuildColoredScene() : BuildSoftScene();
            WorldManager.SetWorld(world);
            WorldManager.SwitchWorld(world.Name);

            if (HasFlag(args, "--noshadow"))
            {
                // Diagnostic: disable shadow receiving on the ground. If the halo survives,
                // it is not produced by the shadow term.
                Shadows groundShadows = world.GetComponent<Shadows>(groundEntity)!;
                groundShadows.shadowReceiver = false;
            }

            RenderPipeline.DeterministicCapture = true;
            DivisionEngine.MathUtilities.Random.Seed(12345);

            RenderPipeline pipeline = new();
            if (!pipeline.BindCurrentWorld())
                throw new InvalidOperationException("Failed to bind the shadow test world.");

            if (HasFlag(args, "--debug-shadow"))
                pipeline.CurrentDebugMode = RenderPipeline.DebugMode.Shadows;

            bool pass;
            if (scene == "colored")
            {
                float4[] colored = RenderOnce(pipeline, outPath);
                pass = AnalyzeColored(colored, pipeline.ObjectIDs!, groundEntity, requireNoHalo: true,
                    out ColoredMetrics coloredMetrics);
                PrintColored(coloredMetrics, outPath, "colored");

                if (caustics)
                {
                    Shadows groundShadows = world.GetComponent<Shadows>(groundEntity)!;
                    groundShadows.shadowStyle = Shadows.ShadowType.Caustic;

                    string causticPath = Path.ChangeExtension(outPath, ".caustic.png");
                    float4[] causticPixels = RenderOnce(pipeline, causticPath);
                    bool causticOk = AnalyzeColored(causticPixels, pipeline.ObjectIDs!, groundEntity,
                        requireNoHalo: false, out ColoredMetrics causticMetrics);
                    PrintColored(causticMetrics, causticPath, "caustic");

                    bool addsLight = causticMetrics.MaxGlassLum >= coloredMetrics.MaxGlassLum * 0.98f;
                    Console.WriteLine(
                        $"caustics: coloredMaxGlassLum={coloredMetrics.MaxGlassLum:F4} " +
                        $"causticMaxGlassLum={causticMetrics.MaxGlassLum:F4} " +
                        $"delta={(causticMetrics.MaxGlassLum - coloredMetrics.MaxGlassLum):F4}");
                    pass &= causticOk && addsLight;
                    if (!addsLight)
                        Console.WriteLine("  caustic style removed light instead of focusing it");
                }
            }
            else
            {
                float4[] pixels = RenderOnce(pipeline, outPath);
                pass = AnalyzeSoft(pixels, out SoftMetrics metrics);
                PrintSoft(metrics, outPath);
            }

            Console.WriteLine(pass ? "shadowtest: PASS" : "shadowtest: FAIL");
            DivisionCLI.SetExitCode(pass ? 0 : 1);
            return true;
        }

        private static float4[] RenderOnce(RenderPipeline pipeline, string outPath)
        {
            float4[]? pixels = pipeline.RenderFixedFrames(width, height, 1, 1.0 / 60.0);
            if (pixels is null) throw new InvalidOperationException("Render produced no pixels.");
            WritePng(pixels, width, height, outPath);
            return pixels;
        }

        // ============================ soft scene ============================

        private sealed class SoftMetrics
        {
            public float Baseline, Darkest, BrightestNear, Contrast;
            public int DarkestColumn;
            public bool ShadowPresent, NoBrightHalo;
        }

        private static bool AnalyzeSoft(float4[] pixels, out SoftMetrics m)
        {
            m = new SoftMetrics();

            int bandTop = (int)(height * 0.55f);
            int bandBottom = (int)(height * 0.95f);
            float[] columnMean = LuminanceColumnMeans(pixels, bandTop, bandBottom, 2);

            m.Baseline = Median(columnMean, 0, Math.Max(4, width / 6));
            float darkest = float.MaxValue;
            int darkestColumn = 0;
            for (int x = 0; x < width; x++)
                if (columnMean[x] < darkest) { darkest = columnMean[x]; darkestColumn = x; }

            float brightestNear = 0f;
            for (int x = Math.Max(0, darkestColumn - width / 2); x < Math.Min(width, darkestColumn + width / 2); x++)
                brightestNear = Math.Max(brightestNear, columnMean[x]);

            m.Darkest = darkest;
            m.DarkestColumn = darkestColumn;
            m.BrightestNear = brightestNear;
            m.Contrast = m.Baseline > 0f ? darkest / m.Baseline : 0f;
            m.ShadowPresent = darkest < m.Baseline * 0.80f;
            m.NoBrightHalo = brightestNear < m.Baseline * 1.05f;

            Console.Write("profile:");
            for (int x = 0; x < width; x += Math.Max(1, width / 32))
                Console.Write($" {columnMean[x]:F2}");
            Console.WriteLine();

            return m.ShadowPresent && m.NoBrightHalo;
        }

        private static void PrintSoft(SoftMetrics m, string outPath)
        {
            Console.WriteLine(
                $"{{\"scene\":\"soft\",\"out\":\"{outPath.Replace("\\", "/")}\",\"width\":{width},\"height\":{height}," +
                $"\"baseline\":{m.Baseline:F4},\"darkest\":{m.Darkest:F4},\"contrast\":{m.Contrast:F3}," +
                $"\"shadowColumn\":{m.DarkestColumn},\"brightestNear\":{m.BrightestNear:F4}," +
                $"\"shadowPresent\":{(m.ShadowPresent ? "true" : "false")}," +
                $"\"noBrightHalo\":{(m.NoBrightHalo ? "true" : "false")}}}");
        }

        // ========================= colored scene ==========================

        private sealed class ColoredMetrics
        {
            public float Baseline = 1f;
            public int RedPixels, TealPixels, CombinedPixels, GroundPixels;
            public float MedianRedLum, MedianTealLum;
            public float MinOpaqueLum, MaxGlassLum, MaxHaloRatio;
            public bool RedTint, TealTint, Combined, SoftShadow, NoBrightHalo;
        }

        /// <summary>
        /// Ground-only analysis. Pixels are filtered with the renderer's object-ID buffer so the
        /// glass panes' own shading cannot pollute the shadow metrics. The lit reference is taken
        /// per row (the ground has a natural luminance gradient with distance), which makes the
        /// halo check local to the shadow instead of global.
        /// </summary>
        private static bool AnalyzeColored(float4[] pixels, uint2[] objectIds, uint groundEntity,
            bool requireNoHalo, out ColoredMetrics m)
        {
            m = new ColoredMetrics();

            int opaqueRight = (int)(width * 0.24f);
            int glassLeft = (int)(width * 0.30f), glassRight = (int)(width * 0.88f);

            List<float> redLums = [];
            List<float> tealLums = [];
            List<float> allGroundLums = [];
            float minOpaque = float.MaxValue;
            float maxGlass = 0f;
            float maxHaloRatio = 1f;

            for (int y = 0; y < height; y++)
            {
                List<float> rowLums = [];

                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    if (objectIds[index].y != groundEntity) continue;

                    float4 c = pixels[index];
                    float lum = c.x * 0.299f + c.y * 0.587f + c.z * 0.114f;
                    rowLums.Add(lum);
                    allGroundLums.Add(lum);

                    bool inGlassRegion = x >= glassLeft && x < glassRight;
                    if (inGlassRegion) maxGlass = Math.Max(maxGlass, lum);

                    if (x < opaqueRight)
                    {
                        float chroma = Math.Max(c.x, Math.Max(c.y, c.z)) - Math.Min(c.x, Math.Min(c.y, c.z));
                        if (chroma < 0.02f) minOpaque = Math.Min(minOpaque, lum);
                    }

                    if (!inGlassRegion) continue;

                    if (c.x > c.y * 1.3f && c.x > c.z * 1.3f && lum < 0.95f)
                    {
                        redLums.Add(lum);
                        m.RedPixels++;
                    }
                    else if (c.y > c.x * 1.3f && c.z > c.x * 1.2f)
                    {
                        tealLums.Add(lum);
                        m.TealPixels++;
                    }
                }

                // Local lit reference for this row (most of a ground row is unshadowed).
                if (rowLums.Count >= 8)
                {
                    float rowLit = Percentile(rowLums, 0.80f);
                    if (rowLit > 0.001f)
                        foreach (float lum in rowLums)
                            maxHaloRatio = Math.Max(maxHaloRatio, lum / rowLit);
                }
            }

            m.GroundPixels = allGroundLums.Count;
            m.Baseline = allGroundLums.Count > 0 ? Percentile(allGroundLums, 0.75f) : 0f;
            m.MedianRedLum = redLums.Count > 0 ? MedianList(redLums) : 0f;
            m.MedianTealLum = tealLums.Count > 0 ? MedianList(tealLums) : 0f;
            m.MinOpaqueLum = minOpaque == float.MaxValue ? 0f : minOpaque;
            m.MaxGlassLum = maxGlass;
            m.MaxHaloRatio = maxHaloRatio;

            // Combined tint: two glass absorptions multiply, so the overlap is clearly darker
            // than either single tint.
            float singleTint = Math.Min(m.MedianRedLum, m.MedianTealLum);
            if (singleTint > 0f)
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = glassLeft; x < glassRight; x++)
                    {
                        int index = y * width + x;
                        if (objectIds[index].y != groundEntity) continue;

                        float4 c = pixels[index];
                        float lum = c.x * 0.299f + c.y * 0.587f + c.z * 0.114f;
                        if (lum < singleTint * 0.70f && lum > 0.0005f) m.CombinedPixels++;
                    }
                }
            }

            m.RedTint = m.RedPixels > 150;
            m.TealTint = m.TealPixels > 150;
            m.Combined = m.CombinedPixels > 100;
            m.SoftShadow = m.MinOpaqueLum < m.Baseline * 0.80f;
            m.NoBrightHalo = m.MaxHaloRatio < 1.08f;

            return m.RedTint && m.TealTint && m.Combined && m.SoftShadow && (m.NoBrightHalo || !requireNoHalo);
        }

        private static void PrintColored(ColoredMetrics m, string outPath, string style)
        {
            Console.WriteLine(
                $"{{\"scene\":\"colored\",\"style\":\"{style}\",\"out\":\"{outPath.Replace("\\", "/")}\"," +
                $"\"width\":{width},\"height\":{height},\"baseline\":{m.Baseline:F4},\"groundPixels\":{m.GroundPixels}," +
                $"\"redPixels\":{m.RedPixels},\"tealPixels\":{m.TealPixels}," +
                $"\"medianRedLum\":{m.MedianRedLum:F4},\"medianTealLum\":{m.MedianTealLum:F4}," +
                $"\"combinedPixels\":{m.CombinedPixels},\"minOpaqueLum\":{m.MinOpaqueLum:F4}," +
                $"\"maxGlassLum\":{m.MaxGlassLum:F4},\"maxHaloRatio\":{m.MaxHaloRatio:F3}," +
                $"\"redTint\":{(m.RedTint ? "true" : "false")}," +
                $"\"tealTint\":{(m.TealTint ? "true" : "false")}," +
                $"\"combines\":{(m.Combined ? "true" : "false")}," +
                $"\"softShadow\":{(m.SoftShadow ? "true" : "false")}," +
                $"\"noBrightHalo\":{(m.NoBrightHalo ? "true" : "false")}}}");
        }

        // ============================ scenes =============================

        private static (World, uint) BuildSoftScene() => BuildSoftWorld();

        private static (World, uint) BuildSoftWorld()
        {
            World world = NewWorld();
            AddEnvironment(world);
            AddSun(world, 35f, 0f, 1.2f);

            uint ground = AddGround(world, Shadows.ShadowType.Soft, penumbra: 8f);
            AddOpaqueSphere(world, new float3(0f, 4f, 0f), 2f);
            AddCamera(world, new float3(0f, 11f, 19f), new float3(0f, 1.5f, 3f), 55f);
            return (world, ground);
        }

        /// <summary>
        /// Red and teal glass boxes (plus a second teal box stacked over the red one, so their
        /// shadows overlap and combine) and an opaque sphere for the neutral soft shadow.
        /// </summary>
        private static (World, uint) BuildColoredScene()
        {
            World world = NewWorld();
            AddEnvironment(world);
            AddSun(world, 72f, 12f, 1.4f);

            Shadows.ShadowType style = HasFlag(diagnosticArgs, "--hard") ? Shadows.ShadowType.Hard
                : HasFlag(diagnosticArgs, "--softstyle") ? Shadows.ShadowType.Soft
                : Shadows.ShadowType.Colored;
            uint ground = AddGround(world, style, penumbra: 2f);

            if (!HasFlag(diagnosticArgs, "--no-glass"))
            {

            float4 red = new(0.9f, 0.15f, 0.15f, 0.12f);
            float4 teal = new(0.15f, 0.85f, 0.85f, 0.12f);

            // Solo red shadow (left of centre).
            AddGlassBox(world, new float3(-4.2f, 2.0f, -1f), new float3(2.4f, 2f, 2.4f),
                absorption: red, name: "GlassRed");
            // Solo teal shadow (right of centre).
            AddGlassBox(world, new float3(3.8f, 2.0f, -1f), new float3(2.4f, 2f, 2.4f),
                absorption: teal, name: "GlassTeal");

            // Red box with a teal box stacked over it: their shadows overlap in the middle and
            // the two absorptions multiply into a darker combined tint.
            AddGlassBox(world, new float3(0.2f, 2.0f, -1f), new float3(2.4f, 2f, 2.4f),
                absorption: red, name: "GlassRedUnder");
            AddGlassBox(world, new float3(0.2f, 4.8f, -1f), new float3(2.4f, 2f, 2.4f),
                absorption: teal, name: "GlassTealOverRed");

            // Clear glass lens: a caustic focus for the caustic comparison.
            AddGlassSphere(world, new float3(6f, 2.2f, 3f), 1.2f);
            }

            // Opaque sphere far left: neutral soft shadow.
            AddOpaqueSphere(world, new float3(-11f, 2.4f, 2.5f), 1.8f);

            AddCamera(world, new float3(0f, 16f, 18f), new float3(0f, 0f, 0f), 58f);
            return (world, ground);
        }

        private static World NewWorld() => new("shadowtest");

        private static void AddEnvironment(World world)
        {
            uint env = world.CreateEntity("Environment");
            world.AddComponent(env, new EngineEnvironment
            {
                skyType = EngineEnvironment.SkyType.Solid,
                skyColor = new float4(0.02f, 0.02f, 0.03f, 1f),
                ambientStrength = 0.03f,
            });
        }

        private static void AddSun(World world, float pitchDegrees, float yawDegrees, float intensity)
        {
            uint sun = world.CreateEntity("Sun");
            world.AddComponent(sun, new Transform
            {
                rotation = new float3(
                    pitchDegrees * DivisionEngine.MathUtilities.Math.Deg2Rad,
                    yawDegrees * DivisionEngine.MathUtilities.Math.Deg2Rad,
                    0f).EulerToQuaternion(),
            });
            world.AddComponent(sun, new DirectionalLight { intensity = intensity, color = new float4(1f, 1f, 1f, 1f) });
        }

        private static string[] diagnosticArgs = [];

        private static uint AddGround(World world, Shadows.ShadowType style, float penumbra)
        {
            uint ground = world.CreateEntity("Ground");
            world.AddComponent(ground, new Transform { position = new float3(0f, -0.25f, 0f) });
            world.AddComponent(ground, new SDFBox { size = new float3(80f, 0.5f, 80f) });
            world.AddComponent(ground, new SDFMaterial
            {
                albedoColor = new float4(0.75f, 0.75f, 0.75f, 1f),
                roughness = 1f,
                metallic = 0f,
                // Purely diffuse: a view-dependent sheen would masquerade as a bright halo.
                specular = 0f,
                reflectance = 0f,
                // Diagnostic toggle: --no-ao turns the ambient-occlusion term off.
                ambientOcclusion = HasFlag(diagnosticArgs, "--no-ao") ? 0f : 0.7f,
            });
            world.AddComponent(ground, new Shadows
            {
                shadowStyle = style,
                shadowCaster = true,
                shadowReceiver = true,
                penumbraDistance = penumbra,
            });
            return ground;
        }

        private static void AddOpaqueSphere(World world, float3 position, float radius)
        {
            uint sphere = world.CreateEntity("Occluder");
            world.AddComponent(sphere, new Transform { position = position });
            world.AddComponent(sphere, new SDFSphere { radius = radius });
            world.AddComponent(sphere, new SDFMaterial
            {
                albedoColor = new float4(0.25f, 0.25f, 0.27f, 1f),
                roughness = 1f,
                metallic = 0f,
                specular = 0f,
                reflectance = 0f,
            });
            world.AddComponent(sphere, new Shadows
            {
                shadowStyle = Shadows.ShadowType.Soft,
                shadowCaster = true,
                shadowReceiver = true,
                penumbraDistance = 2f,
            });
        }

        private static void AddGlassBox(World world, float3 position, float3 size, float4 absorption, string name)
        {
            uint box = world.CreateEntity(name);
            world.AddComponent(box, new Transform { position = position });
            world.AddComponent(box, new SDFBox { size = size });
            world.AddComponent(box, new SDFMaterial
            {
                albedoColor = new float4(1f, 1f, 1f, 1f),
                roughness = 0.08f,
                metallic = 0f,
                ior = 1.5f,
            });
            world.AddComponent(box, new Refractions
            {
                hasRefractions = true,
                absorptionColor = absorption,
            });
            // Caster only: the receiver style that matters is the ground's.
            world.AddComponent(box, new Shadows
            {
                shadowStyle = Shadows.ShadowType.Colored,
                shadowCaster = true,
                shadowReceiver = false,
            });
        }

        private static void AddGlassSphere(World world, float3 position, float radius)
        {
            uint sphere = world.CreateEntity("GlassLens");
            world.AddComponent(sphere, new Transform { position = position });
            world.AddComponent(sphere, new SDFSphere { radius = radius });
            world.AddComponent(sphere, new SDFMaterial
            {
                albedoColor = new float4(1f, 1f, 1f, 1f),
                roughness = 0.05f,
                metallic = 0f,
                ior = 1.5f,
            });
            world.AddComponent(sphere, new Refractions
            {
                hasRefractions = true,
                // Nearly clear: the caustic comes from bending, not absorption.
                absorptionColor = new float4(0.95f, 0.98f, 0.98f, 0.04f),
            });
            world.AddComponent(sphere, new Shadows
            {
                shadowStyle = Shadows.ShadowType.Colored,
                shadowCaster = true,
                shadowReceiver = false,
            });
        }

        private static void AddCamera(World world, float3 position, float3 target, float fov)
        {
            uint camera = world.CreateEntity("Camera");
            float3 look = (target - position).Normalize();
            float pitch = MathF.Asin(Math.Clamp(look.y, -1f, 1f));
            float yaw = MathF.Atan2(-look.x, -look.z);
            world.AddComponent(camera, new Transform
            {
                position = position,
                rotation = new float3(pitch, yaw, 0f).EulerToQuaternion().Normalize(),
            });
            // Post-processing off: FXAA / the denoisers ring around high-contrast shadow edges,
            // which would be measured as a bright halo in the shadow term's own test.
            world.AddComponent(camera, new Camera
            {
                fieldOfView = fov,
                isActive = true,
                enableFxaa = false,
                enableDivisionDenoise = false,
                enableATrousDenoise = false,
            });
            world.AddComponent(camera, new PostProcessing { enableAcesTonemapper = false });
        }

        // ============================ helpers ============================

        private static float[] LuminanceColumnMeans(float4[] pixels, int top, int bottom, int step)
        {
            float[] columnMean = new float[width];
            int rows = 0;
            for (int y = top; y < bottom; y += step)
            {
                rows++;
                for (int x = 0; x < width; x++)
                {
                    float4 c = pixels[y * width + x];
                    columnMean[x] += c.x * 0.299f + c.y * 0.587f + c.z * 0.114f;
                }
            }
            for (int x = 0; x < width; x++) columnMean[x] /= Math.Max(1, rows);
            return columnMean;
        }

        private static float Median(float[] values, int from, int toInclusive)
        {
            int count = Math.Max(1, toInclusive - from + 1);
            float[] copy = new float[count];
            Array.Copy(values, from, copy, 0, Math.Min(count, values.Length - from));
            Array.Sort(copy);
            return copy[copy.Length / 2];
        }

        private static float MedianList(List<float> values)
        {
            float[] copy = [.. values];
            Array.Sort(copy);
            return copy[copy.Length / 2];
        }

        private static float Percentile(List<float> values, float fraction)
        {
            float[] copy = [.. values];
            Array.Sort(copy);
            int index = Math.Clamp((int)(copy.Length * fraction), 0, copy.Length - 1);
            return copy[index];
        }

        private static void WritePng(float4[] pixels, int w, int h, string outPath)
        {
            using SKBitmap bitmap = new(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Opaque));
            byte[] bytes = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float4 p = pixels[(h - 1 - y) * w + x];
                    int i = (y * w + x) * 4;
                    bytes[i + 0] = (byte)Math.Clamp((int)(p.x * 255f), 0, 255);
                    bytes[i + 1] = (byte)Math.Clamp((int)(p.y * 255f), 0, 255);
                    bytes[i + 2] = (byte)Math.Clamp((int)(p.z * 255f), 0, 255);
                    bytes[i + 3] = 255;
                }
            }
            Marshal.Copy(bytes, 0, bitmap.GetPixels(), bytes.Length);

            using SKImage image = SKImage.FromBitmap(bitmap);
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 90);
            string? directory = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            using FileStream fs = File.Create(outPath);
            data.SaveTo(fs);
        }

        private static string GetString(string[] args, string name, string fallback)
        {
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
        }

        private static bool HasFlag(string[] args, string name) => Array.IndexOf(args, name) >= 0;

        private static int GetInt(string[] args, string name, int fallback)
        {
            int index = Array.IndexOf(args, name);
            if (index >= 0 && index + 1 < args.Length &&
                int.TryParse(args[index + 1], System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int value))
                return value;
            return fallback;
        }
    }
}










