//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE file in the project root
// for full license terms.
//
namespace DivisionEngine.Projects.Assets
{
    [AssetType(AssetType.SDF)]
    public class SDFAsset(AssetMetadata metadata) : Asset(metadata)
    {
        private byte[]? modelData;

        /// <summary>
        /// Raw model file data (OBJ/FBX/GLTF bytes).
        /// </summary>
        public byte[]? ModelData => modelData;

        public override async Task<bool> LoadAsync()
        {
            if (IsLoaded) return true;

            try
            {
                string fullPath = Path.Combine(AssetDatabase.ProjectPath, RelativePath);
                modelData = await File.ReadAllBytesAsync(fullPath);

                IsLoaded = true;
                Debug.Info($"SDF model loaded: {Metadata.FileName} ({modelData.Length} bytes)");
                return true;
            }
            catch (Exception ex)
            {
                Debug.Error($"Failed to load SDF model {Metadata.FileName}: {ex.Message}");
                IsLoaded = false;
                return false;
            }
        }

        public override void Unload()
        {
            modelData = null;
            IsLoaded = false;
            Debug.Info($"SDF model unloaded: {Metadata.FileName}");
        }
    }
}
