//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using System.Text.Json.Serialization;

namespace DivisionEngine.Projects.Assets
{
    /// <summary>
    /// Type of an asset.
    /// </summary>
    public enum AssetType
    {
        None, Texture, Script, SDF, Material, Audio, Font, Shader,
    }

    /// <summary>
    /// The base class of all asset types, not for direct use.
    /// </summary>
    /// <param name="metadata">Asset metadata</param>
    public abstract class Asset(AssetMetadata metadata)
    {
        /// <summary>
        /// Identifier of the asset.
        /// </summary>
        public string ID { get; protected set; } = metadata.ID;

        /// <summary>
        /// Path relative to the project assets folder for this asset.
        /// </summary>
        public string RelativePath { get; protected set; } = metadata.RelativePath;

        /// <summary>
        /// Metadata of this asset.
        /// </summary>
        public AssetMetadata Metadata { get; protected set; } = metadata;

        /// <summary>
        /// Whether this asset is loaded or not.
        /// </summary>
        public bool IsLoaded { get; protected set; }

        /// <summary>
        /// Defines how to load this asset type.
        /// </summary>
        /// <returns>Async task whether the asset was loaded or not</returns>
        public abstract Task<bool> LoadAsync();

        /// <summary>
        /// Defines how to unload this asset type.
        /// </summary>
        public abstract void Unload();
    }

    /// <summary>
    /// Reference to an asset. This is what components store.
    /// </summary>
    public struct AssetRef(string id, AssetType type)
    {
        /// <summary>
        /// Identifier of the asset.
        /// </summary>
        public string ID { get; set; } = id;

        /// <summary>
        /// Expected type of the asset.
        /// </summary>
        public AssetType ExpectedType { get; set; } = type;

        public AssetRef() : this(string.Empty, AssetType.None) { }

        /// <summary>
        /// The loaded asset object for this asset reference, null if not loaded.
        /// </summary>
        [JsonIgnore] public readonly Asset? LoadedAsset
        {
            get
            {
                if (string.IsNullOrEmpty(ID)) return null;

                // Try to get from AssetManager
                Asset? asset = ProjectManager.AssetManager?.Get(ID);
                if (asset != null) return asset;
                return null;
            }
        }

        /// <summary>
        /// Whether the asset this reference refers to is loaded or not.
        /// </summary>
        [JsonIgnore] public readonly bool IsLoaded => LoadedAsset != null;

        /// <summary>
        /// Checks whether the asset ID is valid or not.
        /// </summary>
        /// <returns>True if valid asset ID</returns>
        public readonly bool IsValid() => !string.IsNullOrEmpty(ID);
    }

    /// <summary>
    /// Reference to an asset. This is what components store.
    /// </summary>
    public struct AssetRef<T>(string id) where T : Asset
    {
        /// <summary>
        /// Identifier of the asset.
        /// </summary>
        public string ID { get; set; } = id;

        /// <summary>
        /// Expected type of the asset.
        /// </summary>
        public AssetType ExpectedType { get; set; } = AssetDatabase.GetAssetType<T>();

        public AssetRef() : this(string.Empty) { }

        /// <summary>
        /// The loaded type object for this asset reference, null if not loaded.
        /// </summary>
        [JsonIgnore]
        public readonly T? LoadedAsset
        {
            get
            {
                if (string.IsNullOrEmpty(ID)) return null;

                // Try to get from AssetManager
                T? asset = ProjectManager.AssetManager?.Get<T>(ID);
                if (asset != null) return asset;
                return null;
            }
        }

        /// <summary>
        /// Whether the asset this reference refers to is loaded or not.
        /// </summary>
        [JsonIgnore] public readonly bool IsLoaded => LoadedAsset != null;

        public static implicit operator AssetRef(AssetRef<T> generic) =>
            new(generic.ID, generic.ExpectedType);

        public static implicit operator AssetRef<T>(AssetRef standard)
        {
            if (standard.ExpectedType != AssetDatabase.GetAssetType<T>())
                throw new InvalidCastException($"Cannot cast AssetRef of type {standard.ExpectedType} to {typeof(T).Name}");
            return new AssetRef<T> { ID = standard.ID, ExpectedType = standard.ExpectedType };
        }
    }
}
