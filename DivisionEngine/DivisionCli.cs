//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using EngineEnvironment = DivisionEngine.Components.Environment;
using DivisionEngine.Projects;
using DivisionEngine.Components;
using DivisionEngine.Components.SDFs.Effects;
using SkiaSharp;
using DivisionEngine.Components.Lights;
using DivisionEngine.MathUtilities;
using DivisionEngine.Rendering;
using DivisionEngine.Settings;

namespace DivisionEngine.Editor
{
    /// <summary>
    /// Command line interface for the engine, invoked as
    /// <c>DivisionEngine.Editor.exe --cli &lt;command&gt;</c>. Runs without opening
    /// the editor UI, which makes it usable from scripts and CI.
    /// </summary>
    internal static class DivisionCLI
    {
        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int processId);

        /// <summary>
        /// Process exit code for the last command.
        /// </summary>
        public static int ExitCode { get; private set; }

        /// <summary>
        /// Sets the process exit code for a command implemented in another file.
        /// </summary>
        /// <param name="exitCode">The exit code to report.</param>
        public static void SetExitCode(int exitCode) => ExitCode = exitCode;

        /// <summary>
        /// Runs a CLI command when the arguments start with <c>--cli</c>.
        /// </summary>
        /// <returns>True when the command was handled (the app should not start the UI).</returns>
        public static bool TryRun(string[] args)
        {
            if (args.Length == 0 || !string.Equals(args[0], "--cli", StringComparison.OrdinalIgnoreCase))
                return false;

            AttachConsole(-1); // GUI subsystem app: attach to the launching terminal

            string command = args.Length > 1 ? args[1].ToLowerInvariant() : "help";

            try
            {
                switch (command)
                {
                    case "bench": return RunBench(args);
                    case "compare": return RunCompare(args);
                    case "compute": return DivisionComputeCLI.TryRun(args);
                    case "info": return RunInfo();
                    case "selftest": return EngineSelfTest.Run();
                    case "shadowtest": return ShadowTest.Run(args);
                    case "skyprobe": return RunSkyProbe();
                    case "shot": return RunShot(args);
                    case "help":
                    case "--help":
                    case "-h": PrintHelp(); return true;
                    default:
                        Console.Error.WriteLine($"Unknown command '{command}'.");
                        PrintHelp();
                        ExitCode = 1;
                        return true;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"divisioncli: {ex}");
                ExitCode = 1;
                return true;
            }
        }

        /// <summary>
        /// Warns when the shader cache is empty. Compiling the raymarch shader takes minutes
        /// with fxc, and it happens inside the first rendered frame, so a cold run otherwise
        /// looks like a hang with no output.
        /// </summary>
        internal static void WarnIfShaderCacheCold()
        {
            if (System.Environment.GetEnvironmentVariable("DIVISION_DISABLE_SHADER_CACHE") is "1" or "true")
            {
                Console.WriteLine("note: shader cache disabled (DIVISION_DISABLE_SHADER_CACHE) - every shader " +
                                  "is recompiled and the first frame can take several minutes");
                return;
            }

            string cacheDirectory = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "DivisionEngine", "ShaderCache");

            bool cold = !System.IO.Directory.Exists(cacheDirectory) ||
                        System.IO.Directory.GetFiles(cacheDirectory).Length == 0;

            if (cold)
                Console.WriteLine("note: cold shader cache - compiling shaders, the first frame can take " +
                                  "several minutes (this is normal on a fresh checkout or after a shader edit)");
        }

        private static bool RunBench(string[] args)
        {
            int width = GetInt(args, "--width", 1280);
            int height = GetInt(args, "--height", 720);
            int frames = GetInt(args, "--frames", 120);
            int warmup = GetInt(args, "--warmup", 10);

            if (width <= 0 || height <= 0 || frames <= 0)
                throw new ArgumentException("--width, --height and --frames must be positive.");

            Console.WriteLine($"divisioncli bench: {width}x{height}, {frames} frames (+{warmup} warmup)");
            WarnIfShaderCacheCold();

            WorldManager.CreateDefaultWorld(makeCurrent: true);

            if (GetString(args, "--sky", "default").Equals("procedural", StringComparison.OrdinalIgnoreCase))
            {
                foreach ((uint _, EngineEnvironment environment) in WorldManager.CurrentWorld!.QueryData<EngineEnvironment>())
                {
                    environment.skyType = EngineEnvironment.SkyType.Procedural;
                    break;
                }
            }

            RenderPipeline pipeline = new();
            if (!pipeline.BindCurrentWorld())
                throw new InvalidOperationException("Failed to bind the current world.");

            ApplyTestRenderOptions(args);
            RenderPipeline.BenchmarkResult result = pipeline.Benchmark(width, height, frames, warmup);

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{{\"width\":{0},\"height\":{1},\"frames\":{2},\"avgMs\":{3:F3},\"medianMs\":{4:F3},\"p95Ms\":{5:F3},\"fps\":{6:F2}}}",
                result.Width, result.Height, result.Frames,
                result.AverageMs, result.MedianMs, result.P95Ms, result.Fps));

            ExitCode = 0;
            return true;
        }

        private static bool RunShot(string[] args)
        {
            int width = GetInt(args, "--width", 1280);
            int height = GetInt(args, "--height", 720);
            string outPath = GetString(args, "--out", System.IO.Path.Combine(System.Environment.CurrentDirectory, "shot.png"));

            // Deterministic capture: --fixed-frame N renders exactly N frames at a
            // fixed time step with the checkerboard (temporal) path disabled, and
            // seeds the RNG used while building the default world. Two runs then
            // produce identical pixels, which is what the golden renders compare to.
            int? fixedFrame = GetOptionalInt(args, "--fixed-frame");
            if (fixedFrame.HasValue && fixedFrame.Value < 1)
                throw new ArgumentException("--fixed-frame must be at least 1.");
            int seed = GetInt(args, "--seed", 12345);
            int debugMode = GetInt(args, "--debug-mode", 0);
            bool noCheckerboard = HasFlag(args, "--no-checkerboard");
            if (fixedFrame.HasValue || noCheckerboard)
            {
                RenderPipeline.DeterministicCapture = true;
                if (fixedFrame.HasValue) DivisionEngine.MathUtilities.Random.Seed(seed);
            }

            WorldManager.CreateDefaultWorld(makeCurrent: true);
            World world = WorldManager.CurrentWorld!;

            string sky = GetString(args, "--sky", "gradient");
            if (Enum.TryParse(sky, ignoreCase: true, out EngineEnvironment.SkyType skyType))
            {
                foreach ((uint _, EngineEnvironment environment) in world.QueryData<EngineEnvironment>())
                {
                    environment.skyType = skyType;
                    break;
                }
            }

            string look = GetString(args, "--look", "");
            if (!string.IsNullOrWhiteSpace(look))
            {
                string[] parts = look.Split(',');
                if (parts.Length == 3 &&
                    float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float lx) &&
                    float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float ly) &&
                    float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float lz))
                {
                    float3 dir = new float3(lx, ly, lz).Normalize();
                    float pitch = MathF.Asin(System.Math.Clamp(dir.y, -1f, 1f));
                    float yaw = MathF.Atan2(-dir.x, -dir.z);
                    foreach ((uint id, Transform transform) in world.QueryData<Transform>())
                    {
                        if (!world.HasComponent<Camera>(id)) continue;
                        transform.rotation = new float3(pitch, yaw, 0f).EulerToQuaternion().Normalize();
                        break;
                    }
                }
            }

            RenderPipeline pipeline = new();
            pipeline.BindCurrentWorld();
            pipeline.CurrentDebugMode = (RenderPipeline.DebugMode)debugMode;
            ApplyTestRenderOptions(args);
            WarnIfShaderCacheCold();
            float4[]? pixels;
            if (fixedFrame.HasValue)
            {
                if (HasFlag(args, "--reload-shaders"))
                {
                    // Test hook for Edit > Reload All Shaders: drop the compiled shaders
                    // halfway through the capture and prove the rebuilt ones render the
                    // same pixels.
                    int first = System.Math.Max(1, fixedFrame.Value / 2);
                    pipeline.RenderFixedFrames(width, height, first, 1.0 / 60.0);
                    pipeline.ReloadShaders();
                    pixels = pipeline.RenderFixedFrames(width, height, System.Math.Max(1, fixedFrame.Value - first), 1.0 / 60.0);
                }
                else
                {
                    pixels = pipeline.RenderFixedFrames(width, height, fixedFrame.Value, 1.0 / 60.0);
                }
            }
            else
            {
                pixels = pipeline.RenderSingleFrame(width, height);
            }
            if (pixels is null)
                throw new InvalidOperationException("Render produced no pixels.");
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
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 90);
            using FileStream fs = File.OpenWrite(outPath);
            data.SaveTo(fs);

            // Pixel statistics make a black/NaN frame obvious in headless runs.
            float minLum = float.MaxValue, maxLum = float.MinValue;
            double sumLum = 0;
            int nanPixels = 0, nonBlackPixels = 0;
            foreach (float4 pixel in pixels)
            {
                float lum = (pixel.x + pixel.y + pixel.z) / 3f;
                if (float.IsNaN(lum) || float.IsInfinity(lum)) { nanPixels++; continue; }
                minLum = System.Math.Min(minLum, lum);
                maxLum = System.Math.Max(maxLum, lum);
                sumLum += lum;
                if (lum > 0.001f) nonBlackPixels++;
            }
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "stats    : minLum={0:F4} maxLum={1:F4} meanLum={2:F4} nonBlack={3} nan={4} debugMode={5}",
                minLum, maxLum, sumLum / pixels.Length, nonBlackPixels, nanPixels, debugMode));

            if (fixedFrame.HasValue)
            {
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "shot: {0} ({1}x{2}, sky={3}, fixedFrame={4}, seed={5}, deterministic, frameCount={6}, time={7:F6}, sdfObjects={8}, worldHash={9}, objectHash={10})",
                    outPath, width, height, sky, fixedFrame.Value, seed,
                    DivisionEngine.Systems.TimeSystem.FrameCount, RenderPipeline.Time,
                    DivisionEngine.Systems.SDFRenderSystem.PreparedSDFObjectsDTO.Length,
                    HashBytes([DivisionEngine.Systems.SDFRenderSystem.PreparedWorldDTO]),
                    HashBytes(DivisionEngine.Systems.SDFRenderSystem.PreparedSDFObjectsDTO)));
                RenderPipeline.DeterministicCapture = false;
            }
            else Console.WriteLine($"shot: {outPath} ({width}x{height}, sky={sky})");
            ExitCode = 0;
            return true;
        }

        /// <summary>
        /// Compares two PNG renders per channel and reports the maximum difference.
        /// Exit code is 1 when any pixel differs by more than <c>--tolerance</c> (in
        /// 8-bit LSBs), which is the golden-image gate.
        /// </summary>
        private static bool RunCompare(string[] args)
        {
            string a = GetString(args, "--a", "");
            string b = GetString(args, "--b", "");
            int tolerance = GetInt(args, "--tolerance", 2);
            string? diffPath = GetOptionalString(args, "--diff");

            if (a.Length == 0 || b.Length == 0)
                throw new ArgumentException("compare requires --a <golden.png> and --b <candidate.png>.");

            using SKBitmap golden = DecodePng(a);
            using SKBitmap candidate = DecodePng(b);
            if (golden.Width != candidate.Width || golden.Height != candidate.Height)
                throw new InvalidOperationException(
                    $"Image sizes differ: {golden.Width}x{golden.Height} vs {candidate.Width}x{candidate.Height}.");

            int width = golden.Width, height = golden.Height;
            byte[] ga = ReadRgba(golden), ca = ReadRgba(candidate);
            int maxDiff = 0, badPixels = 0, over8 = 0, over32 = 0;
            double totalDiff = 0;
            byte[]? diff = diffPath is null ? null : new byte[ga.Length];

            for (int i = 0; i < ga.Length; i += 4)
            {
                int pixelDiff = 0;
                for (int c = 0; c < 3; c++)
                {
                    int d = System.Math.Abs(ga[i + c] - ca[i + c]);
                    if (d > pixelDiff) pixelDiff = d;
                }
                if (pixelDiff > maxDiff) maxDiff = pixelDiff;
                if (pixelDiff > tolerance) badPixels++;
                if (pixelDiff > 8) over8++;
                if (pixelDiff > 32) over32++;
                totalDiff += pixelDiff;
                if (diff is not null)
                {
                    for (int c = 0; c < 3; c++)
                        diff[i + c] = (byte)System.Math.Min(255, System.Math.Abs(ga[i + c] - ca[i + c]) * 16);
                    diff[i + 3] = 255;
                }
            }

            if (diff is not null)
            {
                using SKBitmap diffBitmap = new(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
                Marshal.Copy(diff, 0, diffBitmap.GetPixels(), diff.Length);
                using SKImage diffImage = SKImage.FromBitmap(diffBitmap);
                using SKData diffData = diffImage.Encode(SKEncodedImageFormat.Png, 100);
                using FileStream diffStream = File.Create(diffPath!);
                diffData.SaveTo(diffStream);
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{{\"golden\":\"{0}\",\"candidate\":\"{1}\",\"width\":{2},\"height\":{3},\"tolerance\":{4},\"maxDiff\":{5},\"badPixels\":{6},\"over8\":{7},\"over32\":{8},\"meanDiff\":{9:F4},\"totalPixels\":{10}}}",
                a.Replace("\\", "/"), b.Replace("\\", "/"), width, height, tolerance, maxDiff, badPixels,
                over8, over32, totalDiff / (width * height), width * height));

            if (badPixels > 0)
            {
                Console.Error.WriteLine($"compare: {badPixels} pixels exceed the {tolerance} LSB tolerance (max diff {maxDiff}).");
                ExitCode = 1;
            }
            else ExitCode = 0;
            return true;
        }

        /// <summary>
        /// FNV-1a 64 over the raw bytes of a value array, used to prove that two
        /// runs fed the GPU identical scene data.
        /// </summary>
        private static string HashBytes<T>(ReadOnlySpan<T> values) where T : unmanaged
        {
            ulong hash = 14695981039346656037UL;
            foreach (byte b in System.Runtime.InteropServices.MemoryMarshal.AsBytes(values))
            {
                hash ^= b;
                hash *= 1099511628211UL;
            }
            return hash.ToString("x16", CultureInfo.InvariantCulture);
        }

        private static SKBitmap DecodePng(string path)
        {
            using SKImage image = SKImage.FromEncodedData(path)
                ?? throw new FileNotFoundException($"Could not decode image '{path}'.", path);
            SKBitmap bitmap = new(new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Opaque));
            if (!image.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0))
            {
                bitmap.Dispose();
                throw new InvalidOperationException($"Could not read pixels from '{path}'.");
            }
            return bitmap;
        }

        private static byte[] ReadRgba(SKBitmap bitmap)
        {
            byte[] bytes = new byte[bitmap.Width * bitmap.Height * 4];
            Marshal.Copy(bitmap.GetPixels(), bytes, 0, bytes.Length);
            return bytes;
        }

        private static bool RunSkyProbe()
        {
            WorldManager.CreateDefaultWorld(makeCurrent: true);
            World world = WorldManager.CurrentWorld!;
            Console.WriteLine("skyprobe for default world:");

            foreach ((uint _, EngineEnvironment environment) in world.QueryData<EngineEnvironment>())
            {
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  environment: skyType={0} skyIntensity={1:F2} sunIntensity={2:F2} sunColor=({3:F2},{4:F2},{5:F2}) discSize={6:F3} falloff={7:F3} planetR={8:F0} atmosH={9:F0} density={10:F2} exposure={11:F2}",
                    environment.skyType, environment.skyIntensity, environment.sunIntensity,
                    environment.sunColor.x, environment.sunColor.y, environment.sunColor.z,
                    environment.sunDiscSize, environment.sunFalloff, environment.planetRadius,
                    environment.atmosphereHeight, environment.atmosphereDensity, environment.atmosphereExposure));
                break;
            }

            foreach ((uint id, Transform transform) in world.QueryData<Transform>())
            {
                if (!world.HasComponent<DirectionalLight>(id)) continue;
                float3 dir = transform.rotation.RotateVector(new float3(0f, 0f, -1f)).Normalize();
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  sun: entity={0} pos=({1:F2},{2:F2},{3:F2}) mainLightDir=({4:F3},{5:F3},{6:F3})",
                    id, transform.position.x, transform.position.y, transform.position.z,
                    dir.x, dir.y, dir.z));
                break;
            }

            ExitCode = 0;
            return true;
        }

        private static bool RunInfo()
        {
            Console.WriteLine("Division Engine CLI");
            Console.WriteLine($"Runtime: {System.Environment.Version}");
            Console.WriteLine($"OS: {System.Environment.OSVersion}");
            Console.WriteLine($"Project loaded: {ProjectManager.IsCurrentLoaded}");
            ExitCode = 0;
            return true;
        }

        private static void PrintHelp()
        {
            Console.WriteLine("""
                Division CLI

                Usage:
                  DivisionEngine.Editor.exe --cli <command> [options]

                Commands:
                  bench     Render frames offscreen and report frame times
                            Options: --width N --height N --frames N --warmup N
                  compare   Compare two PNG renders per channel (golden-image gate)
                            Options: --a <golden.png> --b <candidate.png> [--tolerance N] [--diff file.png]
                  compute   DivisionCompute tooling (see --cli compute help)
                  info      Print engine/runtime information
                  selftest  Run headless regression checks (locale serialization, snapshots,
                            undo, matrix accessors, terrain noise); exits 1 on any failure
                  shadowtest  Render a shadow scene and verify it numerically; exits 1 on failure
                            Options: --scene soft|colored  (colored checks glass tints combine)
                                     --caustics  also render/compare the caustic shadow style
                  skyprobe  Print environment sun/atmosphere values for the default world
                  shot      Render one frame to a PNG
                            Options: --sky <solid|gradient|hdri|procedural> --look x,y,z --out file --width N --height N
                                     --fixed-frame N --seed N  (deterministic capture)
                                     --debug-mode N  (0 = shaded, 1 = depth ... 9 = mip levels)
                                     --no-checkerboard --no-post --no-denoise --no-fxaa
                                     --reload-shaders  (drop compiled shaders mid-capture)
                  help      Show this message

                Deterministic capture (--fixed-frame N) renders exactly N frames at a fixed
                1/60 s step with temporal jitter (checkerboard rendering) disabled and the
                world RNG seeded, so every run produces identical pixels.

                Examples:
                  DivisionEngine.Editor.exe --cli bench --width 1280 --height 720 --frames 120
                  DivisionEngine.Editor.exe --cli shot --fixed-frame 8 --width 1280 --height 720 --out golden.png
                  DivisionEngine.Editor.exe --cli compare --a golden.png --b shot.png --tolerance 2
                  DivisionEngine.Editor.exe --cli compute selftest
                """);
        }

        /// <summary>
        /// Applies the render-testing switches used by `shot` and `bench`:
        /// `--effects` turns on a fixed set of post-processing effects for the first
        /// camera, and `--bloom` includes the (nondeterministic) bloom effect.
        /// </summary>
        private static void ApplyTestRenderOptions(string[] args)
        {
            // --no-post disables every post-processing stage (denoisers, FXAA and the
            // colour effects) so a capture shows exactly what the SDF pass wrote.
            if (HasFlag(args, "--no-post") && WorldManager.CurrentWorld is not null)
            {
                foreach ((uint _, Camera camera) in WorldManager.CurrentWorld.QueryData<Camera>())
                {
                    camera.enableFxaa = false;
                    camera.enableDivisionDenoise = false;
                    camera.enableATrousDenoise = false;
                    break;
                }
                foreach ((uint _, PostProcessing postProcess) in WorldManager.CurrentWorld.QueryData<PostProcessing>())
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
            }

            // Finer-grained isolation switches for the post chain.
            if ((HasFlag(args, "--no-denoise") || HasFlag(args, "--no-fxaa")) && WorldManager.CurrentWorld is not null)
            {
                foreach ((uint _, Camera camera) in WorldManager.CurrentWorld.QueryData<Camera>())
                {
                    if (HasFlag(args, "--no-denoise"))
                    {
                        camera.enableDivisionDenoise = false;
                        camera.enableATrousDenoise = false;
                    }
                    if (HasFlag(args, "--no-fxaa")) camera.enableFxaa = false;
                    break;
                }
            }

            if (!HasFlag(args, "--effects")) return;
            if (WorldManager.CurrentWorld is null) return;

            foreach ((uint _, PostProcessing postProcess) in WorldManager.CurrentWorld.QueryData<PostProcessing>())
            {
                postProcess.enableBlur = true;
                postProcess.blurRadius = 5f;
                postProcess.enableVignette = true;
                postProcess.vignetteIntensity = 0.6f;
                postProcess.enableChromaticAberration = true;
                postProcess.hueShift = 10f;
                postProcess.saturation = 1.1f;

                // Bloom is opt-in: its shader races (it reads texels it writes in the
                // same dispatch), so it makes frame-to-frame comparisons noisy.
                postProcess.enableBloom = HasFlag(args, "--bloom");
                break;
            }
        }

        private static string GetString(string[] args, string name, string fallback)
        {
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
        }

        private static string? GetOptionalString(string[] args, string name)
        {
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        private static int? GetOptionalInt(string[] args, string name)
        {
            int index = Array.IndexOf(args, name);
            if (index >= 0 && index + 1 < args.Length &&
                int.TryParse(args[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                return value;
            return null;
        }

        private static bool HasFlag(string[] args, string name) => Array.IndexOf(args, name) >= 0;

        private static int GetInt(string[] args, string name, int fallback)
        {
            int index = Array.IndexOf(args, name);
            if (index >= 0 && index + 1 < args.Length &&
                int.TryParse(args[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                return value;
            return fallback;
        }
    }
}

