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
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using DivisionEngine.MathLib;
using DivisionEngine.MathUtilities;
using DivisionEngine.Projects;
using DivisionEngine.Projects.Assets;
using Material.Icons;
using Material.Icons.Avalonia;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Path = System.IO.Path;

namespace DivisionEngine.Editor;

/// <summary>
/// Represents all loaded assets windows.
/// </summary>
public partial class AssetsWindow : EditorWindow
{
    public enum ViewState { Tiles, List }

    public ViewState CurrentView { get; private set; }

    private static readonly List<AssetsWindow?> currentWindows = [];

    // Display panels
    private readonly ScrollViewer scrollViewer;
    private readonly UniformGrid assetsTileGrid;
    private readonly TableView tableView;
    private readonly ObservableCollection<AssetRowItem> rowItems = [];
    private readonly StackPanel emptyStateOverlay;
    private readonly TextBlock emptyStateText;

    // Header
    private readonly StackPanel header;
    private readonly TextBox directoryField;
    private readonly TextBlock itemCountText;
    private readonly Button upDirButton;
    private readonly Button viewButton;
    private readonly ComboBox filterDropdown;
    private readonly MaterialIcon viewButtonIcon;

    // Data vars
    private string currentPath;
    private AssetType currentFilter = AssetType.None;
    private static bool subscribedProjectEvents;
    private bool subscribedToFolderEvents;
    private AssetManager? subscribedAssetManager;

    // Asset tint
    private readonly Dictionary<string, Action<IBrush>> assetTintSetters = [];
    private static readonly IBrush TintLoaded = EditorColor.FromRGB(24, 48, 28);
    private static readonly IBrush TintLoading = EditorColor.FromRGB(22, 34, 58);
    private static readonly IBrush TintUnloaded = EditorColor.FromRGB(46, 24, 24);
    private static readonly IBrush TintHover = EditorColor.FromRGB(30, 30, 34);
    private static readonly IBrush TileDefaultBg = EditorColor.FromRGB(20, 20, 20);

    // Selection
    private string? selectedItemPath;
    private AssetRowItem? selectedRowItem;
    private readonly Dictionary<Border, string> tileIdentifiers = [];
    private readonly Dictionary<Border, bool> tileHovered = [];
    private readonly Dictionary<string, AssetRowItem> rowItemsByIdentifier = [];

    static AssetsWindow() => SubscribeToProjectEvents();

    private static void SubscribeToProjectEvents()
    {
        if (subscribedProjectEvents) return;
        ProjectManager.ProjectLoaded += OnProjectLoaded;
        ProjectManager.ProjectClosing += OnProjectClosing;
        ProjectManager.ProjectClosed += OnProjectClosed;
        subscribedProjectEvents = true;
    }

    public AssetsWindow()
    {
        InitializeComponent();

        // Tiles panel
        assetsTileGrid = new UniformGrid
        {
            Columns = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
        };
        scrollViewer = new ScrollViewer
        {
            Content = assetsTileGrid,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        scrollViewer.SizeChanged += (s, e) =>
        {
            if (CurrentView == ViewState.Tiles) Dispatcher.UIThread.Post(UpdateTileColumns);
        };

        // Table (list) panel - TableView handles its own virtualized scrolling
        tableView = new TableView
        {
            IsVisible = false,
            ItemsSource = rowItems,
            Columns =
            {
                new TableViewColumn
                {
                    Header = "Name",
                    Width = new GridLength(1, GridUnitType.Star),
                    CellTemplate = new FuncDataTemplate<AssetRowItem>((item, _) => BuildNameCell(item!)),
                },
                new TableViewColumn { Header = "Type", Width = new GridLength(140), Binding = new Binding(nameof(AssetRowItem.TypeLabel)) },
                new TableViewColumn { Header = "Size", Width = new GridLength(110), Binding = new Binding(nameof(AssetRowItem.SizeLabel)) },
            },
        };
        tableView.Styles.Add(new Style(s => s.OfType<TableViewRow>())
        {
            Setters =
            {
                new Setter(BackgroundProperty, new Binding(nameof(AssetRowItem.RowBackground))),
                new Setter(ContextMenuProperty, new Binding(nameof(AssetRowItem.RowContextMenu))),
            }
        });
        tableView.SelectionChanged += TableView_SelectionChanged;
        tableView.DoubleTapped += TableView_DoubleTapped;

        // Empty-state overlay (shown/hidden regardless of which view is active)
        emptyStateOverlay = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(20),
            IsHitTestVisible = false,
            IsVisible = false,
            Children =
            {
                new MaterialIcon 
                { 
                    Kind = MaterialIconKind.FolderOpenOutline, 
                    Width = 64, 
                    Height = 64, 
                    Foreground = Brushes.Gray
                },
                (emptyStateText = new TextBlock 
                { 
                    Foreground = Brushes.Gray, 
                    FontStyle = FontStyle.Italic, 
                    HorizontalAlignment = HorizontalAlignment.Center
                }),
            }
        };

        // Header setup
        header = new StackPanel
        {
            Background = EditorColor.FromRGB(28, 28, 28),
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            Height = 32,
            VerticalAlignment = VerticalAlignment.Top,
        };
        directoryField = new TextBox
        {
            Text = string.Empty,
            PlaceholderText = "No Project Loaded",
            FontSize = 11,
            Foreground = Brushes.White,
            Margin = new Thickness(5),
            BorderThickness = new Thickness(0),
            Background = EditorColor.FromRGB(20, 20, 20),
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        itemCountText = new TextBlock
        {
            Text = "0 items",
            FontSize = 12,
            Foreground = EditorColor.FromRGB(128, 128, 128),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(5),
        };
        upDirButton = new Button
        {
            Content = new MaterialIcon { Kind = MaterialIconKind.FolderUpload, Width = 18, Height = 18, 
                Foreground = EditorColor.FromRGB(80, 80, 80) },
            Background = EditorColor.FromRGB(12, 12, 12),
            Margin = new Thickness(8, 2, 2, 2),
            Padding = new Thickness(3, 1, 3, 1),
            VerticalAlignment = VerticalAlignment.Center,
        };
        viewButtonIcon = new MaterialIcon
        {
            Kind = MaterialIconKind.FormatListBulleted,
            Width = 18,
            Height = 18,
            Foreground = EditorColor.FromRGB(80, 80, 80),
            VerticalAlignment = VerticalAlignment.Center,
        };
        viewButton = new Button
        {
            Content = viewButtonIcon,
            Background = EditorColor.FromRGB(12, 12, 12),
            Margin = new Thickness(2, 2, 2, 2),
            Padding = new Thickness(3, 1, 3, 1),
            VerticalAlignment = VerticalAlignment.Center,
        };

        filterDropdown = new ComboBox
        {
            MinWidth = 80,
            Margin = new Thickness(2, 2, 2, 2),
            Foreground = Brushes.White,
            Background = EditorColor.FromRGB(17, 17, 17),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        filterDropdown.Items.Add(new ComboBoxItem { Content = "All Assets", Tag = AssetType.None });
        filterDropdown.Items.Add(new ComboBoxItem { Content = "Textures", Tag = AssetType.Texture });
        filterDropdown.Items.Add(new ComboBoxItem { Content = "Models/SDF", Tag = AssetType.SDF });
        filterDropdown.Items.Add(new ComboBoxItem { Content = "Materials", Tag = AssetType.Material });
        filterDropdown.Items.Add(new ComboBoxItem { Content = "Scripts", Tag = AssetType.Script });
        filterDropdown.Items.Add(new ComboBoxItem { Content = "Audio", Tag = AssetType.Audio });
        filterDropdown.Items.Add(new ComboBoxItem { Content = "Fonts", Tag = AssetType.Font });
        filterDropdown.SelectionChanged += (s, e) =>
        {
            if (s is not ComboBox combo || combo.SelectedItem is not ComboBoxItem item) return;
            currentFilter = (AssetType)item.Tag!;
            if (!string.IsNullOrEmpty(currentPath)) Dispatcher.UIThread.Post(() => LoadAssetsAtPathNew(currentPath));
        };
        filterDropdown.SelectedIndex = 0;

        directoryField.TextChanged += DirectoryField_TextChanged;
        upDirButton.Click += (s, e) => NavigateUpOneLevel();
        viewButton.Click += (s, e) => ToggleViewState();
        header.Children.Add(upDirButton);
        header.Children.Add(viewButton);
        header.Children.Add(filterDropdown);
        header.Children.Add(directoryField);
        header.Children.Add(itemCountText);

        Border separatorBorder = new Border { Background = EditorColor.FromRGB(68, 68, 68), Height = 1 };
        Panel contentArea = new Panel { Children = { scrollViewer, tableView, emptyStateOverlay } };
        contentArea.PointerPressed += (s, e) =>
        {
            if (e.Source is not Control source) return; // Get source control

            Control? current = source;
            while (current != null)
            {
                // Check if it's a tile (a Border in tileIdentifiers)
                if (current is Border border && tileIdentifiers.ContainsKey(border)) return;
                if (current is TableViewRow || current is TableViewCell) return;
                current = current.Parent as Control;
            }
            
            ClearSelection(); // Clicked on empty background
            if (Selection.SelectedType == SelectionType.Asset)
                Selection.Clear();
        };
        Grid grid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(32, GridUnitType.Pixel),
                new RowDefinition(1, GridUnitType.Pixel),
                new RowDefinition(1, GridUnitType.Star),
            }
        };
        Grid.SetRow(header, 0);
        Grid.SetRow(separatorBorder, 1);
        Grid.SetRow(contentArea, 2);
        grid.Children.Add(header);
        grid.Children.Add(separatorBorder);
        grid.Children.Add(contentArea);
        this.FindControl<Border>("MainBorder")!.Child = grid;

        AttachBackgroundContextMenu();

        CurrentView = ViewState.Tiles;
        currentPath = string.Empty;
        currentWindows.Add(this);

        // ProjectLoaded may have already fired before this window existed -
        // subscribe now instead of waiting for an event that already happened
        if (ProjectManager.IsCurrentLoaded)
        {
            SubscribeToFolderEventsIfNeeded();
            SyncAssetManagerSubscription();
        }

        Selection.OnSelectionChanged += OnGlobalSelectionChanged; // subscribe to selection
        Dispatcher.UIThread.Post(() => Setup(GetDefaultAssetsPath()));
    }

    #region assetManager

    private static string? GetDefaultAssetsPath()
    {
        if (!ProjectManager.IsCurrentLoaded) return null;
        string assetsPath = Path.Combine(ProjectManager.CurrentProjectPath!, "Assets");
        return Directory.Exists(assetsPath) ? assetsPath : ProjectManager.CurrentProjectPath;
    }

    private void SyncAssetManagerSubscription()
    {
        AssetManager? current = ProjectManager.AssetManager;
        if (ReferenceEquals(subscribedAssetManager, current)) return;

        subscribedAssetManager?.AssetLoadStateChanged -= OnAssetLoadStateChanged;
        subscribedAssetManager = current;
        subscribedAssetManager?.AssetLoadStateChanged += OnAssetLoadStateChanged;
    }

    private void UnsubscribeAssetManager()
    {
        if (subscribedAssetManager == null) return;
        subscribedAssetManager.AssetLoadStateChanged -= OnAssetLoadStateChanged;
        subscribedAssetManager = null;
    }

    private void OnAssetLoadStateChanged(string assetId, AssetLoadState state) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (assetTintSetters.TryGetValue(assetId, out Action<IBrush>? setter)) setter(GetTintForState(state));
        });

    private static IBrush GetTintForState(AssetLoadState state) => state switch
    {
        AssetLoadState.Loaded => TintLoaded,
        AssetLoadState.Loading => TintLoading,
        _ => TintUnloaded,
    };

    private void ApplyAssetTint(string assetId, Action<IBrush> setter)
    {
        assetTintSetters[assetId] = setter;
        setter(GetTintForState(ProjectManager.AssetManager?.GetLoadState(assetId) ?? AssetLoadState.Unloaded));
    }

    /// <summary>
    /// Loads a texture preview bitmap from disk, for thumbnailing texture asset tiles.
    /// </summary>
    private static async Task<Bitmap?> LoadTexturePreviewAsync(string filePath)
    {
        try
        {
            using FileStream stream = File.OpenRead(filePath);
            return await Task.Run(() => new Bitmap(stream));
        }
        catch (Exception ex)
        {
            Debug.Error($"Failed to load texture preview: {ex.Message}");
            return null;
        }
    }

    #endregion
    #region contextMenu

    private void AttachBackgroundContextMenu()
    {
        ContextMenu menu = CreateStyledContextMenu();
        menu.Items.Add(EditorUI.CreateContextMenuItem("Show in Explorer", MaterialIconKind.FolderOpen, () =>
        {
            if (!string.IsNullOrEmpty(currentPath) && Directory.Exists(currentPath)) Process.Start("explorer.exe", currentPath);
        }));
        menu.Items.Add(new Separator());
        menu.Items.Add(EditorUI.CreateContextMenuItem("New Folder", MaterialIconKind.FolderPlus, () => CreateNewAsset(true)));
        menu.Items.Add(new Separator());
        menu.Items.Add(EditorUI.CreateContextMenuItem("New Component", MaterialIconKind.CodeBraces, () =>
            CreateNewAsset(false, "cs", ComponentTemplate, "NewComponent")));
        menu.Items.Add(EditorUI.CreateContextMenuItem("New System", MaterialIconKind.Cog, () =>
            CreateNewAsset(false, "cs", SystemTemplate, "NewSystem")));
        menu.Items.Add(EditorUI.CreateContextMenuItem("New Singleton", MaterialIconKind.Numeric1Circle, () =>
            CreateNewAsset(false, "cs", SingletonTemplate, "NewSingleton")));
        menu.Items.Add(EditorUI.CreateContextMenuItem("New Script", MaterialIconKind.FileCode, () =>
            CreateNewAsset(false, "cs", BlankTemplate, "NewScript")));
        menu.Items.Add(EditorUI.CreateContextMenuItem("New Compute Shader", MaterialIconKind.Chip, () =>
            CreateNewAsset(false, "cs", ComputeShaderTemplate, "NewShader")));
        menu.Items.Add(new Separator());
        menu.Items.Add(EditorUI.CreateContextMenuItem("New Text File", MaterialIconKind.FileDocument, () => CreateNewAsset(false, "txt", "")));
        menu.Items.Add(EditorUI.CreateContextMenuItem("New JSON File", MaterialIconKind.CodeJson, () => CreateNewAsset(false, "json", "{\n    \n}")));

        // Shared between tile mode and table mode - a ContextMenu isn't part of the
        // visual tree until opened, so one instance can serve both owner controls.
        scrollViewer.ContextMenu = menu;
        tableView.ContextMenu = menu;
    }

    private void CreateNewAsset(bool isFolder, string extension = "", string defaultContent = "", string? defaultName = null)
    {
        Border tempItem = new Border
        {
            Width = 80,
            Height = 85,
            Background = EditorColor.FromRGB(34, 34, 68),
            BorderThickness = new Thickness(2),
            BorderBrush = EditorColor.FromRGB(100, 100, 200),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(5),
            Padding = new Thickness(5),
        };
        StackPanel itemStack = new() { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        itemStack.Children.Add(isFolder
            ? new MaterialIcon { Kind = MaterialIconKind.Folder, Width = 48, Height = 48, Foreground = EditorColor.FromColor(ColorPalette.Mint) }
            : CreateFileIcon($".{extension}", 48));

        TextBox nameBox = new()
        {
            // Use the caller-supplied default name when provided (e.g. "NewSystem"
            // for the system template) instead of the generic "NewCSFile".
            Text = isFolder ? "New Folder" : (defaultName ?? $"New{extension.ToUpper()}File"),
            FontSize = 10,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Width = 70,
            MaxLength = 50,
            Background = EditorColor.FromRGB(40, 40, 40),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(1),
            BorderBrush = EditorColor.FromRGB(100, 100, 100),
            Margin = new Thickness(0, 5, 0, 0)
        };
        itemStack.Children.Add(nameBox);
        if (!isFolder && !string.IsNullOrEmpty(extension))
            itemStack.Children.Add(new TextBlock { Text = $".{extension.ToUpper()}", Foreground = Brushes.LightGray, FontSize = 8, HorizontalAlignment = HorizontalAlignment.Center });

        tempItem.Child = itemStack;
        assetsTileGrid.Children.Insert(0, tempItem); // Note: temp creation UI only renders in Tiles view (matches prior behavior)
        nameBox.Focus();
        nameBox.SelectAll();

        bool completed = false;
        void CompleteCreation(bool cancel)
        {
            if (completed) return;
            completed = true;
            if (!cancel)
            {
                string newName = SanitizeFileName(nameBox.Text ?? string.Empty);
                if (!string.IsNullOrEmpty(newName)) Task.Run(() => CreateFileOrFolderAsync(newName, isFolder, extension, defaultContent));
            }
            assetsTileGrid.Children.Remove(tempItem);
        }

        nameBox.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter) { CompleteCreation(false); e.Handled = true; }
            else if (e.Key == Key.Escape) { CompleteCreation(true); e.Handled = true; }
        };
        nameBox.LostFocus += (s, e) => Dispatcher.UIThread.Post(() => CompleteCreation(true), DispatcherPriority.Background);

        UpdateTileColumns();
    }

    private async void CreateFileOrFolderAsync(string name, bool isFolder, string extension, string defaultContent)
    {
        try
        {
            if (isFolder) Directory.CreateDirectory(Path.Combine(currentPath, name));
            else
            {
                string fileName = extension == "cs" ? name : $"{name}.{extension}";
                if (!fileName.EndsWith($".{extension}")) fileName += $".{extension}";
                await File.WriteAllTextAsync(Path.Combine(currentPath, fileName), defaultContent);
            }
            Debug.Info($"Created {(isFolder ? "folder" : "file")}: {name}");
        }
        catch (Exception ex)
        {
            Debug.Error($"Failed to create {(isFolder ? "folder" : "file")}: {name}", ex);
        }
    }

    #endregion
    #region scriptTemplates

    private const string ComponentTemplate =
        "using DivisionEngine;\n" +
        "using DivisionEngine.Components;\n" +
        "using DivisionEngine.Components.FieldAttributes;\n" +
        "using DivisionEngine.MathUtilities;\n\n" +
        "public class NewComponent : IComponent\n" +
        "{\n" +
        "    [Range(0f, 1f)] public float demoValue;\n\n" +
        "    public NewComponent()\n" +
        "    {\n" +
        "        demoValue = 1f;\n" +
        "    }\n\n" +
        "    public IComponent Clone() => new NewComponent\n" +
        "    {\n" +
        "        demoValue = demoValue,\n" +
        "    };\n" +
        "}\n";

    private const string SystemTemplate =
        "using DivisionEngine;\n\n" +
        "public class NewSystem : SystemBase\n" +
        "{\n" +
        "    public override void Update()\n" +
        "    {\n" +
        "        // Called once per frame.\n" +
        "    }\n" +
        "}\n";

    private const string SingletonTemplate =
        "using DivisionEngine;\n" +
        "using DivisionEngine.Components;\n\n" +
        "/// <summary>\n" +
        "/// A globally-accessible component. Only one instance should exist per world.\n" +
        "/// </summary>\n" +
        "public class NewSingleton : IComponent\n" +
        "{\n" +
        "    public static NewSingleton? Instance { get; private set; }\n\n" +
        "    public NewSingleton()\n" +
        "    {\n" +
        "        Instance = this;\n" +
        "    }\n\n" +
        "    public IComponent Clone() => new NewSingleton();\n" +
        "}\n";

    private const string BlankTemplate =
        "using DivisionEngine;\n" +
        "using DivisionEngine.Components;\n" +
        "using DivisionEngine.Components.FieldAttributes;\n" +
        "using DivisionEngine.MathUtilities;\n\n" +
        "// Add your code here.\n";

    private const string ComputeShaderTemplate =
        "using ComputeSharp;\n\n" +
        "[ThreadGroupSize(DefaultThreadGroupSizes.XY)]\n" +
        "[GeneratedComputeShaderDescriptor]\n" +
        "public readonly partial struct NewShader : IComputeShader\n" +
        "{\n" +
        "    public readonly ReadWriteBuffer<float> Buffer;\n\n" +
        "    public void Execute()\n" +
        "    {\n" +
        "        Buffer[ThreadIds.X] = ThreadIds.X;\n" +
        "    }\n" +
        "}\n";

    #endregion
    #region navigation

    private void DirectoryField_TextChanged(object? sender, TextChangedEventArgs e)
    {
        string? newPath = directoryField.Text;
        if (string.IsNullOrEmpty(newPath) || !Directory.Exists(newPath)) return;
        currentPath = newPath;
        Debug.Info($"Directory field updating assets path to: {currentPath}");
        Dispatcher.UIThread.Post(() => LoadAssetsAtPathNew(newPath));
    }

    public static void LoadAssetsForCurrentProject()
    {
        ValidateWindows();
        foreach (AssetsWindow? window in currentWindows)
        {
            string? path = string.IsNullOrEmpty(window!.currentPath) ? GetDefaultAssetsPath() : window.currentPath;
            window.Setup(path);
        }
    }

    public static void LoadAssets(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        ValidateWindows();
        foreach (AssetsWindow? window in currentWindows) window!.Setup(path);
    }

    private static void ValidateWindows()
    {
        foreach (AssetsWindow? window in currentWindows.ToArray())
        {
            if (window == null || !window.IsLoaded)
            {
                window?.UnsubscribeAll();
                currentWindows.Remove(window);
            }
        }
    }

    private void UnsubscribeAll()
    {
        if (subscribedToFolderEvents)
        {
            AssetDatabase.FolderChanged -= OnAssetFolderChanged;
            subscribedToFolderEvents = false;
        }
        UnsubscribeAssetManager();
    }

    private void SubscribeToFolderEventsIfNeeded()
    {
        if (subscribedToFolderEvents) return;
        AssetDatabase.FolderChanged += OnAssetFolderChanged;
        subscribedToFolderEvents = true;
    }

    private bool Setup(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            Debug.Warning("Could not load assets, no project is loaded");
            directoryField.PlaceholderText = "No Project Loaded";
            directoryField.Text = string.Empty;
            itemCountText.Text = "0 items";
            return false;
        }
        currentPath = path;
        if (path == directoryField.Text) Dispatcher.UIThread.Post(() => LoadAssetsAtPathNew(path));
        else directoryField.Text = path;
        return true;
    }

    private bool NavigateUpOneLevel()
    {
        if (string.IsNullOrEmpty(currentPath)) return false;
        DirectoryInfo? parent = new DirectoryInfo(currentPath).Parent;
        if (parent == null) return false;
        Dispatcher.UIThread.Post(() => Setup(parent.FullName));
        return true;
    }

    private void ToggleViewState()
    {
        CurrentView = CurrentView == ViewState.Tiles ? ViewState.List : ViewState.Tiles;
        viewButtonIcon.Kind = CurrentView == ViewState.Tiles ? MaterialIconKind.FormatListBulleted : MaterialIconKind.ViewGrid;
        Setup(currentPath);
    }

    private void LoadAssetsAtPathNew(string path)
    {
        try
        {
            assetTintSetters.Clear();
            assetsTileGrid.Children.Clear();
            rowItems.Clear();
            tileIdentifiers.Clear();
            tileHovered.Clear();
            rowItemsByIdentifier.Clear();
            HideEmptyState();

            string assetsRoot = Path.Combine(ProjectManager.CurrentProjectPath!, "Assets");
            if (path.StartsWith(assetsRoot)) LoadAssetsUsingDatabase(path);
            else LoadAssetsUsingFileSystem(path);

            ClearSelection();
        }
        catch (Exception ex)
        {
            Debug.Error("Failed to load assets", ex);
            ShowEmptyState($"Error: {ex.Message}");
            itemCountText.Text = "Error";
        }
    }

    private void LoadAssetsUsingDatabase(string path)
    {
        if (!ProjectManager.IsCurrentLoaded || AssetDatabase.Folders.Count == 0) { LoadAssetsUsingFileSystem(path); return; }

        DirectoryInfo pathInfo = new(path);
        if (!pathInfo.Exists) { ShowEmptyState("Directory does not exist"); itemCountText.Text = "0 items"; return; }

        string relativePath = AssetDatabase.GetProjectRelativePath(path);
        if (relativePath == ".") relativePath = "";

        DirectoryInfo[] folders = pathInfo.GetDirectories();
        List<AssetMetadata> assets = [.. AssetDatabase.GetAssetsInFolder(relativePath)];
        if (currentFilter != AssetType.None) assets = [.. assets.Where(a => a.Type == currentFilter)];

        PopulateItems(folders, null, assets);

        int total = folders.Length + assets.Count;
        itemCountText.Text = $"{total} item{(total != 1 ? "s" : "")}";
        if (total == 0) ShowEmptyState("No Assets");
    }

    private void LoadAssetsUsingFileSystem(string path)
    {
        DirectoryInfo pathInfo = new(path);
        if (!pathInfo.Exists) { ShowEmptyState("Directory does not exist"); itemCountText.Text = "0 items"; return; }

        DirectoryInfo[] folders = pathInfo.GetDirectories();
        FileInfo[] files = pathInfo.GetFiles();
        PopulateItems(folders, files, null);

        int total = folders.Length + files.Length;
        itemCountText.Text = $"{total} item{(total != 1 ? "s" : "")}";
        if (total == 0) ShowEmptyState("No Items");
    }

    private void PopulateItems(IEnumerable<DirectoryInfo> folders, IEnumerable<FileInfo>? files, IEnumerable<AssetMetadata>? assets)
    {
        scrollViewer.IsVisible = CurrentView == ViewState.Tiles;
        tableView.IsVisible = CurrentView == ViewState.List;

        if (CurrentView == ViewState.Tiles)
        {
            foreach (DirectoryInfo folder in folders) CreateFolderTile(folder);
            if (files != null) foreach (FileInfo file in files) CreateFileTile(file);
            if (assets != null) foreach (AssetMetadata asset in assets) CreateAssetTile(asset);
            UpdateTileColumns();
        }
        else
        {
            foreach (DirectoryInfo folder in folders) AddFolderRow(folder);
            if (files != null) foreach (FileInfo file in files) AddFileRow(file);
            if (assets != null) foreach (AssetMetadata asset in assets) AddAssetRow(asset);
        }
    }

    private static async Task<long> AccumulateFolderSize(DirectoryInfo pathInfo)
    {
        long totalSize = 0;
        int taskDelayTracker = 10;
        EnumerationOptions options = new() { IgnoreInaccessible = true, MaxRecursionDepth = int.MaxValue, RecurseSubdirectories = true };

        foreach (FileInfo file in pathInfo.EnumerateFiles("*", options))
        {
            totalSize += file.Length;
            if (--taskDelayTracker < 0) { await Task.Delay(1); taskDelayTracker = 10; }
        }
        return totalSize;
    }

    private void UpdateTileColumns()
    {
        if (CurrentView != ViewState.Tiles) return;
        double availableWidth = scrollViewer.Bounds.Width;
        if (scrollViewer.VerticalScrollBarVisibility == ScrollBarVisibility.Auto) availableWidth -= 18;
        if (availableWidth <= 0) return;
        int newColumns = math.max(1, (int)(availableWidth / 90));
        if (assetsTileGrid.Columns != newColumns) assetsTileGrid.Columns = newColumns;
    }

    #endregion
    #region selection

    private void OnGlobalSelectionChanged(object? selection)
    {
        if (selection is string assetId && AssetDatabase.GetAssetMetadataByID(assetId) != null)
            SelectItem(assetId); // Asset selected from elsewhere
        else ClearSelection();
    }

    private void SelectItem(string path)
    {
        selectedItemPath = path;
        UpdateTileHighlights();
        UpdateListSelection(path);
    }

    private void ClearSelection()
    {
        selectedItemPath = null;
        selectedRowItem = null;
        UpdateTileHighlights();
        tableView.SelectedItem = null;
    }

    private void UpdateTileHighlights()
    {
        foreach (var kv in tileIdentifiers) UpdateTileBackground(kv.Key);
    }

    private void UpdateListSelection(string identifier)
    {
        if (rowItemsByIdentifier.TryGetValue(identifier, out AssetRowItem? row))
        {
            tableView.SelectedItem = row;
            selectedRowItem = row;
        }
        else
        {
            tableView.SelectedItem = null;
            selectedRowItem = null;
        }
    }

    #endregion
    #region tileView

    private Border BuildTile(Func<double, MaterialIcon> iconFactory, string name, string? subtitle,
        Action onTap, Action onDoubleTap, ContextMenu contextMenu,
        string? tintAssetId = null, string? identifier = null,
        Func<Task<Bitmap?>>? thumbnailLoader = null,
        string? badgeText = null, IBrush? badgeBrush = null,
        Color? accentColor = null)
    {
        Border border = CreateTileBorder();

        // Accent the border for scripts
        if (accentColor.HasValue)
        {
            border.BorderBrush = new SolidColorBrush(accentColor.Value);
            border.BorderThickness = new Thickness(2, 2, 2, 2);
        }

        // Apply tint (sets Tag and Background)
        if (tintAssetId != null) ApplyAssetTint(tintAssetId, brush => SetTileBaseBackground(border, brush));

        string id = identifier ?? tintAssetId ?? Guid.NewGuid().ToString();
        tileIdentifiers[border] = id;
        tileHovered[border] = false;

        border.PointerEntered += (_, _) =>
        {
            tileHovered[border] = true;
            UpdateTileBackground(border);
        };
        border.PointerExited += (_, _) =>
        {
            tileHovered[border] = false;
            UpdateTileBackground(border);
        };

        // Base content - icon, name, subtitle.
        string display = name.Length > 12 ? string.Concat(name.AsSpan(0, 10), "..") : name;
        MaterialIcon icon = iconFactory(48);
        icon.Margin = new Thickness(0, 0, 0, 5);

        StackPanel stack = new()
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 8, 6, 8), // replaces the border's old Padding
        };
        stack.Children.Add(icon);
        stack.Children.Add(new TextBlock
        {
            Text = display,
            Foreground = Brushes.White,
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 80,
        });
        if (subtitle != null) stack.Children.Add(new TextBlock
        {
            Text = subtitle,
            Foreground = Brushes.LightGray,
            FontSize = 8,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        Grid contentGrid = new();
        contentGrid.Children.Add(stack);

        if (!string.IsNullOrEmpty(badgeText))
        {
            Border badge = new()
            {
                Background = badgeBrush ?? EditorColor.FromRGB(60, 60, 60),
                // Top-right rounded to match the border's corner; bottom-left rounded for a "ribbon tab" silhouette
                CornerRadius = new CornerRadius(0, 3, 0, 5),
                Padding = new Thickness(5, 1, 6, 1),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock
                {
                    Text = badgeText,
                    FontSize = 7,
                    FontWeight = FontWeight.Bold,
                    Foreground = Brushes.White,
                },
            };
            contentGrid.Children.Add(badge);
        }

        border.Child = contentGrid;
        border.Tapped += (_, _) => onTap();
        border.DoubleTapped += (_, _) => onDoubleTap();
        border.ContextMenu = contextMenu;

        if (thumbnailLoader != null) _ = LoadAndApplyThumbnailAsync(thumbnailLoader, stack, icon);

        return border;
    }

    /// <summary>
    /// Loads a tile's thumbnail asynchronously and swaps it in for the placeholder icon
    /// once ready. If the tile has since been discarded (folder navigated away from before
    /// the load finished), the icon is no longer in the panel and the swap is silently skipped.
    /// </summary>
    private static async Task LoadAndApplyThumbnailAsync(Func<Task<Bitmap?>> loader, StackPanel stack, MaterialIcon icon)
    {
        Bitmap? bitmap;
        try { bitmap = await loader(); }
        catch { bitmap = null; }
        if (bitmap == null) return;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            int index = stack.Children.IndexOf(icon);
            if (index < 0) return; // tile discarded before the load finished

            stack.Children[index] = new Image
            {
                Source = bitmap,
                Width = 48,
                Height = 48,
                Stretch = Stretch.UniformToFill,
                Margin = icon.Margin,
            };
        });
    }

    private void UpdateTileBackground(Border border)
    {
        if (!tileIdentifiers.TryGetValue(border, out string? id)) return;

        bool isSelected = id == selectedItemPath;
        bool isHovered = tileHovered.TryGetValue(border, out bool hover) && hover;

        IBrush background;
        if (isSelected) background = EditorColor.FromRGB(60, 90, 120); // selection color
        else if (isHovered) background = TintHover;
        else background = (IBrush)border.Tag!; // base background (tint or default)
        border.Background = background;
    }

    private void CreateFolderTile(DirectoryInfo folder)
    {
        Border border = null!;
        border = BuildTile(
            size => new MaterialIcon
            {
                Kind = MaterialIconKind.Folder,
                Width = size,
                Height = size,
                Foreground = EditorColor.FromColor(ColorPalette.Mint),
            },
            folder.Name, null,
            onTap: () => SelectItem(folder.FullName),
            () => Dispatcher.UIThread.Post(() => Setup(folder.FullName)),
            BuildFolderContextMenu(folder, () => ShowInPlaceRename(border, folder.Name, folder.FullName, true)));
        assetsTileGrid.Children.Add(border);
    }

    private void CreateFileTile(FileInfo file)
    {
        Border border = null!;
        border = BuildTile(
            size => CreateFileIcon(file.Extension, size),
            Path.GetFileNameWithoutExtension(file.Name), file.Extension.ToUpperInvariant(),
            onTap: () => SelectItem(file.FullName),
            () => EditorUI.OpenFile(file),
            BuildFileContextMenu(file, () => 
            ShowInPlaceRename(border, Path.GetFileNameWithoutExtension(file.Name), file.FullName, false, file.Extension)));
        assetsTileGrid.Children.Add(border);
    }

    private void CreateAssetTile(AssetMetadata asset)
    {
        string fullPath = Path.Combine(ProjectManager.CurrentProjectPath ?? "", asset.RelativePath);

        Func<Task<Bitmap?>>? thumbnailLoader = null;
        if (asset.Type == AssetType.Texture && File.Exists(fullPath))
            thumbnailLoader = () => LoadTexturePreviewAsync(fullPath);

        // Script assets get an accent border + corner badge showing their target
        string? badgeText = null;
        IBrush? badgeBrush = null;
        Color? accentColor = null;
        if (asset.Type == AssetType.Script)
        {
            bool isEditorScript = asset.RelativePath
                .Replace('/', '\\')
                .Contains(@"\Editor\", StringComparison.OrdinalIgnoreCase);

            if (isEditorScript)
            {
                accentColor = Color.FromRgb(180, 120, 50);   // warm amber
                badgeBrush = new SolidColorBrush(Color.FromRgb(140, 90, 40));
                badgeText = "EDITOR";
            }
            else
            {
                accentColor = Color.FromRgb(60, 120, 190);   // cool blue
                badgeBrush = new SolidColorBrush(Color.FromRgb(40, 80, 130));
                badgeText = "PLAYER";
            }
        }

        Border border = null!;
        border = BuildTile(
            size => EditorUI.CreateAssetTypeIcon(asset.Type, size),
            Path.GetFileNameWithoutExtension(asset.FileName), GetAssetTypeDisplayName(asset.Type),
            onTap: () =>
            {
                Selection.SelectAsset(asset.ID);
                SelectItem(asset.ID);
            },
            () => OpenAssetDoubleTap(asset),
            BuildAssetContextMenu(asset, () => ShowInPlaceRename(border,
                Path.GetFileNameWithoutExtension(asset.FileName), fullPath, false,
                Path.GetExtension(asset.FileName))),
            asset.ID,
            thumbnailLoader: thumbnailLoader,
            badgeText: badgeText,
            badgeBrush: badgeBrush,
            accentColor: accentColor);
        assetsTileGrid.Children.Add(border);
    }

    private static void OpenAssetDoubleTap(AssetMetadata asset)
    {
        if (asset.Type == AssetType.Script && ProjectManager.IsCurrentLoaded)
        {
            string fullPath = Path.Combine(ProjectManager.CurrentProjectPath!, asset.RelativePath);
            VisualStudioLauncher.OpenSolution(ProjectManager.CurrentProjectPath!, fullPath);
            return;
        }
        EditorUI.OpenAsset(asset);
    }

    private static Border CreateTileBorder(double width = 80, double height = 85) => new()
    {
        Width = width,
        Height = height,
        BorderThickness = new Thickness(0, 0, 1, 1),
        BorderBrush = EditorColor.FromRGB(10, 10, 10),
        Background = TileDefaultBg,
        Tag = TileDefaultBg,
        CornerRadius = new CornerRadius(4),
        Margin = new Thickness(5),
        Cursor = new Cursor(StandardCursorType.Hand),
        ClipToBounds = true,
    };

    private static void SetTileBaseBackground(Border border, IBrush background)
    {
        border.Tag = background;
        border.Background = background;
    }

    #endregion
    #region tableView

    private void AddFolderRow(DirectoryInfo folder)
    {
        AssetRowItem row = new()
        {
            Name = folder.Name,
            IconFactory = size => new MaterialIcon
            {
                Kind = MaterialIconKind.Folder,
                Width = size,
                Height = size,
                Foreground = EditorColor.FromColor(ColorPalette.Mint),
            },
            IsFolder = true,
            FullPath = folder.FullName,
            TypeLabel = "Folder",
            SizeLabel = "...",
        };
        row.RowContextMenu = BuildFolderContextMenu(folder, () => row.IsEditing = true);
        rowItems.Add(row);
        rowItemsByIdentifier[folder.FullName] = row;
        _ = UpdateFolderSizeAsync(folder, row);
    }

    private void AddFileRow(FileInfo file)
    {
        AssetRowItem row = new()
        {
            Name = Path.GetFileNameWithoutExtension(file.Name),
            IconFactory = size => CreateFileIcon(file.Extension, size),
            IsFolder = false,
            FullPath = file.FullName,
            TypeLabel = file.Extension.ToUpperInvariant(),
            SizeLabel = EditorUI.FormatFileSize(file.Length),
        };
        row.RowContextMenu = BuildFileContextMenu(file, () => row.IsEditing = true);
        rowItems.Add(row);
        rowItemsByIdentifier[file.FullName] = row;
    }

    private void AddAssetRow(AssetMetadata asset)
    {
        // Script badges
        string badgeText = string.Empty;
        IBrush? badgeBrush = null;
        if (asset.Type == AssetType.Script)
        {
            bool isEditorScript = asset.RelativePath
                .Replace('/', '\\')
                .Contains(@"\Editor\", StringComparison.OrdinalIgnoreCase);

            if (isEditorScript)
            {
                badgeText = "E";
                badgeBrush = new SolidColorBrush(Color.FromRgb(140, 90, 40));
            }
            else
            {
                badgeText = "P";
                badgeBrush = new SolidColorBrush(Color.FromRgb(40, 80, 130));
            }
        }

        AssetRowItem row = new()
        {
            Name = Path.GetFileNameWithoutExtension(asset.FileName),
            IconFactory = size => EditorUI.CreateAssetTypeIcon(asset.Type, size),
            IsFolder = false,
            FullPath = Path.Combine(ProjectManager.CurrentProjectPath ?? "", asset.RelativePath),
            AssetId = asset.ID,
            AssetMetadata = asset,
            TypeLabel = GetAssetTypeDisplayName(asset.Type),
            SizeLabel = EditorUI.FormatFileSize(asset.FileSize),
            BadgeText = badgeText,
            BadgeBrush = badgeBrush,
        };
        row.RowContextMenu = BuildAssetContextMenu(asset, () => row.IsEditing = true);
        rowItems.Add(row);
        rowItemsByIdentifier[asset.ID] = row;
        ApplyAssetTint(asset.ID, brush => row.RowBackground = brush);
    }

    private static async Task UpdateFolderSizeAsync(DirectoryInfo folder, AssetRowItem row)
    {
        long size = await AccumulateFolderSize(folder);
        await Dispatcher.UIThread.InvokeAsync(() => row.SizeLabel = EditorUI.FormatFileSize(size));
    }

    private static StackPanel BuildNameCell(AssetRowItem item)
    {
        MaterialIcon icon = item.IconFactory(20);
        icon.Margin = new Thickness(0);

        TextBlock displayText = new()
        {
            FontSize = 13,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
        };
        displayText.Bind(TextBlock.TextProperty, new Binding(nameof(AssetRowItem.Name)));
        displayText.Bind(IsVisibleProperty, new Binding(nameof(AssetRowItem.IsEditing))
        {
            Converter = InvertBoolConverter.Instance,
        });

        // Badge for scripts - small "P" / "E" tag next to the icon
        Border? badge = null;
        if (!string.IsNullOrEmpty(item.BadgeText))
        {
            badge = new Border
            {
                Background = item.BadgeBrush ?? EditorColor.FromRGB(60, 60, 60),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(3, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = item.BadgeText,
                    FontSize = 9,
                    FontWeight = FontWeight.Bold,
                    Foreground = Brushes.White,
                },
            };
        }

        TextBox editBox = new()
        {
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            MaxLength = 50,
            Padding = new Thickness(2),
        };
        editBox.Bind(TextBox.TextProperty, new Binding(nameof(AssetRowItem.Name)));
        editBox.Bind(IsVisibleProperty, new Binding(nameof(AssetRowItem.IsEditing)));
        editBox.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter && s is TextBox box) { item.CommitRename(box.Text ?? string.Empty); e.Handled = true; }
            else if (e.Key == Key.Escape) { item.IsEditing = false; e.Handled = true; }
        };
        editBox.LostFocus += (_, _) => item.IsEditing = false;
        editBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && editBox.IsVisible)
                Dispatcher.UIThread.Post(() => { editBox.Focus(); editBox.SelectAll(); });
        };

        StackPanel panel = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(6, 2, 2, 2),
            Children = { icon },
        };
        if (badge != null) panel.Children.Add(badge);
        panel.Children.Add(displayText);
        panel.Children.Add(editBox);
        return panel;
    }

    private void TableView_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (tableView.SelectedItem is not AssetRowItem item || item.IsEditing) return;
        if (item.AssetId != null)
        {
            Selection.SelectAsset(item.AssetId);
            SelectItem(item.AssetId);
        }
        else if (item.IsFolder) SelectItem(item.FullPath);
        else SelectItem(item.FullPath);
    }

    private void TableView_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (tableView.SelectedItem is not AssetRowItem item || item.IsEditing) return;
        if (item.IsFolder) Dispatcher.UIThread.Post(() => Setup(item.FullPath));
        else if (item.AssetMetadata?.Type == AssetType.Script && ProjectManager.IsCurrentLoaded)
        {
            string fullPath = Path.Combine(ProjectManager.CurrentProjectPath!, item.AssetMetadata.RelativePath);
            VisualStudioLauncher.OpenSolution(ProjectManager.CurrentProjectPath!, fullPath);
        }
        else EditorUI.OpenFile(new FileInfo(item.FullPath));
    }

    #endregion
    #region contextMenus

    private static ContextMenu CreateStyledContextMenu() => new()
    {
        Background = EditorColor.FromRGB(68, 68, 68),
        BorderBrush = EditorColor.FromRGB(128, 128, 128),
    };

    private static ContextMenu BuildFolderContextMenu(DirectoryInfo folder, Action requestRename)
    {
        ContextMenu menu = CreateStyledContextMenu();
        menu.Items.Add(EditorUI.CreateContextMenuItem("Open", MaterialIconKind.FolderOpen, 
            () => Process.Start("explorer.exe", folder.FullName)));
        menu.Items.Add(EditorUI.CreateContextMenuItem("Rename", MaterialIconKind.Pencil, requestRename));
        menu.Items.Add(new Separator());
        menu.Items.Add(EditorUI.CreateContextMenuItem("Delete", MaterialIconKind.Delete, async () =>
        {
            if (!await ConfirmDeletion(folder.Name, "Folder", true)) return;
            try { Directory.Delete(folder.FullName, true); Debug.Info($"Deleted folder: {folder.Name}"); }
            catch (Exception ex) { Debug.Error($"Failed to delete folder: {folder.Name}", ex); }
        }, Brushes.Red));
        return menu;
    }

    private ContextMenu BuildFileContextMenu(FileInfo file, Action requestRename)
    {
        ContextMenu menu = CreateStyledContextMenu();
        menu.Items.Add(EditorUI.CreateContextMenuItem("Open", MaterialIconKind.FileDocument, 
            () => EditorUI.OpenFile(file)));
        menu.Items.Add(EditorUI.CreateContextMenuItem("Rename", MaterialIconKind.Pencil, requestRename));
        menu.Items.Add(EditorUI.CreateContextMenuItem("Copy Path", MaterialIconKind.ContentCopy, 
            () => CopyToClipboard(file.FullName)));
        menu.Items.Add(new Separator());
        menu.Items.Add(EditorUI.CreateContextMenuItem("Delete", MaterialIconKind.Delete, async () =>
        {
            if (!await ConfirmDeletion(file.Name, "File")) return;
            try { File.Delete(file.FullName); Debug.Info($"Deleted file: {file.Name}"); }
            catch (Exception ex) { Debug.Error($"Failed to delete file: {file.Name}", ex); }
        }, Brushes.Red));
        return menu;
    }

    private ContextMenu BuildAssetContextMenu(AssetMetadata asset, Action requestRename)
    {
        string fullPath = Path.Combine(ProjectManager.CurrentProjectPath ?? "", asset.RelativePath);
        ContextMenu menu = CreateStyledContextMenu();
        menu.Items.Add(EditorUI.CreateContextMenuItem("Open", MaterialIconKind.FileDocument, () => 
        {
            if (File.Exists(fullPath)) EditorUI.OpenFile(new FileInfo(fullPath));
        }));
        menu.Items.Add(EditorUI.CreateContextMenuItem("Show in Explorer", MaterialIconKind.FolderOpen, () =>
        {
            string? dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) Process.Start("explorer.exe", dir);
        }));
        menu.Items.Add(EditorUI.CreateContextMenuItem("Copy GUID", MaterialIconKind.Identifier, 
            () => CopyToClipboard(asset.ID)));
        menu.Items.Add(EditorUI.CreateContextMenuItem("Copy Path", MaterialIconKind.ContentCopy, 
            () => CopyToClipboard(asset.RelativePath)));
        menu.Items.Add(new Separator());
        menu.Items.Add(EditorUI.CreateContextMenuItem("Rename", MaterialIconKind.Pencil, () =>
        {
            if (File.Exists(fullPath)) requestRename();
        }));
        menu.Items.Add(new Separator());
        menu.Items.Add(EditorUI.CreateContextMenuItem("Delete", MaterialIconKind.Delete, async () =>
        {
            if (!File.Exists(fullPath) || !await ConfirmDeletion(asset.FileName, "Asset")) return;
            try { File.Delete(fullPath); Debug.Info($"Deleted asset: {asset.FileName}"); }
            catch (Exception ex) { Debug.Error($"Failed to delete asset: {asset.FileName}", ex); }
        }, Brushes.Red));
        return menu;
    }

    private void CopyToClipboard(string text)
    {
        IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null) return;
        DataTransfer data = new();
        data.Add(DataTransferItem.CreateText(text));
        clipboard.SetDataAsync(data);
    }

    private static async Task<bool> ConfirmDeletion(string itemName, string itemType, bool isFolder = false)
    {
        string message = isFolder
            ? $"Are you sure you want to delete '{itemName}' and ALL its contents?\n\nThis action cannot be undone."
            : $"Are you sure you want to delete '{itemName}'?\n\nThis action cannot be undone.";
        return await new ConfirmationDialog($"Delete {itemType}", message).ShowDialog<bool>(App.MainWindow!);
    }

    private static void ShowInPlaceRename(Border targetBorder, string currentName, string currentFullPath, bool isFolder, string extension = "")
    {
        Control? originalContent = targetBorder.Child;
        TextBox nameBox = new()
        {
            Text = currentName,
            FontSize = 10,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Width = 70,
            MaxLength = 50,
            Background = EditorColor.FromRGB(40, 40, 40),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(1),
            BorderBrush = EditorColor.FromRGB(100, 100, 100),
            Margin = new Thickness(0, 5, 0, 0)
        };

        StackPanel editStack = new()
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        editStack.Children.Add(isFolder ? new MaterialIcon
            {
                Kind = MaterialIconKind.Folder,
                Width = 48,
                Height = 48, 
                Foreground = EditorColor.FromColor(ColorPalette.Mint),
                Margin = new Thickness(0, 0, 0, 5),
            } : CreateFileIcon(extension, 48));
        editStack.Children.Add(nameBox);
        if (!isFolder && !string.IsNullOrEmpty(extension))
        {
            editStack.Children.Add(new TextBlock
            {
                Text = extension.ToUpper(),
                Foreground = Brushes.LightGray,
                FontSize = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
        }

        targetBorder.Child = new Border
        {
            Width = 80,
            Height = 85,
            Background = EditorColor.FromRGB(34, 34, 68),
            BorderThickness = new Thickness(2),
            BorderBrush = EditorColor.FromRGB(100, 100, 200),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(5),
            Padding = new Thickness(5),
            Child = editStack,
        };
        nameBox.Focus();
        nameBox.SelectAll();

        bool completed = false;
        void Complete(bool cancel)
        {
            if (completed) return;
            completed = true;
            if (!cancel) TryRenameOnDisk(currentFullPath, nameBox.Text ?? string.Empty, isFolder, extension);
            targetBorder.Child = originalContent;
        }

        nameBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Complete(false); e.Handled = true; }
            else if (e.Key == Key.Escape) { Complete(true); e.Handled = true; }
        };
        nameBox.LostFocus += (_, _) => Dispatcher.UIThread.Post(() => Complete(true), DispatcherPriority.Background);
    }

    private static bool TryRenameOnDisk(string currentFullPath, string rawNewName, bool isFolder, string extension)
    {
        string newName = SanitizeFileName(rawNewName);
        if (string.IsNullOrEmpty(newName)) return false;

        string directory = Path.GetDirectoryName(currentFullPath)!;
        string newPath = Path.Combine(directory, isFolder ? newName : newName + extension);
        if (newPath == currentFullPath) return false;

        try
        {
            if (isFolder) Directory.Move(currentFullPath, newPath);
            else File.Move(currentFullPath, newPath);
            return true;
        }
        catch (Exception ex)
        {
            Debug.Error($"Failed to rename {(isFolder ? "folder" : "file")}: {Path.GetFileName(currentFullPath)}", ex);
            return false;
        }
    }

    private static string SanitizeFileName(string name)
    {
        name = name.Trim();
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c.ToString(), "");
        return name;
    }

    #endregion
    #region iconsAndLabels

    private static MaterialIcon CreateFileIcon(string extension, double size)
    {
        MaterialIconKind iconKind = extension.ToLower() switch
        {
            ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tga" or ".tiff" or ".ico" => MaterialIconKind.Image,
            ".obj" or ".fbx" or ".gltf" or ".glb" or ".stl" => MaterialIconKind.CubeOutline,
            ".wav" or ".mp3" or ".ogg" or ".flac" => MaterialIconKind.Audio,
            ".cs" or ".js" or ".ts" or ".cpp" or ".h" or ".manifest" => MaterialIconKind.CodeBraces,
            ".dll" => MaterialIconKind.Library,
            ".json" or ".xml" or ".yml" or ".yaml" or ".axaml" => MaterialIconKind.CodeJson,
            ".txt" or ".md" or ".rtf" or ".csproj" or ".gitignore" or ".gitattributes" or ".sln" => MaterialIconKind.TextBox,
            ".shader" or ".hlsl" => MaterialIconKind.Eyedropper,
            ".mat" => MaterialIconKind.Palette,
            ".wld" => MaterialIconKind.ViewDashboard,
            _ => MaterialIconKind.FileDocument,
        };
        float4 iconColor = extension.ToLower() switch
        {
            ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tga" or ".tiff" or ".ico" => ColorPalette.SkyBlue,
            ".obj" or ".fbx" or ".gltf" or ".glb" or ".stl" => ColorPalette.LightSeaGreen,
            ".wav" or ".mp3" or ".ogg" or ".flac" => ColorPalette.Coral,
            ".cs" or ".js" or ".ts" or ".cpp" or ".h" or ".manifest" => ColorPalette.Khaki,
            ".dll" => ColorPalette.SandyBrown,
            ".json" or ".xml" or ".yml" or ".yaml" or ".axaml" => ColorPalette.PaleGreen,
            ".txt" or ".md" or ".rtf" or ".csproj" or ".gitignore" or ".gitattributes" or ".sln" => ColorPalette.Khaki,
            ".shader" or ".hlsl" => ColorPalette.SalmonPink,
            ".mat" => ColorPalette.Orange,
            ".wld" => ColorPalette.PaleGreen,
            _ => ColorPalette.Gray,
        };
        return new MaterialIcon
        {
            Kind = iconKind,
            Width = size,
            Height = size,
            Foreground = EditorColor.FromColor(iconColor),
            Margin = new Thickness(0, 0, 0, 5),
        };
    }

    private static string GetAssetTypeDisplayName(AssetType type) => type switch
    {
        AssetType.Texture => "Texture",
        AssetType.SDF => "SDF",
        AssetType.Material => "Material",
        AssetType.Script => "Script",
        AssetType.Audio => "Audio",
        AssetType.Font => "Font",
        _ => "Asset",
    };

    #endregion
    #region emptyState

    private void ShowEmptyState(string message)
    {
        emptyStateText.Text = message;
        emptyStateOverlay.IsVisible = true;
    }

    private void HideEmptyState() => emptyStateOverlay.IsVisible = false;

    #endregion
    #region projectFolderEvents

    private static void OnProjectLoaded()
    {
        ValidateWindows();
        foreach (AssetsWindow? window in currentWindows) window?.OnProjectLoadedInternal();
    }

    private static void OnProjectClosing()
    {
        ValidateWindows();
        foreach (AssetsWindow? window in currentWindows) window?.OnProjectClosingInternal();
    }

    private static void OnProjectClosed()
    {
        ValidateWindows();
        foreach (AssetsWindow? window in currentWindows) window?.OnProjectClosedInternal();
    }

    private void OnProjectLoadedInternal()
    {
        SubscribeToFolderEventsIfNeeded();
        SyncAssetManagerSubscription();
        string assetsPath = Path.Combine(ProjectManager.CurrentProjectPath!, "Assets");
        if (Directory.Exists(assetsPath)) Dispatcher.UIThread.Post(() => LoadAssets(assetsPath));
    }

    private void OnProjectClosingInternal() => UnsubscribeAssetManager();

    private void OnProjectClosedInternal()
    {
        if (subscribedToFolderEvents)
        {
            AssetDatabase.FolderChanged -= OnAssetFolderChanged;
            subscribedToFolderEvents = false;
        }
        Dispatcher.UIThread.Post(() =>
        {
            currentPath = string.Empty;
            directoryField.PlaceholderText = "No Project Loaded";
            directoryField.Text = string.Empty;
            itemCountText.Text = "0 items";
            assetsTileGrid.Children.Clear();
            rowItems.Clear();
            ShowEmptyState("No Project Loaded");
        });
    }

    private void OnAssetFolderChanged(string folderPath)
    {
        if (string.IsNullOrEmpty(currentPath) || !IsSameOrAncestorPath(currentPath, folderPath)) return;
        Dispatcher.UIThread.Post(() => LoadAssetsAtPathNew(currentPath), DispatcherPriority.Background);
    }

    private static bool IsSameOrAncestorPath(string viewedPath, string changedPath)
    {
        string a = Path.TrimEndingDirectorySeparator(Path.GetFullPath(viewedPath));
        string b = Path.TrimEndingDirectorySeparator(Path.GetFullPath(changedPath));
        if (a.Equals(b, StringComparison.OrdinalIgnoreCase)) return true;
        return b.StartsWith(a + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || a.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    /// <summary>
    /// Row data model backing the TableView. INotifyPropertyChanged so bound cells
    /// (name, type, size, tint, edit state) update live without rebuilding rows.
    /// </summary>
    private sealed class AssetRowItem : INotifyPropertyChanged
    {
        public required string Name { get; set; }
        public required Func<double, MaterialIcon> IconFactory { get; init; }
        public required bool IsFolder { get; init; }
        public required string FullPath { get; init; }
        public string? AssetId { get; init; }
        public AssetMetadata? AssetMetadata { get; init; }
        public ContextMenu? RowContextMenu { get; set; }

        private string typeLabel = string.Empty;
        public string TypeLabel { get => typeLabel; set => SetField(ref typeLabel, value); }

        private string sizeLabel = string.Empty;
        public string SizeLabel { get => sizeLabel; set => SetField(ref sizeLabel, value); }

        private IBrush rowBackground = TileDefaultBg;
        public IBrush RowBackground { get => rowBackground; set => SetField(ref rowBackground, value); }

        /// <summary>
        /// Short badge text shown next to the icon in table view (e.g. "P" or "E").
        /// Empty string for non-script assets.
        /// </summary>
        public string BadgeText { get; init; } = string.Empty;

        /// <summary>
        /// Accent color for the badge background. Ignored if BadgeText is empty.
        /// </summary>
        public IBrush? BadgeBrush { get; init; }

        private bool isEditing;
        public bool IsEditing { get => isEditing; set => SetField(ref isEditing, value); }

        public void CommitRename(string rawNewName)
        {
            TryRenameOnDisk(FullPath, rawNewName, IsFolder, IsFolder ? string.Empty : Path.GetExtension(FullPath));
            IsEditing = false;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    private sealed class InvertBoolConverter : IValueConverter
    {
        public static readonly InvertBoolConverter Instance = new();
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is bool b ? !b : value;
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is bool b ? !b : value;
    }
}
