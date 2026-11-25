//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE.txt file in the project root
// for full license terms.
//
using System.Text.Json;

namespace DivisionEngine.Projects.Assets
{
    [AssetType(AssetType.Material)]
    public class MaterialAsset(AssetMetadata metadata) : Asset(metadata)
    {
        private JsonDocument? materialData;

        /// <summary>
        /// Parsed material JSON data.
        /// </summary>
        public JsonDocument? MaterialData => materialData;

        public override async Task<bool> LoadAsync()
        {
            if (IsLoaded) return true;

            try
            {
                string fullPath = Path.Combine(AssetDatabase.ProjectPath, RelativePath);
                string jsonText = await File.ReadAllTextAsync(fullPath);
                materialData = JsonDocument.Parse(jsonText);

                IsLoaded = true;
                Debug.Info($"Material loaded: {Metadata.FileName}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.Error($"Failed to load material {Metadata.FileName}: {ex.Message}");
                IsLoaded = false;
                return false;
            }
        }

        public override void Unload()
        {
            materialData?.Dispose();
            materialData = null;
            IsLoaded = false;
            Debug.Info($"Material unloaded: {Metadata.FileName}");
        }
    }
}
