//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DivisionEngine.Editor.ViewModels;
using DivisionEngine.MathLib;
using DivisionEngine.MathUtilities;
using DivisionEngine.Projects.Assets;
using Material.Icons;
using Material.Icons.Avalonia;
using System;
using System.Diagnostics;
using System.IO;

namespace DivisionEngine.Editor
{
    /// <summary>
    /// API for common Division-Avalonia UI utilities.
    /// </summary>
    internal static class EditorUI
    {
        /// <summary>
        /// Checks and sees if there is a parent TabItem to this control.
        /// </summary>
        /// <param name="control"></param>
        /// <returns></returns>
        public static TabItem? FindParentTabItem(Control? control)
        {
            Control? current = control;
            while (current != null)
            {
                if (current is TabItem tabItem) return tabItem;
                current = current.Parent as Control;
            }
            return null;
        }

        /// <summary>
        /// Opens a file.
        /// </summary>
        /// <param name="file">File to open</param>
        public static void OpenFile(FileInfo file)
        {
            try
            {
                if (!File.Exists(file.FullName))
                {
                    Debug.Warning($"File does not exist: {file.FullName}");
                    return;
                }
                Process.Start(new ProcessStartInfo
                {
                    FileName = file.FullName,
                    UseShellExecute = true  // This is often needed for file associations
                });
            }
            catch (Exception ex)
            {
                Debug.Warning($"Could not open file", ex);
            }
        }

        /// <summary>
        /// Similar to OpenFile, but works on assets instead.
        /// </summary>
        /// <param name="asset">Asset metadata to open</param>
        public static void OpenAsset(AssetMetadata asset)
        {
            try
            {
                if (!File.Exists(AssetDatabase.GetAssetFullPath(asset.ID)))
                {
                    Debug.Warning($"Asset does not exist: {asset.FileName}");
                    return;
                }
                Process.Start(new ProcessStartInfo
                {
                    FileName = AssetDatabase.GetAssetFullPath(asset.ID),
                    UseShellExecute = true  // This is often needed for file associations
                });
            }
            catch (Exception ex)
            {
                Debug.Warning($"Could not open file", ex);
            }
        }

        /// <summary>
        /// Similar to OpenFile, but works on assets instead.
        /// </summary>
        /// <param name="asset">Asset to open</param>
        public static void OpenAsset(Asset asset) => OpenAsset(asset.Metadata);

        /// <summary>
        /// Converts a number of bytes to a string representation.
        /// </summary>
        /// <param name="bytes">Byte amount</param>
        /// <param name="decimalPlaces">Number of decimal places to include</param>
        /// <returns>Formatted byte size string</returns>
        public static string FormatFileSize(long bytes, int decimalPlaces = 1)
        {
            if (bytes <= 0) return "0 B";
            string[] sizeSuffixes = ["B", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB"];

            // Use log to find the right unit index
            int unitIndex = (int)math.min(math.floor(MathUtilities.Math.Log(bytes, 1024)), sizeSuffixes.Length - 1);
            double size = bytes / math.pow(1024, unitIndex);
            string format = $"{{0:F{decimalPlaces}}} {{1}}";
            return string.Format(format, size, sizeSuffixes[unitIndex]);
        }

        /// <summary>
        /// Static method to create entities from context menu.
        /// </summary>
        /// <param name="entityType">Entity type ID (i.e. empty, roundedBox, Cone, TERRAIN)</param>
        public static void CreateEntityStatic(string entityType)
        {
            try
            {
                uint entityId = entityType.ToLower() switch
                {
                    "empty" => DefaultEntities.Empty(),
                    "emptytransform" => DefaultEntities.EmptyTransform(),
                    "camera" => DefaultEntities.Camera(),
                    "environment" => DefaultEntities.Environment(),
                    "directionallight" => DefaultEntities.DirectionalLight(),
                    "pointlight" => DefaultEntities.PointLight(),
                    "audiosource" => DefaultEntities.AudioSource(),
                    "audiolistener" => DefaultEntities.AudioListener(),
                    "sphere" => DefaultEntities.SDFSphere(),
                    "box" => DefaultEntities.SDFBox(),
                    "roundedbox" => DefaultEntities.SDFRoundedBox(),
                    "torus" => DefaultEntities.SDFTorus(),
                    "pyramid" => DefaultEntities.SDFPyramid(),
                    "plane" => DefaultEntities.SDFPlane(),
                    "cylinder" => DefaultEntities.SDFCylinder(),
                    "capsule" => DefaultEntities.SDFCapsule(),
                    "cone" => DefaultEntities.SDFCone(),
                    "terrain" => DefaultEntities.Terrain(),
                    _ => DefaultEntities.EmptyTransform()
                };
                PropertiesWindow.LoadEntityComponents(entityId); // Select entity when created
                Debug.Info($"Created {entityType} entity with ID: {entityId}");
            }
            catch (Exception ex)
            {
                Debug.Error($"Failed to create entity", ex);
            }
        }

        public static MenuItem CreateContextMenuItem(string text, MaterialIconKind icon, Action action, IBrush? foreground = null)
        {
            MenuItem menuItem = new MenuItem
            {
                Header = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new MaterialIcon
                        {
                            Kind = icon,
                            Width = 16,
                            Height = 16,
                            Foreground = foreground ?? Brushes.White,
                        },
                        new TextBlock
                        {
                            Text = text,
                            Foreground = foreground ?? Brushes.White,
                        }
                    }
                },
                Foreground = foreground ?? Brushes.White,
            };
            menuItem.Click += (s, e) => action();
            return menuItem;
        }

        /// <summary>
        /// Converts an asset type to a material icon kind.
        /// </summary>
        /// <param name="type">Asset type</param>
        /// <returns>Relevant asset icon</returns>
        public static MaterialIconKind GetIconForAssetType(AssetType type) => type switch
        {
            AssetType.Texture => MaterialIconKind.Image,
            AssetType.SDF => MaterialIconKind.CubeOutline,
            AssetType.Material => MaterialIconKind.Palette,
            AssetType.Script => MaterialIconKind.CodeBraces,
            AssetType.Audio => MaterialIconKind.Audio,
            AssetType.Font => MaterialIconKind.FormatFont,
            _ => MaterialIconKind.FileDocument,
        };

        /// <summary>
        /// Creates an Avalonia MaterialIcon for an asset type.
        /// </summary>
        /// <param name="type">Asset type for icon</param>
        /// <param name="size">Size of asset icon</param>
        /// <returns>MaterialIcon asset icon object</returns>
        public static MaterialIcon CreateAssetTypeIcon(AssetType type, double size)
        {
            MaterialIconKind iconKind = type switch
            {
                AssetType.Texture => MaterialIconKind.Image,
                AssetType.SDF => MaterialIconKind.CubeOutline,
                AssetType.Material => MaterialIconKind.Palette,
                AssetType.Script => MaterialIconKind.CodeBraces,
                AssetType.Audio => MaterialIconKind.Audio,
                AssetType.Font => MaterialIconKind.FormatFont,
                _ => MaterialIconKind.FileDocument,
            };
            float4 iconColor = type switch
            {
                AssetType.Texture => ColorPalette.SkyBlue,
                AssetType.SDF => ColorPalette.LightSeaGreen,
                AssetType.Material => ColorPalette.Orange,
                AssetType.Script => ColorPalette.Khaki,
                AssetType.Audio => ColorPalette.Coral,
                AssetType.Font => ColorPalette.Khaki,
                _ => ColorPalette.Gray,
            };
            return new MaterialIcon
            {
                Kind = iconKind,
                Width = size,
                Height = size,
                Foreground = EditorColor.FromColor(iconColor),
                Margin = new Thickness(0),
            };
        }
    }
}
