//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
// `DivisionEngine.Editor.exe --cli compute <command>` - tooling for the
// ComputeSharp to DivisionCompute migration.
//
//   info      Print the DivisionCompute device and runtime info.
//   list      List the [Shader] structs in the loaded engine assemblies.
//   compile   Translate and compile every [Shader] struct; dumps HLSL on request.
//   selftest  Run the probe shaders and verify GPU output against CPU references.
//
#pragma warning disable CA1416 // Validate platform compatibility

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using DivisionEngine.Editor.Compute;
using DivisionEngine.Graphics;
using Vortice.D3DCompiler;
using DivisionEngine.MathLib;
using SkiaSharp;
using Vortice.DXGI;
using M2 = DivisionEngine.MathLib.float2;
using M3 = DivisionEngine.MathLib.float3;
using M4 = DivisionEngine.MathLib.float4;

namespace DivisionEngine.Editor
{
    /// <summary>
    /// DivisionCompute tooling for the CLI: shader discovery, compilation checks, and
    /// GPU-vs-CPU conformance tests.
    /// </summary>
    internal static partial class DivisionComputeCLI
    {
        private const string KernelName = "Execute";
        private const int ProbeWidth = 256;
        private const int ProbeHeight = 192;

        /// <summary>
        /// Runs a <c>--cli compute ...</c> command.
        /// </summary>
        /// <param name="args">The full argument list, starting with <c>--cli</c>.</param>
        /// <returns>True when the command was handled.</returns>
        public static bool TryRun(string[] args)
        {
            string command = args.Length > 2 ? args[2].ToLowerInvariant() : "help";

            try
            {
                switch (command)
                {
                    case "info": return RunInfo(args);
                    case "list": return RunList(args);
                    case "compile": return RunCompile(args);
                    case "selftest": return RunSelfTest(args);
                    case "help":
                    case "--help":
                    case "-h":
                        PrintHelp();
                        DivisionCLI.SetExitCode(0);
                        return true;
                    default:
                        Console.Error.WriteLine($"Unknown compute command '{command}'.");
                        PrintHelp();
                        DivisionCLI.SetExitCode(1);
                        return true;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"compute: {ex}");
                DivisionCLI.SetExitCode(1);
                return true;
            }
        }

        private static void PrintHelp()
        {
            Console.WriteLine("""
                Division CLI - compute commands

                Usage:
                  DivisionEngine.Editor.exe --cli compute <command> [options]

                Commands:
                  info      Show the DivisionCompute device, adapter, and shader count
                  list      List [Shader] structs, their resources and kernels
                            Options: --filter <text>
                  compile   Translate and compile shaders; fails on any error
                            Options: --filter <text> --hlsl <dir> --print-hlsl --verbose
                                     --translate-only --skip-opt --opt1 --opt2 --opt3
                            Compiled bytecode is cached under %LOCALAPPDATA%\DivisionEngine\ShaderCache;
                            set DIVISION_DISABLE_SHADER_CACHE=1 to always recompile.
                  selftest  Run probe shaders and compare GPU output with the CPU reference
                            Options: --iterations N --out <dir>
                  help      Show this message

                'selftest' renders at 256x192 with every probe, so the CPU reference and the
                shader agree on texture dimensions. --out writes expected/actual/diff PNGs.
                """);
        }

        #region info

        private static bool RunInfo(string[] args)
        {
            Console.WriteLine($"DivisionCompute: {DescribeDivisionComputeVersion()}");
            Console.WriteLine($"DivisionMath   : {DescribeDivisionMathVersion()}");

            using DivisionComputeContext context = new();
            using IDXGIDevice dxgiDevice = context.Device.QueryInterface<IDXGIDevice>();
            dxgiDevice.GetAdapter(out IDXGIAdapter adapter);
            using (adapter)
            {
                AdapterDescription description = adapter.Description;
                Console.WriteLine($"Adapter        : {description.Description}");
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "Vendor/Device  : 0x{0:X4} / 0x{1:X4}", description.VendorId, description.DeviceId));
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "Dedicated VRAM : {0:N0} MB", description.DedicatedVideoMemory / (1024 * 1024)));
            }

            Console.WriteLine($"Feature level  : {context.Device.FeatureLevel}");
            Console.WriteLine($"Device removed : {context.Device.DeviceRemovedReason}");

            List<Type> shaders = DiscoverShaderTypes();
            Console.WriteLine($"Shader structs : {shaders.Count}");
            foreach (Type shader in shaders)
                Console.WriteLine($"  - {shader.FullName}");

            DivisionCLI.SetExitCode(0);
            return true;
        }

        private static string DescribeDivisionComputeVersion()
        {
            Assembly assembly = typeof(DivisionComputeContext).Assembly;
            return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? assembly.GetName().Version?.ToString()
                ?? "unknown";
        }

        private static string DescribeDivisionMathVersion()
        {
            Assembly assembly = typeof(M4).Assembly;
            return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? assembly.GetName().Version?.ToString()
                ?? "unknown";
        }

        #endregion info

        #region list

        private static bool RunList(string[] args)
        {
            string? filter = GetString(args, "--filter", null);
            List<Type> shaders = FilterShaders(DiscoverShaderTypes(), filter);

            if (shaders.Count == 0)
                Console.WriteLine("No [Shader] structs found.");

            foreach (Type shader in shaders)
                PrintShaderSummary(shader);

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{{\"shaders\":{0}}}", shaders.Count));

            DivisionCLI.SetExitCode(0);
            return true;
        }

        private static void PrintShaderSummary(Type shader)
        {
            ShaderAttribute? attribute = shader.GetCustomAttribute<ShaderAttribute>();
            Console.WriteLine($"{shader.FullName}");
            Console.WriteLine($"  source   : {attribute?.SourceFilePath ?? "(none)"}");

            var kernels = new List<string>();
            foreach (MethodInfo method in shader.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                KernelAttribute? kernel = method.GetCustomAttribute<KernelAttribute>();
                if (kernel is not null)
                    kernels.Add($"{method.Name}({kernel.ThreadsX},{kernel.ThreadsY},{kernel.ThreadsZ})");
            }
            Console.WriteLine($"  kernels  : {(kernels.Count == 0 ? "(none)" : string.Join(", ", kernels))}");

            var resources = new List<string>();
            foreach (FieldInfo field in shader.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (field.GetCustomAttribute<ShaderResourceAttribute>() is null) continue;
                resources.Add($"{field.Name}: {FormatType(field.FieldType)}");
            }
            Console.WriteLine($"  resources: {(resources.Count == 0 ? "(none)" : string.Join(", ", resources))}");

            var uniforms = new List<string>();
            foreach (FieldInfo field in shader.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (field.IsStatic) continue;
                if (field.GetCustomAttribute<ShaderResourceAttribute>() is not null) continue;
                uniforms.Add($"{field.Name}: {FormatType(field.FieldType)}");
            }
            if (uniforms.Count > 0)
                Console.WriteLine($"  uniforms : {string.Join(", ", uniforms)}");
        }

        private static string FormatType(Type type)
        {
            if (!type.IsGenericType) return type.Name;
            string name = type.Name[..type.Name.IndexOf('`')];
            return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(FormatType))}>";
        }

        #endregion list

        #region compile

        private static bool RunCompile(string[] args)
        {
            string? filter = GetString(args, "--filter", null);
            string? hlslDirectory = GetString(args, "--hlsl", null);
            string? cacheDirectory = GetString(args, "--cache-dir", null);
            bool printHlsl = HasFlag(args, "--print-hlsl");
            bool verbose = HasFlag(args, "--verbose");

            // Diagnosing D3DCompile cost: the optimizer is by far the most expensive
            // stage for the large raymarching shader.
            ShaderFlags compileFlags = ShaderFlags.None;
            if (HasFlag(args, "--skip-opt")) compileFlags = ShaderFlags.SkipOptimization;
            else if (HasFlag(args, "--opt1")) compileFlags = ShaderFlags.OptimizationLevel1;
            else if (HasFlag(args, "--opt2")) compileFlags = ShaderFlags.OptimizationLevel2;
            else if (HasFlag(args, "--opt3")) compileFlags = ShaderFlags.OptimizationLevel3;

            // Translation-only runs skip D3DCompile: useful while iterating on a
            // shader and when dumping HLSL for external analysis.
            bool translateOnly = HasFlag(args, "--translate-only");

            // Point the cache somewhere else, e.g. to regenerate the precompiled cache
            // that ships next to the editor (DivisionEngine.Core/Shaders/Compiled).
            if (cacheDirectory is not null)
            {
                // Compile into the given directory instead of the local user cache.
                Directory.CreateDirectory(cacheDirectory);
                DivisionShaderCompiler.CacheDirectory = cacheDirectory;
            }

            if (hlslDirectory is not null)
                Directory.CreateDirectory(hlslDirectory);

            List<Type> shaders = FilterShaders(DiscoverShaderTypes(), filter);
            if (shaders.Count == 0)
            {
                Console.WriteLine("No [Shader] structs found to compile.");
                DivisionCLI.SetExitCode(1);
                return true;
            }

            DivisionShader.Verbose = verbose;

            int compiled = 0, failed = 0, kernelCount = 0;
            long bytecodeBytes = 0;
            object outputLock = new();

            using DivisionComputeContext context = new();

            // Shaders are independent, so the whole translate+compile pipeline runs in
            // parallel. D3DCompile is single-threaded per kernel, but the engine has
            // more than twenty shaders and this is the difference between a slow serial
            // sweep and a fast one. Interleaved output is not readable, so HLSL printing
            // forces a serial run.
            int maxParallelism = printHlsl ? 1 : Math.Max(1, Environment.ProcessorCount - 1);
            if (maxParallelism > 8) maxParallelism = 8;

            void CompileOne(Type shader)
            {
                StringBuilder output = new();
                Stopwatch stopwatch = Stopwatch.StartNew();
                HLSLTranslationResult translation = context.Compiler.TranslateShader(shader);
                long translateMs = stopwatch.ElapsedMilliseconds;

                output.AppendLine(shader.FullName);
                if (!translation.IsSuccess)
                {
                    lock (outputLock)
                    {
                        failed++;
                        Console.Error.WriteLine($"  translate error: {translation.ErrorMessage}");
                    }
                    return;
                }

                string hlsl = translation.HLSLCode ?? string.Empty;
                int lines = hlsl.Count(c => c == '\n') + 1;

                if (hlslDirectory is not null)
                {
                    string path = Path.Combine(hlslDirectory, $"{shader.Name}.hlsl");
                    File.WriteAllText(path, hlsl);
                    output.AppendLine($"  hlsl     : {lines} lines -> {path}");
                }
                else
                {
                    output.AppendLine($"  hlsl     : {lines} lines");
                }

                if (printHlsl)
                {
                    output.AppendLine("  ---");
                    foreach (string line in hlsl.Split('\n'))
                        output.AppendLine($"  | {line.TrimEnd()}");
                    output.AppendLine("  ---");
                }

                if (translateOnly)
                {
                    output.AppendLine($"  timing   : translate {translateMs} ms, compile skipped");
                    lock (outputLock)
                    {
                        Console.Write(output.ToString());
                        compiled++;
                    }
                    return;
                }

                stopwatch.Restart();
                HLSLCompilationResult compilation = DivisionShaderCompiler.CompileHLSL(translation, compileFlags);
                long compileMs = stopwatch.ElapsedMilliseconds;
                output.AppendLine($"  timing   : translate {translateMs} ms, compile {compileMs} ms");
                if (!compilation.IsSuccess)
                {
                    lock (outputLock)
                    {
                        failed++;
                        Console.Write(output.ToString());
                        Console.Error.WriteLine($"  compile error: {compilation.DebugMessage}");
                    }
                    return;
                }

                foreach (string kernel in compilation.KernelNames)
                    output.AppendLine($"  kernel   : {kernel}");

                if (verbose)
                    output.AppendLine($"  bytecode : {string.Join(", ", compilation.KernelBytecodes.Select(b => $"{b.Length:N0} B"))}");

                // Creating the GPU shader validates the D3D11 bytecode and installs
                // the resource layout (including the uniform constant buffer). It uses
                // the shared device, so it is serialized with the output.
                lock (outputLock)
                {
                    using DivisionShader compiledShader = context.CreateShader(compilation, shader);
                    foreach ((string name, uint slot, string kind) in compiledShader.ResourceLayout)
                        output.AppendLine($"  bind     : {kind,-3} {name} -> slot {slot}");

                    if (compiledShader.HasUniforms)
                        output.AppendLine($"  uniforms : {string.Join(", ", compiledShader.UniformNames)}");

                    Console.Write(output.ToString());
                    kernelCount += compilation.KernelBytecodes.Count;
                    bytecodeBytes += compilation.KernelBytecodes.Sum(b => (long)b.Length);
                    compiled++;
                }
            }

            if (maxParallelism <= 1)
            {
                foreach (Type shader in shaders) CompileOne(shader);
            }
            else
            {
                Parallel.ForEach(shaders, new ParallelOptions { MaxDegreeOfParallelism = maxParallelism }, CompileOne);
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{{\"shaders\":{0},\"compiled\":{1},\"failed\":{2},\"kernels\":{3},\"bytes\":{4}}}",
                shaders.Count, compiled, failed, kernelCount, bytecodeBytes));

            DivisionCLI.SetExitCode(failed == 0 ? 0 : 1);
            return true;
        }

        #endregion compile

        #region selftest

        private static bool RunSelfTest(string[] args)
        {
            int iterations = Math.Max(1, GetInt(args, "--iterations", 20));
            string? outDirectory = GetString(args, "--out", null);
            bool verbose = HasFlag(args, "--verbose");

            if (outDirectory is not null)
                Directory.CreateDirectory(outDirectory);

            DivisionShader.Verbose = verbose;

            using DivisionComputeContext context = new();
            var probes = new List<ProbeResult>();

            probes.Add(RunCopyProbe(context, iterations));
            probes.Add(RunMathProbe(context, iterations));
            probes.Add(RunBlurProbe(context, iterations));
            probes.Add(RunUniformProbe(context, iterations));
            probes.Add(RunUniformApiProbe(context));
            probes.Add(RunHelperProbe(context, iterations));
            probes.Add(RunTypedBufferProbe(context, iterations));
            probes.Add(RunGpuCopyProbe(context));
            probes.Add(RunBufferProbe(context, iterations));

            Console.WriteLine();
            Console.WriteLine("probe        result  max error    mean error   mismatch    gpu ms     cpu ms");
            foreach (ProbeResult probe in probes)
            {
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,-12} {1,-7} {2,10:E2} {3,12:E2} {4,9} {5,9:F3} {6,9:F3}",
                    probe.Name,
                    probe.Passed ? "PASS" : "FAIL",
                    probe.MaxError,
                    probe.MeanError,
                    probe.Mismatches,
                    probe.GpuMs,
                    probe.CpuMs));

                if (probe.Message is not null)
                    Console.WriteLine($"  {probe.Message}");

                if (outDirectory is not null && probe.Expected is not null && probe.Actual is not null)
                    WriteProbeImages(outDirectory, probe);
            }

            int passed = probes.Count(p => p.Passed);
            int failed = probes.Count - passed;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{{\"probes\":{0},\"passed\":{1},\"failed\":{2}}}", probes.Count, passed, failed));

            DivisionCLI.SetExitCode(failed == 0 ? 0 : 1);
            return true;
        }

        private static ProbeResult RunCopyProbe(DivisionComputeContext context, int iterations)
        {
            M4[] input = new M4[ProbeWidth * ProbeHeight];
            for (int y = 0; y < ProbeHeight; y++)
            {
                for (int x = 0; x < ProbeWidth; x++)
                {
                    input[y * ProbeWidth + x] = new M4(
                        x / (float)(ProbeWidth - 1),
                        y / (float)(ProbeHeight - 1),
                        ((x * 37 + y * 17) % 251) / 250f,
                        1f);
                }
            }

            M4[] expected = [.. input];

            using DivisionShader shader = CompileProbe(context, typeof(CopyProbe));
            M4[] actual = RunTextureProbe(context, shader, typeof(CopyProbe), input, iterations, out double gpuMs);

            return Compare("copy", expected, actual, 0f, gpuMs, 0, "exact copy of R32G32B32A32_Float");
        }

        private static ProbeResult RunMathProbe(DivisionComputeContext context, int iterations)
        {
            Stopwatch sw = Stopwatch.StartNew();
            M4[] expected = new M4[ProbeWidth * ProbeHeight];
            for (int y = 0; y < ProbeHeight; y++)
                for (int x = 0; x < ProbeWidth; x++)
                    expected[y * ProbeWidth + x] = MathReference(x, y);
            sw.Stop();

            using DivisionShader shader = CompileProbe(context, typeof(MathProbe));
            M4[] actual = RunTextureProbe(context, shader, typeof(MathProbe), null, iterations, out double gpuMs);

            return Compare("math", expected, actual, 2e-4f, gpuMs, sw.Elapsed.TotalMilliseconds,
                "intrinsics, swizzles, helper functions");
        }

        private static ProbeResult RunBlurProbe(DivisionComputeContext context, int iterations)
        {
            M4[] input = new M4[ProbeWidth * ProbeHeight];
            for (int y = 0; y < ProbeHeight; y++)
            {
                for (int x = 0; x < ProbeWidth; x++)
                {
                    bool checker = ((x / 8) + (y / 8)) % 2 == 0;
                    float glow = 1f - (x / (float)ProbeWidth);
                    input[y * ProbeWidth + x] = checker
                        ? new M4(glow, glow * 0.5f, 1f - glow, 1f)
                        : new M4(0.1f, 0.1f, 0.1f, 1f);
                }
            }

            Stopwatch sw = Stopwatch.StartNew();
            M4[] expected = new M4[ProbeWidth * ProbeHeight];
            for (int y = 0; y < ProbeHeight; y++)
                for (int x = 0; x < ProbeWidth; x++)
                    expected[y * ProbeWidth + x] = BlurReference(input, x, y);
            sw.Stop();

            using DivisionShader shader = CompileProbe(context, typeof(BlurProbe));
            M4[] actual = RunTextureProbe(context, shader, typeof(BlurProbe), input, iterations, out double gpuMs);

            return Compare("blur", expected, actual, 2e-4f, gpuMs, sw.Elapsed.TotalMilliseconds,
                "9-tap gaussian loop with bounds checks");
        }

        private static ProbeResult RunUniformProbe(DivisionComputeContext context, int iterations)
        {
            const float gain = 0.75f;
            const bool invert = false;
            M4 scale = new(1.0f, 0.5f, 0.25f, 0.125f);
            M3 tint = new(0.9f, 0.4f, 0.15f);

            Stopwatch sw = Stopwatch.StartNew();
            M4[] expected = new M4[ProbeWidth * ProbeHeight];
            for (int y = 0; y < ProbeHeight; y++)
                for (int x = 0; x < ProbeWidth; x++)
                    expected[y * ProbeWidth + x] = UniformReference(x, y, gain, scale, tint, invert);
            sw.Stop();

            using DivisionShader shader = CompileProbe(context, typeof(UniformProbe));

            // The Unity-style API: plain fields on the shader struct, set by name.
            shader.SetFloat("width", ProbeWidth);
            shader.SetFloat("height", ProbeHeight);
            shader.SetFloat("gain", gain);
            shader.SetVector("scale", scale);
            shader.SetVector("tint", tint);
            shader.SetBool("invert", invert);

            M4[] actual = RunTextureProbe(context, shader, typeof(UniformProbe), null, iterations, out double gpuMs);

            ProbeResult result = Compare("uniforms", expected, actual, 2e-4f, gpuMs,
                sw.Elapsed.TotalMilliseconds, "SetFloat/SetVector uniforms packed into cbuffer DivisionParams");

            // Verify the CPU-side packing directly too: a wrong offset would still
            // produce plausible colors, so check the bytes at the expected offsets.
            result.PackingDetail = VerifyUniformPacking(shader, gain, scale, tint, invert);
            if (result.PackingDetail is not null)
            {
                result.Passed = false;
                result.Message = $"{result.Message}; packing: {result.PackingDetail}";
            }

            // The core of the Unity model: change a value and dispatch again. The
            // second dispatch must upload the new constant buffer.
            shader.SetBool("invert", !invert);
            M4[] updatedActual = RunTextureProbe(context, shader, typeof(UniformProbe), null, 1, out double _);

            M4[] updatedExpected = new M4[ProbeWidth * ProbeHeight];
            for (int y = 0; y < ProbeHeight; y++)
                for (int x = 0; x < ProbeWidth; x++)
                    updatedExpected[y * ProbeWidth + x] = UniformReference(x, y, gain, scale, tint, !invert);

            ProbeResult updated = Compare("uniforms-update", updatedExpected, updatedActual, 2e-4f, 0, 0,
                "uniforms re-uploaded on the next dispatch");
            if (!updated.Passed)
            {
                result.Passed = false;
                result.Message = $"{result.Message}; updated uniforms not applied: {updated.Message}";
            }
            else if (result.Passed)
            {
                result.Message = "SetFloat/SetVector/SetBool -> cbuffer DivisionParams (packing + re-upload verified)";
            }

            return result;
        }

        /// <summary>
        /// Checks the uniform API contract: unknown names and type mismatches are
        /// reported, and the declared uniform names are exposed.
        /// </summary>
        private static ProbeResult RunUniformApiProbe(DivisionComputeContext context)
        {
            using DivisionShader shader = CompileProbe(context, typeof(UniformProbe));
            var failures = new List<string>();

            Expect<ArgumentException>("unknown uniform name", () => shader.SetFloat("nope", 1f), failures);
            Expect<ArgumentException>("scalar setter on a vector", () => shader.SetFloat("scale", 1f), failures);
            Expect<ArgumentException>("vector setter on a scalar", () => shader.SetVector("width", new M3(1f, 2f, 3f)), failures);

            string[] expectedNames = ["width", "height", "gain", "scale", "tint", "invert"];
            if (!shader.UniformNames.SequenceEqual(expectedNames))
                failures.Add($"UniformNames = [{string.Join(", ", shader.UniformNames)}]");

            return new ProbeResult
            {
                Name = "uniform-api",
                Passed = failures.Count == 0,
                MaxError = 0f,
                MeanError = 0f,
                Mismatches = failures.Count,
                GpuMs = 0,
                CpuMs = 0,
                Message = failures.Count == 0
                    ? "name and type validation"
                    : string.Join("; ", failures),
            };
        }

        private static void Expect<TException>(string what, Action action, List<string> failures)
            where TException : Exception
        {
            try
            {
                action();
                failures.Add($"{what}: no exception thrown");
            }
            catch (TException)
            {
                // expected
            }
            catch (Exception ex)
            {
                failures.Add($"{what}: expected {typeof(TException).Name}, got {ex.GetType().Name}");
            }
        }

        private static ProbeResult RunBufferProbe(DivisionComputeContext context, int iterations)
        {
            const int count = 1000;
            ProbeItem[] items = new ProbeItem[count];
            for (int i = 0; i < count; i++)
            {
                items[i] = new ProbeItem
                {
                    value = i * 0.5f,
                    color = new M3(i / (float)count, 1f - i / (float)count, 0.25f),
                    index = i,
                };
            }

            ProbeItem[] expected = new ProbeItem[count];
            for (int i = 0; i < count; i++)
            {
                expected[i] = items[i];
                expected[i].value = items[i].value * 2f;
                expected[i].color = items[i].color * 0.5f + new M3(0.25f, 0f, 0f);
                expected[i].index = items[i].index + 1;
            }

            using StructuredBuffer<ProbeItem> input = context.CreateStructuredBuffer(items);
            using RWStructuredBuffer<ProbeItem> output = context.CreateRWStructuredBuffer<ProbeItem>(count);
            using DivisionShader shader = CompileProbe(context, typeof(BufferProbe));

            shader.SetStructuredBuffer("input", input);
            shader.SetRWStructuredBuffer("output", output);

            (uint gx, uint gy, uint gz) = GetGroupSize(typeof(BufferProbe), KernelName);
            int kernel = shader.FindKernel(KernelName);
            uint groups = (uint)((count + gx - 1) / gx);

            shader.Dispatch(kernel, groups, 1, 1); // warmup

            Stopwatch sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
                shader.Dispatch(kernel, groups, 1, 1);
            ProbeItem[] actual = output.GetData(context.Context);
            sw.Stop();

            float maxError = 0f;
            int mismatches = 0;
            for (int i = 0; i < count; i++)
            {
                float error = Math.Max(
                    Math.Abs(expected[i].value - actual[i].value),
                    Math.Max(
                        Math.Abs(expected[i].color.x - actual[i].color.x),
                        Math.Max(Math.Abs(expected[i].color.y - actual[i].color.y), Math.Abs(expected[i].color.z - actual[i].color.z))));
                maxError = Math.Max(maxError, error);
                if (error > 0f || expected[i].index != actual[i].index) mismatches++;
            }

            return new ProbeResult
            {
                Name = "buffer",
                Passed = mismatches == 0,
                MaxError = maxError,
                MeanError = maxError,
                Mismatches = mismatches,
                GpuMs = sw.Elapsed.TotalMilliseconds / iterations,
                CpuMs = 0,
                Message = mismatches == 0 ? "structured buffer round trip" : "structured buffer mismatch",
            };
        }

        /// <summary>
        /// Verifies the CPU-side HLSL packing offsets of the uniform probe's fields.
        /// </summary>
        /// <returns>Null when every field is at its expected offset, otherwise a description.</returns>
        private static string? VerifyUniformPacking(DivisionShader shader, float gain, M4 scale, M3 tint, bool invert)
        {
            byte[] packed = shader.GetUniformData();

            // width@0 height@4 gain@8 | scale@16 | tint@32 | invert@44, padded to 48.
            if (packed.Length != 48)
                return $"expected a 48-byte buffer, got {packed.Length}";

            (string Name, int Offset, float Value)[] floats =
            [
                ("width", 0, ProbeWidth),
                ("height", 4, ProbeHeight),
                ("gain", 8, gain),
                ("scale.x", 16, scale.x),
                ("scale.y", 20, scale.y),
                ("scale.z", 24, scale.z),
                ("scale.w", 28, scale.w),
                ("tint.x", 32, tint.x),
                ("tint.y", 36, tint.y),
                ("tint.z", 40, tint.z),
            ];

            foreach ((string name, int offset, float value) in floats)
            {
                float actual = BitConverter.ToSingle(packed, offset);
                if (!actual.Equals(value))
                    return $"{name} at byte {offset} is {actual}, expected {value}";
            }

            int flag = BitConverter.ToInt32(packed, 44);
            int expectedFlag = invert ? 1 : 0;
            return flag == expectedFlag
                ? null
                : $"invert at byte 44 is {flag}, expected {expectedFlag}";
        }

        #endregion selftest

        #region probe helpers

        private static ProbeResult RunTypedBufferProbe(DivisionComputeContext context, int iterations)
        {
            const int count = 4096;
            uint[] inputData = new uint[count];
            for (int i = 0; i < count; i++)
                inputData[i] = (uint)(i * 7);

            using DivisionShader shader = CompileProbe(context, typeof(TypedBufferProbe));
            using Buffer<uint> input = context.CreateBuffer<uint>(inputData);
            using RWBuffer<uint> output = context.CreateRWBuffer<uint>(count);

            shader.SetBuffer("input", input);
            shader.SetRWBuffer("output", output);

            int kernel = shader.FindKernel(KernelName);
            (uint gx, uint gy, uint gz) = GetGroupSize(typeof(TypedBufferProbe), KernelName);
            uint groups = (uint)((count + gx - 1) / gx);
            shader.Dispatch(kernel, groups, 1, gz); // warmup

            Stopwatch sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
                shader.Dispatch(kernel, groups, 1, gz);
            uint[] actual = output.GetData(context.Context);
            sw.Stop();

            int mismatches = 0;
            for (int i = 0; i < count; i++)
            {
                if (actual[i] != inputData[i] * 2u + 1u) mismatches++;
            }

            return new ProbeResult
            {
                Name = "typed-buffer",
                Passed = mismatches == 0,
                MaxError = 0f,
                MeanError = 0f,
                Mismatches = mismatches,
                GpuMs = sw.Elapsed.TotalMilliseconds / iterations,
                CpuMs = 0,
                Message = mismatches == 0
                    ? "Buffer<uint>/RWBuffer<uint> round trip"
                    : $"{mismatches} element(s) mismatched",
            };
        }

        /// <summary>
        /// Validates GPU-to-GPU texture copy and the device-lost poll.
        /// </summary>
        private static ProbeResult RunGpuCopyProbe(DivisionComputeContext context)
        {
            var failures = new List<string>();

            const int copyWidth = 64;
            const int copyHeight = 48;
            M4[] input = new M4[copyWidth * copyHeight];
            for (int y = 0; y < copyHeight; y++)
                for (int x = 0; x < copyWidth; x++)
                    input[y * copyWidth + x] = new M4(x / 64f, y / 48f, (x + y) % 7 / 7f, 1f);

            using RWTexture2D<M4> source = context.CreateRWTexture2D<M4>(copyWidth, copyHeight);
            using RWTexture2D<M4> destination = context.CreateRWTexture2D<M4>(copyWidth, copyHeight);
            source.SetData(context.Context, input);
            destination.SetData(context.Context, new M4[copyWidth * copyHeight]);

            source.CopyTo(context.Context, destination);
            M4[] copied = destination.GetData(context.Context);
            for (int i = 0; i < input.Length; i++)
            {
                if (copied[i] != input[i])
                {
                    failures.Add($"texel {i} differs after copy");
                    break;
                }
            }

            if (context.CheckDeviceLost())
                failures.Add("device reported as lost");
            if (context.IsDeviceLost)
                failures.Add("IsDeviceLost reported true");

            return new ProbeResult
            {
                Name = "gpu-copy",
                Passed = failures.Count == 0,
                MaxError = 0f,
                MeanError = 0f,
                Mismatches = failures.Count,
                GpuMs = 0,
                CpuMs = 0,
                Message = failures.Count == 0
                    ? "CopyResource round trip + device-lost poll"
                    : string.Join("; ", failures),
            };
        }
        private static DivisionShader CompileProbe(DivisionComputeContext context, Type shaderType)
        {
            HLSLTranslationResult translation = context.Compiler.TranslateShader(shaderType);
            if (!translation.IsSuccess)
                throw new InvalidOperationException($"Translation of '{shaderType.Name}' failed: {translation.ErrorMessage}");

            HLSLCompilationResult compilation = DivisionShaderCompiler.CompileHLSL(translation);
            if (!compilation.IsSuccess)
                throw new InvalidOperationException($"Compilation of '{shaderType.Name}' failed: {compilation.DebugMessage}");

            return context.CreateShader(compilation, shaderType);
        }

        private static M4[] RunTextureProbe(
            DivisionComputeContext context,
            DivisionShader shader,
            Type shaderType,
            M4[]? input,
            int iterations,
            out double gpuMs,
            Action<DivisionShader>? bindExtra = null)
        {
            using RWTexture2D<M4> outputTexture = context.CreateRWTexture2D<M4>(ProbeWidth, ProbeHeight);
            outputTexture.SetData(context.Context, new M4[ProbeWidth * ProbeHeight]);

            RWTexture2D<M4>? inputTexture = null;
            if (input is not null)
            {
                inputTexture = context.CreateRWTexture2D<M4>(ProbeWidth, ProbeHeight);
                inputTexture.SetData(context.Context, input);
                shader.SetRWTexture2D("input", inputTexture);
            }

            shader.SetRWTexture2D("output", outputTexture);
            bindExtra?.Invoke(shader);

            int kernel = shader.FindKernel(KernelName);
            if (kernel < 0)
                throw new InvalidOperationException($"Kernel '{KernelName}' not found in '{shaderType.Name}'.");

            shader.Dispatch2D(kernel, ProbeWidth, ProbeHeight); // warmup

            Stopwatch sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
                shader.Dispatch2D(kernel, ProbeWidth, ProbeHeight);
            M4[] result = outputTexture.GetData(context.Context); // forces completion
            sw.Stop();

            inputTexture?.Dispose();

            gpuMs = sw.Elapsed.TotalMilliseconds / iterations;
            return result;
        }

        /// <summary>
        /// Reads the thread-group size from the <see cref="KernelAttribute"/> instead
        /// of hard-coding it, so a shader and its dispatch can never drift apart.
        /// </summary>
        private static (uint x, uint y, uint z) GetGroupSize(Type shaderType, string kernelName)
        {
            MethodInfo? method = shaderType.GetMethod(
                kernelName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            KernelAttribute? kernel = method?.GetCustomAttribute<KernelAttribute>();
            if (kernel is null) return (64, 1, 1);
            return ((uint)kernel.ThreadsX, (uint)kernel.ThreadsY, (uint)kernel.ThreadsZ);
        }

        private static ProbeResult Compare(
            string name,
            M4[] expected,
            M4[] actual,
            float tolerance,
            double gpuMs,
            double cpuMs,
            string description)
        {
            if (expected.Length != actual.Length)
            {
                return new ProbeResult
                {
                    Name = name,
                    Passed = false,
                    MaxError = float.NaN,
                    MeanError = float.NaN,
                    Mismatches = Math.Abs(expected.Length - actual.Length),
                    GpuMs = gpuMs,
                    CpuMs = cpuMs,
                    Message = $"size mismatch: expected {expected.Length} texels, got {actual.Length}",
                };
            }

            float maxError = 0f;
            double sum = 0;
            int components = 0;
            int mismatches = 0;

            for (int i = 0; i < expected.Length; i++)
            {
                M4 e = expected[i];
                M4 a = actual[i];

                float dx = Math.Abs(e.x - a.x);
                float dy = Math.Abs(e.y - a.y);
                float dz = Math.Abs(e.z - a.z);
                float dw = Math.Abs(e.w - a.w);

                float pixelMax = Math.Max(Math.Max(dx, dy), Math.Max(dz, dw));
                maxError = Math.Max(maxError, pixelMax);
                sum += dx + dy + dz + dw;
                components += 4;
                if (pixelMax > tolerance) mismatches++;
            }

            float mean = components > 0 ? (float)(sum / components) : 0f;

            return new ProbeResult
            {
                Name = name,
                Passed = mismatches == 0,
                MaxError = maxError,
                MeanError = mean,
                Mismatches = mismatches,
                GpuMs = gpuMs,
                CpuMs = cpuMs,
                Message = mismatches == 0
                    ? description
                    : $"{mismatches} texel(s) outside tolerance {tolerance:E1} ({description})",
                Expected = expected,
                Actual = actual,
            };
        }

        #endregion probe helpers

        #region CPU references

        private static M4 MathReference(int x, int y)
        {
            M2 uv = new(x / 256f, y / 192f);
            M2 centered = uv - new M2(0.5f, 0.5f);
            float radial = math.length(centered);
            float wave = math.sin(uv.x * 12f) * math.cos(uv.y * 9f);
            float ramp = math.saturate(0.5f + 0.5f * wave);
            float glint = math.pow(ramp, 2f);
            float falloff = math.exp(-radial * 3f);
            float edge = math.smoothstep(0.2f, 0.8f, radial);
            float banding = math.floor(uv.y * 8f) / 8f;

            M3 baseColor = new(ramp, glint, falloff);
            baseColor = Tint(baseColor, 0.35f * edge);
            baseColor = baseColor + new M3(0.05f) * math.frac(uv.x * 7f);
            baseColor = baseColor + new M3(banding * 0.1f);

            float lum = math.dot(baseColor, new M3(0.2126f, 0.7152f, 0.0722f));
            M3 normal = math.normalize(new M3(centered.x, centered.y, 0.5f));
            M3 tangent = math.normalize(math.cross(normal, new M3(0f, 1f, 0f)));
            M3 bitangent = math.cross(normal, tangent);
            float wrap = math.max(math.min(lum, 0.9f), 0.05f);

            M4 color = new(baseColor * wrap + tangent * 0.1f + bitangent * 0.05f, 1f);
            return math.clamp(color, new M4(0f), new M4(1f));
        }

        private static M3 Tint(M3 rgb, float amount)
            => math.lerp(rgb, rgb * new M3(1f, 0.6f, 0.25f), amount);

        private static M4 BlurReference(M4[] input, int x, int y)
        {
            M4 result = new(0f);
            float totalWeight = 0f;

            for (int i = -4; i <= 4; i++)
            {
                int sampleX = x + i;
                if (sampleX < 0 || sampleX >= 256) continue;
                if (y < 0 || y >= 192) continue;

                float weight = GaussianWeight(i, 2f);
                result = result + input[y * ProbeWidth + sampleX] * weight;
                totalWeight += weight;
            }

            return totalWeight > 0f ? result / totalWeight : input[y * ProbeWidth + x];
        }

        private static float GaussianWeight(float x, float sigma)
        {
            float sigma2 = sigma * sigma;
            return 1f / math.sqrt(2f * 3.141592654f * sigma2) * math.exp(-(x * x) / (2f * sigma2));
        }

        private static ProbeResult RunHelperProbe(DivisionComputeContext context, int iterations)
        {
            const float gain = 1.5f;

            Stopwatch sw = Stopwatch.StartNew();
            M4[] expected = new M4[ProbeWidth * ProbeHeight];
            for (int y = 0; y < ProbeHeight; y++)
                for (int x = 0; x < ProbeWidth; x++)
                    expected[y * ProbeWidth + x] = HelperReference(x, y, gain);
            sw.Stop();

            using DivisionShader shader = CompileProbe(context, typeof(HelperProbe));
            shader.SetFloat("gain", gain);
            M4[] actual = RunTextureProbe(context, shader, typeof(HelperProbe), null, iterations, out double gpuMs);

            return Compare("helpers", expected, actual, 2e-4f, gpuMs, sw.Elapsed.TotalMilliseconds,
                "helper class calls, out params, switch, const fields");
        }

        private static M4 HelperReference(int x, int y, float gain)
        {
            M2 uv = new(x / 256f, y / 192f);

            M3 color = new(uv.x, uv.y, math.frac(uv.x + uv.y));
            M3 warm = new(1.0f, 0.6f, 0.25f);
            M3 tinted = math.lerp(color, color * warm, 0.5f);

            M3 sampled = tinted * 0.5f + new M3(0.25f);
            float weight = math.dot(sampled, new M3(0.2126f, 0.7152f, 0.0722f));

            int bucket = (int)(uv.x * 4f);
            float classified = bucket == 0 ? 0.1f : bucket == 1 ? 0.4f : bucket == 2 ? 0.7f : 1f;

            float scaled = classified * 2f * weight * gain;
            return new M4(sampled * scaled, 1f);
        }

        private static M4 UniformReference(int x, int y, float gain, M4 scale, M3 tint, bool invert)
        {
            M2 uv = new(x / (float)ProbeWidth, y / (float)ProbeHeight);
            M4 scaled = new M4(uv.x, uv.y, gain, ProbeHeight) * scale;
            M3 tinted = tint * scaled.x + tint * scaled.y;
            float invertAmount = invert ? 1f : 0f;
            M3 result = tinted * (1f - invertAmount) + (new M3(1f) - tinted) * invertAmount;
            return new M4(result, 1f);
        }

        #endregion CPU references

        #region image output

        private static void WriteProbeImages(string directory, ProbeResult probe)
        {
            WritePng(Path.Combine(directory, $"{probe.Name}_expected.png"), probe.Expected!, ProbeWidth, ProbeHeight, null);
            WritePng(Path.Combine(directory, $"{probe.Name}_actual.png"), probe.Actual!, ProbeWidth, ProbeHeight, null);
            WritePng(Path.Combine(directory, $"{probe.Name}_diff.png"), probe.Expected!, ProbeWidth, ProbeHeight,
                (index, pixel) =>
                {
                    M4 actual = probe.Actual![index];
                    return new M4(
                        Math.Min(1f, Math.Abs(pixel.x - actual.x) * 20f),
                        Math.Min(1f, Math.Abs(pixel.y - actual.y) * 20f),
                        Math.Min(1f, Math.Abs(pixel.z - actual.z) * 20f),
                        1f);
                });
        }

        private static void WritePng(string path, M4[] pixels, int width, int height, Func<int, M4, M4>? transform)
        {
            using SKBitmap bitmap = new(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
            byte[] bytes = new byte[width * height * 4];

            for (int i = 0; i < pixels.Length; i++)
            {
                M4 pixel = transform is null ? pixels[i] : transform(i, pixels[i]);
                bytes[i * 4 + 0] = ToByte(pixel.x);
                bytes[i * 4 + 1] = ToByte(pixel.y);
                bytes[i * 4 + 2] = ToByte(pixel.z);
                bytes[i * 4 + 3] = 255;
            }

            System.Runtime.InteropServices.Marshal.Copy(bytes, 0, bitmap.GetPixels(), bytes.Length);

            using SKImage image = SKImage.FromBitmap(bitmap);
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 95);
            using FileStream stream = File.OpenWrite(path);
            data.SaveTo(stream);
        }

        private static byte ToByte(float value)
            => (byte)Math.Clamp((int)(value * 255f + 0.5f), 0, 255);

        #endregion image output

        #region discovery and argument helpers

        private static List<Type> DiscoverShaderTypes()
        {
            // Engine assemblies load on demand, and a CLI command may not have touched
            // one yet. Referencing an anchor type from each forces the load so the
            // scan below sees shaders in Core as well as in the editor.
            _ = typeof(DivisionCLI).Assembly;
            _ = typeof(Rendering.RenderPipeline).Assembly;

            var types = new List<Type>();

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name = assembly.GetName().Name ?? string.Empty;
                if (!name.StartsWith("DivisionEngine", StringComparison.Ordinal)) continue;

                Type[] candidates;
                try
                {
                    candidates = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    candidates = [.. ex.Types.Where(t => t is not null).Cast<Type>()];
                }

                foreach (Type type in candidates)
                {
                    if (!type.IsValueType || type.IsGenericTypeDefinition) continue;
                    if (type.GetCustomAttribute<ShaderAttribute>() is null) continue;
                    if (type.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() is not null) continue;
                    types.Add(type);
                }
            }

            return [.. types.OrderBy(t => t.Name, StringComparer.Ordinal)];
        }

        private static List<Type> FilterShaders(List<Type> shaders, string? filter)
        {
            if (string.IsNullOrWhiteSpace(filter)) return shaders;
            return [.. shaders.Where(s =>
                s.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                (s.FullName?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false))];
        }

        private static string? GetString(string[] args, string name, string? fallback)
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

        private static bool HasFlag(string[] args, string name) => Array.IndexOf(args, name) >= 0;

        #endregion discovery and argument helpers

        /// <summary>
        /// Result of one probe run.
        /// </summary>
        private sealed class ProbeResult
        {
            public required string Name { get; init; }
            public bool Passed { get; set; }
            public float MaxError { get; init; }
            public float MeanError { get; init; }
            public int Mismatches { get; init; }
            public double GpuMs { get; init; }
            public double CpuMs { get; init; }
            public double CsMs { get; set; }
            public double DcMs { get; set; }
            public string? Message { get; set; }
            public string? PackingDetail { get; set; }
            public M4[]? Expected { get; init; }
            public M4[]? Actual { get; init; }
        }
    }
}
