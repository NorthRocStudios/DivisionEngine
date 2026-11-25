//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE.txt file in the project root
// for full license terms.
//
namespace DivisionEngine.Projects.Assets
{
    /// <summary>
    /// Represents the metadata for asset files.
    /// </summary>
    public class AssetMetadata
    {
        /// <summary>
        /// Asset GUID.
        /// </summary>
        public string ID { get; set; } = Guid.CreateVersion7().ToString();
        
        /// <summary>
        /// Asset file name.
        /// </summary>
        public string FileName { get; set; } = string.Empty;
        
        /// <summary>
        /// Relative path of asset file to assets folder.
        /// </summary>
        public string RelativePath { get; set; } = string.Empty;
        
        /// <summary>
        /// Asset type.
        /// </summary>
        public AssetType Type { get; set; } = AssetType.None;
        
        /// <summary>
        /// Last time asset file was modified.
        /// </summary>
        public DateTime LastModified { get; set; }
        
        /// <summary>
        /// Size of asset file.
        /// </summary>
        public long FileSize { get; set; }
        
        /// <summary>
        /// Tags for asset.
        /// </summary>
        public List<string> Tags { get; set; } = [];
        
        /// <summary>
        /// Custom properties of asset.
        /// </summary>
        public Dictionary<string, object> CustomProperties { get; set; } = [];
    }
}
