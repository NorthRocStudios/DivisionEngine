//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.MathLib;
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
        /// Freed by <see cref="FreeCPUTextureData"/> after upload.
        /// </summary>
        public static uint[]? AllTextureData { get => _allTextureData; private set => _allTextureData = value; }

        /// <summary>
        /// Metadata for each texture (resolution, offset in buffer).
        /// Freed alongside <see cref="AllTextureData"/>.
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
        /// Raised when texture data has fully loaded or been modified.
        /// </summary>
        public static event Action? UpdatedTextureData;

        /// <summary>
        /// Raised when a texture load begins.
        /// </summary>
        public static event Action? StartedLoadingTextureData;

        /// <summary>
        /// 0.0 - 1.0, the progress of the texture loader when active.
        /// </summary>
        public static float TextureLoadProgress { get; private set; } = 0f;

        private static readonly Dictionary<string, int> textureIdToIndex = [];
        private static List<string> textureOrder = [];
        private static readonly Lock textureLock = new();

        private static bool mustReloadTextures = false;
        private static volatile bool loadingTextures = false;

        // Snapshot of texture asset IDs the system last knew about. Additions or
        // removals of textures trigger a reload; unrelated asset changes do not
        private static HashSet<string> knownTextureIds = [];
        private static readonly Lock knownTextureLock = new();

        public override void Render()
        {
            // Only the current world's TextureSystem drives reloads
            //if (!ReferenceEquals(WorldManager.CurrentWorld?.GetSystem<TextureSystem>(), this)) return;

            if (mustReloadTextures && !loadingTextures)
            {
                HashSet<string> snapshot = [];
                foreach (AssetMetadata meta in AssetDatabase.GetAssetsByType(AssetType.Texture))
                    snapshot.Add(meta.ID);
                lock (knownTextureLock) knownTextureIds = snapshot;

                _ = LoadAllTexturesAsync();
                mustReloadTextures = false;
            }

            // Default texture so the pipeline never reads a zero-length buffer
            if (AllTextureMetadata == null || AllTextureData == null) return;
            if (AllTextureMetadata.Length < 1 || AllTextureData.Length < 1)
            {
                AllTextureMetadata = [new TextureMetadata { bufferOffset = 0, resolution = 1, mipCount = 1 }];
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

        private static void OnAssetsUpdated()
        {
            HashSet<string> currentIds = [];
            foreach (AssetMetadata meta in AssetDatabase.GetAssetsByType(AssetType.Texture))
                currentIds.Add(meta.ID);

            lock (knownTextureLock)
            {
                if (!currentIds.SetEquals(knownTextureIds)) MarkDirty();
            }
        }

        private static void OnProjectLoaded()
        {
            lock (knownTextureLock) knownTextureIds.Clear();
            MarkDirty();
        }

        private static void OnProjectClosed()
        {
            lock (knownTextureLock) knownTextureIds.Clear();
            MarkDirty();
        }

        /// <summary>
        /// Flags the texture buffer for a full rebuild. Because this system does
        /// not cache per-texture pixel data, any change - add, remove, edit
        /// import settings - results in a full reload on the next render tick.
        /// Decoding and mip generation are parallelized, so this is fast even
        /// for projects with many textures.
        /// </summary>
        public static void MarkDirty() => mustReloadTextures = true;

        /// <summary>
        /// Loads every texture in the asset database and rebuilds the flat GPU
        /// buffer. Decode and mip generation run in parallel across thread-pool
        /// tasks; the resulting mip chains are consumed by the flat buffer build
        /// and then discarded.
        /// </summary>
        public static async Task LoadAllTexturesAsync()
        {
            if (loadingTextures) return;
            StartedLoadingTextureData?.Invoke();
            TextureLoadProgress = 0f;
            loadingTextures = true;

            try
            {
                if (!ProjectManager.IsCurrentLoaded)
                {
                    Debug.Info("Texture System: Cannot load textures without a current project loaded!");
                    ClearAll();
                    UpdatedTextureData?.Invoke();
                    return;
                }

                List<AssetMetadata> textureMetadatas = [.. AssetDatabase.GetAssetsByType(AssetType.Texture)];
                if (textureMetadatas.Count == 0)
                {
                    Debug.Info("Texture System: No textures found in project");
                    ClearAll();
                    UpdatedTextureData?.Invoke();
                    return;
                }

                Debug.Info($"Texture System: Loading {textureMetadatas.Count} textures...");

                // Prepare TextureAssets (metadata only - cheap, sequential is fine)
                List<TextureAsset> textures = [];
                foreach (AssetMetadata meta in textureMetadatas)
                {
                    TextureAsset? t = await ProjectManager.AssetManager!.LoadAssetAsync<TextureAsset>(meta.ID);
                    if (t != null) textures.Add(t);
                }

                if (textures.Count == 0)
                {
                    Debug.Warning("Texture System: No textures could be prepared");
                    ClearAll();
                    UpdatedTextureData?.Invoke();
                    return;
                }

                // Decode + build mip chains in parallel. Indexed results
                // preserve the asset order regardless of completion order
                var results = new (TextureAsset Asset, List<uint[]>? MipChain, int Width, int Height)[textures.Count];
                int loadedCount = 0;
                object progressLock = new();

                Task[] tasks = new Task[textures.Count];
                for (int i = 0; i < textures.Count; i++)
                {
                    int index = i;
                    tasks[index] = Task.Run(() =>
                    {
                        TextureAsset asset = textures[index];
                        try
                        {
                            var (pixels, w, h) = asset.GetPixelData();
                            int naturalMax = (int)math.floor(math.log2(math.max(w, h))) + 1;
                            int maxLevels = math.clamp(
                                asset.MaxMipmap <= 0 ? naturalMax : asset.MaxMipmap,
                                1, naturalMax);

                            List<uint[]> mips = BuildMipChain(pixels, w, h, maxLevels);
                            results[index] = (asset, mips, w, h);

                            lock (progressLock)
                            {
                                loadedCount++;
                                TextureLoadProgress = (float)loadedCount / textures.Count;
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.Warning($"Texture System: Failed to load {asset.Metadata.FileName}: {ex.Message}");
                        }
                    });
                }

                await Task.WhenAll(tasks);

                // Build the flat buffer. Mip chains are consumed here and then
                // become eligible for collection - nothing keeps them alive
                int totalPixels = 0;
                for (int i = 0; i < results.Length; i++)
                {
                    List<uint[]>? mips = results[i].MipChain;
                    if (mips == null) continue;
                    foreach (uint[] level in mips) totalPixels += level.Length;
                }

                if (totalPixels == 0)
                {
                    Debug.Warning("Texture System: No textures loaded successfully");
                    ClearAll();
                    UpdatedTextureData?.Invoke();
                    return;
                }

                uint[] flat = new uint[totalPixels];
                List<TextureMetadata> metas = new(results.Length);
                List<string> order = new(results.Length);
                int offset = 0;

                for (int i = 0; i < results.Length; i++)
                {
                    var (asset, mips, w, h) = results[i];
                    if (mips == null) continue;

                    metas.Add(new TextureMetadata
                    {
                        bufferOffset = offset,
                        resolution = new int2(w, h),
                        mipCount = mips.Count,
                        cubemapLayout = (int)asset.CubemapLayout,
                    });
                    order.Add(asset.ID);

                    foreach (uint[] level in mips)
                    {
                        Array.Copy(level, 0, flat, offset, level.Length);
                        offset += level.Length;
                    }
                }

                lock (textureLock)
                {
                    textureOrder = order;
                }

                AllTextureData = flat;
                AllTextureMetadata = [.. metas];
                LastLoadedTextureCount = metas.Count;
                LastLoadedTextureBufferSize = flat.Length;

                textureIdToIndex.Clear();
                for (int i = 0; i < order.Count; i++) textureIdToIndex[order[i]] = i;

                Debug.Info($"Texture System: Loaded {metas.Count} textures, {flat.Length} total pixels");
                UpdatedTextureData?.Invoke();
            }
            finally
            {
                loadingTextures = false;
                TextureLoadProgress = 1f;
            }
        }

        /// <summary>
        /// Builds a mip chain from the given base level. The base level is
        /// included as the first element of the result. Purely CPU-bound and
        /// synchronous - callers running it in parallel are responsible for the
        /// threading (see <see cref="LoadAllTexturesAsync"/>).
        /// </summary>
        private static List<uint[]> BuildMipChain(uint[] baseLevel, int width, int height, int maxLevels)
        {
            List<uint[]> levels = new(maxLevels) { baseLevel };
            int w = width, h = height;
            uint[] prev = baseLevel;

            while ((w > 1 || h > 1) && levels.Count < maxLevels)
            {
                int nw = math.max(1, w / 2);
                int nh = math.max(1, h / 2);
                uint[] next = new uint[nw * nh];

                // Capture for closure clarity - these don't change inside the loops
                int srcW = w, srcH = h;
                uint[] src = prev;

                for (int y = 0; y < nh; y++)
                {
                    int y0 = math.min(y * 2, srcH - 1);
                    int y1 = math.min(y * 2 + 1, srcH - 1);
                    int rowOut = y * nw;
                    int rowIn0 = y0 * srcW;
                    int rowIn1 = y1 * srcW;

                    for (int x = 0; x < nw; x++)
                    {
                        int x0 = math.min(x * 2, srcW - 1);
                        int x1 = math.min(x * 2 + 1, srcW - 1);
                        next[rowOut + x] = AveragePixels(
                            src[rowIn0 + x0], src[rowIn0 + x1],
                            src[rowIn1 + x0], src[rowIn1 + x1]);
                    }
                }

                levels.Add(next);
                prev = next;
                w = nw; h = nh;
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

        public static void UnloadAll()
        {
            ClearAll();
            Debug.Info("Texture System: Unloaded all textures");
        }

        private static void ClearAll()
        {
            AllTextureData = [];
            AllTextureMetadata = [];
            LastLoadedTextureCount = 0;
            LastLoadedTextureBufferSize = 0;
            lock (textureLock)
            {
                textureOrder.Clear();
                textureIdToIndex.Clear();
            }
        }

        /// <summary>
        /// Frees the CPU-side flat buffer. Called by the render pipeline after
        /// the GPU upload completes - once this runs, the textures live only on
        /// the GPU, and CPU texture memory is effectively zero.
        /// </summary>
        public static void FreeCPUTextureData()
        {
            _allTextureData = null;
            _allTextureMetadata = null;
            Debug.Info("Texture System: Freed CPU texture data");
        }
    }
}
