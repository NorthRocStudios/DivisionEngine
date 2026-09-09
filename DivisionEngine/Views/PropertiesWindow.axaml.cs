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
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DivisionEngine.Components;
using DivisionEngine.Components.FieldAttributes;
using DivisionEngine.Components.Lights;
using DivisionEngine.Components.SDFs.Effects;
using DivisionEngine.Editor.Systems;
using DivisionEngine.Editor.Undo;
using DivisionEngine.MathLib;
using DivisionEngine.MathUtilities;
using DivisionEngine.Projects;
using DivisionEngine.Projects.Assets;
using DivisionEngine.Systems;
using Material.Icons;
using Material.Icons.Avalonia;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Button = Avalonia.Controls.Button;
using Environment = DivisionEngine.Components.Environment;
using Transform = DivisionEngine.Components.Transform;

namespace DivisionEngine.Editor;

/// <summary>
/// Represents the properties window in the Division editor.
/// </summary>
public partial class PropertiesWindow : EditorWindow
{
    private static readonly List<PropertiesWindow?> currentWindows = [];

    private readonly StackPanel propertiesPanel;
    private readonly ScrollViewer scrollViewer;
    private readonly TabControl worldTabs;
    private readonly StackPanel statsPanel, environmentPanel, renderingPanel;
    private readonly StackPanel header;
    private readonly TextBlock headerText;
    private readonly Button addComponentButton;

    private object? currentSelection;

    // Keyed by (entity, component type) so a field's "Reset to Default" can rebuild exactly
    // the card it lives in — whether that's the selected entity's panel or a World-view tab.
    private readonly Dictionary<(uint EntityId, Type CompType), StackPanel> componentFieldPanels = [];

    // Static dictionary to persist expanded state across all instances and rebuilds
    private static readonly Dictionary<string, bool> cardExpandedState = [];
    private static readonly Lock stateLock = new();

    // Rendering-info tab: live system stats, refreshed on a timer while the World view is open.
    private DispatcherTimer? renderInfoRefreshTimer;
    private TextBlock? renderInfoTextureText, renderInfoSdfText, renderInfoLightText;

    /// <summary>
    /// Loads this entity when the properties window is opened.
    /// </summary>
    private static uint LastSelected { get; set; } = uint.MaxValue;

    public PropertiesWindow()
    {
        InitializeComponent();
        currentSelection = null;

        propertiesPanel = new StackPanel { Margin = new Thickness(5) };
        scrollViewer = new ScrollViewer
        {
            Content = propertiesPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalAlignment = VerticalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top,
            Background = EditorColor.FromRGB(34, 34, 34),
        };

        TabItem statsTab = CreateWorldTab("World Stats", out statsPanel);
        TabItem envTab = CreateWorldTab("Environment", out environmentPanel);
        TabItem renderTab = CreateWorldTab("Rendering", out renderingPanel);
        worldTabs = new TabControl
        { 
            IsVisible = false,
            Items = { statsTab, envTab, renderTab },
            Background = EditorColor.FromRGB(34, 34, 34),
        };

        Panel contentHost = new() { Children = { scrollViewer, worldTabs } };

        // Header
        Border separator = new() { Background = EditorColor.FromRGB(68, 68, 68), Height = 1 };
        header = new StackPanel { Orientation = Orientation.Horizontal, Background = EditorColor.FromRGB(28, 28, 28), 
            VerticalAlignment = VerticalAlignment.Top };
        headerText = new TextBlock { Text = "No Selection", FontSize = 12, FontWeight = FontWeight.Bold, 
            Foreground = Brushes.White, Margin = new Thickness(5), HorizontalAlignment = HorizontalAlignment.Left };
        header.Children.Add(headerText);

        // Footer (add component button)
        Border separator2 = new() { Background = EditorColor.FromRGB(68, 68, 68), Height = 1 };
        DockPanel buttonContent = new() { VerticalAlignment = VerticalAlignment.Stretch };
        MaterialIcon buttonIcon = new() { Kind = MaterialIconKind.BoxAdd, Margin = new Thickness(4), 
            Foreground = EditorColor.FromRGB(200, 255, 200), VerticalAlignment = VerticalAlignment.Center };
        TextBlock buttonText = new() { Text = "Add Component", VerticalAlignment = VerticalAlignment.Center, 
            HorizontalAlignment = HorizontalAlignment.Center };
        DockPanel.SetDock(buttonIcon, Dock.Left);
        DockPanel.SetDock(buttonText, Dock.Top);
        buttonContent.Children.Add(buttonIcon);
        buttonContent.Children.Add(buttonText);
        addComponentButton = new Button
        {
            Content = buttonContent,
            FontSize = 14,
            FontWeight = FontWeight.Medium,
            Foreground = EditorColor.FromRGB(200, 200, 200),
            Background = EditorColor.FromRGB(20, 20, 20),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(3),
            Height = 26,
            Margin = new Thickness(12, 5),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        Flyout addComponentFlyout = new() { Placement = PlacementMode.Top, ShowMode = FlyoutShowMode.Standard, Content = CreateAddComponentMenu() };
        addComponentButton.Click += (_, _) => addComponentFlyout.ShowAt(addComponentButton);

        Grid mainGrid = new()
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
            }
        };
        Grid.SetRow(header, 0);
        Grid.SetRow(separator, 1);
        Grid.SetRow(contentHost, 2);
        Grid.SetRow(separator2, 3);
        Grid.SetRow(addComponentButton, 4);
        mainGrid.Children.Add(header);
        mainGrid.Children.Add(separator);
        mainGrid.Children.Add(contentHost);
        mainGrid.Children.Add(separator2);
        mainGrid.Children.Add(addComponentButton);
        this.FindControl<Border>("MainBorder")!.Child = mainGrid;
        currentWindows.Add(this);

        Unloaded += (_, _) => renderInfoRefreshTimer?.Stop();

        if (W.EntityExists(LastSelected)) DisplayEntityComponents(LastSelected);
        else CreateWorldEditor(WorldManager.CurrentWorld);

        Selection.OnSelectionChanged += OnSelectedObject;
    }

    // Add this method to handle asset selection
    private void OnSelectedObject(object? selection)
    {
        if (selection == null)
        {
            CreateWorldEditor(WorldManager.CurrentWorld);
            return;
        }

        if (Selection.SelectedType == SelectionType.Entity && selection is uint entityId) LoadEntityComponents(entityId);
        else if (Selection.SelectedType == SelectionType.Asset && selection is string assetId) DisplayAssetProperties(assetId);
        else if (selection is AssetMetadata assetMeta) DisplayAssetProperties(assetMeta.ID);
        else CreateWorldEditor(WorldManager.CurrentWorld);
    }

    private static TabItem CreateWorldTab(string title, out StackPanel content)
    {
        content = new StackPanel
        {
            Margin = new Thickness(0, 4, 0, 4),
            Background = EditorColor.FromRGB(34, 34, 34),
        };
        ScrollViewer scrollViewer = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = EditorColor.FromRGB(34, 34, 34),
        };
        return new TabItem { Header = title, Content = scrollViewer };
    }

    /// <summary>
    /// Gets all active properties windows.
    /// </summary>
    public static List<PropertiesWindow?> GetCurrentWindows()
    {
        ValidatePropertiesWindows();
        return currentWindows;
    }

    #region addComponentMenu

    private StackPanel CreateAddComponentMenu()
    {
        TextBox searchBox = new()
        {
            Classes = { "field-editor" },
            InnerLeftContent = new MaterialIcon { Kind = MaterialIconKind.Search, 
                Foreground = EditorColor.FromRGB(128, 128, 128), Margin = new Thickness(6, 0, 0, 0), Width = 12, Height = 12 },
            PlaceholderText = "Search Components...",
            MinWidth = 240,
        };

        StackPanel compListPanel = new();
        ScrollViewer scroll = new() { Content = compListPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 200 };

        List<Type> componentTypes = GetComponentTypes();
        PopulateComponentList(compListPanel, componentTypes, "");
        searchBox.TextChanged += (_, _) => PopulateComponentList(compListPanel, componentTypes, searchBox.Text ?? "");

        return new StackPanel { Spacing = 1, Children = { searchBox, scroll } };
    }

    private void PopulateComponentList(StackPanel compListPanel, List<Type> componentTypes, string searchText)
    {
        compListPanel.Children.Clear();
        string searchLower = searchText.ToLowerInvariant().Replace(" ", "");
        bool hasFilter = !string.IsNullOrWhiteSpace(searchText);

        if (currentSelection is uint curEntityId)
        {
            foreach (Type compType in componentTypes)
            {
                string displayName = FormatComponentName(compType.Name);
                if (hasFilter && !(compType.Name + " " + displayName).Contains(searchLower, StringComparison.InvariantCultureIgnoreCase)) continue;

                Button compTypeButton = new() { Classes = { "menu-btn" }, Content = displayName, MinWidth = 240, Tag = compType };
                compTypeButton.Click += (sender, _) =>
                {
                    if (sender is not Button { Tag: Type type } || curEntityId == uint.MaxValue) return;
                    if (W.HasComponent(curEntityId, type)) return;
                    if (Activator.CreateInstance(type) is IComponent instance)
                    {
                        UndoManager.Execute(new AddComponentCommand(curEntityId, instance));
                        LoadEntityComponents(curEntityId);
                    }
                    else Debug.Warning($"Failed to add component of type {type.Name}");
                };
                compListPanel.Children.Add(compTypeButton);
            }
        }

        if (compListPanel.Children.Count == 0)
            compListPanel.Children.Add(new TextBlock { Text = "No components found", FontSize = 11, 
                Foreground = EditorColor.FromRGB(148, 148, 148), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 20, 0, 20) });
    }

    private static string FormatComponentName(string name)
    {
        string formatted = "";
        for (int i = 0; i < name.Length - 1; i++)
        {
            formatted += name[i];
            if (char.IsLower(name[i]) && char.IsUpper(name[i + 1])) formatted += ' ';
        }
        formatted += name[^1];
        return formatted.Replace("SDF", "SDF ");
    }

    private static List<Type> GetComponentTypes()
    {
        List<Type> componentTypes = [];
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                componentTypes.AddRange(assembly.GetTypes().Where(t => typeof(IComponent).IsAssignableFrom(t) &&
                !t.IsAbstract && !t.IsInterface && t != typeof(IComponent)));
            }
            catch (ReflectionTypeLoadException ex)
            {
                componentTypes.AddRange(ex.Types.Where(t => t != null && typeof(IComponent).IsAssignableFrom(t) &&
                !t.IsAbstract && !t.IsInterface && t != typeof(IComponent)).Cast<Type>());
                Debug.Warning($"Could not load some component types from {assembly.FullName}");
            }
            catch (Exception ex) { Debug.Warning($"Error loading component types from {assembly.FullName}", ex); }
        }
        return componentTypes;
    }

    #endregion
    #region routing

    /// <summary>
    /// Load properties for an entity.
    /// </summary>
    /// <param name="entityId">Entity ID to pull component data from</param>
    public static void LoadEntityComponents(uint entityId)
    {
        LastSelected = entityId;
        ValidatePropertiesWindows();
        foreach (PropertiesWindow? window in currentWindows) Dispatcher.UIThread.Post(() => window!.DisplayEntityComponents(entityId));
    }

    /// <summary>
    /// Load properties for a world.
    /// </summary>
    /// <param name="world">World to pull data from</param>
    public static void LoadWorldData(World? world)
    {
        LastSelected = uint.MaxValue;
        ValidatePropertiesWindows();
        foreach (PropertiesWindow? window in currentWindows) Dispatcher.UIThread.Post(() => window!.CreateWorldEditor(world));
    }

    private static void ValidatePropertiesWindows()
    {
        foreach (PropertiesWindow? window in currentWindows.ToArray())
            if (window == null || !window.IsLoaded) currentWindows.Remove(window);
    }

    private bool DisplayEntityComponents(uint entityId)
    {
        if (WorldManager.CurrentWorld == null || !W.EntityExists(entityId))
        {
            Debug.Warning("Could not load entity, world is null or entity does not exist");
            return false;
        }

        StopRenderInfoRefresh();
        worldTabs.IsVisible = false;
        scrollViewer.IsVisible = true;

        PropertiesRefreshSystem.OnEntitySelected(entityId);
        propertiesPanel.Children.Clear();
        componentFieldPanels.Clear();

        string entityName = W.TryGetEntityName(entityId);
        headerText.Text = string.IsNullOrEmpty(entityName) ? $"Entity_{entityId}" : entityName;
        currentSelection = entityId;

        foreach (IComponent component in W.GetAllComponents(entityId))
            CreateComponentEditor(propertiesPanel, component.GetType(), component, entityId);
        return true;
    }

    /// <summary>
    /// Displays properties for a selected asset.
    /// </summary>
    private async void DisplayAssetProperties(string assetId)
    {
        StopRenderInfoRefresh();
        worldTabs.IsVisible = false;
        scrollViewer.IsVisible = true;

        propertiesPanel.Children.Clear();
        componentFieldPanels.Clear();

        // Get asset metadata
        AssetMetadata? metadata = AssetDatabase.GetAssetMetadataByID(assetId);
        if (metadata == null)
        {
            headerText.Text = "Unknown Asset";
            return;
        }

        // Get loaded asset if available
        Asset? loadedAsset = ProjectManager.AssetManager?.Get(metadata.ID);
        bool isLoaded = loadedAsset != null && loadedAsset.IsLoaded;
        string? fullPath = AssetDatabase.GetAssetFullPath(metadata.ID);

        // Header
        string assetName = Path.GetFileNameWithoutExtension(metadata.FileName);
        headerText.Text = $"{assetName} (Asset)";
        StackPanel assetPanel = new()
        {
            Margin = new Thickness(8, 4, 4, 8),
        };
        DockPanel loadStateRow = new()
        {
            Margin = new Thickness(0, 4, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        TextBlock loadStateLabel = new()
        {
            Text = "State:",
            FontSize = 11,
            Foreground = EditorColor.FromRGB(180, 180, 180),
            MinWidth = 100,
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(loadStateLabel, Dock.Left);
        TextBlock loadStateValue = new()
        {
            Text = isLoaded ? "Loaded" : "Unloaded",
            FontSize = 11,
            Foreground = isLoaded ? new SolidColorBrush(Color.FromRgb(76, 175, 80)) : Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(loadStateValue, Dock.Left);

        loadStateRow.Children.Add(loadStateLabel);
        loadStateRow.Children.Add(loadStateValue);
        assetPanel.Children.Add(loadStateRow);

        // Add texture preview if applicable
        if (metadata.Type == AssetType.Texture && !string.IsNullOrEmpty(fullPath) && File.Exists(fullPath))
        {
            // Create a loading indicator
            StackPanel loadingPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 8),
            };
            loadingPanel.Children.Add(new TextBlock
            {
                Text = "Loading preview...",
                FontSize = 12,
                Foreground = Brushes.Gray,
                VerticalAlignment = VerticalAlignment.Center,
            });
            assetPanel.Children.Add(loadingPanel);

            // Load texture preview
            try
            {
                Bitmap? preview = await LoadTexturePreviewAsync(fullPath);
                if (preview != null)
                {
                    // Remove loading indicator
                    assetPanel.Children.Remove(loadingPanel);
                    AddTexturePreview(assetPanel, preview, metadata);
                }
                else
                {
                    // Show error
                    loadingPanel.Children.Clear();
                    loadingPanel.Children.Add(new TextBlock
                    {
                        Text = "Failed to load preview",
                        FontSize = 12,
                        Foreground = EditorColor.FromRGB(200, 80, 80),
                        VerticalAlignment = VerticalAlignment.Center,
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.Error($"Failed to load texture preview: {ex.Message}");
                loadingPanel.Children.Clear();
                loadingPanel.Children.Add(new TextBlock
                {
                    Text = "Error loading preview",
                    FontSize = 12,
                    Foreground = EditorColor.FromRGB(200, 80, 80),
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }
        }

        // Basic properties
        AddPropertyRow(assetPanel, "Type", metadata.Type.ToString());
        AddPropertyRow(assetPanel, "File Size", EditorUI.FormatFileSize(metadata.FileSize));
        AddPropertyRow(assetPanel, "GUID", metadata.ID);
        AddPathPropertyRow(assetPanel, fullPath, metadata.RelativePath);
        AddPropertyRow(assetPanel, "Last Modified", metadata.LastModified.ToString("g"));

        // Asset-specific properties
        if (loadedAsset != null && isLoaded)
        {
            switch (loadedAsset)
            {
                case TextureAsset texture:
                    AddPropertyRow(assetPanel, "Dimensions", $"{texture.Width} x {texture.Height}");
                    AddPropertyRow(assetPanel, "Pixel Count", $"{texture.PixelData?.Length:N0}");
                    break;
                    // Add more asset types as needed
            }
        }

        if (metadata.Type == AssetType.Texture)
        {
            AddTextureSettingsPanel(assetPanel, metadata);
        }

        // Action buttons panel
        StackPanel actionButtonsPanel = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 4),
        };

        if (isLoaded && ProjectManager.AssetManager != null)
        {
            Button unloadButton = new Button // Unload button
            {
                Content = "Unload Asset",
                FontSize = 12,
                Padding = new Thickness(12, 6),
                Background = EditorColor.FromRGB(60, 40, 40),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(4),
            };
            unloadButton.Click += (s, e) =>
            {
                unloadButton.Content = "Unloading...";
                unloadButton.IsEnabled = false;
                unloadButton.Background = EditorColor.FromRGB(40, 20, 20);

                ProjectManager.AssetManager.UnloadAsset(assetId);
                if (metadata.Type == AssetType.Texture) TextureSystem.RemoveTexture(assetId);
                DisplayAssetProperties(assetId);
            };
            actionButtonsPanel.Children.Add(unloadButton);
        }
        else if (!isLoaded && ProjectManager.AssetManager != null)
        {
            Button loadButton = new Button // Load button
            {
                Content = "Load Asset",
                FontSize = 12,
                Padding = new Thickness(12, 6),
                Background = EditorColor.FromRGB(40, 40, 40),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(4),
            };
            loadButton.Click += async (s, e) =>
            {
                loadButton.Content = "Loading...";
                loadButton.IsEnabled = false;
                loadButton.Background = EditorColor.FromRGB(40, 40, 80);

                Asset? loaded = metadata.Type switch
                {
                    AssetType.Texture => await ProjectManager.AssetManager.LoadAssetAsync<TextureAsset>(assetId),
                    _ => await ProjectManager.AssetManager.LoadAssetAsync<Asset>(assetId),
                };
                if (loaded != null && loaded.IsLoaded)
                {
                    if (metadata.Type == AssetType.Texture) TextureSystem.MarkTextureDirty(assetId);
                    DisplayAssetProperties(assetId);
                }
                else
                {
                    loadButton.Content = "Load Failed";
                    loadButton.Background = EditorColor.FromRGB(80, 30, 30);
                    loadButton.IsEnabled = true;
                }
            };
            actionButtonsPanel.Children.Add(loadButton);
        }

        if (actionButtonsPanel.Children.Count > 0) assetPanel.Children.Add(actionButtonsPanel);

        // Create the card
        StackPanel card = BuildCard(
            assetName,
            EditorUI.GetIconForAssetType(metadata.Type),
            assetPanel,
            onRemove: null
        );

        propertiesPanel.Children.Add(card);
        scrollViewer.ScrollToHome();
    }

    #endregion
    #region worldEditor

    /// <summary>
    /// Builds the editor view when nothing is selected.
    /// </summary>
    /// <param name="curWorld">World to pull data from</param>
    public void CreateWorldEditor(World? curWorld)
    {
        LastSelected = uint.MaxValue;
        currentSelection = null;
        componentFieldPanels.Clear();
        StopRenderInfoRefresh();

        scrollViewer.IsVisible = false;
        worldTabs.IsVisible = true;
        headerText.Text = curWorld?.Name ?? "World";

        statsPanel.Children.Clear();
        environmentPanel.Children.Clear();
        renderingPanel.Children.Clear();
        if (curWorld == null) return;

        BuildStatsTab(curWorld);
        BuildEnvironmentTab();
        BuildRenderingTab();
        StartRenderInfoRefresh();
    }

    private void BuildStatsTab(World curWorld)
    {
        StackPanel fields = new() { Margin = new Thickness(8, 4, 4, 8) };
        fields.Children.Add(new TextBlock { Text = $"Entities: {curWorld.entities.Count}", FontSize = 12, 
            Foreground = EditorColor.FromRGB(200, 200, 200) });
        fields.Children.Add(new TextBlock { Text = $"Next Entity ID: {curWorld.NextEntityId}", FontSize = 12, 
            Foreground = EditorColor.FromRGB(200, 200, 200) });
        statsPanel.Children.Add(BuildCard(curWorld.Name, MaterialIconKind.World, fields));
    }

    private void BuildEnvironmentTab()
    {
        foreach (var (entityId, env) in W.QueryData<Environment>())
            CreateComponentEditor(environmentPanel, typeof(Environment), env, entityId, 
                CardTitleFor("Environment", entityId), allowRemove: false);
        foreach (var (entityId, fog) in W.QueryData<VolumetricFog>())
            CreateComponentEditor(environmentPanel, typeof(VolumetricFog), fog, entityId, 
                CardTitleFor("Volumetric Fog", entityId), allowRemove: false);
        foreach (var (entityId, sun) in W.QueryData<DirectionalLight>())
            CreateComponentEditor(environmentPanel, typeof(DirectionalLight), sun, entityId, 
                CardTitleFor("Directional Light", entityId), allowRemove: false);
        foreach (var (entityId, point) in W.QueryData<PointLight>())
            CreateComponentEditor(environmentPanel, typeof(PointLight), point, entityId, 
                CardTitleFor("Point Light", entityId), allowRemove: false);

        if (environmentPanel.Children.Count == 0)
            environmentPanel.Children.Add(new TextBlock { Text = "No environment or lighting components in this world.", 
                Foreground = Brushes.Gray, FontStyle = FontStyle.Italic, Margin = new Thickness(8) });
    }

    private void BuildRenderingTab()
    {
        renderingPanel.Children.Add(BuildRenderInfoCard());
        foreach (var (entityId, _, cam) in W.QueryData<Transform, Camera>())
        {
            if (entityId == EditorCamera.EditorCameraId) continue;
            CreateComponentEditor(renderingPanel, typeof(Camera), cam, entityId, CardTitleFor("Camera", entityId), allowRemove: false);
            break; // Use first non-editor camera
        }
    }

    private static string CardTitleFor(string componentLabel, uint entityId)
    {
        string name = W.TryGetEntityName(entityId);
        return string.IsNullOrEmpty(name) ? $"{componentLabel} — Entity_{entityId}" : $"{componentLabel} — {name}";
    }

    private StackPanel BuildRenderInfoCard()
    {
        StackPanel fields = new() { Margin = new Thickness(8, 4, 4, 8), Spacing = 2 };
        renderInfoTextureText = new TextBlock { FontSize = 12, Foreground = EditorColor.FromRGB(200, 200, 200) };
        renderInfoSdfText = new TextBlock { FontSize = 12, Foreground = EditorColor.FromRGB(200, 200, 200) };
        renderInfoLightText = new TextBlock { FontSize = 12, Foreground = EditorColor.FromRGB(200, 200, 200) };
        fields.Children.Add(renderInfoTextureText);
        fields.Children.Add(renderInfoSdfText);
        fields.Children.Add(renderInfoLightText);
        RefreshRenderInfoLabels();
        return BuildCard("Render System Info", MaterialIconKind.ChartBoxOutline, fields);
    }

    private void RefreshRenderInfoLabels()
    {
        int texCount = TextureSystem.LastLoadedTextureCount;
        int texPixels = TextureSystem.LastLoadedTextureBufferSize;
        long totalBytes = texPixels * 4;
        string bytesText = EditorUI.FormatFileSize(totalBytes, 1);
        renderInfoTextureText?.Text = $"Textures: {texCount} ({texPixels:N0} px) ({bytesText})";
        renderInfoSdfText?.Text = $"SDF Objects: {SDFRenderSystem.PreparedSDFObjectsDTO.Length}";
        renderInfoLightText?.Text = $"Lights: {SDFRenderSystem.PreparedLightsDTO.Length}";
    }

    private void StartRenderInfoRefresh()
    {
        renderInfoRefreshTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        renderInfoRefreshTimer.Tick -= RenderInfoRefreshTimer_Tick;
        renderInfoRefreshTimer.Tick += RenderInfoRefreshTimer_Tick;
        renderInfoRefreshTimer.Start();
    }

    private void RenderInfoRefreshTimer_Tick(object? sender, EventArgs e) => RefreshRenderInfoLabels();
    private void StopRenderInfoRefresh() => renderInfoRefreshTimer?.Stop();

    #endregion
    #region componentCards

    /// <summary>
    /// Builds a foldout card for a component and registers its field panel so
    /// "Reset to Default" (and any other targeted refresh) can rebuild it later.
    /// </summary>
    private void CreateComponentEditor(StackPanel targetPanel, Type compType, IComponent instance,
        uint entityId, string? cardTitle = null, bool allowRemove = true)
    {
        StackPanel fieldsPanel = new() { Margin = new Thickness(8, 4, 4, 8) };
        foreach (FieldInfo field in compType.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (field.GetCustomAttribute<HideInEditorAttribute>() != null) continue;
            StackPanel? fieldEditor = CreateFieldEditor(field, instance, entityId, () => RefreshComponent(entityId, compType));
            if (fieldEditor != null) fieldsPanel.Children.Add(fieldEditor);
        }
        if (fieldsPanel.Children.Count == 0) return;

        componentFieldPanels[(entityId, compType)] = fieldsPanel;
        Action? onRemove = allowRemove ? () =>
        {
            IComponent? comp = WorldManager.CurrentWorld?.GetComponent(entityId, compType);
            if (comp != null) UndoManager.Execute(new RemoveComponentCommand(entityId, compType, comp));
            componentFieldPanels.Remove((entityId, compType));
            string key = $"{compType.Name}_{entityId}";
            lock (stateLock) { cardExpandedState.Remove(key); }
            LoadEntityComponents(entityId);
        }
        : null;

        // Use a consistent key format
        string cardKey = $"{compType.Name}_{entityId}";
        targetPanel.Children.Add(BuildCard(
            cardTitle ?? compType.Name,
            MaterialIconKind.DataMatrixScan,
            fieldsPanel,
            onRemove,
            cardKey));
    }

    /// <summary>
    /// Builds a collapsible card. Used for component editors and for the World-view info cards.
    /// </summary>
    private StackPanel BuildCard(string title, MaterialIconKind icon, Control content,
        Action? onRemove = null, string? cardKey = null)
    {
        // Use the cardKey or generate one from the title and current selection
        string key = cardKey ?? $"{title}_{currentSelection ?? "world"}";

        // Get saved state or default to true (expanded)
        bool expanded;
        lock (stateLock)
        {
            // Default to true if not found
            expanded = !cardExpandedState.TryGetValue(key, out bool state) || state;
        }

        Border headerBorder = new()
        {
            BorderThickness = new Thickness(0, 0, 1, 1),
            BorderBrush = EditorColor.FromRGB(17, 17, 17),
            Background = EditorColor.FromRGB(44, 44, 44),
            CornerRadius = new CornerRadius(4, 4, 0, 0),
            Margin = new Thickness(4, 8, 12, 0),
            Padding = new Thickness(4, 4),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        DockPanel headerPanel = new();
        MaterialIcon headerCompIcon = new()
        {
            Kind = icon,
            Width = 16,
            Height = 16,
            Margin = new Thickness(6, 2, 6, 2),
            Foreground = EditorColor.FromRGB(148, 148, 148),
            VerticalAlignment = VerticalAlignment.Center,
        };
        TextBlock titleText = new()
        {
            Text = title,
            FontSize = 14,
            Foreground = EditorColor.FromRGB(200, 200, 200),
            VerticalAlignment = VerticalAlignment.Center,
        };
        MaterialIcon chevronIcon = new()
        {
            Kind = expanded ? MaterialIconKind.ChevronDown : MaterialIconKind.ChevronRight,
            Width = 16,
            Height = 16,
            Foreground = EditorColor.FromRGB(148, 148, 148),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 4, 0),
        };

        DockPanel.SetDock(headerCompIcon, Dock.Left);
        DockPanel.SetDock(titleText, Dock.Left);
        DockPanel.SetDock(chevronIcon, Dock.Left);
        headerPanel.Children.Add(chevronIcon);
        headerPanel.Children.Add(headerCompIcon);
        headerPanel.Children.Add(titleText);

        if (onRemove != null)
        {
            Button removeButton = new()
            {
                Classes = { "icon-btn" },
                Content = new MaterialIcon { Kind = MaterialIconKind.Close },
                HorizontalAlignment = HorizontalAlignment.Right
            };
            removeButton.Click += (_, _) => onRemove();
            DockPanel.SetDock(removeButton, Dock.Right);
            headerPanel.Children.Add(removeButton);
        }
        headerBorder.Child = headerPanel;

        Border contentBorder = new()
        {
            BorderThickness = new Thickness(0, 0, 1, 1),
            BorderBrush = EditorColor.FromRGB(10, 10, 10),
            Background = EditorColor.FromRGB(20, 20, 20),
            CornerRadius = new CornerRadius(0, 0, 4, 4),
            Margin = new Thickness(4, 0, 12, 0),
            Padding = new Thickness(8, 4, 4, 4),
            Child = content,
            IsVisible = expanded,
        };
        contentBorder.PointerEntered += (_, _) =>
        {
            contentBorder.BorderThickness = new Thickness(0, 0, 2, 2);
            contentBorder.BorderBrush = EditorColor.FromRGB(12, 12, 12);
            contentBorder.Background = EditorColor.FromRGB(24, 24, 24);
        };
        contentBorder.PointerExited += (_, _) =>
        {
            contentBorder.BorderThickness = new Thickness(0, 0, 1, 1);
            contentBorder.BorderBrush = EditorColor.FromRGB(10, 10, 10);
            contentBorder.Background = EditorColor.FromRGB(20, 20, 20);
        };

        // When toggling, save the state to the static dictionary
        headerBorder.Tapped += (_, _) =>
        {
            expanded = !expanded;
            contentBorder.IsVisible = expanded;
            chevronIcon.Kind = expanded ? MaterialIconKind.ChevronDown : MaterialIconKind.ChevronRight;
            headerBorder.CornerRadius = expanded ? new CornerRadius(4, 4, 0, 0) : new CornerRadius(4, 4, 4, 4);

            // Save the state persistently
            lock (stateLock)
            {
                cardExpandedState[key] = expanded;
            }
        };

        return new StackPanel { Children = { headerBorder, contentBorder } };
    }

    /// <summary>
    /// Rebuilds a single component's field editors from a fresh component instance.
    /// </summary>
    public void RefreshComponent(uint entityId, Type compType)
    {
        if (WorldManager.CurrentWorld == null || !W.EntityExists(entityId)) return;
        if (!componentFieldPanels.TryGetValue((entityId, compType), out StackPanel? fieldsPanel)) return;

        IComponent? fresh = W.GetAllComponents(entityId).FirstOrDefault(c => c.GetType() == compType);
        if (fresh == null) return;

        fieldsPanel.Children.Clear();
        foreach (FieldInfo field in compType.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (field.GetCustomAttribute<HideInEditorAttribute>() != null) continue;
            StackPanel? fieldEditor = CreateFieldEditor(field, fresh, entityId, () => RefreshComponent(entityId, compType));
            if (fieldEditor != null) fieldsPanel.Children.Add(fieldEditor);
        }
    }

    /// <summary>
    /// Refreshes a component on the currently selected entity. Kept for external callers.
    /// </summary>
    /// <param name="compType">Component type to refresh</param>
    public void RefreshComponent(Type compType)
    {
        if (currentSelection is uint curEntityId) RefreshComponent(curEntityId, compType);
    }

    #endregion
    #region fieldEditors

    private static StackPanel? CreateFieldEditor(FieldInfo field, IComponent component, uint entityId, Action onReset)
    {
        Type fieldType = field.FieldType;
        object? fieldValue = field.GetValue(component);
        void Notify() => PropertiesRefreshSystem.OnFieldChanged(entityId, component.GetType().Name);

        float topMargin = field.GetCustomAttribute<SpaceAttribute>()?.Space ?? 0f;
        HeaderAttribute? headerAttr = field.GetCustomAttribute<HeaderAttribute>();

        StackPanel fieldPanel = new() { Orientation = Orientation.Horizontal, MinHeight = 20, Margin = new Thickness(0, topMargin, 0, 0) };
        StackPanel superFieldPanel = new() { Orientation = Orientation.Vertical, MinHeight = 20, Margin = new Thickness(0, topMargin, 0, 0) };
        if (headerAttr != null)
        {
            fieldPanel.Margin = new Thickness(0);
            superFieldPanel.Children.Add(new TextBlock { Text = headerAttr.Header, FontSize = 14, 
                Foreground = Brushes.White, Margin = new Thickness(0, 12, 0, 6) });
            superFieldPanel.Children.Add(fieldPanel);
        }

        string formattedFieldName = Thread.CurrentThread.CurrentCulture.TextInfo.ToTitleCase(FormattedFieldRegex().Replace(field.Name, "$1 $2"));
        TextBlock nameLabel = new() { Text = formattedFieldName, FontSize = 12, 
            Foreground = Brushes.LightGray, VerticalAlignment = VerticalAlignment.Center };
        fieldPanel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, 
            Margin = new Thickness(0, 0, 4, 0), Children = { nameLabel } });

        // Reset to default.
        object? defaultValue = GetDefaultFieldValue(component.GetType(), field.Name);
        ContextMenu fieldContextMenu = new();
        MenuItem resetMenuItem = new()
        {
            Header = "Reset to Default",
            Icon = new MaterialIcon { Kind = MaterialIconKind.Restore, Width = 16, Height = 16, 
                Foreground = EditorColor.FromRGB(200, 140, 120) },
            Foreground = Brushes.White,
        };
        resetMenuItem.Click += (_, _) =>
        {
            if (defaultValue == null) return;
            field.SetValue(component, defaultValue);
            Notify();
            onReset();
        };
        fieldContextMenu.Items.Add(resetMenuItem);

        // "Open Asset" — only meaningful for asset-reference fields
        bool isAssetRefField = fieldType == typeof(AssetRef) ||
            (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(AssetRef<>));
        if (isAssetRefField)
        {
            MenuItem openAssetMenuItem = new()
            {
                Header = "Open Asset",
                Icon = new MaterialIcon
                {
                    Kind = MaterialIconKind.OpenInNew,
                    Width = 16,
                    Height = 16,
                    Foreground = EditorColor.FromRGB(140, 180, 220)
                },
                Foreground = Brushes.White,
            };
            openAssetMenuItem.Click += (_, _) =>
            {
                // Re-read the field rather than using the value captured when this editor was built
                object? liveValue = field.GetValue(component);
                string assetId = liveValue != null ? GetAssetId(liveValue) : "";
                if (string.IsNullOrEmpty(assetId) || AssetDatabase.GetAssetMetadataByID(assetId) == null) return;
                Selection.SelectAsset(assetId);
            };

            fieldContextMenu.Items.Add(new Separator());
            fieldContextMenu.Items.Add(openAssetMenuItem);

            // Enable/disable right before the menu shows, so it always reflects the live value
            fieldContextMenu.Opened += (_, _) =>
            {
                object? liveValue = field.GetValue(component);
                string assetId = liveValue != null ? GetAssetId(liveValue) : "";
                openAssetMenuItem.IsEnabled = !string.IsNullOrEmpty(assetId) && AssetDatabase.GetAssetMetadataByID(assetId) != null;
            };
        }
        fieldPanel.ContextMenu = fieldContextMenu;

        MinAttribute? minAttr = field.GetCustomAttribute<MinAttribute>();
        MaxAttribute? maxAttr = field.GetCustomAttribute<MaxAttribute>();
        RangeAttribute? rangeAttr = field.GetCustomAttribute<RangeAttribute>();

        Control editorControl = new();
        if (fieldValue != null && fieldType == typeof(float)) // Float fields and sliders
        {
            float value = (float)fieldValue;
            float lo = minAttr?.Min ?? -2000000000f, hi = maxAttr?.Max ?? 2000000000f;
            if (rangeAttr != null)
            {
                NumericUpDown box = CreateFloatNumericBox(value, f => { field.SetValue(component, f); Notify(); }, false, lo, hi);
                editorControl = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children = { CreateFloatSlider(value, rangeAttr.Min, rangeAttr.Max, f =>
                    { field.SetValue(component, f); box.Value = (decimal)f; Notify(); }), box }
                };
            }
            else editorControl = CreateFloatNumericBox(value, f => { field.SetValue(component, f); Notify(); }, true, lo, hi);
        }
        else if (fieldValue != null && fieldType == typeof(int)) // Integer fields and sliders
        {
            int value = (int)fieldValue;
            int lo = minAttr != null ? (int)minAttr.Min : int.MinValue, hi = maxAttr != null ? (int)maxAttr.Max : int.MaxValue;
            if (rangeAttr != null)
            {
                NumericUpDown box = CreateIntegerNumericBox(value, f => { field.SetValue(component, f); Notify(); }, false, lo, hi);
                editorControl = new StackPanel { Orientation = Orientation.Horizontal, 
                    Children = { CreateIntegerSlider(value, (int)rangeAttr.Min, (int)rangeAttr.Max, i => 
                    { field.SetValue(component, i); box.Value = i; Notify(); }), box } };
            }
            else editorControl = CreateIntegerNumericBox(value, f => { field.SetValue(component, f); Notify(); }, true, lo, hi);
        }
        else if (fieldValue != null && fieldType == typeof(string)) // Text fields
        {
            TextBox textBox = new() { Classes = { "field-editor" }, Text = (string)fieldValue, 
                AcceptsReturn = field.GetCustomAttribute<MultilineAttribute>() != null };
            textBox.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) 
                { field.SetValue(component, textBox.Text); Notify(); } };
            editorControl = textBox;
        }
        else if (fieldValue != null && fieldType == typeof(bool)) // Toggle fields
        {
            CheckBox checkBox = new() { Classes = { "field-editor" }, IsChecked = (bool)fieldValue, IsDefault = false };
            checkBox.IsCheckedChanged += (_, _) => { field.SetValue(component, checkBox.IsChecked); Notify(); };
            editorControl = checkBox;
        }
        else if (fieldValue != null && fieldType == typeof(float2)) // 2D vector fields
        {
            float2 state = (float2)fieldValue;
            editorControl = BuildAxisRow(["X", "Y"], [state.X, state.Y], (axis, v) =>
            {
                if (axis == 0) state.X = v; else state.Y = v;
                field.SetValue(component, state); Notify();
            }, out _);
        }
        else if (fieldValue != null && fieldType == typeof(float3)) // 3D vector fields and colors (no alpha)
        {
            float3 state = (float3)fieldValue;
            ColorAttribute? colorAttr = field.GetCustomAttribute<ColorAttribute>();

            // Can be color or vector field
            if (colorAttr != null) editorControl = CreateColorFieldEditorF3(field, component, entityId) ?? editorControl;
            else
            {
                editorControl = BuildAxisRow(["X", "Y", "Z"], [state.X, state.Y, state.Z], (axis, v) =>
                {
                    if (axis == 0) state.X = v; else if (axis == 1) state.Y = v; else state.Z = v;
                    field.SetValue(component, state); Notify();
                }, out _);
            }
        }
        else if (fieldValue != null && fieldType == typeof(float4)) // 4D vector fields and colors and quaternions
        {
            float4 state = (float4)fieldValue;
            ColorAttribute? colorAttr = field.GetCustomAttribute<ColorAttribute>();
            RotationAttribute? rotAttr = field.GetCustomAttribute<RotationAttribute>();

            // Can be color, rotation, or vector field
            if (colorAttr != null) editorControl = CreateColorFieldEditor(field, component, colorAttr, entityId) ?? editorControl;
            else if (rotAttr != null) editorControl = CreateRotationFieldEditor(field, component, rotAttr, entityId) ?? editorControl;
            else
            {
                editorControl = BuildAxisRow(["X", "Y", "Z", "W"], [state.X, state.Y, state.Z, state.W], (axis, v) =>
                {
                    switch (axis) { case 0: state.X = v; break; case 1: state.Y = v; break; case 2: state.Z = v; break; default: state.W = v; break; }
                    field.SetValue(component, state); Notify();
                }, out _);
            }
        }
        else if (fieldValue != null && fieldType == typeof(DateTime)) // Time and date fields
        {
            CalendarDatePicker picker = new()
            {
                SelectedDate = (DateTime)fieldValue,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(4),
                FontSize = 11,
                Background = EditorColor.FromRGB(32, 32, 32),
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            picker.SelectedDateChanged += (_, _) => { field.SetValue(component, picker.SelectedDate); Notify(); };
            editorControl = picker;
        }
        else if (fieldValue != null && fieldType == typeof(float4x4)) // 4D matrix fields
            editorControl = CreateMatrixEditor((float4x4)fieldValue, field, component, entityId);
        else if (fieldType.IsEnum) // Dropdown enum fields
            editorControl = CreateEnumEditor(field, component, fieldType, fieldValue, entityId);
        else if (fieldType == typeof(AssetRef) || (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(AssetRef<>))) // Asset fields
            editorControl = CreateAssetRefEditor(field, component);

        ApplyTooltip(editorControl, field);
        fieldPanel.Children.Add(editorControl);

        if (field.GetCustomAttribute<TooltipAttribute>() != null) // Adds tooltips to fields
        {
            MaterialIcon tooltipIcon = new() { Kind = MaterialIconKind.InformationOutline, Width = 12, Height = 12, 
                Margin = new Thickness(4, 0, 0, 0), Foreground = EditorColor.FromRGB(148, 148, 148), VerticalAlignment = VerticalAlignment.Center };
            ApplyTooltip(tooltipIcon, field);
            ApplyTooltip(nameLabel, field);
            fieldPanel.Children.Add(tooltipIcon);
        }

        return headerAttr != null ? superFieldPanel : fieldPanel;
    }

    /// <summary>
    /// Builds a horizontal row of labeled NumericUpDown boxes — shared by float2/float3/float4/rotation.
    /// </summary>
    private static StackPanel BuildAxisRow(string[] labels, float[] initial, Action<int, float> onAxisChanged, out NumericUpDown[] boxes)
    {
        StackPanel panel = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        boxes = new NumericUpDown[labels.Length];
        for (int i = 0; i < labels.Length; i++)
        {
            int axis = i;
            panel.Children.Add(new TextBlock { Text = labels[i], Foreground = Brushes.LightGray, FontSize = 9, 
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 2, 0) });
            NumericUpDown box = CreateFloatNumericBox(initial[i], v => onAxisChanged(axis, v));
            boxes[i] = box;
            panel.Children.Add(box);
        }
        return panel;
    }

    #endregion
    #region enumEditor

    private static StackPanel CreateEnumEditor(FieldInfo field, IComponent component, Type enumType, object? currentValue, uint entityId)
    {
        List<EnumItem> items = [];
        int selectedIndex = 0, index = 0;
        foreach (object enumValue in Enum.GetValues(enumType))
        {
            items.Add(new EnumItem { Value = enumValue, DisplayName = FormatEnumName(enumValue.ToString()!) });
            if (currentValue != null && enumValue.Equals(currentValue)) selectedIndex = index;
            index++;
        }

        ComboBox comboBox = new()
        {
            Classes = { "field-editor" },
            MinWidth = 100,
            MaxWidth = 200,
            PlaceholderText = "Select value...",
            ItemsSource = items,
            SelectedIndex = selectedIndex,
            ItemTemplate = new FuncDataTemplate<EnumItem>((item, _) => new TextBlock { Text = item!.DisplayName, FontSize = 11, 
                FontWeight = FontWeight.Medium, Foreground = Brushes.White, Margin = new Thickness(2, 0, 2, 0) }),
        };
        comboBox.SelectionChanged += (_, _) =>
        {
            if (comboBox.SelectedItem is not EnumItem selected) return;
            try { field.SetValue(component, selected.Value); PropertiesRefreshSystem.OnFieldChanged(entityId, component.GetType().Name); }
            catch (Exception ex) { Debug.Error($"Failed to set enum value for {field.Name}", ex); }
        };
        return new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Spacing = 4, Children = { comboBox } };
    }

    private class EnumItem
    {
        public object Value { get; set; } = null!;
        public string DisplayName { get; set; } = string.Empty;
        public override string ToString() => DisplayName;
    }

    private static string FormatEnumName(string enumName)
    {
        if (string.IsNullOrEmpty(enumName)) return enumName;
        StringBuilder result = new();
        result.Append(char.ToUpperInvariant(enumName[0]));
        for (int i = 1; i < enumName.Length; i++)
        {
            if ((char.IsUpper(enumName[i]) || char.IsDigit(enumName[i])) && !char.IsUpper(enumName[i - 1])) result.Append(' ');
            result.Append(enumName[i]);
        }
        return result.ToString();
    }

    #endregion
    #region matrixEditor

    private static Button CreateMatrixEditor(float4x4 initialValue, FieldInfo field, object component, uint entityId)
    {
        Button matrixButton = new()
        {
            Content = CreateMatrixButtonContent(),
            Padding = new Thickness(8, 4),
            BorderThickness = new Thickness(0),
            Background = EditorColor.FromRGB(32, 32, 32),
            Foreground = Brushes.White,
            CornerRadius = new CornerRadius(4),
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };

        StackPanel mainPanel = new() { Spacing = 8 };
        Flyout flyout = new() { Placement = PlacementMode.BottomEdgeAlignedLeft, ShowMode = FlyoutShowMode.Standard, Content = mainPanel };

        DockPanel headerPanel = new();
        TextBlock headerText = new() { Text = "Edit Matrix", FontSize = 14, FontWeight = FontWeight.SemiBold, 
            Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 4), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(headerText, Dock.Left);
        Button closeButton = new() { Classes = { "icon-btn" }, Content = new MaterialIcon { Kind = MaterialIconKind.Close } };
        closeButton.Click += (_, _) => flyout.Hide();
        DockPanel.SetDock(closeButton, Dock.Right);
        headerPanel.Children.Add(headerText);
        headerPanel.Children.Add(closeButton);
        mainPanel.Children.Add(headerPanel);

        Border gridBorder = new() { CornerRadius = new CornerRadius(3), Background = EditorColor.FromRGB(52, 52, 52), Padding = new Thickness(2) };
        StackPanel gridContainer = new() { Spacing = 1 };
        StackPanel columnHeaders = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 0, 0, 2) };
        for (int col = 0; col < 4; col++)
            columnHeaders.Children.Add(new Border
            {
                Child = new TextBlock { Text = $"C{col + 1}", Foreground = EditorColor.FromRGB(200, 200, 200), 
                    FontSize = 10, FontWeight = FontWeight.Medium, HorizontalAlignment = HorizontalAlignment.Center, 
                    VerticalAlignment = VerticalAlignment.Center },
                Width = 32,
                Margin = new Thickness(2, 0, 2, 0),
            });
        gridContainer.Children.Add(columnHeaders);

        for (int row = 0; row < 4; row++)
        {
            DockPanel rowPanel = new();
            Border rowHeader = new()
            {
                Child = new TextBlock { Text = $"R{row + 1}", Foreground = EditorColor.FromRGB(200, 200, 200), 
                    FontSize = 10, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center, 
                    HorizontalAlignment = HorizontalAlignment.Right },
                Width = 20,
                VerticalAlignment = VerticalAlignment.Center,
            };
            DockPanel.SetDock(rowHeader, Dock.Left);
            rowPanel.Children.Add(rowHeader);

            StackPanel rowCells = new() { Orientation = Orientation.Horizontal };
            for (int col = 0; col < 4; col++)
            {
                int r = row, c = col;
                NumericUpDown numBox = CreateFloatNumericBox(initialValue.GetVal(r, c), val =>
                {
                    float4x4 current = (float4x4)field.GetValue(component)!;
                    current.SetVal(r, c, val);
                    field.SetValue(component, current);
                    PropertiesRefreshSystem.OnFieldChanged(entityId, component.GetType().Name);
                });
                numBox.Width = 24; numBox.Height = 20; numBox.Margin = new Thickness(2);
                rowCells.Children.Add(numBox);
            }
            rowPanel.Children.Add(rowCells);
            gridContainer.Children.Add(rowPanel);
        }

        gridBorder.Child = gridContainer;
        mainPanel.Children.Add(gridBorder);
        matrixButton.Click += (_, _) => flyout.ShowAt(matrixButton);
        return matrixButton;
    }

    private static StackPanel CreateMatrixButtonContent()
    {
        StackPanel textPanel = new() { Orientation = Orientation.Vertical, Spacing = 2, Children = { new TextBlock { Text = "4x4 Matrix", 
            FontSize = 11, FontWeight = FontWeight.Medium, Foreground = EditorColor.FromRGB(220, 220, 220) } } };
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new MaterialIcon { Kind = MaterialIconKind.Matrix, Width = 16, Height = 16, 
                    Foreground = EditorColor.FromRGB(100, 200, 255), VerticalAlignment = VerticalAlignment.Center },
                textPanel,
                new MaterialIcon { Kind = MaterialIconKind.ChevronRight, Width = 12, Height = 12, Foreground = Brushes.Gray, 
                    Margin = new Thickness(2, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center },
            }
        };
    }

    #endregion
    #region colorRotationEditors

    private static StackPanel? CreateColorFieldEditor(FieldInfo field, IComponent component, ColorAttribute colorAttr, uint entityId)
    {
        if (field.GetValue(component) is not float4 colorValue) return null;

        ColorPicker colorPicker = new()
        {
            Width = 150,
            Height = 20,
            Color = EditorColor.FromColor(colorValue).Color,
            Background = EditorColor.FromRGB(32, 32, 32),
            IsAlphaVisible = colorAttr.ShowAlpha,
            IsAlphaEnabled = colorAttr.ShowAlpha,
            IsColorSpectrumVisible = true,
            IsColorPreviewVisible = true,
            IsColorComponentsVisible = true,
            IsComponentTextInputVisible = false,
            IsComponentSliderVisible = true,
            IsHexInputVisible = true,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            FontSize = 12,
        };
        colorPicker.ColorChanged += (_, _) =>
        {
            Color c = colorPicker.Color;
            field.SetValue(component, new float4(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f));
            PropertiesRefreshSystem.OnFieldChanged(entityId, component.GetType().Name);
        };
        return new StackPanel { Orientation = Orientation.Horizontal, MinHeight = 10, 
            VerticalAlignment = VerticalAlignment.Center, Children = { colorPicker } };
    }

    private static StackPanel? CreateColorFieldEditorF3(FieldInfo field, IComponent component, uint entityId)
    {
        if (field.GetValue(component) is not float3 colorValue) return null;

        ColorPicker colorPicker = new()
        {
            Width = 150,
            Height = 20,
            Color = EditorColor.FromColor(colorValue).Color,
            Background = EditorColor.FromRGB(32, 32, 32),
            IsAlphaVisible = false,
            IsAlphaEnabled = false,
            IsColorSpectrumVisible = true,
            IsColorPreviewVisible = true,
            IsColorComponentsVisible = true,
            IsComponentTextInputVisible = false,
            IsComponentSliderVisible = true,
            IsHexInputVisible = true,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            FontSize = 12,
        };
        colorPicker.ColorChanged += (_, _) =>
        {
            Color c = colorPicker.Color;
            field.SetValue(component, new float3(c.R / 255f, c.G / 255f, c.B / 255f));
            PropertiesRefreshSystem.OnFieldChanged(entityId, component.GetType().Name);
        };
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            MinHeight = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { colorPicker }
        };
    }

    private static StackPanel? CreateRotationFieldEditor(FieldInfo field, IComponent component, RotationAttribute rotAttr, uint entityId)
    {
        if (field.GetValue(component) is not float4 quatValue) return null;

        float3 eulerState = MathUtilities.Math.QuaternionToEuler(quatValue);
        if (rotAttr.Degrees) eulerState = new float3(eulerState.X * math.Rad2Deg, eulerState.Y * math.Rad2Deg, eulerState.Z * math.Rad2Deg);

        StackPanel panel = BuildAxisRow(["X", "Y", "Z"], [eulerState.X, eulerState.Y, eulerState.Z], (axis, v) =>
        {
            switch (axis) { case 0: eulerState.X = v; break; case 1: eulerState.Y = v; break; default: eulerState.Z = v; break; }
            float3 radians = rotAttr.Degrees ? new float3(eulerState.X * math.Deg2Rad, 
                eulerState.Y * math.Deg2Rad, eulerState.Z * math.Deg2Rad) : eulerState;
            field.SetValue(component, MathUtilities.Math.EulerToQuaternion(radians));
            PropertiesRefreshSystem.OnFieldChanged(entityId, component.GetType().Name);
        }, out NumericUpDown[] boxes);

        if (rotAttr.Degrees) foreach (NumericUpDown b in boxes) b.Increment = 5;

        panel.Children.Add(new MaterialIcon
        {
            Kind = rotAttr.Degrees ? MaterialIconKind.Rotate360 : MaterialIconKind.Pi,
            Foreground = Brushes.LightGray,
            Width = 12,
            Height = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 2, 0),
        });
        return panel;
    }

    #endregion
    #region numericBoxesSliders

    private static NumericUpDown CreateFloatNumericBox(float initialVal, Action<float> onValueChanged, 
        bool hasSpinner = false, float min = -2000000000f, float max = 2000000000f)
    {
        NumericUpDown box = new()
        {
            Classes = { "field-editor" },
            FormatString = "0.##",
            Value = (decimal)initialVal,
            Minimum = (decimal)min,
            Maximum = (decimal)max,
            Increment = (decimal)math.max(initialVal / 10f, 0.1f),
            ParsingNumberStyle = NumberStyles.Float,
            ShowButtonSpinner = hasSpinner,
        };
        box.LostFocus += (_, _) => { if (string.IsNullOrWhiteSpace(box.Text) || !decimal.TryParse(box.Text, out _)) box.Value = 0; };
        box.ValueChanged += (_, _) =>
        {
            try { if (box.Value.HasValue) onValueChanged((float)(double)box.Value); }
            catch (Exception ex) { Debug.Error("Numeric Box Error", ex); }
        };
        return box;
    }

    private static NumericUpDown CreateIntegerNumericBox(int initialVal, Action<int> onValueChanged, 
        bool hasSpinner = false, int min = int.MinValue, int max = int.MaxValue)
    {
        NumericUpDown box = new()
        {
            Classes = { "field-editor" },
            Value = initialVal,
            Minimum = min,
            Maximum = max,
            Increment = 1,
            ParsingNumberStyle = NumberStyles.Integer,
            ShowButtonSpinner = hasSpinner,
        };
        box.LostFocus += (_, _) => { if (string.IsNullOrWhiteSpace(box.Text) || !decimal.TryParse(box.Text, out _)) box.Value = 0; };
        box.ValueChanged += (_, _) =>
        {
            try { if (box.Value.HasValue) onValueChanged((int)box.Value); }
            catch (Exception ex) { Debug.Error("Numeric Box Error", ex); }
        };
        return box;
    }

    private static StackPanel CreateFloatSlider(float initialVal, float min, float max, Action<float> onValueChanged)
    {
        Slider slider = new() { Classes = { "field-editor" }, Minimum = min, Maximum = max, Value = initialVal, Width = 100 };
        slider.ValueChanged += (_, _) => onValueChanged((float)slider.Value);
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, 
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), Children = { slider } };
    }

    private static StackPanel CreateIntegerSlider(int initialVal, int min, int max, Action<int> onValueChanged)
    {
        Slider slider = new() { Classes = { "field-editor" }, Minimum = min, Maximum = max, 
            Value = initialVal, Width = 100, TickFrequency = 1, IsSnapToTickEnabled = true };
        slider.ValueChanged += (_, _) => onValueChanged((int)slider.Value);
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, 
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), Children = { slider } };
    }

    private static object? GetDefaultFieldValue(Type compType, string fieldName)
    {
        try
        {
            if (Activator.CreateInstance(compType) is IComponent freshInstance)
                return compType.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(freshInstance);
        }
        catch (Exception ex) { Debug.Warning($"Failed to get default value for {compType.Name}.{fieldName}", ex); }
        return null;
    }

    #endregion
    #region assetReferences

    private static Control CreateAssetRefEditor(FieldInfo field, IComponent component)
    {
        object? fieldValue = field.GetValue(component);
        if (fieldValue == null) return new TextBlock { Text = "Error" };

        AssetType expectedType = GetExpectedTypeFromField(field, fieldValue);
        string currentId = GetAssetId(fieldValue);
        AssetMetadata? currentMeta = string.IsNullOrEmpty(currentId) ? null : AssetDatabase.GetAssetMetadataByID(currentId);
        string currentName = currentMeta != null ? Path.GetFileNameWithoutExtension(currentMeta.FileName) : (string.IsNullOrEmpty(currentId) ? "None" : "Missing");

        TextBlock assetRefButtonText = new() { Text = currentName, FontSize = 12, Foreground = EditorColor.FromRGB(200, 200, 200) };
        Button assetRefButton = new()
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, 
                Children = { EditorUI.CreateAssetTypeIcon(expectedType, 12), assetRefButtonText } },
            Background = EditorColor.FromRGB(17, 17, 17),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 4),
            MinWidth = 150,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };

        Flyout flyout = new();
        assetRefButton.Click += (_, _) =>
        {
            StackPanel panel = new() { MinWidth = 200 };
            panel.Children.Add(BuildAssetOptionButton("None", () => 
            {
                SetAssetValue(field, component, null); assetRefButtonText.Text = "None"; flyout.Hide();
            }));
            foreach (AssetMetadata? asset in AssetDatabase.GetAssetsByType(expectedType))
            {
                if (asset == null) continue;
                string label = Path.GetFileNameWithoutExtension(asset.FileName);
                panel.Children.Add(BuildAssetOptionButton(label, () => 
                {
                    SetAssetValue(field, component, asset.ID); assetRefButtonText.Text = label; flyout.Hide();
                }));
            }
            flyout.Content = panel;
            flyout.ShowAt(assetRefButton);
        };
        return assetRefButton;
    }

    private static Button BuildAssetOptionButton(string label, Action onClick)
    {
        Button button = new()
        {
            Content = label,
            FontSize = 10,
            Background = EditorColor.FromRGB(10, 10, 10),
            Foreground = EditorColor.FromRGB(200, 200, 200),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(8, 2),
            Margin = new Thickness(1),
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private static AssetType GetExpectedTypeFromField(FieldInfo field, object fieldValue)
    {
        Type fieldType = field.FieldType;
        if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(AssetRef<>))
            return AssetDatabase.GetAssetType(fieldType.GetGenericArguments()[0]);
        if (fieldType == typeof(AssetRef))
            return (AssetType)(fieldType.GetProperty("ExpectedType")?.GetValue(fieldValue) ?? AssetType.None);
        return AssetType.None;
    }

    private static string GetAssetId(object fieldValue) => fieldValue.GetType().GetProperty("ID")?.GetValue(fieldValue) as string ?? "";

    private static void SetAssetValue(FieldInfo field, IComponent component, string? assetId)
    {
        Type fieldType = field.FieldType;
        if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(AssetRef<>))
            field.SetValue(component, Activator.CreateInstance(fieldType, assetId ?? string.Empty));
        else if (fieldType == typeof(AssetRef))
        {
            object? current = field.GetValue(component);
            AssetType expectedType = current != null ? (AssetType)(fieldType.GetProperty("ExpectedType")?.GetValue(current) ?? AssetType.None) : AssetType.None;
            field.SetValue(component, Activator.CreateInstance(fieldType, assetId ?? string.Empty, expectedType));
        }
    }

    private static void SaveAssetMetadata(AssetMetadata metadata)
    {
        // Replace with individual save in the future
        AssetDatabase.SaveAll();
    }

    #endregion
    #region assetDisplay

    /// <summary>
    /// Adds a property row with label and value to a panel.
    /// </summary>
    private static void AddPropertyRow(StackPanel panel, string label, string value)
    {
        DockPanel row = new()
        {
            Margin = new Thickness(0, 2, 0, 2),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        TextBlock labelBlock = new()
        {
            Text = label + ":",
            FontSize = 11,
            Foreground = EditorColor.FromRGB(180, 180, 180),
            MinWidth = 100,
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(labelBlock, Dock.Left);
        TextBlock valueBlock = new()
        {
            Text = value,
            FontSize = 11,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        DockPanel.SetDock(valueBlock, Dock.Left);

        // For long values like GUID, make them selectable
        if (label == "GUID")
        {
            SelectableTextBlock selectableValue = new()
            {
                Text = value,
                FontSize = 11,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            };
            row.Children.Add(labelBlock);
            row.Children.Add(selectableValue);
            panel.Children.Add(row);
        }
        else
        {
            row.Children.Add(labelBlock);
            row.Children.Add(valueBlock);
            panel.Children.Add(row);
        }
    }

    /// <summary>
    /// Adds a "Path" row whose value acts as a hyperlink — clicking it reveals the asset's
    /// file in the OS file explorer with the file pre-selected/highlighted.
    /// </summary>
    private static void AddPathPropertyRow(StackPanel panel, string? fullPath, string relativePath)
    {
        DockPanel row = new()
        {
            Margin = new Thickness(0, 2, 0, 2),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        TextBlock labelBlock = new()
        {
            Text = "Path:",
            FontSize = 11,
            Foreground = EditorColor.FromRGB(180, 180, 180),
            MinWidth = 100,
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(labelBlock, Dock.Left);
        bool canOpen = !string.IsNullOrEmpty(fullPath) && File.Exists(fullPath);
        TextBlock linkBlock = new()
        {
            Text = relativePath,
            FontSize = 11,
            Foreground = canOpen ? EditorColor.FromRGB(100, 180, 255) : Brushes.White,
            TextDecorations = canOpen ? TextDecorations.Underline : null,
            Cursor = canOpen ? new Cursor(StandardCursorType.Hand) : Cursor.Default,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };

        if (canOpen)
        {
            ToolTip.SetTip(linkBlock, "Click to reveal in File Explorer");
            linkBlock.Tapped += (_, e) =>
            {
                try { Process.Start("explorer.exe", $"/select,\"{fullPath}\""); }
                catch (Exception ex) { Debug.Error($"Failed to reveal file in explorer: {fullPath}", ex); }
            };
        }

        DockPanel.SetDock(linkBlock, Dock.Left);
        row.Children.Add(labelBlock);
        row.Children.Add(linkBlock);
        panel.Children.Add(row);
    }

    /// <summary>
    /// Loads a texture preview from a file path using Avalonia's Bitmap.
    /// </summary>
    private static async Task<Bitmap?> LoadTexturePreviewAsync(string filePath)
    {
        try
        {
            // Read the file asynchronously
            using var stream = File.OpenRead(filePath);
            return await Task.Run(() => new Bitmap(stream));
        }
        catch (Exception ex)
        {
            Debug.Error($"Failed to load texture preview: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Adds a texture preview to the asset panel.
    /// </summary>
    private static void AddTexturePreview(StackPanel panel, Bitmap bitmap, AssetMetadata metadata)
    {
        Border previewBorder = new Border // Create border with preview
        {
            Background = new SolidColorBrush(Color.FromRgb(32, 32, 32)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(64, 64, 64)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 4, 0, 8),
            Padding = new Thickness(4),
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 300,
            MaxHeight = 200,
        };
        Image image = new Image // Create image control
        {
            Source = bitmap,
            Stretch = Stretch.Uniform,
            MaxWidth = 290,
            MaxHeight = 190,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        // Add tooltip showing file info
        ToolTip.SetTip(image, $"{Path.GetFileName(metadata.FileName)}\n{bitmap.Size.Width} x {bitmap.Size.Height}");

        // Hover effect
        previewBorder.PointerEntered += (s, e) =>
        {
            previewBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(100, 180, 255));
            previewBorder.BorderThickness = new Thickness(2);
        };
        previewBorder.PointerExited += (s, e) =>
        {
            previewBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(64, 64, 64));
            previewBorder.BorderThickness = new Thickness(1);
        };
        previewBorder.Child = image;
        panel.Children.Add(previewBorder);
    }

    private static void AddTextureSettingsPanel(StackPanel panel, AssetMetadata metadata)
    {
        // Ensure CustomProperties exists
        metadata.CustomProperties ??= [];

        void ReimportTexture()
        {
            SaveAssetMetadata(metadata);
            ProjectManager.AssetManager?.InvalidateAsset(metadata.ID);
            TextureSystem.MarkTextureDirty(metadata.ID);
        }

        const string samplingKey = "Sampling";
        const string texTypeKey = "TextureType";
        const string maxMipKey = "MaxMipmap";
        const string layoutKey = "CubemapLayout";

        // Read current values (or defaults)
        string? currentSampling = metadata.CustomProperties.TryGetValue(samplingKey, out object? sObj) ? sObj.ToString() : "Bilinear";
        string? currentTexType = metadata.CustomProperties.TryGetValue(texTypeKey, out object? tObj) ? tObj.ToString() : "Texture2D";
        int currentMaxMip = metadata.CustomProperties.TryGetValue(maxMipKey, out object? mObj) && int.TryParse(mObj.ToString(), out int m) ? m : 4;
        string? currentLayout = metadata.CustomProperties.TryGetValue(layoutKey, out object? lObj) ? lObj.ToString() : "None";

        // Header
        panel.Children.Add(new TextBlock
        {
            Text = "Texture Import Settings",
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 12, 0, 4)
        });

        StackPanel settingsPanel = new() { Margin = new Thickness(0, 0, 0, 8) };

        // Sampling
        DockPanel samplingRow = new() { Margin = new Thickness(0, 2, 0, 2) };
        TextBlock samplingLabel = new()
        {
            Text = "Sampling:",
            FontSize = 11,
            Foreground = EditorColor.FromRGB(180, 180, 180),
            MinWidth = 100,
            VerticalAlignment = VerticalAlignment.Center
        };
        ComboBox samplingCombo = new()
        {
            ItemsSource = new[] { "Point", "Bilinear" },
            SelectedItem = currentSampling,
            Classes = { "field-editor" },
            MinWidth = 100
        };
        samplingCombo.SelectionChanged += (_, _) =>
        {
            metadata.CustomProperties[samplingKey] = samplingCombo.SelectedItem?.ToString() ?? "Bilinear";
            ReimportTexture();
        };
        DockPanel.SetDock(samplingLabel, Dock.Left);
        samplingRow.Children.Add(samplingLabel);
        samplingRow.Children.Add(samplingCombo);
        settingsPanel.Children.Add(samplingRow);

        // Texture type
        DockPanel texTypeRow = new() { Margin = new Thickness(0, 2, 0, 2) };
        TextBlock texTypeLabel = new()
        {
            Text = "Texture Type:",
            FontSize = 11,
            Foreground = EditorColor.FromRGB(180, 180, 180),
            MinWidth = 100,
            VerticalAlignment = VerticalAlignment.Center
        };
        ComboBox texTypeCombo = new()
        {
            ItemsSource = new[] { "Texture2D", "Texture3D", "Cubemap" },
            SelectedItem = currentTexType,
            Classes = { "field-editor" },
            MinWidth = 100
        };
        texTypeCombo.SelectionChanged += (_, _) =>
        {
            metadata.CustomProperties[texTypeKey] = texTypeCombo.SelectedItem?.ToString() ?? "Texture2D";
            ReimportTexture();
        };
        DockPanel.SetDock(texTypeLabel, Dock.Left);
        texTypeRow.Children.Add(texTypeLabel);
        texTypeRow.Children.Add(texTypeCombo);
        settingsPanel.Children.Add(texTypeRow);

        // Cubemap layout
        DockPanel layoutRow = new() { Margin = new Thickness(0, 2, 0, 2) };
        TextBlock layoutLabel = new()
        {
            Text = "Cubemap Layout:",
            FontSize = 11,
            Foreground = EditorColor.FromRGB(180, 180, 180),
            MinWidth = 100,
            VerticalAlignment = VerticalAlignment.Center
        };
        ComboBox layoutCombo = new()
        {
            ItemsSource = new[] { "None", "Equirectangular", "Cross", "VerticalCross" },
            SelectedItem = currentLayout,
            Classes = { "field-editor" },
            MinWidth = 100
        };
        layoutCombo.SelectionChanged += (_, _) =>
        {
            metadata.CustomProperties[layoutKey] = layoutCombo.SelectedItem?.ToString() ?? "None";
            ReimportTexture();
        };
        DockPanel.SetDock(layoutLabel, Dock.Left);
        layoutRow.Children.Add(layoutLabel);
        layoutRow.Children.Add(layoutCombo);
        settingsPanel.Children.Add(layoutRow);

        // Initially set visibility
        layoutRow.IsVisible = (texTypeCombo.SelectedItem?.ToString() == "Cubemap");

        // Toggle visibility when texture type changes
        texTypeCombo.SelectionChanged += (_, _) =>
        {
            bool isCubemap = (texTypeCombo.SelectedItem?.ToString() == "Cubemap");
            layoutRow.IsVisible = isCubemap;
            if (!isCubemap)
            {
                metadata.CustomProperties[layoutKey] = "None";
                layoutCombo.SelectedItem = "None";
                ReimportTexture();
            }
        };

        // Max mipmap
        DockPanel mipRow = new() { Margin = new Thickness(0, 2, 0, 2) };
        TextBlock mipLabel = new()
        {
            Text = "Max Mipmap:",
            FontSize = 11,
            Foreground = EditorColor.FromRGB(180, 180, 180),
            MinWidth = 100,
            VerticalAlignment = VerticalAlignment.Center
        };
        NumericUpDown mipBox = CreateIntegerNumericBox(
            currentMaxMip,
            val =>
            {
                metadata.CustomProperties[maxMipKey] = val;
                ReimportTexture();
            },
            false, 0, 16);
        DockPanel.SetDock(mipLabel, Dock.Left);
        mipRow.Children.Add(mipLabel);
        mipRow.Children.Add(mipBox);
        settingsPanel.Children.Add(mipRow);

        panel.Children.Add(settingsPanel);
    }

    #endregion

    private static void ApplyTooltip(Control control, FieldInfo field)
    {
        TooltipAttribute? tooltipAttr = field.GetCustomAttribute<TooltipAttribute>();
        if (tooltipAttr == null) return;

        StackPanel tooltipContent = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Thickness(2),
            Children =
            {
                new MaterialIcon { Kind = MaterialIconKind.InformationOutline, Width = 14, Height = 14, 
                    Foreground = EditorColor.FromRGB(148, 148, 148), VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = tooltipAttr.Tooltip, FontSize = 11, 
                    Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center },
            }
        };
        ToolTip.SetTip(control, tooltipContent);
        ToolTip.SetPlacement(control, PlacementMode.Top);
        ToolTip.SetShowDelay(control, 400);
        ToolTip.SetVerticalOffset(control, 4);
    }

    [GeneratedRegex(@"(\p{Ll})(\p{Lu})")]
    private static partial Regex FormattedFieldRegex();
}
