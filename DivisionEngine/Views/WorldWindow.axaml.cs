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
using DivisionEngine.Components;
using DivisionEngine.Editor.Undo;
using Material.Icons;
using Material.Icons.Avalonia;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DivisionEngine.Editor;

/// <summary>
/// Window for displaying an entity hierarchy view of the ECS world.
/// </summary>
public partial class WorldWindow : EditorWindow
{
    private readonly StackPanel entitiesPanel;
    private readonly ScrollViewer scrollViewer;
    private readonly TextBlock entitiesHeader;
    private readonly TextBox headerSearchBox;
    private readonly StackPanel header;
    private readonly Grid mainGrid;

    private readonly DispatcherTimer worldWinUpdater;
    private readonly Dictionary<uint, EntityItemControl> entityControls;

    private readonly HashSet<uint> curEntities;
    private string searchFilter = string.Empty;

    /// <summary>
    /// Represents an item in the entity display list used by this window's stack panel, internal only.
    /// </summary>
    private class EntityItemControl : Border
    {
        private readonly uint entityId;
        private readonly TextBlock idText;
        private readonly TextBlock nameText;
        private readonly StackPanel panel;
        private readonly ContextMenu contextMenu;
        private readonly TextBox renameTextBox;
        private bool isRenaming = false;
        private bool isSelected = false;
        private bool isPointerOver = false;

        public uint EntityId => entityId;
        public string? CurrentName => nameText.Text;

        public EntityItemControl(uint entityId, World? world)
        {
            this.entityId = entityId;

            // Set up visual appearance
            Background = EditorColor.FromRGB(17, 17, 17);
            BorderBrush = EditorColor.FromRGB(10, 10, 10);
            BorderThickness = new Thickness(0, 0, 1, 1);
            Margin = new Thickness(0, 0);
            Padding = new Thickness(10, 2);
            CornerRadius = new CornerRadius(0);

            // Create content panel
            panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 2,
                IsHitTestVisible = true,
            };
            idText = new TextBlock
            {
                Text = $"{entityId}",
                FontSize = 10,
                Foreground = Brushes.Gray,
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 20,
            };
            nameText = new TextBlock
            {
                FontSize = 12,
                Foreground = EditorColor.FromRGB(220, 220, 220),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 0, 0),
            };
            renameTextBox = new TextBox
            {
                PlaceholderText = "Rename",
                FontSize = 12,
                IsVisible = false,
                Margin = new Thickness(2, 0, 0, 0),
                MinWidth = 100,
                Foreground = EditorColor.FromRGB(220, 220, 220),
                Background = EditorColor.FromRGB(17, 17, 17),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                VerticalAlignment = VerticalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };

            // Handle rename textbox events
            renameTextBox.KeyDown += (s, e) =>
            {
                if (e.Key == Avalonia.Input.Key.Enter) FinishRename();
                else if (e.Key == Avalonia.Input.Key.Escape) CancelRename();
            };
            renameTextBox.LostFocus += (s, e) => FinishRename();

            panel.Children.Add(idText);
            panel.Children.Add(nameText);
            panel.Children.Add(renameTextBox);
            Child = panel;

            contextMenu = CreateContextMenu(entityId); // Build context menu
            UpdateDisplay(world); // Update world display

            // Add handlers
            PointerPressed += (s, e) => 
            {
                if (!isRenaming) Selection.SelectEntity(entityId);
            };
            ContextRequested += (s, e) =>
            {
                if (!isRenaming)
                {
                    contextMenu?.Open(this);
                    e.Handled = true;
                }
            };
            PointerEntered += (s, e) =>
            {
                if (!isRenaming)
                {
                    isPointerOver = true;
                    UpdateBackground();
                }
            };
            PointerExited += (s, e) =>
            {
                if (!isRenaming)
                {
                    isPointerOver = false;
                    UpdateBackground();
                }
            };
        }

        /// <summary>
        /// Creates the context menu for entities in the world window.
        /// </summary>
        /// <param name="entityId">Entity to create context menu for</param>
        /// <returns>Context menu control for entity</returns>
        private ContextMenu CreateContextMenu(uint entityId)
        {
            ContextMenu menu = new ContextMenu
            {
                Background = EditorColor.FromRGB(68, 68, 68),
                BorderBrush = EditorColor.FromRGB(128, 128, 128),
            };
            List<MenuItem> menuItems = [];

            // Rename entity
            MenuItem renameItem = new MenuItem
            {
                Header = "Rename",
                Icon = new MaterialIcon { Kind = MaterialIconKind.Rename, FontSize = 12, Margin = new Thickness(0) },
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                Margin = new Thickness(0),
            };
            renameItem.Click += (s, e) => StartRename();
            menuItems.Add(renameItem);

            // Duplicate entity
            MenuItem duplicateItem = new MenuItem
            {
                Header = "Duplicate",
                Icon = new MaterialIcon { Kind = MaterialIconKind.ContentDuplicate, FontSize = 12, Margin = new Thickness(0) },
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                Margin = new Thickness(0),
            };
            duplicateItem.Click += (s, e) =>
            {
                World? w = WorldManager.CurrentWorld;
                if (w != null && w.EntityExists(entityId))
                    UndoManager.Execute(new DuplicateEntityCommand(entityId, w));
            };
            menuItems.Add(duplicateItem);

            // Delete entity
            MenuItem deleteItem = new MenuItem
            {
                Header = "Delete",
                Icon = new MaterialIcon { Kind = MaterialIconKind.Delete, FontSize = 12, Margin = new Thickness(0) },
                Background = Brushes.Transparent,
                Foreground = EditorColor.FromRGB(220, 68, 68),
                Margin = new Thickness(0),
            };
            deleteItem.Click += (s, e) =>
            {
                World? w = WorldManager.CurrentWorld;
                if (w != null && w.EntityExists(entityId))
                    UndoManager.Execute(new RemoveEntityCommand(entityId, w));
            };
            menuItems.Add(deleteItem);
            menu.ItemsSource = menuItems;
            return menu;
        }

        public void SetSelected(bool selected)
        {
            if (isSelected == selected) return;
            isSelected = selected;
            UpdateBackground();
        }

        private void UpdateBackground()
        {
            if (isSelected)
                Background = EditorColor.FromRGB(40, 60, 100); // highlight color
            else if (isPointerOver)
                Background = EditorColor.FromRGB(32, 32, 32);
            else
                Background = EditorColor.FromRGB(17, 17, 17);
        }

        /// <summary>
        /// Starts the rename operation for entities.
        /// </summary>
        public void StartRename()
        {
            if (isRenaming) return;
            isRenaming = true;
            nameText.IsVisible = false;
            renameTextBox.IsVisible = true;
            renameTextBox.Text = nameText.Text?.Replace($"Entity_{entityId}", "");
            renameTextBox.Focus();
            renameTextBox.SelectAll();
            Background = EditorColor.FromRGB(68, 68, 68);
        }

        /// <summary>
        /// Finalizes the rename operation.
        /// </summary>
        public void FinishRename()
        {
            if (!isRenaming) return;
            string? newName = renameTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(newName)) newName = $"Entity_{entityId}";

            // Update the entity name in the world
            if (WorldManager.CurrentWorld != null)
            {
                if (W.HasComponent<Name>(entityId))
                {
                    Name? nameComp = W.GetComponent<Name>(entityId);
                    nameComp!.name = newName;
                }
                else W.AddComponent(entityId, new Name(newName));
            }

            CancelRename();
            UpdateDisplay(WorldManager.CurrentWorld);
        }

        /// <summary>
        /// Cancels the renaming operation.
        /// </summary>
        public void CancelRename()
        {
            if (!isRenaming) return;
            isRenaming = false;
            nameText.IsVisible = true;
            renameTextBox.IsVisible = false;
            Background = EditorColor.FromRGB(30, 30, 30);
            BorderBrush = EditorColor.FromRGB(30, 30, 30);
            BorderThickness = new Thickness(0);
        }

        /// <summary>
        /// Updates the display name during the world window tick.
        /// </summary>
        /// <param name="world">World to update names from</param>
        public void UpdateDisplay(World? world)
        {
            if (isRenaming) return;

            string displayName;
            if (world != null && world.HasComponent<Name>(entityId))
            {
                Name nameComp = world.GetComponent<Name>(entityId)!;
                displayName = string.IsNullOrWhiteSpace(nameComp.name) ? $"Entity_{entityId}" : nameComp.name;
            }
            else displayName = $"Entity_{entityId}";
            nameText.Text = displayName;
        }

        /// <summary>
        /// Checks if the entity is still visible with a search filter enabled.
        /// </summary>
        /// <param name="filter">Search filter</param>
        /// <returns>If the entity is still visible</returns>
        public bool IsVisibleWithFilter(string filter)
        {
            if (string.IsNullOrWhiteSpace(filter) ||
                entityId.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase) || // Search in entity ID
                CurrentName!.Contains(filter, StringComparison.OrdinalIgnoreCase)) // Search in entity name
                return true;
            return false;
        }
    }

    /// <summary>
    /// Creates a new instance of the world window.
    /// </summary>
    public WorldWindow()
    {
        InitializeComponent();
        curEntities = [];
        entityControls = [];

        header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            Margin = new Thickness(5, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        entitiesHeader = new TextBlock
        {
            Text = "0",
            FontSize = 10,
            Foreground = Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center,
        };
        MaterialIcon searchIcon = new MaterialIcon
        {
            Kind = MaterialIconKind.Search,
            Foreground = EditorColor.FromRGB(128, 128, 128),
            Margin = new Thickness(6, 0, 0, 0),
            Width = 12,
            Height = 12,
        };
        headerSearchBox = new TextBox
        {
            InnerLeftContent = searchIcon,
            Text = "",
            PlaceholderText = "Search Entities...",
            FontSize = 12,
            Foreground = EditorColor.FromRGB(220, 220, 220),
            Background = EditorColor.FromRGB(17, 17, 17),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        headerSearchBox.TextChanged += OnSearchTextChanged;
        header.Children.Add(entitiesHeader);
        header.Children.Add(headerSearchBox);

        entitiesPanel = new StackPanel
        {
            Margin = new Thickness(8, 8, 8, 8),
        };
        Border separator = new Border
        {
            Background = EditorColor.FromRGB(68, 68, 68),
            Height = 1,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        scrollViewer = new ScrollViewer
        {
            Content = entitiesPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        // Attach context menu
        AttachBackgroundContextMenu();

        // Create main grid with row definitions
        mainGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
            },
            IsHitTestVisible = true,
        };
        mainGrid.PointerPressed += (s, e) =>
        {
            AvaloniaObject? source = e.Source as AvaloniaObject;
            while (source != null)
            {
                // Click was on an entity or its child elements, don't trigger background click
                if (source is EntityItemControl) return;
                source = (source as StyledElement)?.Parent;
            }

            Selection.Clear();
            PropertiesWindow.LoadWorldData(WorldManager.CurrentWorld);
        };

        Grid.SetRow(header, 0);
        Grid.SetRow(separator, 1);
        Grid.SetRow(scrollViewer, 2);
        mainGrid.Children.Add(header);
        mainGrid.Children.Add(separator);
        mainGrid.Children.Add(scrollViewer);
        this.FindControl<Border>("MainBorder")!.Child = mainGrid;

        Selection.OnSelectionChanged += OnSelectionChanged;
        Unloaded += (s, e) => Selection.OnSelectionChanged -= OnSelectionChanged;

        worldWinUpdater = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(20),
        };
        worldWinUpdater.Tick += WorldWinUpdater_Tick;
        worldWinUpdater.Start();
    }

    private void OnSelectionChanged(object? selection) => UpdateEntityHighlight();

    private void UpdateEntityHighlight()
    {
        if (Selection.SelectedType == SelectionType.Entity)
        {
            uint selectedId = Selection.Entity;
            foreach (var kv in entityControls) kv.Value.SetSelected(kv.Key == selectedId);
        }
        else
        {
            foreach (var kv in entityControls) kv.Value.SetSelected(kv.Key == uint.MaxValue);
        }
    }

    /// <summary>
    /// Attaches a context menu to the background of the world window.
    /// </summary>
    private void AttachBackgroundContextMenu()
    {
        ContextMenu backgroundContextMenu = new ContextMenu
        {
            Background = EditorColor.FromRGB(68, 68, 68),
            BorderBrush = EditorColor.FromRGB(128, 128, 128),
        };

        // Entity types
        var entityTypes = new Dictionary<string, (string DisplayName, MaterialIconKind Icon)>
        {
            ["empty"] = ("Empty Entity", MaterialIconKind.DotsGrid),
            ["emptyTransform"] = ("Empty Transform", MaterialIconKind.Axis),
            ["camera"] = ("Camera", MaterialIconKind.Camera),
            ["environment"] = ("Environment", MaterialIconKind.Grass),
            ["directionalLight"] = ("Directional Light", MaterialIconKind.WeatherSunny),
            ["pointLight"] = ("Point Light", MaterialIconKind.Lightbulb),
            ["audioSource"] = ("Audio Source", MaterialIconKind.VolumeHigh),
            ["audioListener"] = ("Audio Listener", MaterialIconKind.Headphones),
            ["sphere"] = ("Sphere", MaterialIconKind.Circle),
            ["box"] = ("Box", MaterialIconKind.Square),
            ["roundedBox"] = ("Rounded Box", MaterialIconKind.SquareRounded),
            ["torus"] = ("Donut", MaterialIconKind.CircleDouble),
            ["pyramid"] = ("Pyramid", MaterialIconKind.Pyramid),
            ["plane"] = ("Plane", MaterialIconKind.SquareOutline),
            ["cylinder"] = ("Cylinder", MaterialIconKind.Cylinder),
            ["capsule"] = ("Capsule", MaterialIconKind.Capsule),
            ["cone"] = ("Cone", MaterialIconKind.Cone),
            ["terrain"] = ("Terrain", MaterialIconKind.Terrain),
        };

        foreach (var type in entityTypes)
        {
            MenuItem entityItem = new MenuItem
            {
                Header = type.Value.DisplayName,
                Icon = new MaterialIcon { Kind = type.Value.Icon, Width = 16, Height = 16 },
                Foreground = Brushes.White,
            };
            entityItem.Click += (s, e) => EditorUI.CreateEntityStatic(type.Key);
            backgroundContextMenu.Items.Add(entityItem);
        }

        // SDF fractals live in their own section next to the primitives
        backgroundContextMenu.Items.Add(new Separator());

        MenuItem fractalsMenu = new MenuItem
        {
            Header = "SDF Fractals",
            Icon = new MaterialIcon { Kind = MaterialIconKind.Shape, Width = 16, Height = 16 },
            Foreground = Brushes.White,
        };

        var fractalTypes = new Dictionary<string, (string DisplayName, MaterialIconKind Icon)>
        {
            ["mandelbulb"] = ("Mandelbulb", MaterialIconKind.Star),
            ["mengersponge"] = ("Menger Sponge", MaterialIconKind.Grain),
            ["mandelbox"] = ("Mandelbox", MaterialIconKind.Cube),
        };

        foreach (var type in fractalTypes)
        {
            MenuItem fractalItem = new MenuItem
            {
                Header = type.Value.DisplayName,
                Icon = new MaterialIcon { Kind = type.Value.Icon, Width = 16, Height = 16 },
                Foreground = Brushes.White,
            };
            fractalItem.Click += (s, e) => EditorUI.CreateEntityStatic(type.Key);
            fractalsMenu.Items.Add(fractalItem);
        }

        backgroundContextMenu.Items.Add(fractalsMenu);

        scrollViewer.ContextMenu = backgroundContextMenu;
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        searchFilter = headerSearchBox.Text?.Trim() ?? string.Empty;
        ApplySearchFilter();
    }

    /// <summary>
    /// Applies a search filter from the header search field.
    /// </summary>
    private void ApplySearchFilter()
    {
        if (string.IsNullOrWhiteSpace(searchFilter))
        {
            foreach (EntityItemControl control in entityControls.Values)
                control.IsVisible = true;
        }
        else
        {
            foreach (EntityItemControl control in entityControls.Values)
                control.IsVisible = control.IsVisibleWithFilter(searchFilter);
        }

        // Update entity count display
        int visibleCount = entityControls.Values.Count(c => c.IsVisible);
        entitiesHeader.Text = $"{visibleCount} / {entityControls.Count}";
    }

    /// <summary>
    /// Called when the world window updates.
    /// </summary>
    private void WorldWinUpdater_Tick(object? sender, EventArgs e)
    {
        if (WorldManager.CurrentWorld == null) return;
        World w = WorldManager.CurrentWorld;

        // Update existing entity displays
        foreach (uint entityId in curEntities.ToList())
            if (entityControls.TryGetValue(entityId, out EntityItemControl? control))
                control.UpdateDisplay(w);

        UpdateListEntries();
    }

    /// <summary>
    /// Called when the entity list must be updated in-place.
    /// </summary>
    private void UpdateListEntries()
    {
        if (WorldManager.CurrentWorld == null) return;
        HashSet<uint> newEntities = [.. WorldManager.CurrentWorld.entities
            .Where(id => id != EditorCamera.EditorCameraId)];

        foreach (uint entityId in curEntities.ToList()) // Remove entities that no longer exist
        {
            if (!newEntities.Contains(entityId))
            {
                if (entityControls.TryGetValue(entityId, out EntityItemControl? control))
                {
                    entitiesPanel.Children.Remove(control);
                    entityControls.Remove(entityId);
                }
            }
        }

        foreach (uint entityId in newEntities) // Add new entities
        {
            if (!curEntities.Contains(entityId))
            {
                EntityItemControl control = new EntityItemControl(entityId, WorldManager.CurrentWorld);
                entityControls[entityId] = control;
                entitiesPanel.Children.Add(control);
            }
        }

        // Update current entities set
        curEntities.Clear();
        curEntities.UnionWith(newEntities);
        ApplySearchFilter(); // Apply search filter after updating entities
        UpdateEntityHighlight(); // Update selection highlighting

        // Update entity count display
        int visibleCount = entityControls.Values.Count(c => c.IsVisible);
        entitiesHeader.Text = $"{visibleCount} / {entityControls.Count}";
    }
}