//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.Graphics;
using DivisionEngine.MathLib;
using DivisionEngine.Rendering;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DivisionEngine.Rendering.VoxelSDF
{
    /// <summary>
    /// World-stable SDF clipmap. The world is represented by four nested toroidal
    /// grids with power-of-two cell sizes. Every level stores cell values by
    /// absolute world-cell index modulo the grid resolution, so shifting the active
    /// window never moves the voxels: only the newly exposed shell is baked.
    /// Updates are queued and processed under a per-frame cell budget.
    /// </summary>
    public sealed class VoxelClipmapRenderer : IDisposable
    {
        public const int LevelCount = 4;
        public const int ReferenceResolution = 96;
        public const float ReferenceCellSize = 0.15f;
        public const int DefaultCellBudget = 2_000_000;

        private struct BakeJob
        {
            public int Level;
            public int3 MinCell;
            public int3 Count;
        }

        private readonly VoxelClipmapLevel[] levels = new VoxelClipmapLevel[LevelCount];
        private readonly Queue<BakeJob> pendingJobs = new();
        private DivisionShader? bakeShader;
        private RWTexture3D<float>? distanceAtlas;
        private RWTexture3D<uint>? objectAtlas;
        private StructuredBuffer<VoxelLevelDTO>? levelBuffer;
        private readonly VoxelLevelDTO[] levelData = new VoxelLevelDTO[LevelCount];
        private SDFObjectDTO[] previousObjects = [];
        private int previousTerrainVersion = -1;
        private int resolution = ReferenceResolution;
        private bool texturesReady;
        private bool initialized;
        private bool loggedLayout;

        public VoxelClipmapRenderer()
        {
            for (int i = 0; i < LevelCount; i++)
                levels[i] = new VoxelClipmapLevel(i, resolution);
            UpdateLevelScales();
        }

        /// <summary>Current number of grid points along each axis of every level.</summary>
        public int Resolution => resolution;

        /// <summary>Current level list, finest to coarsest.</summary>
        public IReadOnlyList<VoxelClipmapLevel> Levels => levels;

        /// <summary>Number of completed bake dispatches.</summary>
        public int BakeCount { get; private set; }

        /// <summary>Cells baked during the most recent update.</summary>
        public int LastBakedCells { get; private set; }

        /// <summary>Jobs dispatched during the most recent update.</summary>
        public int LastJobCount { get; private set; }

        /// <summary>Number of queued jobs that have not been dispatched yet.</summary>
        public int PendingJobCount => pendingJobs.Count;

        /// <summary>CPU time spent in the most recent update.</summary>
        public double LastUpdateMilliseconds { get; private set; }

        /// <summary>Number of levels whose ready region is non-empty.</summary>
        public int ReadyLevelCount
        {
            get
            {
                int count = 0;
                foreach (VoxelClipmapLevel level in levels)
                    if (level.HasReady) count++;
                return count;
            }
        }

        /// <summary>Total number of cells stored by all levels.</summary>
        public int TotalCells => LevelCount * resolution * resolution * resolution;

        public bool HasAnyReady => ReadyLevelCount > 0;

        /// <summary>
        /// Updates the clipmap and dispatches at most <paramref name="cellBudget"/>
        /// newly baked cells, except for the initial prime which is completed in one
        /// update so the first voxel frame is already fully populated.
        /// </summary>
        public bool Update(
            DivisionComputeContext context,
            SDFWorldDTO world,
            SDFObjectDTO[] objects,
            StructuredBuffer<SDFObjectDTO> objectBuffer,
            Buffer<float> terrainHeights,
            StructuredBuffer<TerrainDTO> terrainMetadata,
            int terrainVersion,
            int requestedResolution,
            int cellBudget)
        {
            if (objects.Length < 1) return false;

            try
            {
                return UpdateCore(context, world, objects, objectBuffer, terrainHeights,
                    terrainMetadata, terrainVersion, requestedResolution, cellBudget);
            }
            catch (Exception ex)
            {
                Debug.Error("Voxel clipmap: update failed; falling back to the exact SDF path", ex);
                distanceAtlas?.Dispose();
                objectAtlas?.Dispose();
                levelBuffer?.Dispose();
                distanceAtlas = null;
                objectAtlas = null;
                levelBuffer = null;
                texturesReady = false;
                initialized = false;
                return false;
            }
        }

        private bool UpdateCore(
            DivisionComputeContext context,
            SDFWorldDTO world,
            SDFObjectDTO[] objects,
            StructuredBuffer<SDFObjectDTO> objectBuffer,
            Buffer<float> terrainHeights,
            StructuredBuffer<TerrainDTO> terrainMetadata,
            int terrainVersion,
            int requestedResolution,
            int cellBudget)
        {
            if (objects.Length < 1) return false;

            Stopwatch updateTimer = Stopwatch.StartNew();
            requestedResolution = System.Math.Clamp(requestedResolution, 32, 128);
            bool resolutionChanged = requestedResolution != resolution;
            if (resolutionChanged || !texturesReady)
            {
                resolution = requestedResolution;
                RecreateLevels(context);
            }

            bakeShader ??= context.CompileShader(typeof(VoxelBakeShader));

            bool terrainChanged = terrainVersion != previousTerrainVersion;
            bool firstUpdate = !initialized;
            bool fullRebake = firstUpdate || terrainChanged;

            // Desired active windows are world-aligned: they only change when the
            // camera crosses a cell boundary, never while moving inside a cell.
            for (int i = 0; i < LevelCount; i++)
            {
                VoxelClipmapLevel level = levels[i];
                level.CellSize = CellSizeFor(i);
                level.MaxDistance = math.max(level.CellSize * 6f, 0.5f);
                ComputeActiveWindow(world.cameraOrigin, level, out int3 targetMin, out int3 targetMax);

                if (fullRebake)
                {
                    level.ActiveMinCell = targetMin;
                    level.ActiveMaxCell = targetMax;
                }
                else if (!SameCell(targetMin, targetMax, level.ActiveMinCell, level.ActiveMaxCell))
                {
                    int3 newReadyMin, newReadyMax;
                    if (level.HasReady)
                        Intersect(level.ReadyMinCell, level.ReadyMaxCell, targetMin, targetMax,
                            out newReadyMin, out newReadyMax);
                    else
                        Clear(new int3(1, 0, 0), out newReadyMin, out newReadyMax);

                    level.ActiveMinCell = targetMin;
                    level.ActiveMaxCell = targetMax;
                    level.ReadyMinCell = newReadyMin;
                    level.ReadyMaxCell = newReadyMax;

                    EnqueueDifference(level, targetMin, targetMax, newReadyMin, newReadyMax);
                }
            }

            if (fullRebake)
            {
                QueueFullActive(clearReady: true);
            }
            else
            {
                DetectObjectChanges(objects);
            }

            int budget = firstUpdate ? int.MaxValue : System.Math.Max(1, cellBudget);
            ProcessJobs(context, objectBuffer, terrainHeights, terrainMetadata, budget);

            previousObjects = (SDFObjectDTO[])objects.Clone();
            previousTerrainVersion = terrainVersion;
            if (firstUpdate) initialized = true;

            UpdateLevelBuffer(context);
            updateTimer.Stop();
            LastUpdateMilliseconds = updateTimer.Elapsed.TotalMilliseconds;
            return HasAnyReady;
        }

        /// <summary>Binds all level textures and ready bounds to the render shader.</summary>
        public void Bind(DivisionShader shader)
        {
            shader.SetInt("levelCount", LevelCount);
            shader.SetInt("voxelResolution", resolution);
            float level0Radius = levels[0].CellSize * resolution * 0.5f;
            shader.SetFloat("level0Radius", level0Radius);
            if (!loggedLayout)
            {
                loggedLayout = true;
                Debug.Info($"Voxel clipmap: resolution={resolution}, cell0={levels[0].CellSize:F4}, radius0={level0Radius:F2}, cells={TotalCells}");
            }

            shader.SetStructuredBuffer("voxelLevels", levelBuffer!);
            shader.SetTexture3D("voxelDistance", distanceAtlas!);
            shader.SetTexture3D("voxelObjectId", objectAtlas!);
        }

        public void Dispose()
        {
            foreach (VoxelClipmapLevel level in levels)
                level.Dispose();
            distanceAtlas?.Dispose();
            objectAtlas?.Dispose();
            levelBuffer?.Dispose();
            distanceAtlas = null;
            objectAtlas = null;
            levelBuffer = null;
            bakeShader?.Dispose();
            bakeShader = null;
            pendingJobs.Clear();
            texturesReady = false;
            initialized = false;
            BakeCount = 0;
            LastBakedCells = 0;
            LastJobCount = 0;
            LastUpdateMilliseconds = 0;
        }

        private void UpdateLevelBuffer(DivisionComputeContext context)
        {
            for (int i = 0; i < LevelCount; i++)
            {
                VoxelClipmapLevel level = levels[i];
                levelData[i] = new VoxelLevelDTO
                {
                    cellSize = level.CellSize,
                    full = level.IsFullWindow ? 1 : 0,
                    minCell = level.ReadyMinCell,
                    maxCell = level.ReadyMaxCell,
                };
            }

            if (levelBuffer == null || levelBuffer.Length != LevelCount)
            {
                levelBuffer?.Dispose();
                levelBuffer = context.CreateStructuredBuffer(levelData);
            }
            else levelBuffer.SetData(context.Context, levelData);
        }

        private void RecreateLevels(DivisionComputeContext context)
        {
            foreach (VoxelClipmapLevel level in levels)
                level.Dispose();

            distanceAtlas?.Dispose();
            objectAtlas?.Dispose();
            levelBuffer?.Dispose();
            distanceAtlas = context.CreateRWTexture3D<float>(resolution, resolution, resolution * LevelCount);
            objectAtlas = context.CreateRWTexture3D<uint>(resolution, resolution, resolution * LevelCount);
            levelBuffer = null;

            pendingJobs.Clear();
            previousObjects = [];
            previousTerrainVersion = -1;
            initialized = false;

            for (int i = 0; i < LevelCount; i++)
            {
                VoxelClipmapLevel level = new(i, resolution);
                level.CellSize = CellSizeFor(i);
                level.MaxDistance = math.max(level.CellSize * 6f, 0.5f);
                level.ClearReady();
                levels[i] = level;
            }

            texturesReady = true;
        }

        private void UpdateLevelScales()
        {
            for (int i = 0; i < LevelCount; i++)
            {
                levels[i].CellSize = CellSizeFor(i);
                levels[i].MaxDistance = math.max(levels[i].CellSize * 6f, 0.5f);
            }
        }

        private float CellSizeFor(int level) =>
            ReferenceCellSize * ReferenceResolution / resolution * (1 << level);

        private void ComputeActiveWindow(float3 camera, VoxelClipmapLevel level,
            out int3 minCell, out int3 maxCell)
        {
            int half = resolution / 2;
            int cx = (int)math.floor(camera.x / level.CellSize);
            int cy = (int)math.floor(camera.y / level.CellSize);
            int cz = (int)math.floor(camera.z / level.CellSize);

            minCell = new int3(cx - half, cy - half, cz - half);
            maxCell = new int3(minCell.x + resolution - 1, minCell.y + resolution - 1, minCell.z + resolution - 1);
        }

        private void QueueFullActive(bool clearReady)
        {
            pendingJobs.Clear();
            for (int i = 0; i < LevelCount; i++)
            {
                VoxelClipmapLevel level = levels[i];
                level.PendingJobs = 0;
                if (clearReady) level.ClearReady();
                EnqueueRegion(level, level.ActiveMinCell, level.ActiveMaxCell);
            }
        }

        private void EnqueueDifference(VoxelClipmapLevel level,
            int3 targetMin, int3 targetMax, int3 readyMin, int3 readyMax)
        {
            bool hasReady = readyMin.x <= readyMax.x && readyMin.y <= readyMax.y && readyMin.z <= readyMax.z;
            if (!hasReady)
            {
                EnqueueRegion(level, targetMin, targetMax);
                return;
            }

            // Six shells around the already-baked box. Overlap at the corners is
            // harmless and keeps the job construction simple.
            if (readyMin.x > targetMin.x)
                EnqueueRegion(level,
                    new int3(targetMin.x, targetMin.y, targetMin.z),
                    new int3(readyMin.x - 1, targetMax.y, targetMax.z));
            if (readyMax.x < targetMax.x)
                EnqueueRegion(level,
                    new int3(readyMax.x + 1, targetMin.y, targetMin.z),
                    new int3(targetMax.x, targetMax.y, targetMax.z));

            if (readyMin.y > targetMin.y)
                EnqueueRegion(level,
                    new int3(targetMin.x, targetMin.y, targetMin.z),
                    new int3(targetMax.x, readyMin.y - 1, targetMax.z));
            if (readyMax.y < targetMax.y)
                EnqueueRegion(level,
                    new int3(targetMin.x, readyMax.y + 1, targetMin.z),
                    new int3(targetMax.x, targetMax.y, targetMax.z));

            if (readyMin.z > targetMin.z)
                EnqueueRegion(level,
                    new int3(targetMin.x, targetMin.y, targetMin.z),
                    new int3(targetMax.x, targetMax.y, readyMin.z - 1));
            if (readyMax.z < targetMax.z)
                EnqueueRegion(level,
                    new int3(targetMin.x, targetMin.y, readyMax.z + 1),
                    new int3(targetMax.x, targetMax.y, targetMax.z));
        }

        private void EnqueueRegion(VoxelClipmapLevel level, int3 minCell, int3 maxCell)
        {
            Clip(minCell, maxCell, level.ActiveMinCell, level.ActiveMaxCell, out int3 clippedMin, out int3 clippedMax);
            if (clippedMin.x > clippedMax.x || clippedMin.y > clippedMax.y || clippedMin.z > clippedMax.z)
                return;

            BakeJob job = new BakeJob
            {
                Level = level.Index,
                MinCell = clippedMin,
                Count = new int3(
                    clippedMax.x - clippedMin.x + 1,
                    clippedMax.y - clippedMin.y + 1,
                    clippedMax.z - clippedMin.z + 1),
            };
            level.PendingJobs++;
            pendingJobs.Enqueue(job);
        }

        private void ProcessJobs(DivisionComputeContext context,
            StructuredBuffer<SDFObjectDTO> objectBuffer,
            Buffer<float> terrainHeights,
            StructuredBuffer<TerrainDTO> terrainMetadata,
            int cellBudget)
        {
            int bakedCells = 0;
            int dispatched = 0;

            while (pendingJobs.Count > 0 && bakedCells < cellBudget)
            {
                BakeJob job = pendingJobs.Dequeue();
                VoxelClipmapLevel level = levels[job.Level];

                // The active window may have moved while this job was queued.
                int3 minCell = job.MinCell;
                int3 maxCell = new int3(
                    job.MinCell.x + job.Count.x - 1,
                    job.MinCell.y + job.Count.y - 1,
                    job.MinCell.z + job.Count.z - 1);
                Clip(minCell, maxCell, level.ActiveMinCell, level.ActiveMaxCell,
                    out int3 clippedMin, out int3 clippedMax);
                if (clippedMin.x > clippedMax.x || clippedMin.y > clippedMax.y || clippedMin.z > clippedMax.z)
                {
                    CompleteJob(level);
                    continue;
                }

                int3 count = new int3(
                    clippedMax.x - clippedMin.x + 1,
                    clippedMax.y - clippedMin.y + 1,
                    clippedMax.z - clippedMin.z + 1);

                DispatchBake(context, level, clippedMin, count, objectBuffer, terrainHeights, terrainMetadata);
                bakedCells += count.x * count.y * count.z;
                dispatched++;
                CompleteJob(level);
            }

            LastBakedCells = bakedCells;
            LastJobCount = dispatched;
        }

        private void CompleteJob(VoxelClipmapLevel level)
        {
            level.PendingJobs = System.Math.Max(0, level.PendingJobs - 1);
            if (level.PendingJobs == 0 && level.ActiveMinCell.x <= level.ActiveMaxCell.x)
            {
                // All shells queued for this active window are now written. GPU
                // dispatches are ordered on the context, so this window is ready
                // for the render dispatch that follows.
                level.ReadyMinCell = level.ActiveMinCell;
                level.ReadyMaxCell = level.ActiveMaxCell;
            }
        }

        private void DispatchBake(DivisionComputeContext context, VoxelClipmapLevel level,
            int3 minCell, int3 count,
            StructuredBuffer<SDFObjectDTO> objectBuffer,
            Buffer<float> terrainHeights,
            StructuredBuffer<TerrainDTO> terrainMetadata)
        {
            bakeShader!.SetVector("cellOrigin", minCell);
            bakeShader.SetVector("cellCount", count);
            bakeShader.SetInt("voxelResolution", resolution);
            bakeShader.SetFloat("cellSize", level.CellSize);
            bakeShader.SetFloat("maxDistance", level.MaxDistance);
            bakeShader.SetInt("atlasZOffset", level.Index * resolution);
            bakeShader.SetStructuredBuffer("sdfObjects", objectBuffer);
            bakeShader.SetBuffer("terrainHeightData", terrainHeights);
            bakeShader.SetStructuredBuffer("terrainMetadata", terrainMetadata);
            bakeShader.SetRWTexture3D("outputDistance", distanceAtlas!);
            bakeShader.SetRWTexture3D("outputObjectId", objectAtlas!);

            int kernel = bakeShader.FindKernel("Execute");
            bakeShader.Dispatch(
                kernel,
                (uint)((count.x + 3) / 4),
                (uint)((count.y + 3) / 4),
                (uint)((count.z + 3) / 4));
            BakeCount++;
        }

        private void DetectObjectChanges(SDFObjectDTO[] objects)
        {
            bool changed = false;
            bool infiniteChange = false;
            float3 min = new float3(float.PositiveInfinity);
            float3 max = new float3(float.NegativeInfinity);

            int count = System.Math.Max(objects.Length, previousObjects.Length);
            for (int i = 0; i < count; i++)
            {
                bool existsNow = i < objects.Length;
                bool existedBefore = i < previousObjects.Length;

                bool objectChanged = !existsNow || !existedBefore ||
                    !ObjectBytesEqual(objects, i, previousObjects, i);
                if (!objectChanged) continue;

                changed = true;
                if (existedBefore)
                    UnionBounds(previousObjects[i], ref min, ref max, ref infiniteChange);
                if (existsNow)
                    UnionBounds(objects[i], ref min, ref max, ref infiniteChange);
            }

            if (!changed) return;

            if (infiniteChange)
            {
                QueueFullActive(clearReady: true);
                return;
            }

            for (int i = 0; i < LevelCount; i++)
            {
                VoxelClipmapLevel level = levels[i];
                float pad = level.MaxDistance + level.CellSize * 2f;
                int3 cellMin = FloorToCell(min - new float3(pad), level.CellSize);
                int3 cellMax = FloorToCell(max + new float3(pad), level.CellSize);
                EnqueueRegion(level, cellMin, cellMax);
            }
        }

        private static void UnionBounds(SDFObjectDTO obj, ref float3 min, ref float3 max, ref bool infinite)
        {
            float r = obj.bounds.w;
            if (r >= 1e6f)
            {
                infinite = true;
                return;
            }

            float3 lo = new float3(obj.bounds.x - r, obj.bounds.y - r, obj.bounds.z - r);
            float3 hi = new float3(obj.bounds.x + r, obj.bounds.y + r, obj.bounds.z + r);
            min.x = math.min(min.x, lo.x);
            min.y = math.min(min.y, lo.y);
            min.z = math.min(min.z, lo.z);
            max.x = math.max(max.x, hi.x);
            max.y = math.max(max.y, hi.y);
            max.z = math.max(max.z, hi.z);
        }

        private static bool ObjectBytesEqual(SDFObjectDTO[] a, int ai, SDFObjectDTO[] b, int bi) =>
            MemoryMarshal.AsBytes(new ReadOnlySpan<SDFObjectDTO>(a, ai, 1))
                .SequenceEqual(MemoryMarshal.AsBytes(new ReadOnlySpan<SDFObjectDTO>(b, bi, 1)));

        private static int3 FloorToCell(float3 world, float cellSize) => new int3(
            (int)math.floor(world.x / cellSize),
            (int)math.floor(world.y / cellSize),
            (int)math.floor(world.z / cellSize));

        private static bool SameCell(int3 minA, int3 maxA, int3 minB, int3 maxB) =>
            minA.x == minB.x && minA.y == minB.y && minA.z == minB.z &&
            maxA.x == maxB.x && maxA.y == maxB.y && maxA.z == maxB.z;

        private static void Intersect(int3 minA, int3 maxA, int3 minB, int3 maxB,
            out int3 min, out int3 max)
        {
            min = new int3(
                System.Math.Max(minA.x, minB.x),
                System.Math.Max(minA.y, minB.y),
                System.Math.Max(minA.z, minB.z));
            max = new int3(
                System.Math.Min(maxA.x, maxB.x),
                System.Math.Min(maxA.y, maxB.y),
                System.Math.Min(maxA.z, maxB.z));
        }

        private static void Clip(int3 min, int3 max, int3 clipMin, int3 clipMax,
            out int3 clippedMin, out int3 clippedMax)
        {
            clippedMin = new int3(
                System.Math.Max(min.x, clipMin.x),
                System.Math.Max(min.y, clipMin.y),
                System.Math.Max(min.z, clipMin.z));
            clippedMax = new int3(
                System.Math.Min(max.x, clipMax.x),
                System.Math.Min(max.y, clipMax.y),
                System.Math.Min(max.z, clipMax.z));
        }

        private static void Clear(int3 emptyMin, out int3 min, out int3 max)
        {
            min = emptyMin;
            max = new int3(emptyMin.x - 1, emptyMin.y - 1, emptyMin.z - 1);
        }
    }
}
