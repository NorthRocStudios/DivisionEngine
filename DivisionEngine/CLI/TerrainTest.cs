//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE file in the project root
// for full license terms.
//
// `DivisionEngine.Editor.exe --cli terrain <command>` - terrain system tools and
// regression tests for the procedural height field, the erosion filter, the
// Catmull-Rom heightmap reconstruction and the quadtree chunk addressing.
//
#pragma warning disable CA1416 // Validate platform compatibility

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DivisionEngine.Components;
using DivisionEngine.Components.Lights;
using DivisionEngine.Components.SDFs;
using DivisionEngine.Components.SDFs.Effects;
using DivisionEngine.Components.SDFs.Fractals;
using DivisionEngine.Components.SDFs.Primitives;
using DivisionEngine.MathUtilities;
using DivisionEngine.Graphics;
using DivisionEngine.MathLib;
using DivisionEngine.Rendering;
using DivisionEngine.Rendering.Terrain;
using DivisionEngine.Systems;
using SkiaSharp;
using Environment = DivisionEngine.Components.Environment;

namespace DivisionEngine.Editor
{
    /// <summary>
    /// Terrain CLI: reference images, benchmarks, unit checks, GPU/CPU conformance
    /// and a dedicated terrain capture scene.
    /// </summary>
    internal static class TerrainTest
    {
        public static bool Run(string[] args)
        {
            string command = args.Length > 2 ? args[2].ToLowerInvariant() : "help";
            try
            {
                switch (command)
                {
                    case "image": return RunImage(args);
                    case "bench": return RunBench(args);
                    case "unit": return RunUnit(args);
                    case "gputest": return RunGpuTest(args);
                    case "preview": return RunPreview(args);
                    case "shot": return RunShot(args);
                    case "help":
                    case "--help":
                    case "-h":
                        PrintHelp();
                        DivisionCLI.SetExitCode(0);
                        return true;
                    default:
                        Console.Error.WriteLine($"Unknown terrain command '{command}'.");
                        PrintHelp();
                        DivisionCLI.SetExitCode(1);
                        return true;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"terrain: {ex}");
                DivisionCLI.SetExitCode(1);
                return true;
            }
        }

        private static void PrintHelp()
        {
            Console.WriteLine("""
                Division CLI - terrain commands

                Usage:
                  DivisionEngine.Editor.exe --cli terrain <command> [options]

                Commands:
                  image    Render CPU height/ridge/erosion/shaded reference images
                           Options: --out <dir> --size N --terrain-size N --terrain-height N
                                    --terrain-frequency F --no-erosion
                  bench    Benchmark CPU chunk baking
                           Options: --chunk N --iterations N
                  unit     Run height-field, interpolation, seam and quadtree checks
                  gputest  Compare the GPU bake shader against the CPU bake
                           Options: --chunk N
                  preview  Ray-march the CPU height field into a reference render
                           Options: --out <file> --width N --height N --grid N
                                    --terrain-size N --terrain-height N --terrain-frequency F --no-erosion
                  shot     Render a dedicated eroded-terrain scene
                           Options: --out <file> --width N --height N --fixed-frame N
                                    --terrain-size N --terrain-height N
                                    --terrain-frequency F --heightmap-size N
                                    --interpolation bilinear|hermite
                                    --infinite --no-erosion --pos x,y,z --look x,y,z
                                    --no-post --voxel (use the voxel clipmap renderer)
                                    --no-shadows  (disable terrain shadow casting)
                                    --no-prewarm  (infinite: capture mid-streaming)
                                    --diagnostics  (print GPU metadata / buffer diagnostics)
                  help     Show this message
                """);
        }

        #region options

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

        private static float GetFloat(string[] args, string name, float fallback)
        {
            int index = Array.IndexOf(args, name);
            if (index >= 0 && index + 1 < args.Length &&
                float.TryParse(args[index + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                return value;
            return fallback;
        }

        private static bool HasFlag(string[] args, string name) => Array.IndexOf(args, name) >= 0;

        private static SDFTerrain MakeTerrain(string[] args)
        {
            SDFTerrain terrain = new SDFTerrain
            {
                size = GetFloat(args, "--terrain-size", 500f),
                height = GetFloat(args, "--terrain-height", 90f),
                frequency = GetFloat(args, "--terrain-frequency", 0.0015f),
                heightmapSize = GetInt(args, "--heightmap-size", 1024),
                infinite = HasFlag(args, "--infinite"),
                erosion = !HasFlag(args, "--no-erosion"),
                levels = GetInt(args, "--levels", 8),
                chunkResolution = GetInt(args, "--chunk-resolution", 80),
                chunkSize = GetFloat(args, "--chunk-size", 24f),
                chunksPerLevel = GetInt(args, "--chunks-per-level", 5),
            };

            // --interpolation bilinear|hermite (case-insensitive; "bicubic" is the
            // old name for Hermite and is still accepted so existing scripts work)
            string interpolation = GetString(args, "--interpolation", "");
            if (!string.IsNullOrWhiteSpace(interpolation))
            {
                if (string.Equals(interpolation, "bicubic", StringComparison.OrdinalIgnoreCase))
                    terrain.interpolation = SDFTerrain.Interpolation.Hermite;
                else if (Enum.TryParse(interpolation, ignoreCase: true, out SDFTerrain.Interpolation mode))
                    terrain.interpolation = mode;
                else
                    throw new ArgumentException(
                        $"--interpolation must be bilinear or hermite (got '{interpolation}').");
            }

            return terrain;
        }

        #endregion

        #region image

        private static bool RunImage(string[] args)
        {
            int size = GetInt(args, "--size", 768);
            string outDirectory = GetString(args, "--out", Path.Combine(System.Environment.CurrentDirectory, "terrain-images"));
            Directory.CreateDirectory(outDirectory);

            SDFTerrain terrain = MakeTerrain(args);
            TerrainDTO meta = TerrainSystem.CreateMetadata(terrain, default);

            float extent = terrain.infinite ? 900f : terrain.size;
            float[] heights = new float[size * size];
            float[] ridges = new float[size * size];
            float[] erosions = new float[size * size];
            float[] slopes = new float[size * size];
            float minHeight = float.MaxValue, maxHeight = float.MinValue;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float wx = ((x + 0.5f) / size * 2f - 1f) * extent;
                    float wz = ((y + 0.5f) / size * 2f - 1f) * extent;
                    float ridge, erosion;
                    float4 sample = TerrainRendering.Heightmap(new float2(wx, wz), meta, out ridge, out erosion);
                    int i = y * size + x;
                    heights[i] = sample.x;
                    slopes[i] = math.length(new float2(sample.y, sample.z));
                    ridges[i] = sample.w;
                    erosions[i] = erosion;
                    minHeight = math.min(minHeight, sample.x);
                    maxHeight = math.max(maxHeight, sample.x);
                }
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "terrain image: {0}x{0} over [-{1:F0},{1:F0}], height [{2:F2},{3:F2}], erosion={4}",
                size, extent, minHeight, maxHeight, terrain.erosion));

            WriteImage(Path.Combine(outDirectory, "height.png"), size, size, (x, y) =>
            {
                float t = (heights[y * size + x] - minHeight) / math.max(maxHeight - minHeight, 1e-6f);
                byte v = (byte)System.Math.Clamp((int)(t * 255f), 0, 255);
                return new SKColor(v, v, v);
            });

            WriteImage(Path.Combine(outDirectory, "ridge.png"), size, size, (x, y) =>
            {
                byte v = (byte)System.Math.Clamp((int)(ridges[y * size + x] * 255f), 0, 255);
                return new SKColor(v, v, v);
            });

            WriteImage(Path.Combine(outDirectory, "erosion.png"), size, size, (x, y) =>
            {
                byte v = (byte)System.Math.Clamp((int)(erosions[y * size + x] * 255f), 0, 255);
                return new SKColor(v, v, v);
            });

            // Shaded relief with the reference palette.
            float3 sun = math.normalize(new float3(-0.55f, 0.7f, -0.45f));
            WriteImage(Path.Combine(outDirectory, "shaded.png"), size, size, (x, y) =>
            {
                int i = y * size + x;
                float h = (heights[i] - minHeight) / math.max(maxHeight - minHeight, 1e-6f);
                float slope = slopes[i];
                float3 normal = math.normalize(new float3(-slope, 1f, 0f));
                float ndotl = math.max(math.dot(normal, sun), 0f);
                float light = 0.32f + 0.78f * ndotl;

                float3 grass = math.lerp(new float3(0.10f, 0.20f, 0.07f), new float3(0.38f, 0.46f, 0.16f), ridges[i]);
                float3 rock = math.lerp(new float3(0.20f, 0.22f, 0.26f), new float3(0.46f, 0.47f, 0.5f), ridges[i]);
                float3 dirt = new float3(0.38f, 0.32f, 0.24f);
                float3 snow = new float3(0.93f, 0.95f, 0.98f);

                float steep = math.saturate((0.82f - 1f / math.sqrt(1f + slope * slope)) * 3f);
                float3 color = math.lerp(grass, dirt, math.saturate(steep * 0.5f));
                color = math.lerp(color, rock, steep);
                color = math.lerp(color, snow, math.smoothstep(0.72f, 0.82f, h));
                float drainage = math.saturate((1f - math.saturate(ridges[i] / 0.34f)) * 1.5f);
                color = math.lerp(color, new float3(0.05f, 0.08f, 0.07f), drainage * 0.6f);
                color = color * light * 1.25f;

                return ToColor(color);
            });

            Console.WriteLine($"terrain image: wrote {outDirectory}");
            DivisionCLI.SetExitCode(0);
            return true;
        }

        #endregion

        #region bench

        private static bool RunBench(string[] args)
        {
            int chunk = GetInt(args, "--chunk", 96);
            int iterations = GetInt(args, "--iterations", 3);
            SDFTerrain terrain = MakeTerrain(args);
            TerrainDTO meta = TerrainSystem.CreateMetadata(terrain, default);
            int stride = chunk + 2;

            // Warm up the JIT.
            TerrainChunkBaker.BakeChunk(meta, new float2(0f, 0f), 32f, chunk, stride);

            double best = double.MaxValue;
            for (int i = 0; i < iterations; i++)
            {
                System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                TerrainChunkBaker.BakeChunk(meta, new float2(i * 32f, 0f), 32f, chunk, stride);
                watch.Stop();
                best = System.Math.Min(best, watch.Elapsed.TotalMilliseconds);
            }

            int texels = stride * stride;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{{\"chunk\":{0},\"texels\":{1},\"erosion\":{2},\"bestMs\":{3:F2},\"msPerTexel\":{4:F4},\"chunksPerSecond\":{5:F2}}}",
                chunk, texels, terrain.erosion, best, best / texels, 1000.0 / best));
            DivisionCLI.SetExitCode(0);
            return true;
        }

        #endregion

        #region unit

        private static bool RunUnit(string[] args)
        {
            int passed = 0, failed = 0;
            void Check(string name, Func<bool> check)
            {
                try
                {
                    if (check()) { passed++; Console.WriteLine($"  PASS  {name}"); }
                    else { failed++; Console.WriteLine($"  FAIL  {name}"); }
                }
                catch (Exception ex)
                {
                    failed++;
                    Console.WriteLine($"  FAIL  {name} ({ex.GetType().Name}: {ex.Message})");
                    Console.WriteLine(ex.StackTrace);
                }
            }

            Console.WriteLine("Terrain unit checks");
            SDFTerrain terrain = MakeTerrain(args);
            TerrainDTO meta = TerrainSystem.CreateMetadata(terrain, default);

            Check("Base height stays finite and bounded", () =>
            {
                for (int i = 0; i < 2000; i++)
                {
                    float x = (i % 50 - 25) * 31.7f;
                    float z = (i / 50 - 20) * 27.3f;
                    float3 b = TerrainRendering.BaseHeight(new float2(x, z), meta);
                    if (!float.IsFinite(b.x) || !float.IsFinite(b.y) || !float.IsFinite(b.z)) return false;
                    if (math.abs(b.x) > meta.heightScale * 2.0f) return false;
                }
                return true;
            });

            Check("Eroded height stays finite", () =>
            {
                for (int i = 0; i < 2000; i++)
                {
                    float x = (i % 50 - 25) * 31.7f;
                    float z = (i / 50 - 20) * 27.3f;
                    float ridge, erosion;
                    float4 s = TerrainRendering.Heightmap(new float2(x, z), meta, out ridge, out erosion);
                    if (!float.IsFinite(s.x) || !float.IsFinite(s.y) || !float.IsFinite(s.z)) return false;
                    if (ridge < 0f || ridge > 1f) return false;
                    if (erosion < 0f || erosion > 1f) return false;
                }
                return true;
            });

            Check("Catmull-Rom interpolates its control points", () =>
            {
                float a = 3.25f, b = -1.5f, c = 7.75f, d = 0.125f;
                return math.abs(TerrainRendering.CatmullRom(a, b, c, d, 0f) - b) < 1e-5f
                    && math.abs(TerrainRendering.CatmullRom(a, b, c, d, 1f) - c) < 1e-5f;
            });

            Check("Catmull-Rom is C1 across texel boundaries", () =>
            {
                // Derivative at t=1 of one span must match t=0 of the next.
                float a = 1f, b = 3f, c = 2f, d = 5f, e = 0.5f;
                float left = TerrainRendering.CatmullRom(b, c, d, e, 0f) - TerrainRendering.CatmullRom(a, b, c, d, 1f);
                return math.abs(left) < 1e-5f;
            });

            Check("Catmull-Rom reconstruction beats bilinear on curved data", () =>
            {
                int n = 9;
                float[] data = new float[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                        data[y * n + x] = math.sin(x * 0.9f) * math.cos(y * 0.8f);

                float bilinearError = 0f, cubicError = 0f;
                for (int sample = 0; sample < 200; sample++)
                {
                    // Stay at least one texel away from every edge so both
                    // interpolation kernels have their full 4x4 neighbourhood.
                    float tx = 1.5f + (sample * 0.37f) % (n - 5f);
                    float ty = 1.5f + (sample * 0.61f) % (n - 5f);
                    float truth = math.sin(tx * 0.9f) * math.cos(ty * 0.8f);

                    int x0 = (int)math.floor(tx), y0 = (int)math.floor(ty);
                    float fx = tx - x0, fy = ty - y0;
                    float bilinear = math.lerp(
                        math.lerp(data[y0 * n + x0], data[y0 * n + x0 + 1], fx),
                        math.lerp(data[(y0 + 1) * n + x0], data[(y0 + 1) * n + x0 + 1], fx), fy);

                    float r0 = TerrainRendering.CatmullRom(data[(y0 - 1) * n + x0 - 1], data[(y0 - 1) * n + x0], data[(y0 - 1) * n + x0 + 1], data[(y0 - 1) * n + x0 + 2], fx);
                    float r1 = TerrainRendering.CatmullRom(data[y0 * n + x0 - 1], data[y0 * n + x0], data[y0 * n + x0 + 1], data[y0 * n + x0 + 2], fx);
                    float r2 = TerrainRendering.CatmullRom(data[(y0 + 1) * n + x0 - 1], data[(y0 + 1) * n + x0], data[(y0 + 1) * n + x0 + 1], data[(y0 + 1) * n + x0 + 2], fx);
                    float r3 = TerrainRendering.CatmullRom(data[(y0 + 2) * n + x0 - 1], data[(y0 + 2) * n + x0], data[(y0 + 2) * n + x0 + 1], data[(y0 + 2) * n + x0 + 2], fx);
                    float cubic = TerrainRendering.CatmullRom(r0, r1, r2, r3, fy);

                    bilinearError += math.abs(bilinear - truth);
                    cubicError += math.abs(cubic - truth);
                }
                Console.WriteLine($"        mean error: bilinear={bilinearError / 200f:F5} cubic={cubicError / 200f:F5}");
                return cubicError < bilinearError * 0.75f;
            });

            Check("Hermite reconstruction is C1, accurate and cheaper than Catmull-Rom", () =>
            {
                // Analytic test surface and its slopes.
                float F(float x, float z) => math.sin(x * 0.9f) * math.cos(z * 0.8f);
                float Fx(float x, float z) => 0.9f * math.cos(x * 0.9f) * math.cos(z * 0.8f);
                float Fz(float x, float z) => -0.8f * math.sin(x * 0.9f) * math.sin(z * 0.8f);

                float4 P(float x, float z) => new float4(F(x, z), Fx(x, z), Fz(x, z), 0.5f);

                float heightError = 0f;
                float bilinearError = 0f;
                for (int sample = 0; sample < 100; sample++)
                {
                    float tx = 0.05f + sample * 0.0091f;
                    float tz = 0.05f + sample * 0.0073f;
                    float4 h = TerrainRendering.Hermite4(P(2f, 3f), P(3f, 3f), P(2f, 4f), P(3f, 4f), tx, tz, 1f, 1f);
                    float truth = F(2f + tx, 3f + tz);
                    heightError += math.abs(h.x - truth);

                    float b = math.lerp(
                        math.lerp(F(2f, 3f), F(3f, 3f), tx),
                        math.lerp(F(2f, 4f), F(3f, 4f), tx), tz);
                    bilinearError += math.abs(b - truth);
                }
                heightError /= 100f;
                bilinearError /= 100f;
                Console.WriteLine($"        mean error: bilinear={bilinearError:F5} hermite={heightError:F5}");
                if (heightError > bilinearError * 0.25f) return false;

                // Height and the cross-cell derivative are continuous across the
                // shared edge of two cells along Z.
                float4 a = TerrainRendering.Hermite4(P(2f, 2f), P(3f, 2f), P(2f, 3f), P(3f, 3f), 0.37f, 1f, 1f, 1f);
                float4 b2 = TerrainRendering.Hermite4(P(2f, 3f), P(3f, 3f), P(2f, 4f), P(3f, 4f), 0.37f, 0f, 1f, 1f);
                float4 c = TerrainRendering.Hermite4(P(2f, 2f), P(3f, 2f), P(2f, 3f), P(3f, 3f), 1f, 0.42f, 1f, 1f);
                float4 d = TerrainRendering.Hermite4(P(3f, 2f), P(4f, 2f), P(3f, 3f), P(4f, 3f), 0f, 0.42f, 1f, 1f);
                Console.WriteLine($"        z-edge: dh={math.abs(a.x - b2.x):E2} dslope={math.abs(a.z - b2.z):E2}");
                Console.WriteLine($"        x-edge: dh={math.abs(c.x - d.x):E2} dslope={math.abs(c.y - d.y):E2}");
                return math.abs(a.x - b2.x) < 1e-4f
                    && math.abs(a.z - b2.z) < 1e-4f
                    && math.abs(c.x - d.x) < 1e-4f
                    && math.abs(c.y - d.y) < 1e-4f;
            });

            Check("Bilinear terrain uses the bilinear surface gradient, not stored slopes", () =>
            {
                // The stored slopes describe the source surface. Bilinear mode must
                // derive its shading normal from the bilinear patch itself, otherwise
                // faceted geometry is lit as if it were still smooth.
                float4 p00 = new float4(1.0f, 3.0f, -2.0f, 0.2f);
                float4 p10 = new float4(4.0f, 7.0f, 5.0f, 0.4f);
                float4 p01 = new float4(2.0f, -4.0f, 1.0f, 0.6f);
                float4 p11 = new float4(6.0f, 2.0f, -3.0f, 0.8f);
                float dx = 2.5f, dz = 4.0f, tx = 0.37f, tz = 0.62f;

                float4 s = TerrainRendering.BilinearTerrain4(p00, p10, p01, p11, tx, tz, dx, dz);
                float expectedHeight = math.lerp(
                    math.lerp(p00.x, p10.x, tx), math.lerp(p01.x, p11.x, tx), tz);
                float expectedDx = ((p10.x - p00.x) * (1f - tz) + (p11.x - p01.x) * tz) / dx;
                float expectedDz = ((p01.x - p00.x) * (1f - tx) + (p11.x - p10.x) * tx) / dz;

                // At the cell corner the bilinear gradient is the row/column
                // finite difference, never the stored analytic slope.
                float4 corner = TerrainRendering.BilinearTerrain4(p00, p10, p01, p11, 0f, 0f, dx, dz);
                Console.WriteLine($"        h={s.x:F4} dx={s.y:F4} dz={s.z:F4} " +
                    $"corner dx={corner.y:F4} dz={corner.z:F4}");

                return math.abs(s.x - expectedHeight) < 1e-5f
                    && math.abs(s.y - expectedDx) < 1e-5f
                    && math.abs(s.z - expectedDz) < 1e-5f
                    && math.abs(corner.y - (p10.x - p00.x) / dx) < 1e-5f
                    && math.abs(corner.z - (p01.x - p00.x) / dz) < 1e-5f;
            });

            Check("Hermite patch reproduces corner slopes and exact derivatives", () =>
            {
                float4 P(float h, float fx, float fz) => new float4(h, fx, fz, 0.5f);
                float4 p00 = P(1.0f, 0.5f, -0.25f);
                float4 p10 = P(4.0f, -0.75f, 1.5f);
                float4 p01 = P(2.0f, 1.25f, 0.75f);
                float4 p11 = P(6.0f, 0.25f, -1.0f);
                float dx = 0.75f, dz = 1.25f;

                // Every corner is interpolated and its tangent equals the stored slope.
                float4 c00 = TerrainRendering.Hermite4(p00, p10, p01, p11, 0f, 0f, dx, dz);
                float4 c10 = TerrainRendering.Hermite4(p00, p10, p01, p11, 1f, 0f, dx, dz);
                float4 c01 = TerrainRendering.Hermite4(p00, p10, p01, p11, 0f, 1f, dx, dz);
                float4 c11 = TerrainRendering.Hermite4(p00, p10, p01, p11, 1f, 1f, dx, dz);
                bool corners = math.abs(c00.x - p00.x) < 1e-5f && math.abs(c00.y - p00.y) < 1e-4f && math.abs(c00.z - p00.z) < 1e-4f
                    && math.abs(c10.x - p10.x) < 1e-5f && math.abs(c10.y - p10.y) < 1e-4f && math.abs(c10.z - p10.z) < 1e-4f
                    && math.abs(c01.x - p01.x) < 1e-5f && math.abs(c01.y - p01.y) < 1e-4f && math.abs(c01.z - p01.z) < 1e-4f
                    && math.abs(c11.x - p11.x) < 1e-5f && math.abs(c11.y - p11.y) < 1e-4f && math.abs(c11.z - p11.z) < 1e-4f;

                // The returned slopes must be the analytic derivatives of the same
                // patch, since shading normals are built from them.
                float maxDx = 0f, maxDz = 0f;
                const float eps = 1e-3f;
                for (int sample = 0; sample < 64; sample++)
                {
                    float tx = 0.15f + (sample % 8) * 0.1f;
                    float tz = 0.15f + (sample / 8) * 0.1f;
                    float4 h = TerrainRendering.Hermite4(p00, p10, p01, p11, tx, tz, dx, dz);
                    float hxp = TerrainRendering.Hermite4(p00, p10, p01, p11, tx + eps, tz, dx, dz).x;
                    float hxm = TerrainRendering.Hermite4(p00, p10, p01, p11, tx - eps, tz, dx, dz).x;
                    float hzp = TerrainRendering.Hermite4(p00, p10, p01, p11, tx, tz + eps, dx, dz).x;
                    float hzm = TerrainRendering.Hermite4(p00, p10, p01, p11, tx, tz - eps, dx, dz).x;
                    maxDx = math.max(maxDx, math.abs(h.y - (hxp - hxm) / (2f * eps * dx)));
                    maxDz = math.max(maxDz, math.abs(h.z - (hzp - hzm) / (2f * eps * dz)));
                }
                Console.WriteLine($"        derivative error: max dx={maxDx:E2} dz={maxDz:E2}");
                return corners && maxDx < 2e-3f && maxDz < 2e-3f;
            });

            Check("Adjacent chunks agree along their shared seam", () =>
            {
                int resolution = 96;
                int stride = resolution + 2;
                float levelSize = 64f;
                float2 originA = new float2(0f, 0f);
                float2 originB = new float2(levelSize, 0f);
                float4[] a = TerrainChunkBaker.BakeChunk(meta, originA, levelSize, resolution, stride);
                float4[] b = TerrainChunkBaker.BakeChunk(meta, originB, levelSize, resolution, stride);

                float maxError = 0f;
                for (int row = 0; row <= resolution; row++)
                {
                    float wz = row / (float)resolution * levelSize;
                    float ha = SampleChunk(a, stride, resolution, new float2(levelSize, wz), originA, levelSize, true);
                    float hb = SampleChunk(b, stride, resolution, new float2(levelSize, wz), originB, levelSize, true);
                    maxError = math.max(maxError, math.abs(ha - hb));
                }
                Console.WriteLine($"        max seam error: {maxError:F6}");
                return maxError < 1e-3f;
            });

            Check("Quadtree slots are unique and stable", () =>
            {
                List<TerrainChunkKey> desired = [];
                int gridSize = 5;
                for (int cameraStep = 0; cameraStep < 20; cameraStep++)
                {
                    float2 camera = new float2(cameraStep * 17.3f - 120f, cameraStep * -9.1f);
                    TerrainClipmap.CollectDesiredChunks(camera, new float2(0f, 0f), 16f, 6, gridSize, desired);

                    HashSet<int> slots = [];
                    HashSet<TerrainChunkKey> keys = [];
                    foreach (TerrainChunkKey key in desired)
                    {
                        int slot = TerrainClipmap.SlotIndex(key.Level, key.ChunkX, key.ChunkZ, gridSize);
                        if (!slots.Add(slot)) return false;
                        if (!keys.Add(key)) return false;
                    }
                    if (desired.Count != 6 * gridSize * gridSize) return false;

                    // The camera's own position must be covered at every level.
                    for (int level = 0; level < 6; level++)
                    {
                        float levelSize = TerrainClipmap.LevelSize(16f, level);
                        int cx = TerrainClipmap.ChunkCoord(camera.x, 0f, levelSize);
                        int cz = TerrainClipmap.ChunkCoord(camera.y, 0f, levelSize);
                        bool found = false;
                        foreach (TerrainChunkKey key in desired)
                            if (key.Level == level && key.ChunkX == cx && key.ChunkZ == cz) found = true;
                        if (!found) return false;
                    }
                }
                return true;
            });

            Check("GPU-buffer sampling matches the analytic height field (fixed terrain)", () =>
            {
                // Smooth base noise so the comparison measures the reconstruction,
                // not the aliasing of erosion octaves that are near the texel Nyquist
                // (the separate infinite test covers the eroded case).
                SDFTerrain fixedTerrain = MakeTerrain(args);
                fixedTerrain.infinite = false;
                fixedTerrain.erosion = false;
                fixedTerrain.size = 200f;
                fixedTerrain.heightmapSize = 256;
                fixedTerrain.frequency = 0.004f;
                TerrainDTO fixedMeta = TerrainSystem.CreateMetadata(fixedTerrain, default);
                fixedMeta.bufferOffset = 0;
                fixedMeta.chunkInfoOffset = 0;

                int stride = fixedMeta.chunkStride;
                float2 origin = new float2(-fixedTerrain.size, -fixedTerrain.size);
                float4[] data = TerrainChunkBaker.BakeChunk(fixedMeta, origin, fixedMeta.chunkSize,
                    fixedMeta.resolution.x, stride);
                int4[] info = new int4[1];
                info[0] = new int4(0, 0, 1, 0);

                float maxHeightError = 0f;
                float maxSlopeError = 0f;
                double sumHeightError = 0;
                double sumSlopeError = 0;
                int missing = 0;
                for (int i = 0; i < 600; i++)
                {
                    float x = (i * 37 % 200 - 100) / 100f * fixedTerrain.size * 0.95f;
                    float z = (i * 53 % 200 - 100) / 100f * fixedTerrain.size * 0.95f;
                    float4 sampled = TerrainSampling.Sample(fixedMeta, data, info, new float2(x, z),
                        out int found, out _);
                    if (found == 0) { missing++; continue; }

                    float ridge, erosion;
                    float4 truth = TerrainRendering.Heightmap(new float2(x, z), fixedMeta, out ridge, out erosion);
                    float heightError = math.abs(sampled.x - truth.x);
                    float slopeError = math.max(math.abs(sampled.y - truth.y), math.abs(sampled.z - truth.z));
                    maxHeightError = math.max(maxHeightError, heightError);
                    maxSlopeError = math.max(maxSlopeError, slopeError);
                    sumHeightError += heightError;
                    sumSlopeError += slopeError;
                }
                int count = 600 - missing;
                double meanHeightError = sumHeightError / math.max(count, 1);
                double meanSlopeError = sumSlopeError / math.max(count, 1);

                Console.WriteLine($"        height error mean/max: {meanHeightError:F3}/{maxHeightError:F2} m, " +
                    $"slope error mean/max: {meanSlopeError:F3}/{maxSlopeError:F3}, missing: {missing}");
                return missing == 0
                    && meanHeightError < fixedTerrain.height * 0.002
                    && maxHeightError < fixedTerrain.height * 0.02
                    && meanSlopeError < 0.1;
            });

            Check("Infinite quadtree sampling finds the right LOD and falls back", () =>
            {
                SDFTerrain infiniteTerrain = MakeTerrain(args);
                infiniteTerrain.infinite = true;
                infiniteTerrain.levels = 4;
                infiniteTerrain.chunksPerLevel = 3;
                infiniteTerrain.chunkResolution = 32;
                infiniteTerrain.chunkSize = 8f;
                infiniteTerrain.height = 30f;
                infiniteTerrain.frequency = 0.01f;
                infiniteTerrain.erosion = false;

                TerrainDTO meta2 = TerrainSystem.CreateMetadata(infiniteTerrain, default);
                meta2.bufferOffset = 0;
                meta2.chunkInfoOffset = 0;
                meta2.lodCenter = new float2(12.3f, -7.4f);

                int slots = meta2.levels * meta2.gridSize * meta2.gridSize;
                int stride2 = meta2.chunkStride;
                float4[] data = new float4[slots * stride2 * stride2];
                int4[] info = new int4[slots];
                for (int i = 0; i < slots; i++) info[i] = new int4(int.MinValue, int.MinValue, 0, 0);

                List<TerrainChunkKey> desired = [];
                TerrainClipmap.CollectDesiredChunks(meta2.lodCenter, new float2(0f, 0f),
                    meta2.chunkSize, meta2.levels, meta2.gridSize, desired);

                foreach (TerrainChunkKey key in desired)
                {
                    float levelSize = TerrainClipmap.LevelSize(meta2.chunkSize, key.Level);
                    float2 chunkOrigin = new float2(key.ChunkX * levelSize, key.ChunkZ * levelSize);
                    float4[] chunk = TerrainChunkBaker.BakeChunk(meta2, chunkOrigin, levelSize,
                        meta2.resolution.x, stride2);
                    int slot = TerrainClipmap.SlotIndex(key.Level, key.ChunkX, key.ChunkZ, meta2.gridSize);
                    Array.Copy(chunk, 0, data, slot * stride2 * stride2, chunk.Length);
                    info[slot] = new int4(key.ChunkX, key.ChunkZ, 1, 0);
                }

                float4 atCamera = TerrainSampling.Sample(meta2, data, info, meta2.lodCenter,
                    out int foundCamera, out int levelCamera);
                float ridge2, erosion2;
                float4 truth2 = TerrainRendering.Heightmap(
                    meta2.lodCenter + new float2(meta2.position.x, meta2.position.z), meta2,
                    out ridge2, out erosion2);

                float4 farSample = TerrainSampling.Sample(meta2, data, info,
                    new float2(4000f, 4000f), out int foundFar, out int levelFar);

                // Remove the finest chunk under the camera: sampling must fall back
                // to a coarser level instead of disappearing.
                int cameraChunkX = (int)math.floor(meta2.lodCenter.x / meta2.chunkSize);
                int cameraChunkZ = (int)math.floor(meta2.lodCenter.y / meta2.chunkSize);
                int cameraSlot = TerrainClipmap.SlotIndex(0, cameraChunkX, cameraChunkZ, meta2.gridSize);
                info[cameraSlot] = new int4(int.MinValue, int.MinValue, 0, 0);
                float4 fallback = TerrainSampling.Sample(meta2, data, info, meta2.lodCenter,
                    out int foundFallback, out int levelFallback);

                Console.WriteLine($"        camera: level={levelCamera} h={atCamera.x:F2} (truth {truth2.x:F2}), " +
                    $"far: found={foundFar} level={levelFar}, fallback: found={foundFallback} level={levelFallback}");

                return foundCamera == 1
                    && levelCamera == 0
                    && math.abs(atCamera.x - truth2.x) < infiniteTerrain.height * 0.1f
                    && foundFar == 1
                    && foundFallback == 1
                    && levelFallback > 0;
            });

            Check("Infinite quadtree follows the editor camera while editing, the main camera in play mode", () =>
            {
                // Reproduces the editor setup: the gameplay camera (with Player controls)
                // and a free-flying editor camera are both in the world.
                World world = new("terrain-camera-test");
                WorldManager.SetWorld(world);
                WorldManager.SwitchWorld(world.Name);

                uint terrainEntity = world.CreateTransformEntity("Terrain");
                world.GetComponent<Transform>(terrainEntity)!.position = new float3(0f, 0f, 0f);
                world.AddComponent(terrainEntity, new SDFTerrain
                {
                    infinite = true,
                    levels = 3,
                    chunksPerLevel = 3,
                    chunkResolution = 16,
                    chunkSize = 32f,
                    bakeThreads = 1,
                    chunksPerFrame = 8,
                    erosion = false,
                    height = 30f,
                    frequency = 0.01f,
                });

                uint mainCamera = world.CreateTransformEntity("Camera");
                world.GetComponent<Transform>(mainCamera)!.position = new float3(100f, 5f, 200f);
                world.AddComponent(mainCamera, new Camera { isActive = false });
                world.AddComponent(mainCamera, new Player());

                if (!world.AddEntityWithId(EditorCamera.EditorCameraId, "EditorCamera")) return false;
                world.AddComponent(EditorCamera.EditorCameraId, new Transform
                {
                    position = new float3(-3000f, 8f, 5000f),
                });
                world.AddComponent(EditorCamera.EditorCameraId, new Camera { isActive = true });
                world.AddComponent(EditorCamera.EditorCameraId, new EditorCamera());

                TerrainSystem system = new();
                bool enteredPlayMode = false;
                try
                {
                    // Editing: the render view is the editor camera, so the quadtree
                    // and the GPU LOD/fade anchor must follow it.
                    system.Render();
                    bool editFollowsEditor = TerrainSystem.TryGetTerrainMetadata(terrainEntity, out TerrainDTO editA)
                        && math.abs(editA.lodCenter.x + 3000f) < 1e-3f
                        && math.abs(editA.lodCenter.y - 5000f) < 1e-3f;
                    bool gpuEditFollowsEditor = TerrainSystem.AllTerrainMetadata is { Length: 1 } gpuEditA
                        && math.abs(gpuEditA[0].lodCenter.x + 3000f) < 1e-3f
                        && math.abs(gpuEditA[0].lodCenter.y - 5000f) < 1e-3f;

                    world.GetComponent<Transform>(EditorCamera.EditorCameraId)!.position =
                        new float3(-6000f, 12f, 7000f);
                    system.Render();
                    bool editFollowsMovedEditor = TerrainSystem.TryGetTerrainMetadata(terrainEntity, out TerrainDTO editB)
                        && math.abs(editB.lodCenter.x + 6000f) < 1e-3f
                        && math.abs(editB.lodCenter.y - 7000f) < 1e-3f;

                    // Play mode: the rendering camera is the gameplay camera. The editor
                    // camera is still in the world, but must be ignored.
                    EngineCore.EnterPlayMode();
                    enteredPlayMode = true;
                    system.Render();
                    bool playFollowsMain = TerrainSystem.TryGetTerrainMetadata(terrainEntity, out TerrainDTO playA)
                        && math.abs(playA.lodCenter.x - 100f) < 1e-3f
                        && math.abs(playA.lodCenter.y - 200f) < 1e-3f;
                    bool gpuPlayFollowsMain = TerrainSystem.AllTerrainMetadata is { Length: 1 } gpuPlayA
                        && math.abs(gpuPlayA[0].lodCenter.x - 100f) < 1e-3f
                        && math.abs(gpuPlayA[0].lodCenter.y - 200f) < 1e-3f;

                    world.GetComponent<Transform>(mainCamera)!.position = new float3(500f, 5f, -400f);
                    system.Render();
                    bool playFollowsMovedMain = TerrainSystem.TryGetTerrainMetadata(terrainEntity, out TerrainDTO playB)
                        && math.abs(playB.lodCenter.x - 500f) < 1e-3f
                        && math.abs(playB.lodCenter.y + 400f) < 1e-3f;
                    bool gpuPlayFollowsMovedMain = TerrainSystem.AllTerrainMetadata is { Length: 1 } gpuPlayB
                        && math.abs(gpuPlayB[0].lodCenter.x - 500f) < 1e-3f
                        && math.abs(gpuPlayB[0].lodCenter.y + 400f) < 1e-3f;

                    world.GetComponent<Transform>(EditorCamera.EditorCameraId)!.position =
                        new float3(8000f, 12f, -8000f);
                    system.Render();
                    bool playIgnoresEditor = TerrainSystem.TryGetTerrainMetadata(terrainEntity, out TerrainDTO playC)
                        && math.abs(playC.lodCenter.x - 500f) < 1e-3f
                        && math.abs(playC.lodCenter.y + 400f) < 1e-3f;

                    Console.WriteLine($"        lodCenter edit={editB.lodCenter} play={playB.lodCenter}");

                    return editFollowsEditor && gpuEditFollowsEditor && editFollowsMovedEditor
                        && playFollowsMain && gpuPlayFollowsMain && playFollowsMovedMain
                        && gpuPlayFollowsMovedMain && playIgnoresEditor;
                }
                finally
                {
                    // Leave the engine exactly as a unit test found it (no play mode,
                    // no background terrain state, no current world).
                    if (enteredPlayMode) EngineCore.ExitPlayMode();
                    world.DestroyEntity(terrainEntity);
                    system.Render();
                    WorldManager.ClearAllWorlds();
                }
            });

            Check("LOD level grows with distance and clamps", () =>
            {
                return TerrainRendering.LodLevel(1f, 60f, 8) == 0
                    && TerrainRendering.LodLevel(70f, 60f, 8) == 0
                    && TerrainRendering.LodLevel(121f, 60f, 8) == 1
                    && TerrainRendering.LodLevel(100000f, 60f, 8) == 7;
            });

            Console.WriteLine();
            Console.WriteLine($"{{\"checks\":{passed + failed},\"passed\":{passed},\"failed\":{failed}}}");
            Console.WriteLine(failed == 0 ? "terrain unit: PASS" : "terrain unit: FAIL");
            DivisionCLI.SetExitCode(failed == 0 ? 0 : 1);
            return true;
        }

        private static float SampleChunk(float4[] data, int stride, int resolution, float2 local,
            float2 chunkOrigin, float levelSize, bool cubic)
        {
            float2 texel = (local - chunkOrigin) / levelSize * (resolution - 1) + 1f;
            if (!cubic)
            {
                int x0 = Clamp((int)math.floor(texel.x), 0, stride - 2);
                int z0 = Clamp((int)math.floor(texel.y), 0, stride - 2);
                float fx = texel.x - x0, fz = texel.y - z0;
                float4 a = math.lerp(data[z0 * stride + x0], data[z0 * stride + x0 + 1], fx);
                float4 b = math.lerp(data[(z0 + 1) * stride + x0], data[(z0 + 1) * stride + x0 + 1], fx);
                return math.lerp(a, b, fz).x;
            }

            int ix = (int)math.floor(texel.x);
            int iz = (int)math.floor(texel.y);
            float tx = texel.x - ix, tz = texel.y - iz;
            float r0 = TerrainRendering.CatmullRom(
                data[Clamp(iz - 1, 0, stride - 1) * stride + Clamp(ix - 1, 0, stride - 1)].x,
                data[Clamp(iz - 1, 0, stride - 1) * stride + Clamp(ix, 0, stride - 1)].x,
                data[Clamp(iz - 1, 0, stride - 1) * stride + Clamp(ix + 1, 0, stride - 1)].x,
                data[Clamp(iz - 1, 0, stride - 1) * stride + Clamp(ix + 2, 0, stride - 1)].x, tx);
            float r1 = TerrainRendering.CatmullRom(
                data[Clamp(iz, 0, stride - 1) * stride + Clamp(ix - 1, 0, stride - 1)].x,
                data[Clamp(iz, 0, stride - 1) * stride + Clamp(ix, 0, stride - 1)].x,
                data[Clamp(iz, 0, stride - 1) * stride + Clamp(ix + 1, 0, stride - 1)].x,
                data[Clamp(iz, 0, stride - 1) * stride + Clamp(ix + 2, 0, stride - 1)].x, tx);
            float r2 = TerrainRendering.CatmullRom(
                data[Clamp(iz + 1, 0, stride - 1) * stride + Clamp(ix - 1, 0, stride - 1)].x,
                data[Clamp(iz + 1, 0, stride - 1) * stride + Clamp(ix, 0, stride - 1)].x,
                data[Clamp(iz + 1, 0, stride - 1) * stride + Clamp(ix + 1, 0, stride - 1)].x,
                data[Clamp(iz + 1, 0, stride - 1) * stride + Clamp(ix + 2, 0, stride - 1)].x, tx);
            float r3 = TerrainRendering.CatmullRom(
                data[Clamp(iz + 2, 0, stride - 1) * stride + Clamp(ix - 1, 0, stride - 1)].x,
                data[Clamp(iz + 2, 0, stride - 1) * stride + Clamp(ix, 0, stride - 1)].x,
                data[Clamp(iz + 2, 0, stride - 1) * stride + Clamp(ix + 1, 0, stride - 1)].x,
                data[Clamp(iz + 2, 0, stride - 1) * stride + Clamp(ix + 2, 0, stride - 1)].x, tx);
            return TerrainRendering.CatmullRom(r0, r1, r2, r3, tz);
        }

        private static int Clamp(int value, int min, int max)
            => value < min ? min : (value > max ? max : value);

        #endregion

        #region gputest

        private static bool RunGpuTest(string[] args)
        {
            int chunk = GetInt(args, "--chunk", 64);
            SDFTerrain terrain = MakeTerrain(args);
            TerrainDTO meta = TerrainSystem.CreateMetadata(terrain, default);
            int stride = chunk + 2;
            float levelSize = 64f;
            float2 origin = new float2(24f, -16f);

            System.Diagnostics.Stopwatch cpuWatch = System.Diagnostics.Stopwatch.StartNew();
            float4[] cpu = TerrainChunkBaker.BakeChunk(meta, origin, levelSize, chunk, stride);
            cpuWatch.Stop();

            using DivisionComputeContext context = new();
            System.Diagnostics.Stopwatch gpuWatch = System.Diagnostics.Stopwatch.StartNew();
            float4[] gpu = TerrainSystem.BakeChunkOnGpu(context, meta, origin, levelSize, chunk, stride);
            gpuWatch.Stop();

            float maxHeightDiff = 0f, maxSlopeDiff = 0f, maxRidgeDiff = 0f;
            int mismatches = 0;
            for (int i = 0; i < cpu.Length; i++)
            {
                float hd = math.abs(cpu[i].x - gpu[i].x);
                float sd = math.max(math.abs(cpu[i].y - gpu[i].y), math.abs(cpu[i].z - gpu[i].z));
                float rd = math.abs(cpu[i].w - gpu[i].w);
                maxHeightDiff = math.max(maxHeightDiff, hd);
                maxSlopeDiff = math.max(maxSlopeDiff, sd);
                maxRidgeDiff = math.max(maxRidgeDiff, rd);
                if (hd > 5e-3f || sd > 5e-3f || rd > 5e-3f) mismatches++;
            }

            bool ok = mismatches == 0;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{{\"texels\":{0},\"cpuMs\":{1:F2},\"gpuMs\":{2:F2},\"maxHeightDiff\":{3:E4},\"maxSlopeDiff\":{4:E4},\"maxRidgeDiff\":{5:E4},\"mismatches\":{6},\"pass\":{7}}}",
                cpu.Length, cpuWatch.Elapsed.TotalMilliseconds, gpuWatch.Elapsed.TotalMilliseconds,
                maxHeightDiff, maxSlopeDiff, maxRidgeDiff, mismatches, ok ? "true" : "false"));
            Console.WriteLine(ok ? "terrain gputest: PASS" : "terrain gputest: FAIL");
            DivisionCLI.SetExitCode(ok ? 0 : 1);
            return true;
        }

        #endregion

        #region preview

        private static bool RunPreview(string[] args)
        {
            int width = GetInt(args, "--width", 960);
            int height = GetInt(args, "--height", 540);
            int grid = GetInt(args, "--grid", 1024);
            string outPath = GetString(args, "--out", Path.Combine(System.Environment.CurrentDirectory, "terrain-preview.png"));

            SDFTerrain terrain = MakeTerrain(args);
            TerrainDTO meta = TerrainSystem.CreateMetadata(terrain, default);
            float extent = terrain.size;
            float cell = extent * 2f / (grid - 1);

            float[] heights = new float[grid * grid];
            float[] ridgeMap = new float[grid * grid];
            System.Threading.Tasks.Parallel.For(0, grid, y =>
            {
                for (int x = 0; x < grid; x++)
                {
                    float wx = -extent + x * cell;
                    float wz = -extent + y * cell;
                    float ridge, erosion;
                    float4 sample = TerrainRendering.Heightmap(new float2(wx, wz), meta, out ridge, out erosion);
                    heights[y * grid + x] = sample.x;
                    ridgeMap[y * grid + x] = ridge;
                }
            });

            // Slopes are sampled from the height grid (finite differences) so the
            // preview shading matches what the GPU reconstructs, not the analytic
            // derivative of the source noise.
            float3[] normals = new float3[grid * grid];
            System.Threading.Tasks.Parallel.For(0, grid, y =>
            {
                int ym = y > 0 ? y - 1 : 0;
                int yp = y < grid - 1 ? y + 1 : grid - 1;
                for (int x = 0; x < grid; x++)
                {
                    int xm = x > 0 ? x - 1 : 0;
                    int xp = x < grid - 1 ? x + 1 : grid - 1;
                    float hx = (heights[y * grid + xp] - heights[y * grid + xm]) / (cell * (xp - xm));
                    float hz = (heights[yp * grid + x] - heights[ym * grid + x]) / (cell * (yp - ym));
                    normals[y * grid + x] = math.normalize(new float3(-hx, 1f, -hz));
                }
            });

            float3 cameraPosition = new float3(extent * 1.9f, extent * 1.25f, extent * 1.9f);
            float3 target = new float3(0f, -terrain.height * 0.15f, 0f);
            float3 forward = math.normalize(target - cameraPosition);
            float3 right = math.normalize(math.cross(forward, new float3(0f, 1f, 0f)));
            float3 up = math.cross(right, forward);
            float fov = math.tan(GetFloat(args, "--fov", 26f) * 0.5f * (float)System.Math.PI / 180f);

            float3 sun = math.normalize(new float3(-0.55f, 0.62f, -0.45f));
            float3 skyColor = new float3(0.44f, 0.52f, 0.62f);
            float minHeight = float.MaxValue, maxHeight = float.MinValue;
            for (int i = 0; i < heights.Length; i++)
            {
                minHeight = math.min(minHeight, heights[i]);
                maxHeight = math.max(maxHeight, heights[i]);
            }

            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            WriteImage(outPath, width, height, (px, py) =>
            {
                float u = ((px + 0.5f) / width * 2f - 1f) * fov * (width / (float)height);
                float v = (0.5f - (py + 0.5f) / height) * 2f * fov;
                float3 dir = math.normalize(forward + right * u + up * v);

                // Ray/box clip so sky pixels cost almost nothing.
                float tMin = 0f, tMax = 1e9f;
                if (!ClipBox(cameraPosition, dir, extent, terrain.height * 4f, ref tMin, ref tMax))
                    return ToColor(skyColor);

                float t = math.max(tMin, 0.05f);
                float hit = -1f;
                float3 hitPoint = new float3(0f);
                for (int step = 0; step < 420 && t < tMax; step++)
                {
                    float3 p = cameraPosition + dir * t;
                    float h = SampleGrid(heights, grid, cell, extent, new float2(p.x, p.z));
                    float3 n = SampleGridNormal(normals, grid, cell, extent, new float2(p.x, p.z));
                    float slope = math.length(new float2(n.x, n.z)) / math.max(n.y, 1e-3f);
                    float distance = (p.y - h) / math.sqrt(1f + slope * slope);
                    if (distance < 0.05f + t * 0.0004f)
                    {
                        hit = t;
                        hitPoint = p;
                        break;
                    }
                    t += math.max(distance * 0.75f, 0.12f + t * 0.0008f);
                }

                if (hit < 0f) return ToColor(skyColor);

                float3 normal = SampleGridNormal(normals, grid, cell, extent, new float2(hitPoint.x, hitPoint.z));
                float h02 = SampleGrid(heights, grid, cell, extent, new float2(hitPoint.x, hitPoint.z));
                float ridge = SampleGrid(ridgeMap, grid, cell, extent, new float2(hitPoint.x, hitPoint.z));
                float altitude = math.saturate(h02 / math.max(terrain.height * 1.6f, 1e-3f) * 0.5f + 0.5f);
                float normalY = normal.y;
                float steep = math.saturate((0.86f - normalY) * 3.2f);

                float3 grass = math.lerp(new float3(0.10f, 0.20f, 0.07f), new float3(0.38f, 0.46f, 0.16f),
                    math.saturate((1f - ridge) * 1.4f));
                float3 rock = math.lerp(new float3(0.19f, 0.21f, 0.25f), new float3(0.44f, 0.45f, 0.49f), ridge);
                float3 dirt = new float3(0.36f, 0.31f, 0.24f);
                float3 snow = new float3(0.93f, 0.95f, 0.98f);
                float3 color = math.lerp(grass, dirt, math.saturate(steep * 0.55f));
                color = math.lerp(color, rock, steep);
                float snowAmount = math.smoothstep(0.80f, 0.89f, altitude + normalY * 0.05f - 0.02f);
                color = math.lerp(color, snow, snowAmount);
                float drainage = math.saturate((1f - math.saturate(ridge / 0.34f)) * 1.5f);
                color = math.lerp(color, new float3(0.045f, 0.07f, 0.06f), drainage * 0.65f);

                float shadow = ShadowRay(heights, grid, cell, extent, hitPoint + normal * 0.3f, sun, terrain.height * 3f);
                float ndotl = math.max(math.dot(normal, sun), 0f);
                float3 light = new float3(0.34f, 0.40f, 0.50f) * 1.15f + new float3(1.0f, 0.96f, 0.9f) * (ndotl * 1.55f * shadow);
                color *= light;
                color = math.lerp(color, skyColor, Fog(hit / (extent * 3f)));
                return ToColor(color);
            });
            watch.Stop();

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "terrain preview: {0} ({1}x{2}, grid={3}, height [{4:F1},{5:F1}], {6:F0} ms)",
                outPath, width, height, grid, minHeight, maxHeight, watch.Elapsed.TotalMilliseconds));
            DivisionCLI.SetExitCode(0);
            return true;
        }

        private static float Fog(float amount) => math.saturate(amount * 0.55f) * 0.65f;

        private static bool ClipBox(float3 origin, float3 dir, float halfXZ, float halfY,
            ref float tMin, ref float tMax)
        {
            tMin = 0f;
            tMax = 1e9f;
            for (int axis = 0; axis < 3; axis++)
            {
                float o = axis == 0 ? origin.x : (axis == 1 ? origin.y : origin.z);
                float d = axis == 0 ? dir.x : (axis == 1 ? dir.y : dir.z);
                float half = axis == 1 ? halfY : halfXZ;
                if (math.abs(d) < 1e-6f)
                {
                    if (o < -half || o > half) return false;
                    continue;
                }
                float t0 = (-half - o) / d;
                float t1 = (half - o) / d;
                if (t0 > t1) { float tmp = t0; t0 = t1; t1 = tmp; }
                tMin = math.max(tMin, t0);
                tMax = math.min(tMax, t1);
                if (tMin > tMax) return false;
            }
            return tMax > 0f;
        }

        private static float SampleGrid(float[] heights, int grid, float cell, float extent, float2 worldXZ)
        {
            float fx = (worldXZ.x + extent) / cell;
            float fz = (worldXZ.y + extent) / cell;
            fx = math.clamp(fx, 0f, grid - 1.001f);
            fz = math.clamp(fz, 0f, grid - 1.001f);
            int x0 = (int)math.floor(fx), z0 = (int)math.floor(fz);
            int x1 = math.min(x0 + 1, grid - 1), z1 = math.min(z0 + 1, grid - 1);
            float tx = fx - x0, tz = fz - z0;
            float a = math.lerp(heights[z0 * grid + x0], heights[z0 * grid + x1], tx);
            float b = math.lerp(heights[z1 * grid + x0], heights[z1 * grid + x1], tx);
            return math.lerp(a, b, tz);
        }

        private static float3 SampleGridNormal(float3[] normals, int grid, float cell, float extent, float2 worldXZ)
        {
            float fx = (worldXZ.x + extent) / cell;
            float fz = (worldXZ.y + extent) / cell;
            fx = math.clamp(fx, 0f, grid - 1.001f);
            fz = math.clamp(fz, 0f, grid - 1.001f);
            int x0 = (int)math.floor(fx), z0 = (int)math.floor(fz);
            int x1 = math.min(x0 + 1, grid - 1), z1 = math.min(z0 + 1, grid - 1);
            float tx = fx - x0, tz = fz - z0;
            float3 a = math.lerp(normals[z0 * grid + x0], normals[z0 * grid + x1], tx);
            float3 b = math.lerp(normals[z1 * grid + x0], normals[z1 * grid + x1], tx);
            return math.normalize(math.lerp(a, b, tz));
        }

        private static float RidgeAt(float[] heights, int grid, float cell, float extent, float2 worldXZ)
        {
            // Approximate the ridge map from the local curvature: creases are
            // concave (positive Laplacian), ridges convex.
            float h = SampleGrid(heights, grid, cell, extent, worldXZ);
            float hx = SampleGrid(heights, grid, cell, extent, worldXZ + new float2(cell, 0f));
            float hz = SampleGrid(heights, grid, cell, extent, worldXZ + new float2(0f, cell));
            float hxx = SampleGrid(heights, grid, cell, extent, worldXZ - new float2(cell, 0f));
            float hzz = SampleGrid(heights, grid, cell, extent, worldXZ - new float2(0f, cell));
            float laplacian = (hx + hxx + hz + hzz - 4f * h) / (cell * cell);
            float ridge = math.clamp(0.5f + laplacian * cell * 1.2f, 0f, 1f);
            return ridge;
        }

        private static float ShadowRay(float[] heights, int grid, float cell, float extent,
            float3 origin, float3 sun, float maxDistance)
        {
            float step = math.max(cell * 1.5f, 1f);
            float t = step;
            for (int i = 0; i < 64 && t < maxDistance; i++)
            {
                float3 p = origin + sun * t;
                float h = SampleGrid(heights, grid, cell, extent, new float2(p.x, p.z));
                if (p.y < h) return 0.25f;
                t += step;
            }
            return 1f;
        }

        #endregion

        #region shot

        private static bool RunShot(string[] args)
        {
            int width = GetInt(args, "--width", 1280);
            int height = GetInt(args, "--height", 720);
            string outPath = GetString(args, "--out", Path.Combine(System.Environment.CurrentDirectory, "terrain.png"));
            int fixedFrame = GetInt(args, "--fixed-frame", 2);

            RenderPipeline.DeterministicCapture = true;
            MathUtilities.Random.Seed(GetInt(args, "--seed", 12345));

            WorldManager.CreateDefaultWorld(makeCurrent: true);
            World world = WorldManager.CurrentWorld!;

            // Remove the default demo objects: the terrain capture should show only
            // the terrain.
            List<uint> remove = [];
            foreach (uint id in new List<uint>(world.entities))
            {
                if (world.HasComponent<SDFSphere>(id) || world.HasComponent<SDFMandelbulb>(id)
                    || world.HasComponent<SDFMengerSponge>(id) || world.HasComponent<SDFRoundedBox>(id)
                    || world.HasComponent<SDFTerrain>(id))
                    remove.Add(id);
            }
            foreach (uint id in remove) world.DestroyEntity(id);

            // Place the terrain.
            uint terrainEntity = world.CreateTransformEntity("Terrain");
            world.AddComponent(terrainEntity, new Transform { position = new float3(0f, 0f, 0f) });
            SDFTerrain terrain = MakeTerrain(args);
            world.AddComponent(terrainEntity, terrain);
            world.AddComponent(terrainEntity, new SDFMaterial
            {
                albedoColor = ColorPalette.ForestGreen,
                roughness = 1f,
                metallic = 0f,
            });
            if (!HasFlag(args, "--no-shadows"))
                world.AddComponent(terrainEntity, new Shadows { maxDistance = 500f, penumbraDistance = 90f });

            // Frame it like the reference renders: a 3/4 aerial view of the slab.
            float extent = terrain.infinite ? 1200f : terrain.size;
            foreach ((uint id, Transform transform, Camera camera) in world.QueryData<Transform, Camera>())
            {
                if (id == EditorCamera.EditorCameraId) continue;

                string position = GetString(args, "--pos", "");
                string look = GetString(args, "--look", "");
                float3 camPos = position.Length > 0
                    ? ParseVector(position, new float3(extent * 1.9f, extent * 1.25f, extent * 1.9f))
                    : new float3(extent * 1.9f, extent * 1.25f, extent * 1.9f);
                float3 target = look.Length > 0
                    ? ParseVector(look, new float3(0f, -terrain.height * 0.1f, 0f))
                    : new float3(0f, -terrain.height * 0.1f, 0f);
                float3 dir = math.normalize(target - camPos);
                float pitch = MathF.Asin(System.Math.Clamp(dir.y, -1f, 1f));
                float yaw = MathF.Atan2(-dir.x, -dir.z);

                transform.position = camPos;
                transform.rotation = new float3(pitch, yaw, 0f).EulerToQuaternion().Normalize();
                camera.fieldOfView = GetFloat(args, "--fov", terrain.infinite ? 50f : 42f);
                camera.nearClip = 0.5f;
                camera.farClip = 20000f;
                camera.maxRaySteps = 320;
                camera.maxShadowRaySteps = 256;
                camera.antiAliasing = Camera.AntiAliasingMode.None;
                break;
            }

            foreach ((uint _, Environment environment) in world.QueryData<Environment>())
            {
                environment.skyType = Environment.SkyType.Gradient;
                environment.bottomSkyColor = new float4(0.20f, 0.24f, 0.29f, 1f);
                environment.middleSkyColor = new float4(0.32f, 0.38f, 0.46f, 1f);
                environment.topSkyColor = new float4(0.25f, 0.32f, 0.43f, 1f);
                environment.skyIntensity = 1f;
                environment.ambientStrength = 0.44f;
                break;
            }

            foreach ((uint _, DirectionalLight light) in world.QueryData<DirectionalLight>())
            {
                light.intensity = 1.45f;
                break;
            }

            // Terrain captures default to the exact SDF path: the voxel clipmap is
            // a separate renderer with its own (longer) shader compile, and the
            // terrain feature is defined against the exact path.
            RenderPipeline.VoxelModeOverride = HasFlag(args, "--voxel") ? true : false;

            RenderPipeline pipeline = new();
            pipeline.BindCurrentWorld();
            pipeline.CurrentDebugMode = (RenderPipeline.DebugMode)GetInt(args, "--debug-mode", 0);
            if (HasFlag(args, "--no-post"))
            {
                foreach ((uint _, Camera camera) in world.QueryData<Camera>())
                {
                    camera.enableDenoise = false;
                    camera.antiAliasing = Camera.AntiAliasingMode.None;
                    break;
                }
                foreach ((uint _, PostProcessing post) in world.QueryData<PostProcessing>())
                {
                    post.enableDepthOfField = false;
                    post.enableBlur = false;
                    post.enableBloom = false;
                    post.enableChromaticAberration = false;
                    post.enableVignette = false;
                    break;
                }
            }

            // Stream the quadtree to completion before capturing. Without this the
            // frame loop can outrun the background bakers (especially for a
            // deterministic capture, which does not wait on wall-clock time).
            if (terrain.infinite && !HasFlag(args, "--no-prewarm"))
            {
                TerrainSystem? terrainSystem = world.GetSystem<TerrainSystem>();
                if (terrainSystem != null)
                {
                    System.Diagnostics.Stopwatch prewarm = System.Diagnostics.Stopwatch.StartNew();
                    int lastLoaded = -1;
                    while (prewarm.Elapsed.TotalSeconds < 45.0)
                    {
                        terrainSystem.Render();
                        if (TerrainSystem.PendingChunkCount == 0 && TerrainSystem.LoadedChunkCount == lastLoaded) break;
                        lastLoaded = TerrainSystem.LoadedChunkCount;
                        System.Threading.Thread.Sleep(5);
                    }
                    Console.WriteLine($"prewarm: loaded={TerrainSystem.LoadedChunkCount} pending={TerrainSystem.PendingChunkCount} " +
                        $"in {prewarm.Elapsed.TotalSeconds:F2}s");
                }
            }

            WarnIfShaderCacheCold();
            float4[]? pixels = pipeline.RenderFixedFrames(width, height, fixedFrame, 1.0 / 60.0);
            if (pixels is null) throw new InvalidOperationException("Render produced no pixels.");

            SavePng(outPath, width, height, pixels);

            int loaded = TerrainSystem.LoadedChunkCount;
            int pending = TerrainSystem.PendingChunkCount;
            if (HasFlag(args, "--diagnostics"))
            {
                SDFWorldDTO worldInfo = SDFRenderSystem.PreparedWorldDTO;
                Console.WriteLine($"  camera origin={worldInfo.cameraOrigin} forward={worldInfo.camForward} " +
                    $"up={worldInfo.camUp} far={worldInfo.farPlane} steps={worldInfo.maxRaySteps}");
                Console.WriteLine($"diagnostics: sdfObjects={SDFRenderSystem.PreparedSDFObjectsDTO.Length}, " +
                    $"terrains={TerrainSystem.AllTerrainMetadata?.Length}, texels={TerrainSystem.AllTerrainData?.Length}, " +
                    $"chunkInfo={TerrainSystem.AllTerrainChunkInfo?.Length}");
                foreach (SDFObjectDTO obj in SDFRenderSystem.PreparedSDFObjectsDTO)
                    Console.WriteLine($"  obj type={obj.type} terrainIndex={obj.terrainDataIndex} pos={obj.position} scale={obj.parameters.x}");
                if (TerrainSystem.AllTerrainMetadata is { Length: > 0 } metas)
                {
                    TerrainDTO m0 = metas[0];
                    ProbeTerrainMetadata(m0);
                    Console.WriteLine($"  terrain meta: res={m0.resolution.x} stride={m0.chunkStride} levels={m0.levels} " +
                        $"chunkSize={m0.chunkSize} worldSpace={m0.worldSpace} bufferOffset={m0.bufferOffset} " +
                        $"infoOffset={m0.chunkInfoOffset} size={m0.size} pos={m0.position} " +
                        $"lodCenter={m0.lodCenter} cameraHeight={m0.worldMin.x}");
                }
                if (TerrainSystem.AllTerrainChunkInfo is { Length: > 0 } info0)
                    Console.WriteLine($"  chunk info[0]: {info0[0]}");
                if (TerrainSystem.AllTerrainData is { Length: > 0 } data0 && TerrainSystem.AllTerrainMetadata is { Length: > 0 } metas0
                    && TerrainSystem.AllTerrainChunkInfo is { Length: > 0 } info1)
                {
                    float hmin = float.MaxValue, hmax = float.MinValue;
                    for (int i = 0; i < math.min(data0.Length, 20000); i++)
                    {
                        hmin = math.min(hmin, data0[i].x);
                        hmax = math.max(hmax, data0[i].x);
                    }
                    float4 s0 = TerrainSampling.Sample(metas0[0], data0, info1, new float2(0f, 0f),
                        out int found0, out int level0);
                    Console.WriteLine($"  data: h=[{hmin:F1},{hmax:F1}] sample(0,0)=({s0.x:F2},{s0.y:F2},{s0.z:F2}) found={found0} level={level0}");
                    Console.WriteLine($"  object scaling={SDFRenderSystem.PreparedSDFObjectsDTO[0].scaling} rotation={SDFRenderSystem.PreparedSDFObjectsDTO[0].rotation}");
                }
            }
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "terrain shot: {0} ({1}x{2}, infinite={3}, erosion={4}, loadedChunks={5}, pendingChunks={6})",
                outPath, width, height, terrain.infinite, terrain.erosion, loaded, pending));
            RenderPipeline.DeterministicCapture = false;
            DivisionCLI.SetExitCode(0);
            return true;
        }

        private static float3 ParseVector(string text, float3 fallback)
        {
            string[] parts = text.Split(',');
            if (parts.Length == 3 &&
                float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
                float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                return new float3(x, y, z);
            return fallback;
        }

        private static void WarnIfShaderCacheCold() => DivisionCLI.WarnIfShaderCacheCold();

        private static void SavePng(string path, int width, int height, float4[] pixels)
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
            System.Runtime.InteropServices.Marshal.Copy(bytes, 0, bitmap.GetPixels(), bytes.Length);
            using SKImage image = SKImage.FromBitmap(bitmap);
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 95);
            using FileStream stream = File.Create(path);
            data.SaveTo(stream);
        }

        #endregion

        #region image helpers

        private static void WriteImage(string path, int width, int height, Func<int, int, SKColor> color)
        {
            using SKBitmap bitmap = new(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
            unsafe
            {
                byte* pixels = (byte*)bitmap.GetPixels().ToPointer();
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        SKColor c = color(x, y);
                        int i = (y * width + x) * 4;
                        pixels[i + 0] = c.Red;
                        pixels[i + 1] = c.Green;
                        pixels[i + 2] = c.Blue;
                        pixels[i + 3] = 255;
                    }
                }
            }
            using SKImage image = SKImage.FromBitmap(bitmap);
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 95);
            using FileStream stream = File.Create(path);
            data.SaveTo(stream);
        }

        private static SKColor ToColor(float3 color)
        {
            return new SKColor(
                (byte)System.Math.Clamp((int)(color.x * 255f), 0, 255),
                (byte)System.Math.Clamp((int)(color.y * 255f), 0, 255),
                (byte)System.Math.Clamp((int)(color.z * 255f), 0, 255));
        }

        #endregion

        #region probes

        /// <summary>
        /// Reads the terrain metadata struct on the GPU and writes its fields back,
        /// so the C# and HLSL struct layouts can be compared field by field.
        /// </summary>
        [Shader]
        public struct TerrainMetadataProbe
        {
            [ShaderResource] public StructuredBuffer<TerrainDTO> terrainMetadata;
            [ShaderResource] public RWBuffer<float4> output;

            [Kernel(1, 1, 1)]
            public void Execute(uint3 id)
            {
                TerrainDTO meta = terrainMetadata[0];
                output[0] = new float4((float)meta.resolution.x, (float)meta.chunkStride, (float)meta.levels, (float)meta.worldSpace);
                output[1] = new float4((float)meta.bufferOffset, (float)meta.chunkInfoOffset, meta.size, meta.heightScale);
                output[2] = new float4(meta.position.x, meta.position.y, meta.position.z, (float)meta.gridSize);
                output[3] = new float4(meta.chunkSize, meta.lodBaseRadius, (float)meta.interpolation, (float)meta.hasErosion);
                output[4] = new float4(meta.lodCenter.x, meta.lodCenter.y, meta.frequency, meta.heightAmp);
            }
        }

        private static void ProbeTerrainMetadata(TerrainDTO meta)
        {
            try
            {
                using DivisionComputeContext context = new();
                using StructuredBuffer<TerrainDTO> metaBuffer = context.CreateStructuredBuffer([meta]);
                using RWBuffer<float4> output = context.CreateRWBuffer<float4>(5);
                using DivisionShader shader = context.CompileShader(typeof(TerrainMetadataProbe));
                shader.SetStructuredBuffer("terrainMetadata", metaBuffer);
                shader.SetRWBuffer("output", output);
                shader.Dispatch2D(shader.FindKernel("Execute"), 1, 1);
                float4[] gpu = output.GetData(context.Context);

                Console.WriteLine($"  gpu meta: res={gpu[0].x} stride={gpu[0].y} levels={gpu[0].z} worldSpace={gpu[0].w}");
                Console.WriteLine($"  gpu meta: bufferOffset={gpu[1].x} infoOffset={gpu[1].y} size={gpu[1].z} heightScale={gpu[1].w}");
                Console.WriteLine($"  gpu meta: pos=({gpu[2].x},{gpu[2].y},{gpu[2].z}) gridSize={gpu[2].w}");
                Console.WriteLine($"  gpu meta: chunkSize={gpu[3].x} baseRadius={gpu[3].y} interpolation={gpu[3].z} erosion={gpu[3].w}");
                Console.WriteLine($"  gpu meta: lodCenter=({gpu[4].x},{gpu[4].y}) frequency={gpu[4].z} heightAmp={gpu[4].w}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  gpu meta probe failed: {ex.Message}");
            }
        }

        #endregion
    }
}
