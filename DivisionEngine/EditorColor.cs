//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using Avalonia.Media;
using System;
using Color = Avalonia.Media.Color;

namespace DivisionEngine.Editor
{
    /// <summary>
    /// A class to help with creating color brushes.
    /// </summary>
    internal static class EditorColor
    {
        /// <summary>
        /// Creates a new color from 3 RGB bytes.
        /// </summary>
        /// <param name="r">Red value from 0-255</param>
        /// <param name="g">Green value from 0-255</param>
        /// <param name="b">Blue value from 0-255</param>
        /// <returns>New SolidColorBrush with selected RGB values</returns>
        public static SolidColorBrush FromRGB(byte r, byte g, byte b) => new SolidColorBrush(Color.FromRgb(r, g, b));

        /// <summary>
        /// Creates a new color from 3 RGB floats.
        /// </summary>
        /// <param name="r">Red value from 0-1</param>
        /// <param name="g">Green value from 0-1</param>
        /// <param name="b">Blue value from 0-1</param>
        /// <returns>New SolidColorBrush with selected RGB values</returns>
        public static SolidColorBrush FromRGB(float r, float g, float b) => new SolidColorBrush(Color.FromRgb(
            Convert.ToByte(r * 255f),
            Convert.ToByte(g * 255f),
            Convert.ToByte(b * 255f)));

        /// <summary>
        /// Creates a new color from 4 RGBA bytes.
        /// </summary>
        /// <param name="r">Red value from 0-255</param>
        /// <param name="g">Green value from 0-255</param>
        /// <param name="b">Blue value from 0-255</param>
        /// <param name="a">Blue value from 0-255</param>
        /// <returns>New SolidColorBrush with selected RGBA values</returns>
        public static SolidColorBrush FromRGBA(byte r, byte g, byte b, byte a) => new SolidColorBrush(Color.FromArgb(a, r, g, b));

        /// <summary>
        /// Creates a new color from 4 RGBA floats.
        /// </summary>
        /// <param name="r">Red value from 0-1</param>
        /// <param name="g">Green value from 0-1</param>
        /// <param name="b">Blue value from 0-1</param>
        /// <param name="a">Blue value from 0-1</param>
        /// <returns>New SolidColorBrush with selected RGBA values</returns>
        public static SolidColorBrush FromRGBA(float r, float g, float b, float a) => new SolidColorBrush(Color.FromArgb(
            Convert.ToByte(a * 255f),
            Convert.ToByte(r * 255f),
            Convert.ToByte(g * 255f),
            Convert.ToByte(b * 255f)));

        /// <summary>
        /// Creates a new color from a float4 color vector.
        /// </summary>
        /// <param name="color">Float4 color vector to apply to brush</param>
        /// <returns>New SolidColorBrush with selected RGBA values</returns>
        public static SolidColorBrush FromColor(float4 color) => new SolidColorBrush(Color.FromArgb(
            Convert.ToByte(color.w * 255f),
            Convert.ToByte(color.x * 255f),
            Convert.ToByte(color.y * 255f),
            Convert.ToByte(color.z * 255f)));

        /// <summary>
        /// Creates a new color from a float3 color vector.
        /// </summary>
        /// <param name="color">Float3 color vector to apply to brush</param>
        /// <returns>New SolidColorBrush with selected RGB values</returns>
        public static SolidColorBrush FromColor(float3 color) => new SolidColorBrush(Color.FromRgb(
            Convert.ToByte(color.x * 255f),
            Convert.ToByte(color.y * 255f),
            Convert.ToByte(color.z * 255f)));

        /// <summary>
        /// Converts a hex string starting with '#' to a color.
        /// </summary>
        /// <param name="hex">Input hex string</param>
        /// <returns>Solid color output</returns>
        public static SolidColorBrush FromHex(string hex)
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 6)
            {
                int rgb = Convert.ToInt32(hex, 16);
                int r = (rgb >> 16) & 0xFF;
                int g = (rgb >> 8) & 0xFF;
                int b = rgb & 0xFF;
                return new SolidColorBrush(Color.FromRgb(
                    Convert.ToByte(r),
                    Convert.ToByte(g),
                    Convert.ToByte(b)));
            }
            else if (hex.Length == 8)
            {
                int argb = Convert.ToInt32(hex, 16);
                int a = (argb >> 24) & 0xFF;
                int r = (argb >> 16) & 0xFF;
                int g = (argb >> 8) & 0xFF;
                int b = argb & 0xFF;
                return new SolidColorBrush(Color.FromArgb(
                    Convert.ToByte(a),
                    Convert.ToByte(r),
                    Convert.ToByte(g),
                    Convert.ToByte(b)));
            }
            return new SolidColorBrush(new Color(0, 0, 0, 0));
        }
    }
}
