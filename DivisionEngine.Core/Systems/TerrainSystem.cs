//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using System.Collections.Concurrent;
using DivisionEngine.Graphics;
using DivisionEngine.Components;
using DivisionEngine.Components.SDFs;
using DivisionEngine.MathLib;
using DivisionEngine.Rendering;
using DivisionEngine.Rendering.Terrain;

namespace DivisionEngine.Systems
{
    /// <summary>
    /// Manages terrain heightmaps: fixed heightmaps are baked on demand, while
    /// infinite terrains stream a quadtree of heightmap chunks in the background.
    /// </summary>
    public class TerrainSystem : SystemBase
    {
        /// <summary>
        /// Terrain buffers must be ready before the SDF object list is prepared, so
        /// the render systems see this frame's metadata index.
        /// </summary>
        public override int Priority => -100;

        private static float4[]? _allTerrainData = [];
        private static TerrainDTO[]? _allTerrainMetadata = [];
        private static int4[]? _allTerrainChunkInfo = [];

        /// <summary>
        /// Packed heightmap data for every terrain, concatenated. Each texel is
        /// (height, dHdX, dHdZ, ridge01) in world units relative to the terrain.
        /// </summary>
        public static float4[]? AllTerrainData { get => _allTerrainData; private set => _allTerrainData = value; }

        /// <summary>
        /// Metadata for every terrain.
        /// </summary>
        public static TerrainDTO[]? AllTerrainMetadata { get => _allTerrainMetadata; private set => _allTerrainMetadata = value; }

        /// <summary>
        /// Toroidal chunk identity records: (chunkX, chunkZ, ready, generation).
        /// </summary>
        public static int4[]? AllTerrainChunkInfo { get => _allTerrainChunkInfo; private set => _allTerrainChunkInfo = value; }

        /// <summary>
        /// Raised when the terrain buffer layout changes and the whole GPU copy
        /// must be rebuilt.
        /// </summary>
        public static event Action? UpdatedTerrainData;

        private static readonly List<(int Offset, int Count)> pendingUploads = [];

        /// <summary>
        /// Moves the texel ranges that changed since the last call into
        /// <paramref name="destination"/>. The render pipeline uses this to upload
        /// streamed chunks without rebuilding the whole heightmap buffer.
        /// </summary>
        public static void ConsumeTerrainUploads(List<(int Offset, int Count)> destination)
        {
            destination.AddRange(pendingUploads);
            pendingUploads.Clear();
        }

        /// <summary>
        /// Incremented whenever <see cref="AllTerrainData"/> contents change. The voxel
        /// grid uses it as part of its scene hash so a terrain rebuild invalidates
        /// the baked grid.
        /// </summary>
        public static int DataVersion { get; private set; }

        /// <summary>Incremented whenever <see cref="AllTerrainChunkInfo"/> changes.</summary>
        public static int ChunkInfoVersion { get; private set; }

        /// <summary>Total texels currently allocated across all terrains.</summary>
        public static int TexelCount => _allTerrainData?.Length ?? 0;

        private sealed class TerrainState
        {
            public uint EntityId;
            public SDFTerrain Settings = null!;
            public TerrainDTO Meta;
            public int ParamHash = -1;
            public float4[] Data = [];
            public int4[] ChunkInfo = [];
            public int SlotCount;
            public int Levels = 1;
            public int GridSize = 1;
            public int Resolution;
            public int Stride;
            public float ChunkSize;
            public float2 Anchor;
            public int Epoch;
            public int InFlight;
            public bool FixedBaked;
            public bool Rebuild;
            public readonly HashSet<TerrainChunkKey> Pending = [];
            public readonly ConcurrentQueue<ChunkResult> Completed = new();
            public readonly List<TerrainChunkKey> Desired = [];
        }

        private sealed class ChunkResult
        {
            public uint EntityId;
            public int Epoch;
            public TerrainChunkKey Key;
            public int Slot;
            public float4[] Data = [];
        }

        private static readonly Dictionary<uint, TerrainState> terrainStates = [];
        private static readonly List<TerrainState> stateOrder = [];
        private static DivisionShader? bakeShader;

        /// <summary>Debug counters for tools and tests.</summary>
        public static int LoadedChunkCount { get; private set; }
        /// <summary>Number of chunk bake jobs currently in flight.</summary>
        public static int PendingChunkCount { get; private set; }

        /// <summary>
        /// Per-frame terrain update: rebuild changed heightmaps, drive the
        /// asynchronous quadtree streamer and publish GPU-ready buffers.
        /// </summary>
        public override void Render()
        {
            List<(uint Id, SDFTerrain Terrain, Transform? Transform)> terrains = [];
            foreach ((uint id, SDFTerrain terrain) in W.QueryData<SDFTerrain>())
                terrains.Add((id, terrain, W.GetComponent<Transform>(id)));

            float3 cameraPosition = GetCameraPosition();
            float2 cameraXZ = new float2(cameraPosition.x, cameraPosition.z);

            bool structureChanged = false;
            List<(int Offset, int Count)> dirtyRanges = [];

            // Remove states for destroyed terrains.
            for (int i = stateOrder.Count - 1; i >= 0; i--)
            {
                TerrainState state = stateOrder[i];
                bool present = false;
                for (int t = 0; t < terrains.Count; t++)
                {
                    if (terrains[t].Id == state.EntityId) { present = true; break; }
                }
                if (!present)
                {
                    stateOrder.RemoveAt(i);
                    terrainStates.Remove(state.EntityId);
                    structureChanged = true;
                }
            }

            // Create or refresh a state per terrain.
            foreach ((uint id, SDFTerrain terrain, Transform? transform) in terrains)
            {
                float3 position = transform?.position ?? new float3(0f, 0f, 0f);
                bool added = false;
                if (!terrainStates.TryGetValue(id, out TerrainState? state))
                {
                    state = new TerrainState { EntityId = id };
                    terrainStates[id] = state;
                    stateOrder.Add(state);
                    added = true;
                }

                state.Settings = terrain;

                int paramHash = ComputeParamHash(terrain);
                bool moved = state.Meta.position.x != position.x
                    || state.Meta.position.y != position.y
                    || state.Meta.position.z != position.z;

                if (added || state.Rebuild || state.ParamHash != paramHash || state.Data.Length == 0
                    || moved)
                {
                    InitializeTerrain(state, terrain, position);
                    structureChanged = true;
                }
            }

            // Stable buffer offsets before anything bakes into them.
            AssignBufferOffsets();

            foreach (TerrainState state in stateOrder)
            {
                if (state.Settings.infinite)
                {
                    float2 localCamera = new float2(
                        cameraXZ.x - state.Meta.position.x,
                        cameraXZ.y - state.Meta.position.z);
                    if (state.Meta.lodCenter.x != localCamera.x || state.Meta.lodCenter.y != localCamera.y)
                        state.Meta.lodCenter = localCamera;

                    // Legacy field reused for the camera height above the terrain.
                    // An aerial camera needs a coarser LOD ring than its XZ distance
                    // suggests, or grazing-angle detail aliases into moire bands.
                    state.Meta.worldMin = new float3(
                        math.max(cameraPosition.y - state.Meta.position.y, 0f), 0f, 0f);

                    UpdateInfiniteTerrain(state, localCamera, dirtyRanges);
                }
                else if (!state.FixedBaked)
                {
                    TryBakeFixedTerrain(state, dirtyRanges);
                }

                state.Rebuild = false;
            }

            // The GPU reads AllTerrainMetadata every frame for the LOD anchor and the
            // outer-ring fade. Copy the refreshed per-state structs into it even when no
            // chunk was added: otherwise moving the camera leaves the GPU LOD/fade circle
            // at its old position until a structural change happens to rebuild the array.
            SyncGlobalMetadata();

            LoadedChunkCount = 0;
            PendingChunkCount = 0;
            foreach (TerrainState state in stateOrder)
            {
                PendingChunkCount += state.InFlight;
                for (int i = 0; i < state.ChunkInfo.Length; i++)
                    if (state.ChunkInfo[i].z != 0) LoadedChunkCount++;
            }

            if (structureChanged || AllTerrainData == null || AllTerrainData.Length == 0)
            {
                RebuildGlobalBuffers();
                pendingUploads.Clear();
                DataVersion++;
                ChunkInfoVersion++;
                UpdatedTerrainData?.Invoke();
            }
            else
            {
                if (dirtyRanges.Count > 0)
                {
                    pendingUploads.AddRange(dirtyRanges);
                    DataVersion++;
                }
                ChunkInfoVersion++;
            }
        }

        private static int ComputeParamHash(SDFTerrain t)
        {
            var hash = new HashCode();
            hash.Add(t.infinite);
            hash.Add(t.size);
            hash.Add(t.height);
            hash.Add(t.frequency);
            hash.Add(t.interpolation);
            hash.Add(t.heightmapSize);
            hash.Add(t.levels);
            hash.Add(t.chunksPerLevel);
            hash.Add(t.chunkResolution);
            hash.Add(t.chunkSize);
            hash.Add(t.erosion);
            hash.Add(t.erosionStrength);
            hash.Add(t.erosionGullyWeight);
            hash.Add(t.erosionDetail);
            hash.Add(t.erosionScale);
            hash.Add(t.erosionOctaves);
            hash.Add(t.erosionLacunarity);
            hash.Add(t.erosionGain);
            hash.Add(t.erosionCellScale);
            hash.Add(t.erosionNormalization);
            hash.Add(t.ridgeBlend);
            hash.Add(t.ridgeWeight);
            hash.Add(t.edgeFalloff);
            return hash.ToHashCode();
        }

        private static void InitializeTerrain(TerrainState state, SDFTerrain terrain, float3 position)
        {
            state.Epoch++;
            state.Pending.Clear();
            state.InFlight = 0;
            while (state.Completed.TryDequeue(out _)) { }
            state.Desired.Clear();
            state.FixedBaked = false;

            TerrainDTO meta = CreateMetadata(terrain, position);

            if (terrain.infinite)
            {
                state.Levels = meta.levels;
                state.GridSize = meta.gridSize;
                state.Resolution = meta.resolution.x;
                state.ChunkSize = meta.chunkSize;
                state.Stride = meta.chunkStride;
                state.SlotCount = state.Levels * state.GridSize * state.GridSize;
                state.Anchor = new float2(0f, 0f);
            }
            else
            {
                state.Levels = 1;
                state.GridSize = 1;
                state.Resolution = meta.resolution.x;
                state.ChunkSize = meta.chunkSize;
                state.Stride = meta.chunkStride;
                state.SlotCount = 1;
                state.Anchor = new float2(-math.max(terrain.size, 1f), -math.max(terrain.size, 1f));
            }

            state.Meta = meta;
            state.ParamHash = ComputeParamHash(terrain);

            int texelCount = state.SlotCount * state.Stride * state.Stride;
            if (state.Data.Length != texelCount) state.Data = new float4[texelCount];
            else Array.Clear(state.Data);
            if (state.ChunkInfo.Length != state.SlotCount) state.ChunkInfo = new int4[state.SlotCount];
            else Array.Clear(state.ChunkInfo);

            for (int i = 0; i < state.ChunkInfo.Length; i++)
                state.ChunkInfo[i] = new int4(int.MinValue, int.MinValue, 0, 0);
        }

        /// <summary>
        /// Builds the GPU-facing metadata for a terrain component. Also used by the
        /// CLI terrain tests so they exercise the exact production parameters.
        /// </summary>
        public static TerrainDTO CreateMetadata(SDFTerrain terrain, float3 position)
        {
            TerrainDTO meta = new TerrainDTO
            {
                heightScale = math.max(terrain.height, 1f),
                heightOffset = 0f,
                worldSpace = terrain.infinite ? 1 : 0,
                size = terrain.size,
                position = position,
                interpolation = (int)terrain.interpolation,
                hasErosion = terrain.erosion ? 1 : 0,
                normalStrength = 1f,

                frequency = math.max(terrain.frequency, 1e-6f),
                heightFrequency = 3f,
                heightAmp = 0.125f,
                heightOctaves = 3,
                heightLacunarity = 2f,
                heightGain = 0.1f,

                erosionStrength = terrain.erosionStrength,
                erosionGullyWeight = terrain.erosionGullyWeight,
                erosionDetail = terrain.erosionDetail,
                erosionScale = math.max(terrain.erosionScale, 1e-4f),
                erosionOctaves = terrain.erosionOctaves,
                erosionLacunarity = terrain.erosionLacunarity,
                erosionGain = terrain.erosionGain,
                erosionCellScale = terrain.erosionCellScale,
                erosionNormalization = terrain.erosionNormalization,
                erosionRounding = new float4(0.1f, 0.0f, 0.1f, 2.0f),
                erosionOnset = new float4(1.25f, 1.25f, 2.8f, 1.5f),
                erosionAssumedSlope = new float2(0.7f, 1.0f),
                terrainHeightOffset = new float2(-0.65f, 0.0f),
            };

            if (terrain.infinite)
            {
                int levels = System.Math.Clamp(terrain.levels, 2, 10);
                int gridSize = System.Math.Max(3, terrain.chunksPerLevel | 1);
                int resolution = System.Math.Clamp(terrain.chunkResolution, 16, 256);
                float chunkSize = math.max(terrain.chunkSize, 1f);
                int stride = resolution + 2;

                meta.resolution = new int2(resolution, resolution);
                meta.levels = levels;
                meta.gridSize = gridSize;
                meta.chunkSize = chunkSize;
                meta.chunkStride = stride;
                meta.lodBaseRadius = chunkSize * gridSize * 0.5f;
                meta.lodCenter = new float2(0f, 0f);
            }
            else
            {
                int resolution = System.Math.Clamp(terrain.heightmapSize, 32, 4096);
                float chunkSize = math.max(terrain.size, 1f) * 2f;
                int stride = resolution + 2;

                meta.resolution = new int2(resolution, resolution);
                meta.levels = 1;
                meta.gridSize = 1;
                meta.chunkSize = chunkSize;
                meta.chunkStride = stride;
                meta.lodBaseRadius = chunkSize * 0.5f;
                meta.lodCenter = new float2(0f, 0f);
            }

            return meta;
        }

        private static void AssignBufferOffsets()
        {
            int offset = 0;
            int infoOffset = 0;
            int index = 0;
            foreach (TerrainState state in stateOrder)
            {
                state.Meta.bufferOffset = offset;
                state.Meta.chunkInfoOffset = infoOffset;
                state.Meta.terrainIndex = index;
                offset += state.Data.Length;
                infoOffset += state.ChunkInfo.Length;
                index++;
            }
        }

        private static void UpdateInfiniteTerrain(TerrainState state, float2 cameraXZ,
            List<(int Offset, int Count)> dirtyRanges)
        {
            TerrainClipmap.CollectDesiredChunks(cameraXZ, state.Anchor, state.ChunkSize,
                state.Levels, state.GridSize, state.Desired);

            // Enqueue missing chunks while leaving room for queued work.
            int maxInFlight = System.Math.Max(1, state.Settings.bakeThreads) * 2;
            foreach (TerrainChunkKey key in state.Desired)
            {
                if (state.InFlight >= maxInFlight) break;

                int slot = TerrainClipmap.SlotIndex(key.Level, key.ChunkX, key.ChunkZ, state.GridSize);
                int4 info = state.ChunkInfo[slot];
                if (info.z != 0 && info.x == key.ChunkX && info.y == key.ChunkZ) continue;
                if (state.Pending.Contains(key)) continue;

                state.Pending.Add(key);
                state.InFlight++;

                TerrainDTO meta = state.Meta;
                float levelSize = TerrainClipmap.LevelSize(state.ChunkSize, key.Level);
                float2 chunkOrigin = new float2(
                    state.Anchor.x + key.ChunkX * levelSize,
                    state.Anchor.y + key.ChunkZ * levelSize);
                int resolution = state.Resolution;
                int stride = state.Stride;
                int epoch = state.Epoch;
                uint entityId = state.EntityId;

                _ = Task.Run(() =>
                {
                    float4[] data = TerrainChunkBaker.BakeChunk(meta, chunkOrigin, levelSize, resolution, stride);
                    TerrainChunkBaker.SmoothSlopes(data, stride);
                    state.Completed.Enqueue(new ChunkResult
                    {
                        EntityId = entityId,
                        Epoch = epoch,
                        Key = key,
                        Slot = slot,
                        Data = data,
                    });
                });
            }

            // Apply a bounded number of finished chunks per frame.
            int applied = 0;
            while (applied < System.Math.Max(1, state.Settings.chunksPerFrame)
                && state.Completed.TryDequeue(out ChunkResult? result))
            {
                state.InFlight = System.Math.Max(0, state.InFlight - 1);
                state.Pending.Remove(result.Key);
                if (result.Epoch != state.Epoch) continue;
                if (!IsDesired(state, result.Key)) continue;

                int chunkTexels = state.Stride * state.Stride;
                int slotOffset = result.Slot * chunkTexels;
                Array.Copy(result.Data, 0, state.Data, slotOffset, chunkTexels);

                // Patch the GPU-facing concatenated buffers in place. The pipeline
                // uploads exactly these ranges, so updating only the per-terrain
                // array would leave the GPU reading the zeros from initialization.
                int globalTexel = state.Meta.bufferOffset + slotOffset;
                if (AllTerrainData != null && globalTexel >= 0
                    && globalTexel + chunkTexels <= AllTerrainData.Length)
                    Array.Copy(result.Data, 0, AllTerrainData, globalTexel, chunkTexels);

                state.ChunkInfo[result.Slot] = new int4(result.Key.ChunkX, result.Key.ChunkZ, 1, result.Epoch);
                int globalInfo = state.Meta.chunkInfoOffset + result.Slot;
                if (AllTerrainChunkInfo != null && globalInfo >= 0
                    && globalInfo < AllTerrainChunkInfo.Length)
                    AllTerrainChunkInfo[globalInfo] = state.ChunkInfo[result.Slot];

                dirtyRanges.Add((globalTexel, chunkTexels));
                applied++;
            }
        }

        private static bool IsDesired(TerrainState state, TerrainChunkKey key)
        {
            for (int i = 0; i < state.Desired.Count; i++)
                if (state.Desired[i].Equals(key)) return true;
            return false;
        }

        private static void TryBakeFixedTerrain(TerrainState state, List<(int Offset, int Count)> dirtyRanges)
        {
            DivisionComputeContext? context = RenderPipeline.Instance?.Context;
            float2 chunkOrigin = state.Anchor;
            float levelSize = state.ChunkSize;

            float4[] data;
            if (context != null)
            {
                try { data = BakeChunkOnGpu(context, state.Meta, chunkOrigin, levelSize, state.Resolution, state.Stride); }
                catch (Exception ex)
                {
                    Debug.Warning($"TerrainSystem: GPU bake failed, falling back to CPU ({ex.Message})");
                    data = TerrainChunkBaker.BakeChunk(state.Meta, chunkOrigin, levelSize, state.Resolution, state.Stride);
                }
            }
            else
            {
                data = TerrainChunkBaker.BakeChunk(state.Meta, chunkOrigin, levelSize, state.Resolution, state.Stride);
            }

            TerrainChunkBaker.SmoothSlopes(data, state.Stride);

            if (state.Data.Length != data.Length) state.Data = new float4[data.Length];
            Array.Copy(data, state.Data, data.Length);
            state.ChunkInfo[0] = new int4(0, 0, 1, state.Epoch);
            state.FixedBaked = true;
            dirtyRanges.Add((state.Meta.bufferOffset, data.Length));
        }

        /// <summary>
        /// Bakes one heightmap chunk on the GPU through <see cref="TerrainBakeShader"/>.
        /// </summary>
        public static float4[] BakeChunkOnGpu(DivisionComputeContext context, TerrainDTO meta,
            float2 chunkOrigin, float levelSize, int resolution, int stride)
        {
            using StructuredBuffer<TerrainDTO> metaBuffer = context.CreateStructuredBuffer([meta]);
            using RWBuffer<float4> output = context.CreateRWBuffer<float4>(stride * stride);

            bakeShader ??= context.CompileShader(typeof(TerrainBakeShader));
            bakeShader.SetInt("terrainIndex", 0);
            bakeShader.SetVector("chunkOrigin", chunkOrigin);
            bakeShader.SetFloat("levelSize", levelSize);
            bakeShader.SetInt("resolution", resolution);
            bakeShader.SetInt("stride", stride);
            bakeShader.SetStructuredBuffer("terrainMetadata", metaBuffer);
            bakeShader.SetRWBuffer("output", output);
            bakeShader.Dispatch2D(bakeShader.FindKernel("Execute"), stride, stride);

            return output.GetData(context.Context);
        }

        private static float3 GetCameraPosition()
        {
            float3 result = new float3(0f, 0f, 0f);
            bool hasFallback = false;

            // The quadtree must follow the camera the render window is actually showing.
            // In the editor that is the free-flying editor camera while editing (and while
            // a play session is paused, because EditorCameraSystem re-activates it then).
            // During play mode and in a player build the rendering camera is the gameplay
            // ("main") camera, so the editor camera must not drag the terrain away from it.
            bool editorView = !EngineCore.IsInPlayMode || EngineCore.IsPaused;
            if (editorView)
            {
                foreach ((uint id, Transform transform, Camera camera) in W.QueryData<Transform, Camera>())
                {
                    if (id == EditorCamera.EditorCameraId || W.HasComponent<EditorCamera>(id))
                        return transform.position;
                }
            }

            // Gameplay camera selection. Prefer the camera with Player controls when
            // there are several, exactly like the audio listener.
            foreach ((uint id, Transform transform, Camera camera) in W.QueryData<Transform, Camera>())
            {
                if (id == EditorCamera.EditorCameraId || W.HasComponent<EditorCamera>(id)) continue;
                if (W.HasComponent<Player>(id)) return transform.position;
                if (!hasFallback)
                {
                    result = transform.position;
                    hasFallback = true;
                }
            }
            if (hasFallback) return result;

            // No gameplay camera at all: keep the old active-camera fallback so a
            // camera-less editor scene still streams around something sensible.
            foreach ((uint id, Transform transform, Camera camera) in W.QueryData<Transform, Camera>())
                if (camera.isActive) return transform.position;

            return result;
        }

        private static void RebuildGlobalBuffers()
        {
            List<TerrainDTO> metas = [];
            List<float4> data = [];
            List<int4> chunkInfo = [];

            foreach (TerrainState state in stateOrder)
            {
                metas.Add(state.Meta);
                data.AddRange(state.Data);
                chunkInfo.AddRange(state.ChunkInfo);
            }

            AllTerrainData = [.. data];
            AllTerrainMetadata = [.. metas];
            AllTerrainChunkInfo = [.. chunkInfo];
        }

        /// <summary>
        /// Refreshes the GPU-facing metadata entries in place. The LOD anchor
        /// (<see cref="TerrainDTO.lodCenter"/>) and the camera height used by the outer-ring
        /// fade change every frame while the camera moves, but they must not trigger the full
        /// height-buffer rebuild that <see cref="RebuildGlobalBuffers"/> performs.
        /// </summary>
        private static void SyncGlobalMetadata()
        {
            if (AllTerrainMetadata == null || AllTerrainMetadata.Length != stateOrder.Count)
                return;

            for (int i = 0; i < stateOrder.Count; i++)
                AllTerrainMetadata[i] = stateOrder[i].Meta;
        }

        /// <summary>
        /// Get the terrain metadata index for an entity, or -1 when it has no terrain.
        /// </summary>
        public static int GetTerrainMetadataIndex(uint entityId)
            => terrainStates.TryGetValue(entityId, out TerrainState? state) ? state.Meta.terrainIndex : -1;

        /// <summary>
        /// Gets the metadata for a terrain entity.
        /// </summary>
        public static bool TryGetTerrainMetadata(uint entityId, out TerrainDTO meta)
        {
            if (terrainStates.TryGetValue(entityId, out TerrainState? state))
            {
                meta = state.Meta;
                return true;
            }
            meta = default;
            return false;
        }
    }
}