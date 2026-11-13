//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using Avalonia.Media;
using DivisionEngine.Nodes;
using System;
using System.Collections.Concurrent;

namespace DivisionEngine.Editor.ViewModels
{
    /// <summary>
    /// Colours, brushes and layout constants shared by the node editor view models and
    /// the graph canvas. Keeping them in one place makes custom node views trivial to
    /// theme consistently.
    /// </summary>
    public static class NodeEditorTheme
    {
        /// <summary>Default width of a node, in graph units.</summary>
        public const double NodeWidth = 280;

        /// <summary>Height of a node header, in graph units.</summary>
        public const double HeaderHeight = 30;

        /// <summary>Height of one port row, in graph units.</summary>
        public const double PortRowHeight = 24;

        /// <summary>Height of one inspector-style property row inside a node, in graph units.</summary>
        public const double PropertyRowHeight = 24;

        /// <summary>Padding below the last content row.</summary>
        public const double ContentBottomPadding = 6;

        /// <summary>Minimum zoom factor.</summary>
        public const double MinZoom = 0.2;

        /// <summary>Maximum zoom factor.</summary>
        public const double MaxZoom = 2.5;

        /// <summary>Graph background.</summary>
        public static IBrush CanvasBackground { get; } = Brush("#1E1E1E");

        /// <summary>Minor grid dot colour.</summary>
        public static IBrush GridDot { get; } = Brush("#2A2A2A");

        /// <summary>Major grid dot colour.</summary>
        public static IBrush GridDotMajor { get; } = Brush("#333333");

        /// <summary>Node body background.</summary>
        public static IBrush NodeBody { get; } = Brush("#2B2B2B");

        /// <summary>Node header background.</summary>
        public static IBrush NodeHeader { get; } = Brush("#363636");

        /// <summary>Node border.</summary>
        public static IBrush NodeBorder { get; } = Brush("#111111");

        /// <summary>Hovered node border.</summary>
        public static IBrush NodeBorderHover { get; } = Brush("#555555");

        /// <summary>Selected node border.</summary>
        public static IBrush NodeBorderSelected { get; } = Brush("#6D8CA8");

        /// <summary>Node drop shadow.</summary>
        public static IBrush NodeShadow { get; } = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0));

        /// <summary>Primary text.</summary>
        public static IBrush TextPrimary { get; } = Brush("#DDDDDD");

        /// <summary>Secondary text.</summary>
        public static IBrush TextSecondary { get; } = Brush("#AAAAAA");

        /// <summary>Dimmed text.</summary>
        public static IBrush TextDim { get; } = Brush("#777777");

        /// <summary>Value field background.</summary>
        public static IBrush FieldBackground { get; } = Brush("#1C1C1C");

        /// <summary>Value field border.</summary>
        public static IBrush FieldBorder { get; } = Brush("#4A4A4A");

        /// <summary>Accent used for marquee selection and highlights.</summary>
        public static IBrush Selection { get; } = Brush("#3F6E9E");

        /// <summary>Accepted-connection glow colour.</summary>
        public static IBrush ConnectionValid { get; } = Brush("#7FA870");

        /// <summary>Default connection colour.</summary>
        public static IBrush Connection { get; } = Brush("#8A8A8A");

        /// <summary>Pending / invalid connection colour.</summary>
        public static IBrush ConnectionInvalid { get; } = Brush("#C06060");

        /// <summary>Toolbar background.</summary>
        public static IBrush ToolbarBackground { get; } = Brush("#3A3A3A");

        /// <summary>Toolbar border.</summary>
        public static IBrush ToolbarBorder { get; } = Brush("#222222");

        /// <summary>Inspector background.</summary>
        public static IBrush InspectorBackground { get; } = Brush("#252525");

        /// <summary>Status bar background.</summary>
        public static IBrush StatusBackground { get; } = Brush("#303030");

        /// <summary>Default typeface used by custom node previews.</summary>
        public static Typeface Typeface { get; } = new(FontFamily.Default);

        /// <summary>Pen used to draw node borders.</summary>
        public static IPen NodeBorderPen { get; } = new Pen(NodeBorder, 1);

        private static readonly ConcurrentDictionary<NodeValueKind, IBrush> PortBrushes = new();
        private static readonly ConcurrentDictionary<string, IBrush> CategoryBrushes = new(StringComparer.OrdinalIgnoreCase);
        private static readonly IBrush FallbackPortBrush = Brush("#999999");
        private static readonly IBrush FallbackCategoryBrush = Brush("#7A7A7A");

        /// <summary>
        /// Returns the semantic colour for a port kind.
        /// </summary>
        /// <param name="kind">Port value kind.</param>
        public static IBrush GetPortBrush(NodeValueKind kind)
            => PortBrushes.GetOrAdd(kind, static k => k switch
            {
                NodeValueKind.Float => Brush("#A8C46A"),
                NodeValueKind.Int => Brush("#7FB7A9"),
                NodeValueKind.Bool => Brush("#C06060"),
                NodeValueKind.String => Brush("#B08BC0"),
                NodeValueKind.Vector2 => Brush("#7FA6B0"),
                NodeValueKind.Vector3 => Brush("#7F9EC0"),
                NodeValueKind.Vector4 => Brush("#B08BC0"),
                NodeValueKind.Color => Brush("#C7AE7A"),
                NodeValueKind.Sdf => Brush("#C08A55"),
                NodeValueKind.Audio => Brush("#B57A8C"),
                NodeValueKind.Material => Brush("#C06060"),
                NodeValueKind.Texture => Brush("#7FA6B0"),
                NodeValueKind.Object => Brush("#999999"),
                _ => FallbackPortBrush,
            });

        /// <summary>
        /// Returns the accent colour for a node category.
        /// </summary>
        /// <param name="category">Top level menu category.</param>
        public static IBrush GetCategoryBrush(string? category)
            => CategoryBrushes.GetOrAdd(category ?? string.Empty, static value => value.ToLowerInvariant() switch
            {
                "input" => Brush("#6FA89A"),
                "math" => Brush("#6F8FB0"),
                "sdf" => Brush("#C08A55"),
                "output" => Brush("#9A7FB5"),
                "audio" => Brush("#B57A8C"),
                "values" => Brush("#6FA89A"),
                "texture" => Brush("#7FA6B0"),
                "material" => Brush("#C7AE7A"),
                _ => FallbackCategoryBrush,
            });

        /// <summary>
        /// Creates a solid brush from a hex colour string.
        /// </summary>
        /// <param name="hex">RGB or ARGB hex string, with or without a leading '#'.</param>
        public static IBrush Brush(string hex)
        {
            string value = hex.TrimStart('#');
            Color color = value.Length switch
            {
                8 => Color.FromArgb(
                    Convert.ToByte(value[..2], 16), Convert.ToByte(value[2..4], 16),
                    Convert.ToByte(value[4..6], 16), Convert.ToByte(value[6..8], 16)),
                6 => Color.FromRgb(
                    Convert.ToByte(value[..2], 16), Convert.ToByte(value[2..4], 16),
                    Convert.ToByte(value[4..6], 16)),
                _ => Colors.Magenta,
            };
            return new SolidColorBrush(color);
        }
    }
}
