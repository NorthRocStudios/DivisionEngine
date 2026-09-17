//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
namespace DivisionEngine.Projects.Assets
{
    /// <summary>
    /// Load state for an asset. Purely informational - callers should not make
    /// behavioral decisions based on this. Kept so the assets window can tint
    /// tiles by state and the properties panel can show a status row.
    /// </summary>
    public enum AssetLoadState
    {
        Unloaded = 0,
        Loading = 1,
        Loaded = 2
    }

    /// <summary>
    /// Caches loaded assets for the current project. Loading is idempotent -
    /// repeated calls return the same instance while it stays cached. Because
    /// textures hold no pixel data (see <see cref="TextureAsset"/>), a
    /// "texture load" is just a metadata read.
    /// </summary>
    public class AssetManager
    {
        private readonly Dictionary<string, Asset> cache = [];
        private readonly Dictionary<string, AssetLoadState> states = [];
        private readonly Lock gate = new();

        /// <summary>
        /// Raised whenever an asset's cache entry changes. Fires at most twice
        /// per asset load (Loading, Loaded) and once on unload.
        /// </summary>
        public event Action<string, AssetLoadState>? AssetLoadStateChanged;

        /// <summary>Returns the cached asset, or null if not currently loaded.</summary>
        public Asset? Get(string id)
        {
            lock (gate) return cache.GetValueOrDefault(id);
        }

        /// <summary>Typed variant of <see cref="Get"/>.</summary>
        public T? Get<T>(string id) where T : Asset
        {
            lock (gate) return cache.GetValueOrDefault(id) as T;
        }

        /// <summary>Current load state of an asset.</summary>
        public AssetLoadState GetLoadState(string id)
        {
            lock (gate) return states.GetValueOrDefault(id, AssetLoadState.Unloaded);
        }

        /// <summary>
        /// Loads an asset and caches it. Idempotent: if the asset is already
        /// cached, the cached instance is returned immediately. Returns null
        /// if the asset metadata is missing, has the wrong type, or fails to
        /// load.
        /// </summary>
        public async Task<T?> LoadAssetAsync<T>(string id) where T : Asset
        {
            lock (gate)
            {
                if (cache.TryGetValue(id, out Asset? existing)) return existing as T;
            }

            AssetMetadata? metadata = AssetDatabase.GetAssetMetadataByID(id);
            if (metadata == null) return null;
            if (metadata.Type != AssetDatabase.GetAssetType<T>()) return null;

            Asset? asset = CreateAssetFromMetadata(metadata);
            if (asset == null) return null;

            SetState(id, AssetLoadState.Loading);

            bool ok;
            try { ok = await asset.LoadAsync(); }
            catch (Exception ex)
            {
                Debug.Error($"Asset Manager: load threw for {metadata.FileName}", ex);
                SetState(id, AssetLoadState.Unloaded);
                return null;
            }

            if (!ok)
            {
                SetState(id, AssetLoadState.Unloaded);
                return null;
            }

            lock (gate) cache[id] = asset;
            SetState(id, AssetLoadState.Loaded);
            return asset as T;
        }

        /// <summary>
        /// Removes an asset from the cache and calls its <see cref="Asset.Unload"/>.
        /// No-op if the asset isn't cached.
        /// </summary>
        public void UnloadAsset(string id)
        {
            Asset? removed;
            lock (gate)
            {
                if (!cache.Remove(id, out removed)) return;
            }
            removed.Unload();
            SetState(id, AssetLoadState.Unloaded);
        }

        /// <summary>
        /// Drops the cached instance without calling <c>Unload</c>. The next
        /// <see cref="LoadAssetAsync{T}"/> call reloads it fresh - useful when
        /// asset metadata (import settings) changed and the cached instance
        /// holds stale configuration.
        /// </summary>
        public void InvalidateAsset(string id)
        {
            lock (gate)
            {
                if (!cache.Remove(id)) return;
            }
            SetState(id, AssetLoadState.Unloaded);
        }

        /// <summary>Unloads every cached asset.</summary>
        public void UnloadAll()
        {
            List<Asset> snapshot;
            lock (gate)
            {
                snapshot = [.. cache.Values];
                cache.Clear();
                states.Clear();
            }
            foreach (Asset asset in snapshot) asset.Unload();
            Debug.Info("Asset Manager: unloaded all assets");
        }

        private void SetState(string id, AssetLoadState state)
        {
            lock (gate) states[id] = state;
            AssetLoadStateChanged?.Invoke(id, state);
        }

        private static Asset? CreateAssetFromMetadata(AssetMetadata metadata) => metadata.Type switch
        {
            AssetType.Texture => new TextureAsset(metadata),
            AssetType.Material => new MaterialAsset(metadata),
            AssetType.Script => new ScriptAsset(metadata),
            AssetType.SDF => new SDFAsset(metadata),
            AssetType.Audio => new AudioAsset(metadata),
            _ => null
        };
    }
}
