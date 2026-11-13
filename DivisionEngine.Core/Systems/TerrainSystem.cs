//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.Graphics;
using DivisionEngine.Components.SDFs;
using DivisionEngine.Rendering;
using DivisionEngine.Rendering.Terrain;

namespace DivisionEngine.Systems
{
    /// <summary>
    /// Manages terrain heightmaps.
    /// </summary>
    public class TerrainSystem : SystemBase
    {
        private static float[]? _allTerrainData = [];
        private static TerrainDTO[]? _allTerrainMetadata = [];
        private static readonly Dictionary<uint, int> entityToTerrainIndex = [];
        private static readonly Dictionary<uint, int> lastBakedParamHash = [];

        /// <summary>
        /// Stores the heightmaps for all terrains.
        /// </summary>
        public static float[]? AllTerrainData { get => _allTerrainData; private set => _allTerrainData = value; }

        /// <summary>
        /// Stores the information on all terrain data.
        /// </summary>
        public static TerrainDTO[]? AllTerrainMetadata { get => _allTerrainMetadata; private set => _allTerrainMetadata = value; }

        /// <summary>
        /// Called when terrain data is updated.
        /// </summary>
        public static event Action? UpdatedTerrainData;

        public override void Render()
        {
            bool anyDirty = false;
            List<float> allData = [];
            List<TerrainDTO> allMeta = [];
            int offset = 0;

            foreach (var (id, terrain) in W.QueryData<SDFTerrain>())
            {
                int paramHash = HashCode.Combine(
                    terrain.heightmapSize, terrain.scale, terrain.height, terrain.frequency,
                    terrain.octaves, HashCode.Combine(terrain.lacunarity, terrain.persistence,
                    terrain.ridgeBlend, terrain.ridgeWeight, terrain.edgeFalloff)
                );

                bool needsBake = !lastBakedParamHash.TryGetValue(id, out int prevHash) || prevHash != paramHash;
                if (needsBake)
                {
                    float[] baked = BakeTerrain(terrain);
                    anyDirty = true;
                    lastBakedParamHash[id] = paramHash;
                    bakedCache[id] = baked;
                }

                float[] data = bakedCache[id];
                entityToTerrainIndex[id] = allMeta.Count;
                allMeta.Add(new TerrainDTO
                {
                    resolution = new int2(terrain.heightmapSize, terrain.heightmapSize),
                    bufferOffset = offset,
                    heightScale = terrain.height,
                    size = terrain.scale,
                    terrainIndex = allMeta.Count,
                });
                allData.AddRange(data);
                offset += data.Length;
            }

            if (anyDirty || AllTerrainData == null)
            {
                AllTerrainData = [.. allData];
                AllTerrainMetadata = [.. allMeta];
                UpdatedTerrainData?.Invoke();
            }
        }

        private static readonly Dictionary<uint, float[]> bakedCache = [];

        // Compiled once: the bake shader is stateless between dispatches.
        private static DivisionShader? bakeShader;

        private static float[] BakeTerrain(SDFTerrain terrain)
        {
            DivisionComputeContext context = RenderPipeline.Instance!.Context!;
            using RWBuffer<float> gpuOutput = context.CreateRWBuffer<float>(terrain.heightmapSize * terrain.heightmapSize);

            bakeShader ??= context.CompileShader(typeof(TerrainBakeShader));
            bakeShader.SetVector("resolution", new int2(terrain.heightmapSize, terrain.heightmapSize));
            bakeShader.SetFloat("size", terrain.scale);
            bakeShader.SetFloat("heightScale", terrain.height);
            bakeShader.SetFloat("frequency", terrain.frequency);
            bakeShader.SetInt("octaves", terrain.octaves);
            bakeShader.SetFloat("lacunarity", terrain.lacunarity);
            bakeShader.SetFloat("persistence", terrain.persistence);
            bakeShader.SetFloat("ridgeBlend", terrain.ridgeBlend);
            bakeShader.SetFloat("ridgeWeight", terrain.ridgeWeight);
            bakeShader.SetRWBuffer("output", gpuOutput);
            bakeShader.Dispatch2D(bakeShader.FindKernel("Execute"), terrain.heightmapSize, terrain.heightmapSize);

            return gpuOutput.GetData(context.Context);
        }

        /// <summary>
        /// Get the terrain metadata from an entityID index.
        /// </summary>
        /// <param name="entityId">Entity with terrain data</param>
        /// <returns>Terrain metadata index</returns>
        public static int GetTerrainMetadataIndex(uint entityId) =>
            entityToTerrainIndex.TryGetValue(entityId, out int idx) ? idx : -1;
    }
}
