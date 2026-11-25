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
    /// Assigns asset types to asset classes.
    /// </summary>
    /// <param name="type">Type of asset</param>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    internal class AssetTypeAttribute(AssetType type) : Attribute
    {
        /// <summary>
        /// Type of asset.
        /// </summary>
        public AssetType Type { get; } = type;
    }
}
