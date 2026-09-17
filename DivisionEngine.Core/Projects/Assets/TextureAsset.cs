//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.Rendering;
using SkiaSharp;

namespace DivisionEngine.Projects.Assets
{
    /// <summary>
    /// Represents a texture file in a project.
    /// </summary>
    /// <param name="metadata">Asset metadata for this texture</param>
    [AssetType(AssetType.Texture)]
    public class TextureAsset(AssetMetadata metadata) : Asset(metadata)
    {
        // Import settings (populated from metadata during LoadAsync)
        public TextureSampling Sampling { get; private set; } = TextureSampling.Bilinear;
        public TextureDimension Dimension { get; private set; } = TextureDimension.Texture2D;
        public int MaxMipmap { get; private set; } = 12;
        public CubemapLayout CubemapLayout { get; private set; } = CubemapLayout.None;

        /// <summary>
        /// Reads import settings from the asset metadata. No image decoding is
        /// performed - pixel data is produced on demand by <see cref="GetPixelData"/>.
        /// </summary>
        public override Task<bool> LoadAsync()
        {
            if (IsLoaded) return Task.FromResult(true);

            try
            {
                Dictionary<string, object> props = Metadata.CustomProperties ?? [];

                if (props.TryGetValue("Sampling", out object? s))
                    Sampling = Enum.TryParse(s.ToString(), out TextureSampling samp) ? samp : TextureSampling.Bilinear;

                if (props.TryGetValue("TextureType", out object? t))
                    Dimension = t.ToString() switch
                    {
                        "Texture3D" => TextureDimension.Texture3D,
                        "Cubemap" => TextureDimension.Cubemap,
                        _ => TextureDimension.Texture2D
                    };

                if (props.TryGetValue("MaxMipmap", out object? m) && int.TryParse(m.ToString(), out int mip))
                    MaxMipmap = Math.Clamp(mip, 0, 16);

                if (props.TryGetValue("CubemapLayout", out object? layoutObj) &&
                    Enum.TryParse(layoutObj.ToString(), out CubemapLayout layout))
                    CubemapLayout = layout;

                // Force no mips for cubemaps
                if (Dimension == TextureDimension.Cubemap) MaxMipmap = 1;

                IsLoaded = true;
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Debug.Error($"Failed to load texture asset {Metadata.FileName}: {ex.Message}");
                IsLoaded = false;
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// Decodes the texture from disk and returns raw ARGB pixel data plus the
        /// source dimensions. The caller owns the returned array; nothing is cached
        /// on this asset, so each call re-reads the file (edits on disk are picked
        /// up on the next call). Safe to call from multiple threads - opens with
        /// <see cref="FileShare.Read"/> so parallel loads of different textures
        /// don't collide, and parallel loads of the same file are still permitted.
        /// </summary>
        /// <exception cref="FileNotFoundException">The source file is missing.</exception>
        /// <exception cref="InvalidOperationException">SkiaSharp failed to decode the image.</exception>
        public (uint[] Pixels, int Width, int Height) GetPixelData()
        {
            string fullPath = Path.Combine(AssetDatabase.ProjectPath, RelativePath);

            using FileStream stream = new(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using SKBitmap bitmap = SKBitmap.Decode(stream)
                ?? throw new InvalidOperationException($"Failed to decode image: {Metadata.FileName}");

            int w = bitmap.Width, h = bitmap.Height;
            SKColor[] skData = bitmap.Pixels;

            uint[] data = new uint[skData.Length];
            for (int i = 0; i < skData.Length; i++)
            {
                SKColor p = skData[i];
                data[i] = (uint)((p.Red << 24) | (p.Green << 16) | (p.Blue << 8) | p.Alpha);
            }
            return (data, w, h);
        }

        public override void Unload()
        {
            if (!IsLoaded) return;
            IsLoaded = false;
            // No CPU-side pixel data to release - nothing else to do here.
        }
    }
}
