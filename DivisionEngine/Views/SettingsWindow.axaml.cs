//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using DivisionEngine.Editor.Settings;
using DivisionEngine.Settings;
using Material.Icons;
using Material.Icons.Avalonia;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DivisionEngine.Editor;

/// <summary>
/// Represents the settings window in the Division editor.
/// </summary>
public partial class SettingsWindow : EditorWindow
{
    private static readonly List<SettingsWindow?> currentWindows = [];

    private readonly StackPanel settingsPanel;
    private readonly ScrollViewer scrollViewer;
    private readonly StackPanel header;
    private readonly TextBlock headerText;

    public SettingsWindow()
    {
        InitializeComponent();

        // Panel
        settingsPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(5),
        };
        scrollViewer = new ScrollViewer
        {
            Content = settingsPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalAlignment = VerticalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top,
        };

        // Header
        Border separator = new Border
        {
            Background = EditorColor.FromRGB(68, 68, 68),
            Height = 1,
        };
        header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Background = EditorColor.FromRGB(28, 28, 28),
            VerticalAlignment = VerticalAlignment.Top,
        };
        headerText = new TextBlock
        {
            Text = "Settings",
            FontSize = 14,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            Margin = new Thickness(8, 5),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        header.Children.Add(headerText);

        // Assemble main layout
        Grid mainGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
            }
        };
        header.SetValue(Grid.RowProperty, 0);
        separator.SetValue(Grid.RowProperty, 1);
        scrollViewer.SetValue(Grid.RowProperty, 2);
        mainGrid.Children.Add(header);
        mainGrid.Children.Add(separator);
        mainGrid.Children.Add(scrollViewer);

        this.FindControl<Border>("MainBorder")!.Child = mainGrid;
        currentWindows.Add(this);
        LoadSettings(); // Load all settings
    }

    /// <summary>
    /// Assembles the layout of settings in the settings window.
    /// </summary>
    private void LoadSettings()
    {
        settingsPanel.Children.Clear();
        AddSectionHeader("Editor Settings", MaterialIconKind.Cog); // Editor Settings
        LoadEditorSettings();
        AddSectionHeader("Engine Settings", MaterialIconKind.Engine); // Engine Settings
        LoadEngineSettings();
        AddSectionHeader("Input Settings", MaterialIconKind.Keyboard); // Input Settings
        LoadInputSettings();
    }

    /// <summary>
    /// Builds editor settings layout.
    /// </summary>
    private void LoadEditorSettings()
    {
        EditorSettings settings = EditorSettings.Instance;
        AddBoolSetting("Auto Save", settings.AutoSave, val => settings.AutoSave = val);
        AddIntSetting("Auto Save Interval (frames)", settings.AutoSaveInterval, 30, 600, val => settings.AutoSaveInterval = val);
        AddIntSetting("Max Recent Projects", settings.MaxRecentProjects, 2, 20, val => settings.MaxRecentProjects = val);
        AddFloatSetting("Editor Handle Scale", settings.EditorHandleScale, 0.001f, 10f, val => settings.EditorHandleScale = val);
    }
    
    /// <summary>
    /// Builds engine settings layout
    /// </summary>
    private void LoadEngineSettings()
    {
        EngineSettings settings = EngineSettings.Instance;
        AddIntSetting("Resolution Width", settings.ResolutionWidth, 640, 7680, val => settings.ResolutionWidth = val);
        AddIntSetting("Resolution Height", settings.ResolutionHeight, 480, 4320, val => settings.ResolutionHeight = val);
        AddBoolSetting("Fullscreen", settings.Fullscreen, val => settings.Fullscreen = val);
        AddBoolSetting("VSync", settings.VSync, val => settings.VSync = val);
        AddBoolSetting("Checkerboard Rendering", settings.CheckerboardRendering, val => settings.CheckerboardRendering = val);
        AddIntSetting("Max FPS (0 = unlimited)", settings.MaxFPS, 0, 1024, val => settings.MaxFPS = val);
        AddBoolSetting("Auto Recompile On Script Change", settings.AutoRecompileOnScriptChange, val => settings.AutoRecompileOnScriptChange = val);
        AddBoolSetting("Reload World On Recompile", settings.ReloadWorldOnRecompile, val => settings.ReloadWorldOnRecompile = val);
    }

    /// <summary>
    /// Builds input settings layout.
    /// </summary>
    private void LoadInputSettings()
    {
        EngineSettings settings = EngineSettings.Instance;
        AddFloatSetting("Mouse Sensitivity", settings.MouseSensitivity, 0.01f, 20f, val => settings.MouseSensitivity = val);

        // Key bindings placeholder
        Border keyBindingsBorder = CreateSettingBorder();
        TextBlock keyBindingsText = new TextBlock
        {
            Text = "Key bindings can be configured in the Input Settings panel",
            FontSize = 11,
            Foreground = EditorColor.FromRGB(148, 148, 148),
            Padding = new Thickness(8, 6),
        };
        keyBindingsBorder.Child = keyBindingsText;
        settingsPanel.Children.Add(keyBindingsBorder);
    }

    /// <summary>
    /// Adds a setting section header to the settings window.
    /// </summary>
    /// <param name="title">Title of the settings group</param>
    /// <param name="iconKind">Icon to use for the section header</param>
    private void AddSectionHeader(string title, MaterialIconKind iconKind)
    {
        Border headerBorder = new Border
        {
            BorderThickness = new Thickness(0, 0, 1, 1),
            BorderBrush = EditorColor.FromRGB(17, 17, 17),
            Background = EditorColor.FromRGB(44, 44, 44),
            CornerRadius = new CornerRadius(4, 4, 0, 0),
            Margin = new Thickness(4, 8, 12, 0),
            Padding = new Thickness(8, 4),
        };
        DockPanel headerPanel = new DockPanel();
        MaterialIcon icon = new MaterialIcon
        {
            Kind = iconKind,
            Width = 16,
            Height = 16,
            Margin = new Thickness(0, 0, 6, 0),
            Foreground = EditorColor.FromRGB(148, 148, 148),
            VerticalAlignment = VerticalAlignment.Center,
        };
        TextBlock titleText = new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = EditorColor.FromRGB(220, 220, 220),
            VerticalAlignment = VerticalAlignment.Center,
        };

        DockPanel.SetDock(icon, Dock.Left);
        DockPanel.SetDock(titleText, Dock.Left);
        headerPanel.Children.Add(icon);
        headerPanel.Children.Add(titleText);
        headerBorder.Child = headerPanel;
        settingsPanel.Children.Add(headerBorder);
    }

    private void AddBoolSetting(string label, bool initialValue, Action<bool> onChanged)
    {
        Border settingBorder = CreateSettingBorder();
        DockPanel panel = new DockPanel();
        TextBlock labelText = new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = EditorColor.FromRGB(200, 200, 200),
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(labelText, Dock.Left);
        CheckBox checkBox = new CheckBox
        {
            IsChecked = initialValue,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        checkBox.IsCheckedChanged += (_, _) => onChanged(checkBox.IsChecked ?? false);
        DockPanel.SetDock(checkBox, Dock.Right);

        panel.Children.Add(labelText);
        panel.Children.Add(checkBox);
        settingBorder.Child = panel;
        settingsPanel.Children.Add(settingBorder);
    }

    private void AddFloatSetting(string label, float initialValue, float min, float max, Action<float> onChanged)
    {
        Border settingBorder = CreateSettingBorder();
        DockPanel panel = new DockPanel();
        TextBlock labelText = new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = EditorColor.FromRGB(200, 200, 200),
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(labelText, Dock.Left);
        NumericUpDown numBox = CreateFloatNumericBox(initialValue, onChanged);
        numBox.Minimum = (decimal)min;
        numBox.Maximum = (decimal)max;
        numBox.HorizontalAlignment = HorizontalAlignment.Right;
        numBox.MinWidth = 100;
        DockPanel.SetDock(numBox, Dock.Right);

        panel.Children.Add(labelText);
        panel.Children.Add(numBox);
        settingBorder.Child = panel;
        settingsPanel.Children.Add(settingBorder);
    }

    private void AddIntSetting(string label, int initialValue, int min, int max, Action<int> onChanged)
    {
        Border settingBorder = CreateSettingBorder();
        DockPanel panel = new DockPanel();
        TextBlock labelText = new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = EditorColor.FromRGB(200, 200, 200),
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(labelText, Dock.Left);
        NumericUpDown numBox = CreateIntegerNumericBox(initialValue, onChanged);
        numBox.Minimum = min;
        numBox.Maximum = max;
        numBox.HorizontalAlignment = HorizontalAlignment.Right;
        numBox.MinWidth = 100;
        DockPanel.SetDock(numBox, Dock.Right);

        panel.Children.Add(labelText);
        panel.Children.Add(numBox);
        settingBorder.Child = panel;
        settingsPanel.Children.Add(settingBorder);
    }

    private void AddEnumSetting(string label, object currentValue, string[] displayNames, Action<string> onChanged)
    {
        Border settingBorder = CreateSettingBorder();
        DockPanel panel = new DockPanel();
        TextBlock labelText = new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = EditorColor.FromRGB(200, 200, 200),
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(labelText, Dock.Left);
        ComboBox comboBox = new ComboBox
        {
            ItemsSource = displayNames,
            SelectedItem = GetCurrentDisplayName(currentValue, displayNames),
            Width = 120,
            Height = 24,
            FontSize = 11,
            Background = EditorColor.FromRGB(32, 32, 32),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(3),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        comboBox.SelectionChanged += (_, _) =>
        {
            if (comboBox.SelectedItem is string selected) onChanged(selected);
        };
        DockPanel.SetDock(comboBox, Dock.Right);

        panel.Children.Add(labelText);
        panel.Children.Add(comboBox);
        settingBorder.Child = panel;
        settingsPanel.Children.Add(settingBorder);
    }

    private static string GetCurrentDisplayName(object currentValue, string[] displayNames)
    {
        if (currentValue is int intVal && intVal < displayNames.Length) return displayNames[intVal];
        if (currentValue is string strVal && displayNames.Contains(strVal)) return strVal;
        return displayNames[0];
    }

    private static Border CreateSettingBorder()
    {
        Border border = new Border
        {
            BorderThickness = new Thickness(0, 0, 1, 1),
            BorderBrush = EditorColor.FromRGB(10, 10, 10),
            Background = EditorColor.FromRGB(20, 20, 20),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(4, 2, 12, 2),
            Padding = new Thickness(8, 6),
        };
        border.PointerEntered += (_, _) =>
        {
            border.BorderThickness = new Thickness(0, 0, 2, 2);
            border.BorderBrush = EditorColor.FromRGB(12, 12, 12);
            border.Background = EditorColor.FromRGB(24, 24, 24);
        };
        border.PointerExited += (_, _) =>
        {
            border.BorderThickness = new Thickness(0, 0, 1, 1);
            border.BorderBrush = EditorColor.FromRGB(10, 10, 10);
            border.Background = EditorColor.FromRGB(20, 20, 20);
        };
        return border;
    }

    private static NumericUpDown CreateFloatNumericBox(float initialVal, Action<float> onValueChanged)
    {
        NumericUpDown numericBox = new NumericUpDown
        {
            Value = (decimal)initialVal,
            Increment = (decimal)Math.Max(initialVal / 10f, 0.1f),
            FontSize = 11,
            AllowSpin = true,
            ParsingNumberStyle = NumberStyles.Float,
            Background = EditorColor.FromRGB(32, 32, 32),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4),
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            FormatString = "0.##",
            Width = 70,
        };
        numericBox.ValueChanged += (s, e) =>
        {
            try
            {
                onValueChanged((float)(double)numericBox.Value);
            }
            catch (Exception ex) { Debug.Error("Numeric Box Error", ex); }
        };
        return numericBox;
    }

    private static NumericUpDown CreateIntegerNumericBox(int initialVal, Action<int> onValueChanged)
    {
        NumericUpDown numericBox = new NumericUpDown
        {
            Value = initialVal,
            Increment = 1,
            FontSize = 11,
            AllowSpin = true,
            ParsingNumberStyle = NumberStyles.Integer,
            Background = EditorColor.FromRGB(32, 32, 32),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4),
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Width = 70,
        };
        numericBox.ValueChanged += (s, e) =>
        {
            try
            {
                onValueChanged((int)numericBox.Value);
            }
            catch (Exception ex) { Debug.Error("Numeric Box Error", ex); }
        };
        return numericBox;
    }

    /// <summary>
    /// Refreshes all settings windows with new data.
    /// </summary>
    public static void RefreshAll()
    {
        ValidateSettingsWindows();
        foreach (SettingsWindow? window in currentWindows)
            Dispatcher.UIThread.Post(() => window!.LoadSettings());
    }

    /// <summary>
    /// Makes sure all settings windows in current list are active.
    /// </summary>
    private static void ValidateSettingsWindows()
    {
        foreach (SettingsWindow? window in currentWindows.ToArray())
            if (window == null || !window.IsLoaded)
                currentWindows.Remove(window);
    }
}