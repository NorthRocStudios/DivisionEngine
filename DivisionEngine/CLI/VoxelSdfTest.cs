//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.Components;
using DivisionEngine.Components.SDFs.Primitives;
using DivisionEngine.MathLib;
using DivisionEngine.Rendering;
using DivisionEngine.Rendering.VoxelSDF;
using System;
using System.Globalization;

namespace DivisionEngine.Editor
{
    /// <summary>
    /// Headless regression test for the world-stable SDF clipmap. Verifies that the
    /// initial four levels prime, a static scene does not rebake, sub-cell camera
    /// motion does not shift a level, crossing a cell boundary does, and an object
    /// edit incrementally refreshes only the affected level regions.
    /// </summary>
    internal static class VoxelSdfTest
    {
        public static bool Run(string[] args)
        {
            int width = GetInt(args, "--width", 640);
            int height = GetInt(args, "--height", 360);
            int resolution = GetInt(args, "--resolution", 96);

            if (width < 64 || height < 64)
                throw new ArgumentException("--width and --height must be at least 64.");
            if (resolution < 32 || resolution > 128)
                throw new ArgumentException("--resolution must be between 32 and 128.");

            Console.WriteLine($"divisioncli voxeltest: {width}x{height}, clipmap resolution {resolution}");

            RenderPipeline.DeterministicCapture = true;
            RenderPipeline.VoxelResolutionOverride = resolution;
            RenderPipeline.VoxelModeOverride = false;

            try
            {
                WorldManager.CreateDefaultWorld(makeCurrent: true);
                World world = WorldManager.CurrentWorld!;
                RenderPipeline pipeline = new();
                if (!pipeline.BindCurrentWorld())
                    throw new InvalidOperationException("Failed to bind the default world.");

                float4[] exactA = Render(pipeline, width, height);
                RenderPipeline.VoxelModeOverride = true;
                float4[] voxelA = Render(pipeline, width, height);

                VoxelClipmapRenderer? clipmap = pipeline.VoxelClipmap;
                if (clipmap is null || !clipmap.HasAnyReady)
                    throw new InvalidOperationException("The voxel clipmap was not baked.");

                int bakeAfterPrime = clipmap.BakeCount;
                int3 activeAfterPrime = clipmap.Levels[0].ActiveMinCell;
                bool clipmapValid = clipmap.ReadyLevelCount == VoxelClipmapRenderer.LevelCount &&
                                    clipmap.TotalCells > 100_000 &&
                                    clipmap.Levels[0].CellSize > 0f;

                float4[] voxelStatic = Render(pipeline, width, height);
                int staticBakeDelta = clipmap.BakeCount - bakeAfterPrime;
                bool staticStable = staticBakeDelta == 0 &&
                                    clipmap.LastBakedCells == 0 &&
                                    Compare(voxelA, voxelStatic).MaxChannelDiff <= 2;

                // A sub-cell camera move must not shift any active window.
                Transform? cameraTransform = null;
                foreach ((uint id, Transform transform) in world.QueryData<Transform>())
                {
                    if (!world.HasComponent<Camera>(id)) continue;
                    cameraTransform = transform;
                    break;
                }

                float3 originalCameraPosition = cameraTransform?.position ?? new float3(0f);
                float cell0 = clipmap.Levels[0].CellSize;
                if (cameraTransform is not null)
                    cameraTransform.position += new float3(cell0 * 0.1f, 0f, 0f);

                Render(pipeline, width, height);
                int subCellBakeDelta = clipmap.BakeCount - bakeAfterPrime;
                bool subCellStable = subCellBakeDelta == 0 &&
                                     SameCell(activeAfterPrime, clipmap.Levels[0].ActiveMinCell);

                // Crossing several cells must shift at least the finest level.
                if (cameraTransform is not null)
                    cameraTransform.position = originalCameraPosition + new float3(cell0 * 3f, 0f, 0f);

                float4[] voxelCellShift = Render(pipeline, width, height);
                int cellBakeDelta = clipmap.BakeCount - bakeAfterPrime;
                bool cellShiftRebaked = cellBakeDelta > 0 &&
                                        !SameCell(activeAfterPrime, clipmap.Levels[0].ActiveMinCell);

                // Move a finite object and prove only its affected regions refresh.
                int bakeBeforeObjectMove = clipmap.BakeCount;
                bool moved = false;
                float3 originalObjectPosition = new float3(0f);
                foreach ((uint id, Transform transform) in world.QueryData<Transform>())
                {
                    if (!world.HasComponent<SDFSphere>(id)) continue;
                    originalObjectPosition = transform.position;
                    transform.position += new float3(1.5f, 0f, 0f);
                    moved = true;
                    break;
                }

                float4[] voxelObjectMove = moved ? Render(pipeline, width, height) : voxelCellShift;
                int objectBakeDelta = clipmap.BakeCount - bakeBeforeObjectMove;
                int objectChangedPixels = moved ? Compare(voxelCellShift, voxelObjectMove).ChangedPixels : 0;
                bool objectRebaked = moved && objectBakeDelta > 0 && objectChangedPixels > 100;

                if (cameraTransform is not null) cameraTransform.position = originalCameraPosition;
                foreach ((uint id, Transform transform) in world.QueryData<Transform>())
                {
                    if (!world.HasComponent<SDFSphere>(id)) continue;
                    transform.position = originalObjectPosition;
                    break;
                }

                // Teleport far enough to invalidate every level, then give the
                // asynchronous queue a few frames to stream the new windows in.
                int bakeBeforeTeleport = clipmap.BakeCount;
                if (cameraTransform is not null)
                    cameraTransform.position = originalCameraPosition + new float3(cell0 * 100f, 0f, 0f);
                float4[]? teleportPixels = pipeline.RenderFixedFrames(width, height, 6, 1.0 / 60.0);
                int teleportBakeDelta = clipmap.BakeCount - bakeBeforeTeleport;
                Stats teleportStats = Analyze(teleportPixels ?? []);
                bool teleportValid = teleportBakeDelta > 0 &&
                                     clipmap.ReadyLevelCount == VoxelClipmapRenderer.LevelCount &&
                                     teleportStats.NonBlack > width * height * 0.5f &&
                                     teleportStats.NaN == 0;
                if (cameraTransform is not null) cameraTransform.position = originalCameraPosition;

                RenderPipeline.VoxelModeOverride = false;
                float4[] exactB = Render(pipeline, width, height);

                Stats exactStats = Analyze(exactA);
                Stats voxelStats = Analyze(voxelA);
                Stats exactBStats = Analyze(exactB);
                Diff directDiff = Compare(exactA, voxelA);

                bool exactValid = exactStats.NonBlack > exactA.Length * 0.5f && exactStats.NaN == 0;
                bool voxelValid = voxelStats.NonBlack > voxelA.Length * 0.5f && voxelStats.NaN == 0;
                bool exactBValid = exactBStats.NonBlack > exactB.Length * 0.5f && exactBStats.NaN == 0;
                bool modesDiffer = directDiff.ChangedPixels > exactA.Length * 0.01f;
                bool pass = exactValid && voxelValid && exactBValid && modesDiffer &&
                            clipmapValid && staticStable && subCellStable &&
                            cellShiftRebaked && objectRebaked && teleportValid;

                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "{{\"pass\":{0},\"width\":{1},\"height\":{2},\"resolution\":{3}," +
                    "\"exactNonBlack\":{4},\"voxelNonBlack\":{5},\"exactMean\":{6:F4},\"voxelMean\":{7:F4}," +
                    "\"exactNaNs\":{8},\"voxelNaNs\":{9},\"changedPixels\":{10},\"changedPercent\":{11:F2}," +
                    "\"maxDiff\":{12},\"voxelRepeatMaxDiff\":{13}," +
                    "\"clipmapCells\":{14},\"levelsReady\":{15},\"bakeCount\":{16}," +
                    "\"staticBakeDelta\":{17},\"subCellBakeDelta\":{18},\"cellBakeDelta\":{19}," +
                    "\"objectBakeDelta\":{20},\"objectChangedPixels\":{21},\"lastBakedCells\":{22}," +
                    "\"teleportBakeDelta\":{23},\"teleportReadyLevels\":{24},\"teleportValid\":{25}}}",
                    pass ? "true" : "false", width, height, resolution,
                    exactStats.NonBlack, voxelStats.NonBlack, exactStats.Mean, voxelStats.Mean,
                    exactStats.NaN, voxelStats.NaN, directDiff.ChangedPixels,
                    directDiff.ChangedPixels * 100.0 / exactA.Length, directDiff.MaxChannelDiff,
                    Compare(voxelA, voxelStatic).MaxChannelDiff,
                    clipmap.TotalCells, clipmap.ReadyLevelCount, clipmap.BakeCount,
                    staticBakeDelta, subCellBakeDelta, cellBakeDelta,
                    objectBakeDelta, objectChangedPixels, clipmap.LastBakedCells,
                    teleportBakeDelta, clipmap.ReadyLevelCount, teleportValid ? "true" : "false"));

                if (!exactValid) Console.Error.WriteLine("voxeltest: exact render is black or contains NaNs");
                if (!voxelValid) Console.Error.WriteLine("voxeltest: voxel render is black or contains NaNs");
                if (!exactBValid) Console.Error.WriteLine("voxeltest: exact render after toggling back is black or contains NaNs");
                if (!modesDiffer) Console.Error.WriteLine("voxeltest: voxel and exact renders are effectively identical");
                if (!clipmapValid) Console.Error.WriteLine("voxeltest: clipmap did not prime all four levels");
                if (!staticStable) Console.Error.WriteLine("voxeltest: a static scene rebaked the clipmap");
                if (!subCellStable) Console.Error.WriteLine("voxeltest: sub-cell camera motion rebaked the clipmap");
                if (!cellShiftRebaked) Console.Error.WriteLine("voxeltest: crossing a cell boundary did not rebake");
                if (!objectRebaked) Console.Error.WriteLine("voxeltest: moving an SDF object did not incrementally rebake");
                if (!teleportValid) Console.Error.WriteLine("voxeltest: teleport streaming did not recover all levels");

                DivisionCLI.SetExitCode(pass ? 0 : 1);
                return true;
            }
            finally
            {
                RenderPipeline.VoxelModeOverride = null;
                RenderPipeline.VoxelResolutionOverride = null;
                RenderPipeline.DeterministicCapture = false;
            }
        }

        private static float4[] Render(RenderPipeline pipeline, int width, int height)
        {
            float4[]? pixels = pipeline.RenderFixedFrames(width, height, 2, 1.0 / 60.0);
            if (pixels is null || pixels.Length != width * height)
                throw new InvalidOperationException("Render produced no pixels.");
            return (float4[])pixels.Clone();
        }

        private readonly record struct Stats(int NonBlack, int NaN, float Mean);

        private static Stats Analyze(float4[] pixels)
        {
            int nonBlack = 0, nan = 0;
            double sum = 0;
            foreach (float4 p in pixels)
            {
                float lum = (p.x + p.y + p.z) / 3f;
                if (float.IsNaN(lum) || float.IsInfinity(lum)) { nan++; continue; }
                if (lum > 0.001f) nonBlack++;
                sum += lum;
            }
            return new Stats(nonBlack, nan, (float)(sum / pixels.Length));
        }

        private readonly record struct Diff(int ChangedPixels, int MaxChannelDiff);

        private static Diff Compare(float4[] a, float4[] b)
        {
            if (a.Length != b.Length) throw new ArgumentException("Image sizes differ.");
            int changed = 0, max = 0;
            for (int i = 0; i < a.Length; i++)
            {
                int pixelDiff = 0;
                pixelDiff = Math.Max(pixelDiff, ChannelDiff(a[i].x, b[i].x));
                pixelDiff = Math.Max(pixelDiff, ChannelDiff(a[i].y, b[i].y));
                pixelDiff = Math.Max(pixelDiff, ChannelDiff(a[i].z, b[i].z));
                if (pixelDiff > 2) changed++;
                max = Math.Max(max, pixelDiff);
            }
            return new Diff(changed, max);
        }

        private static bool SameCell(int3 a, int3 b) =>
            a.x == b.x && a.y == b.y && a.z == b.z;

        private static int ChannelDiff(float a, float b) =>
            Math.Min(255, (int)(Math.Abs(a - b) * 255f + 0.5f));

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
