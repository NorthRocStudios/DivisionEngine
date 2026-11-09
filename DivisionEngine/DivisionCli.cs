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
using SkiaSharp;
using DivisionEngine.Components.Lights;
using DivisionEngine.MathUtilities;
using DivisionEngine.Rendering;

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
                    case "info": return RunInfo();
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

        private static bool RunBench(string[] args)
        {
            int width = GetInt(args, "--width", 1280);
            int height = GetInt(args, "--height", 720);
            int frames = GetInt(args, "--frames", 120);
            int warmup = GetInt(args, "--warmup", 10);

            if (width <= 0 || height <= 0 || frames <= 0)
                throw new ArgumentException("--width, --height and --frames must be positive.");

            Console.WriteLine($"divisioncli bench: {width}x{height}, {frames} frames (+{warmup} warmup)");

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
                    float pitch = MathF.Asin(System.Math.Clamp(dir.Y, -1f, 1f));
                    float yaw = MathF.Atan2(-dir.X, -dir.Z);
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
            float4[]? pixels = pipeline.RenderSingleFrame(width, height) ?? throw new InvalidOperationException("Render produced no pixels.");
            using SKBitmap bitmap = new(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
            byte[] bytes = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float4 p = pixels[(height - 1 - y) * width + x];
                    int i = (y * width + x) * 4;
                    bytes[i + 0] = (byte)System.Math.Clamp((int)(p.X * 255f), 0, 255);
                    bytes[i + 1] = (byte)System.Math.Clamp((int)(p.Y * 255f), 0, 255);
                    bytes[i + 2] = (byte)System.Math.Clamp((int)(p.Z * 255f), 0, 255);
                    bytes[i + 3] = 255;
                }
            }
            Marshal.Copy(bytes, 0, bitmap.GetPixels(), bytes.Length);

            using SKImage image = SKImage.FromBitmap(bitmap);
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 90);
            using FileStream fs = File.OpenWrite(outPath);
            data.SaveTo(fs);

            Console.WriteLine($"shot: {outPath} ({width}x{height}, sky={sky})");
            ExitCode = 0;
            return true;
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
                    environment.sunColor.X, environment.sunColor.Y, environment.sunColor.Z,
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
                    id, transform.position.X, transform.position.Y, transform.position.Z,
                    dir.X, dir.Y, dir.Z));
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
                  info      Print engine/runtime information
                  skyprobe  Print environment sun/atmosphere values for the default world
                  shot      Render one frame to a PNG
                            Options: --sky <solid|gradient|hdri|procedural> --look x,y,z --out file --width N --height N
                  help      Show this message

                Examples:
                  DivisionEngine.Editor.exe --cli bench --width 1280 --height 720 --frames 120
                """);
        }

        private static string GetString(string[] args, string name, string fallback)
        {
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
        }

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
