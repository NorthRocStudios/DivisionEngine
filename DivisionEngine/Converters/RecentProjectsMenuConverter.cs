//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE file in the project root
// for full license terms.
//
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using Material.Icons.Avalonia;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace DivisionEngine.Editor.Converters
{
    /// <summary>
    /// Converts a list of recent project paths and a command into menu items for display.
    /// </summary>
    internal class RecentProjectsMenuConverter : IMultiValueConverter
    {
        public static readonly RecentProjectsMenuConverter Instance = new();

        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count < 2 || values[0] is not IEnumerable<string> projects || values[1] is not IRelayCommand command)
                return new List<MenuItem>();

            List<MenuItem> menuItems = [];
            foreach (string project in projects)
            {
                if (string.IsNullOrEmpty(project)) continue;
                MenuItem menuItem = new MenuItem
                {
                    Foreground = Brushes.White,
                    Command = command,
                    CommandParameter = project,
                    DataContext = values[1], // Ensure command has proper context
                };

                StackPanel displayPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Margin = new Thickness(2, 2, 10, 2),
                };
                displayPanel.Children.Add(new MaterialIcon
                {
                    Kind = Material.Icons.MaterialIconKind.Folder,
                    Width = 16,
                    Height = 16,
                    Foreground = Brushes.Gold,
                    VerticalAlignment = VerticalAlignment.Center,
                });
                StackPanel textPanel = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Spacing = 0,
                };

                string projectName = Path.GetFileName(project);
                string projectDir = Path.GetDirectoryName(project) ?? "";

                textPanel.Children.Add(new TextBlock
                {
                    Text = projectName,
                    FontWeight = FontWeight.SemiBold,
                    FontSize = 12,
                    Foreground = Brushes.White
                });
                textPanel.Children.Add(new TextBlock
                {
                    Text = projectDir,
                    FontSize = 10,
                    Foreground = Brushes.Gray,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 300
                });
                displayPanel.Children.Add(textPanel);
                menuItem.Header = displayPanel;
                menuItems.Add(menuItem);
            }
            return menuItems;
        }
    }
}
