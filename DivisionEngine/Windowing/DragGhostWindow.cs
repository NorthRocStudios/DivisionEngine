//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE.txt file in the project root
// for full license terms.
//
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DivisionEngine.Editor.ViewModels;
using Material.Icons.Avalonia;

namespace DivisionEngine.Editor.Windowing
{
    /// <summary>
    /// A tiny always-on-top window that follows the cursor while a tab is dragged. It is
    /// deliberately a real window rather than an overlay inside the source window so the
    /// drag remains visible when the pointer leaves the window to float a tab.
    /// </summary>
    public sealed class DragGhostWindow : Window
    {
        /// <summary>Creates the ghost for one window view model.</summary>
        public DragGhostWindow(EditorWindowViewModel tab)
        {
            SystemDecorations = Avalonia.Controls.WindowDecorations.None;
            ShowInTaskbar = false;
            ShowActivated = false;
            CanResize = false;
            Topmost = true;
            Focusable = false;
            IsHitTestVisible = false;
            Width = 170;
            Height = 30;
            Background = Brushes.Transparent;
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent];

            StackPanel content = new()
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                VerticalAlignment = VerticalAlignment.Center,
            };
            content.Children.Add(new MaterialIcon
            {
                Kind = tab.Icon,
                Width = 12,
                Height = 12,
                Foreground = Brushes.LightGray,
                VerticalAlignment = VerticalAlignment.Center,
            });
            content.Children.Add(new TextBlock
            {
                Text = tab.Title,
                FontSize = 12,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 130,
            });

            Content = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(235, 32, 32, 32)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(200, 110, 110, 110)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8, 4),
                Child = content,
            };
        }

        /// <summary>Moves the ghost so it sits just under the cursor.</summary>
        public void MoveTo(PixelPoint screenPoint)
        {
            Position = new PixelPoint(screenPoint.X - 14, screenPoint.Y - 8);
        }
    }
}
