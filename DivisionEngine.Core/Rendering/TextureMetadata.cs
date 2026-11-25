//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE file in the project root
// for full license terms.
//
namespace DivisionEngine.Rendering
{
    /// <summary>
    /// Metadata for a loaded texture.
    /// </summary>
    public struct TextureMetadata
    {
        public int2 resolution; // Width, Height
        public int bufferOffset; // Starting index in the big buffer
        public int mipCount; // Mip levels in texture
        public int cubemapLayout; // Cubemap potential layout
    }

    public enum TextureSampling
    {
        Point = 0,
        Bilinear = 1,
    }

    public enum TextureDimension
    {
        Texture2D = 0,
        Texture3D = 1,
        Cubemap = 2,
    }

    public enum CubemapLayout
    {
        None = 0,
        Equirectangular = 1,
        Cross = 2,
        VerticalCross = 3,
    }
}
