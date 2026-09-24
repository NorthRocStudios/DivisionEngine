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
using Avalonia.Threading;
using DivisionEngine.Editor.Controls;
using DivisionEngine.MathLib;
using DivisionEngine.Rendering;
using DivisionEngine.Systems;
using System;
using System.Collections.Generic;

namespace DivisionEngine.Editor;

/// <summary>
/// Window responsible for displaying the render target, and renderer controls.
/// </summary>
public partial class EnvironmentWindow : EditorWindow
{
    private static readonly List<EnvironmentWindow?> currentWindows = [];

    private readonly DockPanel mainPanel;
    private readonly StackPanel headerPanel;
    private readonly ComboBox debugMode;
    private readonly CheckBox? iconsToggle;
    private readonly DispatcherTimer fpsUpdateTimer;

    public readonly DivisionRenderView renderVisualizerFrame;
    public readonly TextBlock widthHeightText;

    public EnvironmentWindow()
    {
        InitializeComponent();

        // Create main dock panel
        mainPanel = new DockPanel
        {
            Background = Brushes.Transparent,
        };
        headerPanel = new StackPanel
        {
            Background = EditorColor.FromRGB(28, 28, 28),
            Orientation = Orientation.Horizontal,
            Height = 25,
            VerticalAlignment = VerticalAlignment.Center,
        };
        TextBlock debugModeText = new TextBlock
        {
            Text = "Debug Mode",
            FontSize = 12,
            FontWeight = FontWeight.Regular,
            Foreground = EditorColor.FromRGB(128, 128, 128),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(4, 2, 4, 2),
        };
        debugMode = new ComboBox
        {
            ItemsSource = new[] { "None", "Depth", "World Normals", "Object ID", "Ray Steps", "Shadows", "BRDF", "Specular", "Diffuse", "Mip Cascades" },
            SelectedIndex = 0,
            FontSize = 12,
            FontWeight = FontWeight.Regular,
            BorderThickness = new Thickness(0),
            Background = EditorColor.FromRGB(17, 17, 17),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(4, 2, 4, 2),
        };
        debugMode.SelectionChanged += (e, s) => UpdateRendererDebugMode();

        Border iconsSeparator = new Border
        {
            Background = EditorColor.FromRGB(68, 68, 68),
            Width = 1,
            Height = 16,
            Margin = new Thickness(4, 0),
        };
        TextBlock iconsText = new TextBlock
        {
            Text = "Show Icons",
            FontSize = 12,
            FontWeight = FontWeight.Regular,
            Foreground = EditorColor.FromRGB(128, 128, 128),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 2, 2, 2),
        };

        iconsToggle = new CheckBox
        {
            IsChecked = IconRendererSystem.Enabled,
            Margin = new Thickness(2, 2, 8, 2),
            VerticalAlignment = VerticalAlignment.Center,
        };
        iconsToggle.IsCheckedChanged += (e, s) =>
        {
            IconRendererSystem.Enabled = iconsToggle.IsChecked ?? true;
            if (!IconRendererSystem.Enabled) RenderPipeline.Instance?.ClearIcons();
        };

        int width = 0, height = 0;
        if (App.Renderer != null && App.Renderer.RendererWindow != null)
        {
            width = App.Renderer.RendererWindow.Size.X;
            height = App.Renderer.RendererWindow.Size.Y;
        }
        widthHeightText = new TextBlock
        {
            Text = $"(Width {width}px,  Height {height}px,  FPS {math.round(TimeSystem.FPS)})",
            FontSize = 12,
            FontWeight = FontWeight.Regular,
            Foreground = EditorColor.FromRGB(128, 128, 128),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(4, 2, 4, 2),
        };

        // Add controls to header
        headerPanel.Children.Add(debugModeText);
        headerPanel.Children.Add(debugMode);
        headerPanel.Children.Add(iconsSeparator);
        headerPanel.Children.Add(iconsText);
        headerPanel.Children.Add(iconsToggle);
        headerPanel.Children.Add(widthHeightText);
        DockPanel.SetDock(headerPanel, Dock.Top);
        mainPanel.Children.Add(headerPanel);

        Border separator = new Border
        {
            Background = EditorColor.FromRGB(68, 68, 68),
            Height = 1,
        };
        DockPanel.SetDock(separator, Dock.Top);
        mainPanel.Children.Add(separator);

        // Set up FPS update timer
        fpsUpdateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100) // Update 10 times per second for smooth display
        };
        fpsUpdateTimer.Tick += (s, e) => UpdateDisplayText();
        fpsUpdateTimer.Start();

        renderVisualizerFrame = new DivisionRenderView
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        renderVisualizerFrame.SizeChanged += (s, e) =>
        {
            UpdateSizeAndFPS();
        };
        mainPanel.Children.Add(renderVisualizerFrame);

        this.FindControl<Border>("MainBorder")!.Child = mainPanel;
        currentWindows.Add(this);
    }

    /// <summary>
    /// Syncs the environment window tool values to the renderer window.
    /// </summary>
    public static void SyncToolValuesToRenderer()
    {
        for (int i = 0; i < currentWindows.Count; i++)
            currentWindows[i]?.UpdateRendererDebugMode();
    }

    private void UpdateDisplayText()
    {
        if (widthHeightText != null && renderVisualizerFrame != null)
        {
            Rect bounds = renderVisualizerFrame.Bounds;
            int width = (int)bounds.Width;
            int height = (int)bounds.Height;
            double fps = TimeSystem.FPS;

            widthHeightText.Text = $"(Width {width}px,  Height {height}px,  FPS {math.round(fps)})";
        }
    }

    public void UpdateSizeAndFPS()
    {
        if (renderVisualizerFrame != null)
        {
            // Get actual rendered size (considering DPI scaling)
            Rect bounds = renderVisualizerFrame.Bounds;
            TopLevel? topLevel = TopLevel.GetTopLevel(renderVisualizerFrame);
            double dpiScale = topLevel?.RenderScaling ?? 1.0;

            int physicalWidth = (int)(bounds.Width * dpiScale);
            int physicalHeight = (int)(bounds.Height * dpiScale);

            // Update the renderer's embedded viewport size
            if (App.Renderer != null && App.Renderer.Mode == RenderPipeline.RunMode.Embedded)
                App.Renderer.SetEmbeddedViewportSize(physicalWidth, physicalHeight);

            // Update display text
            UpdateDisplayText();
        }
    }

    /// <summary>
    /// Updates the render window's debug mode.
    /// </summary>
    private void UpdateRendererDebugMode()
    {
        RenderPipeline.DebugMode mode = (RenderPipeline.DebugMode)debugMode.SelectedIndex;
        if (App.Renderer != null) App.Renderer!.CurrentDebugMode = mode;
    }

    /// <summary>
    /// Makes sure all environment windows in current list are active.
    /// </summary>
    private static void ValidateEnvironmentWindows()
    {
        foreach (EnvironmentWindow? window in currentWindows.ToArray()) // Don't forget to create iterator copy
            if (window == null || !window.IsLoaded)
                currentWindows.Remove(window);
    }

    /// <summary>
    /// Gets the first active environment window.
    /// </summary>
    public static EnvironmentWindow? GetFirstActiveWindow()
    {
        ValidateEnvironmentWindows();
        return currentWindows.Count > 0 ? currentWindows[0] : null;
    }

    /// <summary>
    /// Gets all active environment windows.
    /// </summary>
    public static EnvironmentWindow?[] GetActiveWindows()
    {
        ValidateEnvironmentWindows();
        return [.. currentWindows];
    }
}
