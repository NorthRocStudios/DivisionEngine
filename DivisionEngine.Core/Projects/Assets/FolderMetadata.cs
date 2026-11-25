//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE file in the project root
// for full license terms.
//
namespace DivisionEngine.Projects.Assets
{
    /// <summary>
    /// Represents metadata for folders in Assets folder.
    /// </summary>
    public class FolderMetadata
    {
        /// <summary>
        /// Relative folder path.
        /// </summary>
        public string FolderPath { get; set; } = string.Empty;

        /// <summary>
        /// Asset metadatas organized by asset full file path.
        /// </summary>
        public Dictionary<string, AssetMetadata> Assets { get; set; } = [];

        /// <summary>
        /// Last time the folder was scanned.
        /// </summary>
        public DateTime LastScanTime { get; set; }
    }
}
