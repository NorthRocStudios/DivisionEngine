//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE.txt file in the project root
// for full license terms.
//
// `DivisionEngine.Editor.exe --cli ctsstest`
//
// Headless quality gate for cone-traced supersampling. Renders the default scene
// without anti-aliasing and with CTSS, renders a 2x-supersampled reference of the
// same frame, downsamples it, and compares both candidates against that reference
// on high-gradient edge pixels. Exits 1 when CTSS does not measurably beat no-AA.
//
using DivisionEngine.Components;
using DivisionEngine.Components.SDFs.Effects;
using DivisionEngine.Rendering;
using SkiaSharp;
using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace DivisionEngine.Editor
{
    /// <summary>
    /// Headless CTSS regression/quality test.
    /// </summary>
    internal static class CtssTest
    {
        /// <summary>Minimum relative error improvement on CTSS-touched pixels.</summary>
        private const double RequiredImprovement = 0.25;

        /// <summary>
        /// Runs the <c>--cli ctsstest</c> command.
        /// </summary>
        public static bool Run(string[] args)
        {
            int width = GetInt(args, "--width", 640);
            int height = GetInt(args, "--height", 360);
            int frames = GetInt(args, "--fixed-frame", 2);
            if (width < 64 || height < 64 || frames < 1)
                throw new ArgumentException("--width/--height must be at least 64 and --fixed-frame at least 1.");

            RenderPipeline.DeterministicCapture = true;
            DivisionEngine.MathUtilities.Random.Seed(12345);

            WorldManager.CreateDefaultWorld(makeCurrent: true);
            World world = WorldManager.CurrentWorld!;

            Camera? camera = null;
            foreach ((uint _, Camera candidate) in world.QueryData<Camera>())
            {
                camera = candidate;
                break;
            }
            if (camera is null) throw new InvalidOperationException("Default world has no camera.");

            // Both candidates must see exactly the same shading; reflections and
            // refractions use frame-dependent sample indices, and the AA comparison
            // only cares about geometric edges.
            camera.antiAliasing = Camera.AntiAliasingMode.None;
            camera.enableDenoise = false;
            camera.maxRaySteps = 256;
            foreach ((uint _, PostProcessing postProcess) in world.QueryData<PostProcessing>())
            {
                postProcess.enableDepthOfField = false;
                postProcess.enableBlur = false;
                postProcess.enableBloom = false;
                postProcess.enableChromaticAberration = false;
                postProcess.enableVignette = false;
                postProcess.hueShift = 0f;
                postProcess.saturation = 1f;
                postProcess.lightness = 1f;
                postProcess.contrast = 1f;
                break;
            }
            foreach ((uint _, Reflections reflections) in world.QueryData<Reflections>())
                reflections.hasReflections = false;
            foreach ((uint _, Refractions refractions) in world.QueryData<Refractions>())
                refractions.hasRefractions = false;

            RenderPipeline pipeline = new();
            if (!pipeline.BindCurrentWorld())
                throw new InvalidOperationException("Failed to bind the current world.");

            // The main renderer samples pixel corners; a 2x reference downsampled from
            // that grid is phase-shifted by a quarter pixel. Put every pass on pixel
            // centres so the comparison isolates anti-aliasing, not sample phase.
            RenderPipeline.ForceCenteredSampling = true;

            Console.WriteLine($"ctsstest: rendering {width}x{height} no-AA, CTSS and a {width * 2}x{height * 2} reference...");

            camera.antiAliasing = Camera.AntiAliasingMode.None;
            // Pixels is reused across same-sized renders, so snapshot each pass.
            float4[] noAaPixels = (float4[])(pipeline.RenderFixedFrames(width, height, frames, 1.0 / 60.0)
                ?? throw new InvalidOperationException("No-AA render produced no pixels.")).Clone();

            camera.antiAliasing = Camera.AntiAliasingMode.CTSS;
            camera.ctssConeCoverage = 1f;
            camera.debugCTSS = Camera.AntiAliasingDebugMode.Disabled;
            float4[] ctssPixels = (float4[])(pipeline.RenderFixedFrames(width, height, frames, 1.0 / 60.0)
                ?? throw new InvalidOperationException("CTSS render produced no pixels.")).Clone();

            // 4x SSAA reference: render at 2x and average each 2x2 block.
            camera.antiAliasing = Camera.AntiAliasingMode.None;
            float4[] referencePixels = (float4[])(pipeline.RenderFixedFrames(width * 2, height * 2, frames, 1.0 / 60.0)
                ?? throw new InvalidOperationException("Reference render produced no pixels.")).Clone();

            float[] noAaLuma = Luminance(noAaPixels);
            float[] ctssLuma = Luminance(ctssPixels);
            float[] reference = Downsample2x(Luminance(referencePixels), width, height);

            bool[] edges = EdgeMask(reference, width, height);

            string? outputDirectory = GetString(args, "--out", null);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
                SavePng(Path.Combine(outputDirectory, "noaa.png"), noAaPixels, width, height);
                SavePng(Path.Combine(outputDirectory, "ctss.png"), ctssPixels, width, height);
                SaveLuma(Path.Combine(outputDirectory, "reference.png"), reference, width, height);
                SaveMask(Path.Combine(outputDirectory, "edges.png"), edges, width, height);
                Console.WriteLine($"ctsstest: wrote images to {outputDirectory}");
            }
            int edgeCount = 0;
            double noAaEdgeError = 0;
            double ctssEdgeError = 0;
            double noAaError = 0;
            double ctssError = 0;
            int nan = 0;
            int changedPixels = 0;
            int changedEdgePixels = 0;
            double noAaChangedError = 0;
            double ctssChangedError = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    if (float.IsNaN(ctssLuma[index]) || float.IsInfinity(ctssLuma[index])) nan++;

                    double a = Math.Abs(noAaLuma[index] - reference[index]);
                    double b = Math.Abs(ctssLuma[index] - reference[index]);
                    noAaError += a;
                    ctssError += b;
                    if (edges[index])
                    {
                        edgeCount++;
                        noAaEdgeError += a;
                        ctssEdgeError += b;
                    }

                    if (Math.Abs(ctssLuma[index] - noAaLuma[index]) > 0.01)
                    {
                        changedPixels++;
                        noAaChangedError += a;
                        ctssChangedError += b;
                        if (edges[index]) changedEdgePixels++;
                    }
                }
            }
            int pixels = width * height;
            noAaError /= pixels;
            ctssError /= pixels;
            if (edgeCount > 0)
            {
                noAaEdgeError /= edgeCount;
                ctssEdgeError /= edgeCount;
            }

            double improvement = noAaEdgeError > 1e-9 ? 1.0 - ctssEdgeError / noAaEdgeError : 0.0;
            double changedFraction = pixels > 0 ? changedPixels / (double)pixels : 0.0;
            double changedImprovement = noAaChangedError > 1e-9
                ? 1.0 - ctssChangedError / noAaChangedError
                : 0.0;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{{\"width\":{0},\"height\":{1},\"frames\":{2},\"edgePixels\":{3},\"noAaEdgeMae\":{4:F6},\"ctssEdgeMae\":{5:F6},\"edgeImprovement\":{6:F4},\"changedPixels\":{7},\"changedFraction\":{8:F4},\"changedEdgePixels\":{9},\"changedNoAaMae\":{10:F6},\"changedCtssMae\":{11:F6},\"changedImprovement\":{12:F4},\"noAaMae\":{13:F6},\"ctssMae\":{14:F6},\"nan\":{15}}}",
                width, height, frames, edgeCount, noAaEdgeError, ctssEdgeError, improvement,
                changedPixels, changedFraction, changedEdgePixels, noAaChangedError / Math.Max(changedPixels, 1),
                ctssChangedError / Math.Max(changedPixels, 1), changedImprovement,
                noAaError, ctssError, nan));

            // The gate is on the pixels CTSS actually changes: unchanged pixels are
            // (deliberately) the fallback and interior paths, where both candidates are
            // identical. This verifies that where CTSS acts, it moves toward the 4x
            // reference, and that it acts on a meaningful number of pixels.
            bool pass = nan == 0 && edgeCount > 0 &&
                        changedFraction >= 0.002 &&
                        changedImprovement >= RequiredImprovement;
            Console.WriteLine(pass
                ? $"ctsstest: PASS (CTSS changed {changedFraction:P2} of pixels and reduced their error by {changedImprovement:P1})"
                : $"ctsstest: FAIL (changed {changedFraction:P2} of pixels, error improvement {changedImprovement:P1}, required {RequiredImprovement:P0})");
            DivisionCLI.SetExitCode(pass ? 0 : 1);
            RenderPipeline.DeterministicCapture = false;
            RenderPipeline.ForceCenteredSampling = false;
            return true;
        }

        private static float[] Luminance(float4[] pixels)
        {
            float[] luma = new float[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
                luma[i] = pixels[i].x * 0.299f + pixels[i].y * 0.587f + pixels[i].z * 0.114f;
            return luma;
        }

        /// <summary>Box-filters a 2x image down to the target resolution.</summary>
        private static float[] Downsample2x(float[] source, int width, int height)
        {
            float[] result = new float[width * height];
            int sourceWidth = width * 2;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int sx = x * 2;
                    int sy = y * 2;
                    float sum = source[sy * sourceWidth + sx] +
                                source[sy * sourceWidth + sx + 1] +
                                source[(sy + 1) * sourceWidth + sx] +
                                source[(sy + 1) * sourceWidth + sx + 1];
                    result[y * width + x] = sum * 0.25f;
                }
            }
            return result;
        }

        /// <summary>High-gradient pixels in the reference, dilated by one pixel.</summary>
        private static bool[] EdgeMask(float[] luma, int width, int height)
        {
            bool[] raw = new bool[width * height];
            for (int y = 1; y < height - 1; y++)
            {
                for (int x = 1; x < width - 1; x++)
                {
                    int i = y * width + x;
                    float dx = Math.Abs(luma[i + 1] - luma[i - 1]);
                    float dy = Math.Abs(luma[i + width] - luma[i - width]);
                    raw[i] = dx + dy > 0.06f;
                }
            }

            bool[] dilated = new bool[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool edge = false;
                    for (int oy = -1; oy <= 1 && !edge; oy++)
                    {
                        for (int ox = -1; ox <= 1; ox++)
                        {
                            int nx = x + ox;
                            int ny = y + oy;
                            if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                            if (raw[ny * width + nx]) { edge = true; break; }
                        }
                    }
                    dilated[y * width + x] = edge;
                }
            }
            return dilated;
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
                    bytes[i + 0] = (byte)Math.Clamp((int)(p.x * 255f), 0, 255);
                    bytes[i + 1] = (byte)Math.Clamp((int)(p.y * 255f), 0, 255);
                    bytes[i + 2] = (byte)Math.Clamp((int)(p.z * 255f), 0, 255);
                    bytes[i + 3] = 255;
                }
            }
            Marshal.Copy(bytes, 0, bitmap.GetPixels(), bytes.Length);
            using SKImage image = SKImage.FromBitmap(bitmap);
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
            using FileStream stream = File.Create(path);
            data.SaveTo(stream);
        }

        private static void SaveLuma(string path, float[] luma, int width, int height)
        {
            float4[] pixels = new float4[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new float4(luma[i], luma[i], luma[i], 1f);
            SavePng(path, pixels, width, height);
        }

        private static void SaveMask(string path, bool[] mask, int width, int height)
        {
            float4[] pixels = new float4[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                float v = mask[i] ? 1f : 0f;
                pixels[i] = new float4(v, v, v, 1f);
            }
            SavePng(path, pixels, width, height);
        }

        private static int GetInt(string[] args, string name, int fallback)
        {
            int index = Array.IndexOf(args, name);
            if (index >= 0 && index + 1 < args.Length &&
                int.TryParse(args[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                return value;
            return fallback;
        }

        private static string? GetString(string[] args, string name, string? fallback)
        {
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
        }
    }
}
