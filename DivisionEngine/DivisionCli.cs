//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using System;
using System.Globalization;
using System.Runtime.InteropServices;
using DivisionEngine.Projects;
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
                Console.Error.WriteLine($"divisioncli: {ex.GetType().Name}: {ex.Message}");
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

        private static bool RunInfo()
        {
            Console.WriteLine("Division Engine CLI");
            Console.WriteLine($"Runtime: {Environment.Version}");
            Console.WriteLine($"OS: {Environment.OSVersion}");
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
                  help      Show this message

                Examples:
                  DivisionEngine.Editor.exe --cli bench --width 1280 --height 720 --frames 120
                """);
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

