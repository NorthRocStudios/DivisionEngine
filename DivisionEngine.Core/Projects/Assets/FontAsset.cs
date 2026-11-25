//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE file in the project root
// for full license terms.
//
namespace DivisionEngine.Projects.Assets
{
    [AssetType(AssetType.Font)]
    public class FontAsset(AssetMetadata metadata) : Asset(metadata)
    {
        private byte[]? fontData;

        /// <summary>
        /// Raw font file data (TTF/OTF bytes).
        /// </summary>
        public byte[]? FontData => fontData;

        public override async Task<bool> LoadAsync()
        {
            if (IsLoaded) return true;

            try
            {
                string fullPath = Path.Combine(AssetDatabase.ProjectPath, RelativePath);
                fontData = await File.ReadAllBytesAsync(fullPath);

                IsLoaded = true;
                Debug.Info($"Font loaded: {Metadata.FileName} ({fontData.Length} bytes)");
                return true;
            }
            catch (Exception ex)
            {
                Debug.Error($"Failed to load font {Metadata.FileName}: {ex.Message}");
                IsLoaded = false;
                return false;
            }
        }

        public override void Unload()
        {
            fontData = null;
            IsLoaded = false;
            Debug.Info($"Font unloaded: {Metadata.FileName}");
        }
    }
}
