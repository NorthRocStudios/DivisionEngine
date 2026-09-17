//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.Projects;
using DivisionEngine.Projects.Assets;
using DivisionEngine.Rendering;

namespace DivisionEngine.Systems
{
    /// <summary>
    /// Used for manipulating all project textures.
    /// </summary>
    public class TextureSystem : SystemBase
    {
        private static uint[]? _allTextureData = [];
        private static TextureMetadata[]? _allTextureMetadata = [];

        /// <summary>
        /// All texture data flattened into a single array (for GPU buffer).
        /// </summary>
        public static uint[]? AllTextureData { get => _allTextureData; private set => _allTextureData = value; }

        /// <summary>
        /// Metadata for each texture (resolution, offset in buffer).
        /// </summary>
        public static TextureMetadata[]? AllTextureMetadata { get => _allTextureMetadata; private set => _allTextureMetadata = value; }

        /// <summary>
        /// How many textures were loaded on latest texture buffer rebuild.
        /// </summary>
        public static int LastLoadedTextureCount { get; private set; } = 0;

        /// <summary>
        /// The texture buffer size on the latest texture buffer rebuild.
        /// </summary>
        public static int LastLoadedTextureBufferSize { get; private set; } = 0;

        /// <summary>
        /// Use this to determine when the texture data has fully loaded or been modified.
        /// </summary>
        public static event Action? UpdatedTextureData;

        /// <summary>
        /// Called when texture data is called to load.
        /// </summary>
        public static event Action? StartedLoadingTextureData;

        /// <summary>
        /// 0.0 - 1.0, the progress of the texture loader when active.
        /// </summary>
        public static float TextureLoadProgress { get; private set; } = 0f;

        private static readonly Dictionary<string, int> textureIdToIndex = [];

        // Per-texture cache: decoded mip chain + metadata, independent of buffer position.
        // This is what lets a single texture be reimported without touching the others.
        private static readonly Dictionary<string, List<uint[]>> textureMipCache = [];
        private static readonly Dictionary<string, TextureMetadata> textureMetaCache = []; // bufferOffset unused here, recomputed on flatten
        private static List<string> textureOrder = []; // buffer order, stable across single-texture reimports
        private static readonly Lock textureCacheLock = new();

        // Queued single-texture reimports, drained on the next Render() tick
        private static readonly HashSet<string> pendingSingleReimports = [];
        private static readonly Lock pendingLock = new();

        private static bool mustReloadTextures = false;
        private static volatile bool loadingTextures = false;

        // Snapshot of texture asset IDs the system last knew about. Any addition or
        // removal of a texture asset triggers a reload; unrelated asset changes (scripts, materials, folders) do not
        private static HashSet<string> knownTextureIds = [];
        private static readonly Lock knownTextureLock = new();

        public override void Render()
        {
            // Only the current world's TextureSystem drives reloads. Orphaned
            // instances from previous worlds may still be invoked by the render
            // pipeline while it's bound to an old world during a project
            // transition, and they would otherwise consume the shared static flag
            // and fire their own load. Guard against that by checking identity.
            if (!ReferenceEquals(WorldManager.CurrentWorld?.GetSystem<TextureSystem>(), this)) return;

            if (mustReloadTextures && !loadingTextures)
            {
                // Snapshot the current texture set before starting the reload. Any
                // texture added or removed during the reload will be picked up by the next OnAssetsUpdated
                HashSet<string> snapshot = [];
                foreach (AssetMetadata meta in AssetDatabase.GetAssetsByType(AssetType.Texture))
                    snapshot.Add(meta.ID);
                lock (knownTextureLock) knownTextureIds = snapshot;

                _ = LoadAllTexturesAsync();
                mustReloadTextures = false;
            }
            else if (!loadingTextures)
            {
                List<string> toProcess = [];
                lock (pendingLock)
                {
                    if (pendingSingleReimports.Count > 0)
                    {
                        toProcess.AddRange(pendingSingleReimports);
                        pendingSingleReimports.Clear();
                    }
                }
                if (toProcess.Count > 0) _ = ReimportTexturesAsync(toProcess);
            }

            // Create default texture if none exist to prevent a crash
            if (AllTextureMetadata == null || AllTextureData == null) return;
            if (AllTextureMetadata.Length < 1 || AllTextureData.Length < 1)
            {
                AllTextureMetadata = [
                    new TextureMetadata { bufferOffset = 0, resolution = 1, mipCount = 1 }
                ];
                AllTextureData = [0];
            }
        }

        public override void AppStart()
        {
            loadingTextures = false;
            AssetDatabase.AssetsUpdated += OnAssetsUpdated;
            ProjectManager.ProjectLoaded += OnProjectLoaded;
            ProjectManager.ProjectClosed += OnProjectClosed;
        }

        public override void Unload()
        {
            AssetDatabase.AssetsUpdated -= OnAssetsUpdated;
            ProjectManager.ProjectLoaded -= OnProjectLoaded;
            ProjectManager.ProjectClosed -= OnProjectClosed;
        }

        // Awake intentionally does NOT set mustReloadTextures. ProjectLoaded already covers the project-load path,
        // and clearing the flag here would race against an in-flight reload started from an earlier Render tick
        public override void Awake() { }

        private static void OnAssetsUpdated()
        {
            HashSet<string> currentIds = [];
            foreach (AssetMetadata meta in AssetDatabase.GetAssetsByType(AssetType.Texture))
                currentIds.Add(meta.ID);

            lock (knownTextureLock)
            {
                if (!currentIds.SetEquals(knownTextureIds))
                    mustReloadTextures = true;
            }
        }

        private static void OnProjectLoaded()
        {
            lock (knownTextureLock) knownTextureIds.Clear();
            mustReloadTextures = true;
        }

        private static void OnProjectClosed()
        {
            lock (knownTextureLock) knownTextureIds.Clear();
            mustReloadTextures = true;
        }

        /// <summary>
        /// Flags the entire texture buffer for a full rebuild (every texture re-decoded
        /// from disk). Use for project load/close or when assets are added/removed -
        /// which change the whole set of textures anyway (AssetDatabase.AssetsUpdated
        /// already triggers this automatically). For editing a single texture's import
        /// settings, use <see cref="MarkTextureDirty"/> instead - it's far cheaper since
        /// it leaves every other texture's cached data untouched.
        /// </summary>
        public static void MarkDirty() => mustReloadTextures = true;

        /// <summary>
        /// Flags a single texture for reimport - reloads and re-mips just this one asset
        /// from disk (respecting its current import settings), then recombines the GPU
        /// buffer from cache. Every other texture's cached mip data is reused as-is, so
        /// this stays cheap no matter how many textures are in the project. Call this
        /// after invalidating the asset (e.g. via AssetManager.InvalidateAsset) whenever
        /// you change one texture's sampling, dimension, cubemap layout, or max mip.
        /// </summary>
        public static void MarkTextureDirty(string assetId)
        {
            lock (pendingLock) pendingSingleReimports.Add(assetId);
        }

        /// <summary>
        /// Removes a single texture from the GPU buffer without attempting to reload it -
        /// use this after explicitly unloading an asset (e.g. the editor's "Unload Asset"
        /// button), where the intent is for it to no longer occupy GPU memory at all.
        /// </summary>
        public static void RemoveTexture(string assetId)
        {
            lock (textureCacheLock)
            {
                textureMipCache.Remove(assetId);
                textureMetaCache.Remove(assetId);
                textureOrder.Remove(assetId);
            }
            RebuildFlatBuffer();
            UpdatedTextureData?.Invoke();
        }

        /// <summary>
        /// Loads all textures from the asset database (full rebuild - every texture is
        /// re-decoded and re-mipped from disk). Use MarkTextureDirty for single-texture
        /// updates instead of calling this directly wherever possible.
        /// </summary>
        public static async Task LoadAllTexturesAsync()
        {
            if (loadingTextures) return;
            StartedLoadingTextureData?.Invoke();
            TextureLoadProgress = 0f;
            loadingTextures = true;

            if (!ProjectManager.IsCurrentLoaded)
            {
                Debug.Info("Texture System: Cannot load textures without a current project loaded!");
                ClearAll();
                UpdatedTextureData?.Invoke();
                loadingTextures = false;
                return;
            }

            List<AssetMetadata> textureMetadatas = [.. AssetDatabase.GetAssetsByType(AssetType.Texture)];
            if (textureMetadatas.Count == 0)
            {
                Debug.Info("Texture System: No textures found in project");
                ClearAll();
                UpdatedTextureData?.Invoke();
                loadingTextures = false;
                return;
            }

            Debug.Info($"Texture System: Loading {textureMetadatas.Count} textures...");

            List<string> newOrder = [];
            int loadedCount = 0;
            foreach (AssetMetadata meta in textureMetadatas)
            {
                bool ok = await LoadAndCacheTextureAsync(meta.ID, reportProgress: true, totalCount: textureMetadatas.Count);
                if (ok)
                {
                    newOrder.Add(meta.ID);
                    loadedCount++;
                }
                TextureLoadProgress = (float)loadedCount / textureMetadatas.Count;
            }

            lock (textureCacheLock) textureOrder = newOrder;

            if (loadedCount == 0)
            {
                Debug.Warning("Texture System: No textures loaded successfully");
                ClearAll();
                loadingTextures = false;
                TextureLoadProgress = 0f;
                return;
            }

            RebuildFlatBuffer();
            Debug.Info($"Texture System: Loaded {loadedCount} textures, {AllTextureData!.Length} total pixels");
            UpdatedTextureData?.Invoke();
            loadingTextures = false;
            TextureLoadProgress = 1f;
        }

        /// <summary>
        /// Reimports a specific set of textures (reload from disk, re-mip per current
        /// import settings) and recombines the flat GPU buffer from cache afterward.
        /// Every texture NOT in <paramref name="assetIds"/> is left completely untouched -
        /// its cached mip chain is just re-copied into the new buffer as-is.
        /// </summary>
        private static async Task ReimportTexturesAsync(List<string> assetIds)
        {
            if (loadingTextures) return; // a full reload is already in flight and will supersede this
            loadingTextures = true;
            try
            {
                foreach (string id in assetIds)
                {
                    bool ok = await LoadAndCacheTextureAsync(id, reportProgress: false, totalCount: 1);
                    lock (textureCacheLock)
                    {
                        if (ok)
                        {
                            if (!textureOrder.Contains(id)) textureOrder.Add(id);
                        }
                        else
                        {
                            // Asset failed to load (deleted, decode error, etc.)
                            textureMipCache.Remove(id);
                            textureMetaCache.Remove(id);
                            textureOrder.Remove(id);
                        }
                    }
                }
                RebuildFlatBuffer();
                Debug.Info($"Texture System: Reimported {assetIds.Count} texture(s)");
                UpdatedTextureData?.Invoke();
            }
            finally
            {
                loadingTextures = false;
            }
        }

        /// <summary>
        /// Loads (or reloads) a single texture asset from disk, builds its mip chain per
        /// its current import settings, and stores the result in the per-texture cache.
        /// Does not touch the flattened GPU buffer - callers must invoke RebuildFlatBuffer
        /// afterward (LoadAllTexturesAsync and ReimportTexturesAsync both do this).
        /// </summary>
        private static async Task<bool> LoadAndCacheTextureAsync(string assetId, bool reportProgress, int totalCount)
        {
            TextureAsset? texture = await ProjectManager.AssetManager!.LoadAssetAsync<TextureAsset>(assetId);
            if (texture?.PixelData == null)
            {
                Debug.Warning($"Texture System: Failed to load texture: {assetId}");
                return false;
            }

            int naturalMaxLevels = (int)Math.Floor(Math.Log2(Math.Max(texture.Width, texture.Height))) + 1;
            int maxLevels = Math.Clamp(texture.MaxMipmap <= 0 ? naturalMaxLevels : texture.MaxMipmap, 1, naturalMaxLevels);

            List<uint[]> mipChain = await BuildMipChainAsync(texture.PixelData, texture.Width, texture.Height, maxLevels,
                reportProgress ? (_ => TextureLoadProgress += 1f / maxLevels / totalCount) : (_ => { }));

            lock (textureCacheLock)
            {
                textureMipCache[assetId] = mipChain;
                textureMetaCache[assetId] = new TextureMetadata
                {
                    resolution = new int2(texture.Width, texture.Height),
                    bufferOffset = 0, // recomputed in RebuildFlatBuffer
                    mipCount = mipChain.Count,
                    cubemapLayout = (int)texture.CubemapLayout,
                };
            }

            GC.Collect(); // Collect GC after every texture (re)loaded, matches prior behavior
            return true;
        }

        /// <summary>
        /// Flattens the per-texture mip-chain cache into the single GPU buffer +
        /// metadata array, in textureOrder. This is pure array concatenation and offset
        /// bookkeeping - no image decoding or mip generation happens here, which is why
        /// it's cheap enough to call after reimporting just one texture.
        /// </summary>
        private static void RebuildFlatBuffer()
        {
            List<uint> allData = [];
            List<TextureMetadata> metadataList = [];
            Dictionary<string, int> newIndex = [];

            lock (textureCacheLock)
            {
                int currentOffset = 0;
                foreach (string id in textureOrder)
                {
                    if (!textureMipCache.TryGetValue(id, out List<uint[]>? mipChain) ||
                        !textureMetaCache.TryGetValue(id, out TextureMetadata meta)) continue;

                    meta.bufferOffset = currentOffset;
                    metadataList.Add(meta);
                    newIndex[id] = metadataList.Count - 1;

                    foreach (uint[] level in mipChain)
                    {
                        foreach (uint pixel in level) allData.Add(pixel);
                        currentOffset += level.Length;
                    }
                }
            }

            AllTextureData = [.. allData];
            AllTextureMetadata = [.. metadataList];
            LastLoadedTextureCount = AllTextureMetadata.Length;
            LastLoadedTextureBufferSize = AllTextureData.Length;

            textureIdToIndex.Clear();
            foreach (var kvp in newIndex) textureIdToIndex[kvp.Key] = kvp.Value;
        }

        private static void ClearAll()
        {
            AllTextureData = [];
            AllTextureMetadata = [];
            LastLoadedTextureCount = 0;
            LastLoadedTextureBufferSize = 0;
            lock (textureCacheLock)
            {
                textureMipCache.Clear();
                textureMetaCache.Clear();
                textureOrder.Clear();
                textureIdToIndex.Clear();
            }
        }

        private static async Task<List<uint[]>> BuildMipChainAsync(uint[] baseLevel, int width, int height, int maxLevels, Action<int> onProgress)
        {
            List<uint[]> levels = [baseLevel];
            int w = width, h = height;
            uint[] prev = baseLevel;
            onProgress(1); // report base level

            while ((w > 1 || h > 1) && levels.Count < maxLevels)
            {
                int nw = Math.Max(1, w / 2);
                int nh = Math.Max(1, h / 2);
                uint[] next = new uint[nw * nh];

                await Task.Run(() =>
                {
                    for (int y = 0; y < nh; y++)
                    {
                        for (int x = 0; x < nw; x++)
                        {
                            int x0 = Math.Min(x * 2, w - 1);
                            int x1 = Math.Min(x * 2 + 1, w - 1);
                            int y0 = Math.Min(y * 2, h - 1);
                            int y1 = Math.Min(y * 2 + 1, h - 1);
                            next[y * nw + x] = AveragePixels(
                                prev[y0 * w + x0], prev[y0 * w + x1],
                                prev[y1 * w + x0], prev[y1 * w + x1]);
                        }
                    }
                });

                levels.Add(next);
                prev = next;
                w = nw; h = nh;
                onProgress(1);
            }
            return levels;
        }

        private static uint AveragePixels(uint a, uint b, uint c, uint d)
        {
            int r = (UnpackChannel(a, 0) + UnpackChannel(b, 0) + UnpackChannel(c, 0) + UnpackChannel(d, 0)) / 4;
            int g = (UnpackChannel(a, 1) + UnpackChannel(b, 1) + UnpackChannel(c, 1) + UnpackChannel(d, 1)) / 4;
            int b_ = (UnpackChannel(a, 2) + UnpackChannel(b, 2) + UnpackChannel(c, 2) + UnpackChannel(d, 2)) / 4;
            int al = (UnpackChannel(a, 3) + UnpackChannel(b, 3) + UnpackChannel(c, 3) + UnpackChannel(d, 3)) / 4;
            return (uint)((r << 24) | (g << 16) | (b_ << 8) | al);
        }

        private static int UnpackChannel(uint packed, int channel)
        {
            int shift = channel switch { 0 => 24, 1 => 16, 2 => 8, 3 => 0, _ => 0 };
            return (int)((packed >> shift) & 0xFF);
        }

        public static int GetTextureMetadataIndex(string assetId) =>
            textureIdToIndex.TryGetValue(assetId, out int index) ? index : -1;

        public static TextureMetadata? GetTextureMetadata(string assetId) =>
            textureIdToIndex.TryGetValue(assetId, out int index) ? AllTextureMetadata?[index] : null;

        public static uint[]? GetTextureData(string assetId)
        {
            if (textureIdToIndex.TryGetValue(assetId, out int index))
            {
                if (AllTextureMetadata == null || AllTextureData == null) return null;
                TextureMetadata meta = AllTextureMetadata[index];
                int start = meta.bufferOffset;
                int length = meta.resolution.X * meta.resolution.Y;
                uint[] result = new uint[length];
                Array.Copy(AllTextureData, start, result, 0, length);
                return result;
            }
            return null;
        }

        public static void UnloadAll()
        {
            ClearAll();
            Debug.Info("Texture System: Unloaded all textures");
        }

        public static void FreeCPUTextureData()
        {
            _allTextureData = null;
            _allTextureMetadata = null;
            GC.Collect();
            Debug.Info("Texture System: Freed CPU texture data");
        }
    }
}
