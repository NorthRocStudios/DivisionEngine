//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using System.Reflection;
using System.Text.Json;

namespace DivisionEngine.Projects.Assets
{
    /// <summary>
    /// Manages the asset database for the currently loaded project.
    /// </summary>
    public static class AssetDatabase
    {
        // Database variables
        private static readonly JsonSerializerOptions jsonSerializerOptions =
            new JsonSerializerOptions { WriteIndented = true };

        // Metadata of assets whose file disappeared, keyed by (size, last-write ticks). If a
        // file with the same signature shows up under a new name (rename or move within this
        // refresh) its metadata is adopted again, keeping the GUID - and therefore every
        // AssetRef that points at the asset - valid.
        private static readonly Dictionary<(long Size, long Ticks), AssetMetadata> orphanPool = [];

        // File watching
        private static FileSystemWatcher? watcher;
        private static Timer? debounceTimer;
        private static string? pendingFolderToRefresh;
        // Thread that started the watcher (the UI thread): refreshes are marshalled back to it
        // so Folders/AllAssetsByID are never mutated from a thread-pool callback.
        private static SynchronizationContext? refreshContext;

        /// <summary>
        /// Contains all the folder metadatas.
        /// </summary>
        public  static Dictionary<string, FolderMetadata> Folders { get; private set; } = []; // Key = relative folder path

        /// <summary>
        /// Contains all the asset metadatas.
        /// </summary>
        public static Dictionary<string, AssetMetadata> AllAssetsByID { get; private set; } = []; // Master GUID

        /// <summary>
        /// Called when an assets folder is changed.
        /// </summary>
        /// <remarks>Path of folder that changed, not currently used</remarks>
        public static event Action<string>? FolderChanged;

        /// <summary>
        /// Called when the asset database is modified.
        /// </summary>
        public static event Action? AssetsUpdated;

        /// <summary>
        /// Current project path, if loaded.
        /// </summary>
        public static string ProjectPath => ProjectManager.CurrentProjectPath
            ?? throw new InvalidOperationException("Asset Database: No project loaded");

        /// <summary>
        /// Current assets path for project.
        /// </summary>
        public static string AssetsPath => Path.Combine(ProjectPath, "Assets");

        /// <summary>
        /// Gets a path relative to the project folder.
        /// </summary>
        /// <param name="folderPath">Folder path</param>
        /// <returns>Sub path of folder</returns>
        public static string GetProjectRelativePath(string folderPath) =>
            Path.GetRelativePath(ProjectPath, folderPath);

        /// <summary>
        /// Scans all folders and loads/creates metadata.
        /// </summary>
        public static void Initialize()
        {
            AllAssetsByID.Clear();
            Folders.Clear();
            orphanPool.Clear();
            Directory.CreateDirectory(AssetsPath); // Ensures assets folder exists

            // Get all directories including root
            List<string> directories = [AssetsPath, .. Directory.GetDirectories(AssetsPath, "*", SearchOption.AllDirectories)];
            foreach (string dir in directories) ProcessFolder(dir);
            Debug.Info($"Asset Database: Loaded {AllAssetsByID.Count} assets from {Folders.Count} folders");
        }

        #region fileWatcher

        /// <summary>
        /// Called when a new project is loaded, also can be started manually.
        /// </summary>
        public static void StartFileWatcher()
        {
            if (!Directory.Exists(AssetsPath)) return;

            watcher = new FileSystemWatcher(AssetsPath)
            {
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };

            // Simple file changed event handler
            watcher.Changed += OnWatcherEvent;
            watcher.Created += OnWatcherEvent;
            watcher.Deleted += OnWatcherEvent;
            watcher.Renamed += OnWatcherEvent;

            debounceTimer = new Timer(RefreshScheduledFolder, null, Timeout.Infinite, Timeout.Infinite);
            refreshContext = SynchronizationContext.Current;
            Debug.Info($"Asset Database: file watcher started on: {AssetsPath}");
        }

        private static void OnWatcherEvent(object sender, FileSystemEventArgs e)
        {
            if (e.FullPath.EndsWith(".divmeta", StringComparison.OrdinalIgnoreCase)) return;
            string? folder = Path.GetDirectoryName(e.FullPath);
            if (!string.IsNullOrEmpty(folder)) ScheduleFolderRefresh(folder);
        }

        private static void ScheduleFolderRefresh(string folderPath)
        {
            if (folderPath?.EndsWith(".divmeta") == true) return; // Skip .divmeta files
            pendingFolderToRefresh = folderPath;
            debounceTimer?.Change(50, Timeout.Infinite); // Debounce timer set at 50ms
        }

        private static void RefreshScheduledFolder(object? state)
        {
            string? folderPath = pendingFolderToRefresh;
            if (string.IsNullOrEmpty(folderPath)) return;
            pendingFolderToRefresh = null;

            // This runs on a thread-pool thread while the UI thread reads the same dictionaries.
            // Marshal back to the thread that started the watcher so refreshes are serialized
            // with the UI (and with SaveAll).
            SynchronizationContext? context = refreshContext;
            if (context != null) context.Post(_ => RefreshFolder(folderPath), null);
            else RefreshFolder(folderPath);
        }

        /// <summary>
        /// Refresh a specific folder in the project.
        /// </summary>
        /// <param name="folderPath">Folder path to refresh (global folder path)</param>
        public static void RefreshFolder(string folderPath)
        {
            if (!folderPath.StartsWith(AssetsPath)) return;
            Debug.Info($"Asset Database: Refreshing folder: {folderPath}");

            ProcessFolder(folderPath); // Only reprocess this specific folder
            FolderChanged?.Invoke(folderPath); // Notify UI to update
        }

        /// <summary>
        /// Stop the file watcher system (assets will need to be manually imported).
        /// </summary>
        public static void StopFileWatcher()
        {
            watcher?.Dispose();
            watcher = null;
            debounceTimer?.Dispose();
            debounceTimer = null;
            refreshContext = null;
            pendingFolderToRefresh = null;
        }

        #endregion fileWatcher

        private static void ProcessFolder(string folderPath)
        {
            string relativeFolder = GetProjectRelativePath(folderPath);
            string folderName = Path.GetFileName(folderPath);
            string metadataPath = Path.Combine(folderPath, folderName + ".divmeta");

            Dictionary<string, string> assetFiles = Directory.GetFiles(folderPath)
                .Where(f => !f.EndsWith(".divmeta", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase);

            string[] allMetaFiles = Directory.GetFiles(folderPath, "*.divmeta", SearchOption.TopDirectoryOnly);

            bool haveInMemory = Folders.TryGetValue(relativeFolder, out FolderMetadata? existing);
            FolderMetadata folderMeta;
            bool isNewMeta = false;

            if (haveInMemory)
            {
                // In-memory copy is the source of truth once a project is open - never
                // clobber it with a possibly-stale read from disk.
                folderMeta = existing!;
            }
            else if (File.Exists(metadataPath))
            {
                string json = File.ReadAllText(metadataPath);
                folderMeta = JsonSerializer.Deserialize<FolderMetadata>(json, jsonSerializerOptions)
                    ?? new FolderMetadata { FolderPath = relativeFolder };

                if (folderMeta.FolderPath != relativeFolder)
                    folderMeta.FolderPath = relativeFolder;

                foreach (AssetMetadata asset in folderMeta.Assets.Values)
                {
                    if (!AllAssetsByID.ContainsKey(asset.ID))
                    {
                        AllAssetsByID[asset.ID] = asset;
                        AssetsUpdated?.Invoke();
                    }
                }
            }
            else
            {
                folderMeta = new FolderMetadata { FolderPath = relativeFolder };
                isNewMeta = true;
            }

            foreach (string oldMetaFile in allMetaFiles)
            {
                if (!oldMetaFile.Equals(metadataPath, StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(oldMetaFile); Debug.Info($"Deleted old metadata file: {oldMetaFile}"); }
                    catch (Exception ex) { Debug.Error($"Failed to delete old metadata file {oldMetaFile}", ex); }
                }
            }

            bool changed = isNewMeta;

            foreach (string filename in folderMeta.Assets.Keys.ToList())
            {
                if (!assetFiles.ContainsKey(filename))
                {
                    AssetMetadata asset = folderMeta.Assets[filename];
                    folderMeta.Assets.Remove(filename);
                    AllAssetsByID.Remove(asset.ID);

                    // Keep it around for rename/move detection this refresh.
                    orphanPool[(asset.FileSize, asset.LastModified.Ticks)] = asset;

                    changed = true;
                    AssetsUpdated?.Invoke();
                    Debug.Info($"Asset removed: {asset.FileName}");
                }
            }

            foreach (var kvp in assetFiles)
            {
                string filename = kvp.Key;
                DateTime lastModified = File.GetLastWriteTime(kvp.Value);

                if (folderMeta.Assets.TryGetValue(filename, out AssetMetadata? existingAsset))
                {
                    if (lastModified > existingAsset.LastModified)
                    {
                        UpdateAssetMetadata(existingAsset, kvp.Value);
                        changed = true;
                        Debug.Info($"Asset updated: {existingAsset.FileName}");
                    }
                }
                else
                {
                    // A file we have never seen may be a rename/move of one we just lost.
                    AssetMetadata? adopted = TryAdoptOrphan(kvp.Value, lastModified);
                    AssetMetadata newAsset = adopted ?? CreateAssetMetadata(kvp.Value);
                    folderMeta.Assets[filename] = newAsset;
                    AllAssetsByID[newAsset.ID] = newAsset;
                    changed = true;
                    AssetsUpdated?.Invoke();
                    Debug.Info(adopted != null
                        ? $"Asset renamed/moved: {newAsset.FileName} (GUID preserved: {newAsset.ID})"
                        : $"Asset added: {newAsset.FileName} (GUID: {newAsset.ID})");
                }
            }

            Folders[relativeFolder] = folderMeta;

            // Persist right away so a subsequent refresh never reads stale data again.
            if (changed) SaveFolderMetadata(folderPath, folderMeta);
        }

        /// <summary>
        /// Saves all folder metadata files (called on project exit as well).
        /// </summary>
        public static void SaveAll()
        {
            // Prune folders whose directory no longer exists. Writing into them throws
            // DirectoryNotFoundException (e.g. after deleting a folder in the Assets panel),
            // which used to abort CloseProject half-way.
            List<string> missingFolders = [];

            foreach (var kvp in Folders)
            {
                string folderPath = Path.Combine(ProjectPath, kvp.Key);
                if (!Directory.Exists(folderPath))
                {
                    missingFolders.Add(kvp.Key);
                    continue;
                }
                SaveFolderMetadata(folderPath, kvp.Value);
            }

            foreach (string relativeFolder in missingFolders)
            {
                Folders.Remove(relativeFolder);
                Debug.Info($"Asset Database: dropped missing folder '{relativeFolder}'");
            }

            Debug.Info("Asset Database: All metadata saved");
        }

        private static void SaveFolderMetadata(string folderPath, FolderMetadata metadata)
        {
            string folderName = Path.GetFileName(folderPath);
            string metadataPath = Path.Combine(folderPath, folderName + ".divmeta");
            string json = JsonSerializer.Serialize(metadata, jsonSerializerOptions);
            File.WriteAllText(metadataPath, json);
        }

        /// <summary>
        /// Reuses the metadata of a recently removed asset when a file with the same size and
        /// modification time appears under a new name (a rename or a move). Keeping the GUID
        /// means every AssetRef that points at the asset stays valid.
        /// </summary>
        private static AssetMetadata? TryAdoptOrphan(string filePath, DateTime lastModified)
        {
            if (orphanPool.Count == 0) return null;

            FileInfo info = new FileInfo(filePath);

            if (!orphanPool.TryGetValue((info.Length, lastModified.Ticks), out AssetMetadata? orphan))
            {
                // Mtime can move by a tick or two across some filesystems: fall back to a
                // size match, but only when it identifies exactly one orphan.
                AssetMetadata? candidate = null;
                int matches = 0;
                foreach (AssetMetadata item in orphanPool.Values)
                {
                    if (item.FileSize != info.Length) continue;
                    candidate = item;
                    matches++;
                }

                if (matches != 1) return null;
                orphan = candidate;
            }

            if (orphan == null) return null;

            orphanPool.Remove((orphan.FileSize, orphan.LastModified.Ticks));
            orphan.FileName = Path.GetFileName(filePath);
            orphan.RelativePath = GetProjectRelativePath(filePath);
            orphan.Type = DetermineAssetType(filePath);
            orphan.LastModified = lastModified;
            orphan.FileSize = info.Length;
            return orphan;
        }

        private static AssetMetadata CreateAssetMetadata(string filePath)
        {
            FileInfo fileInfo = new FileInfo(filePath);
            return new AssetMetadata
            {
                // ID is auto-generated by constructor, but we can set it explicitly if needed.
                // Here we rely on the default constructor generating a new GUID.
                FileName = Path.GetFileName(filePath),
                RelativePath = GetProjectRelativePath(filePath),
                Type = DetermineAssetType(filePath),
                LastModified = fileInfo.LastWriteTime,
                FileSize = fileInfo.Length,
            };
        }

        private static void UpdateAssetMetadata(AssetMetadata metadata, string filePath)
        {
            FileInfo fileInfo = new FileInfo(filePath);
            metadata.LastModified = fileInfo.LastWriteTime;
            metadata.FileSize = fileInfo.Length;
            // GUID remains unchanged
        }

        private static AssetType DetermineAssetType(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            return ext switch 
            {
                ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tga" => AssetType.Texture,
                ".obj" or ".fbx" or ".gltf" => AssetType.SDF,
                ".mat" => AssetType.Material,
                // Roslyn only ever compiles .cs files: HLSL sources are their own asset type so
                // dropping one into the project can never break the script compilation.
                ".cs" => AssetType.Script,
                ".shader" or ".hlsl" => AssetType.Shader,
                // The extension is lower-cased above, so this case must be lower-case too.
                ".sdf" or ".sdflib" => AssetType.SDF,
                ".wav" or ".mp3" or ".ogg" => AssetType.Audio,
                ".ttf" or ".otf" => AssetType.Font,
                _ => AssetType.None,
            };
        }

        // ----------
        // Public API
        // ----------

        /// <summary>
        /// Gets an asset type from <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">Type of asset</typeparam>
        /// <returns>AssetType identifier</returns>
        public static AssetType GetAssetType<T>() where T : Asset => GetAssetType(typeof(T));

        /// <summary>
        /// Gets an asset type from Type.
        /// </summary>
        /// <param name="type">Type of asset</param>
        /// <returns>AssetType identifier</returns>
        public static AssetType GetAssetType(Type type) => 
            type.GetCustomAttribute<AssetTypeAttribute>()?.Type ?? AssetType.None;

        /// <summary>
        /// Gets an asset metadata by its GUID.
        /// </summary>
        public static AssetMetadata? GetAssetMetadataByID(string? id) =>
            AllAssetsByID.TryGetValue(id ?? string.Empty, out var asset) ? asset : null;

        /// <summary>
        /// Gets all assets in a folder path relative to the asset folder.
        /// </summary>
        public static IEnumerable<AssetMetadata> GetAssetsInFolder(string relativeFolder)
        {
            if (Folders.TryGetValue(relativeFolder, out var folder))
            {
                Debug.Info($"GetAssetsInFolder('{relativeFolder}') found {folder.Assets.Count} assets");
                return folder.Assets.Values;
            }

            Debug.Warning($"GetAssetsInFolder('{relativeFolder}') - folder not found");
            return [];
        }

        /// <summary>
        /// Gets all the assets in the project database.
        /// </summary>
        public static IEnumerable<AssetMetadata> GetAllAssets() => AllAssetsByID.Values;

        /// <summary>
        /// Gets an enumerable of all asset metadatas of a type.
        /// </summary>
        public static IEnumerable<AssetMetadata> GetAssetsByType(AssetType type) =>
            AllAssetsByID.Values.Where(a => a.Type == type);

        /// <summary>
        /// Gets the full filesystem path for an asset.
        /// </summary>
        public static string? GetAssetFullPath(string id)
        {
            AssetMetadata? asset = GetAssetMetadataByID(id);
            return asset != null ? Path.Combine(ProjectPath, asset.RelativePath) : null;
        }

        // Future items: Import, Delete, Rename (might be added later)
    }
}
