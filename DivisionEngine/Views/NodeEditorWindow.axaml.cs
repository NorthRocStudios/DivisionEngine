//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DivisionEngine.Editor.ViewModels;
using DivisionEngine.Nodes;
using Material.Icons;
using Material.Icons.Avalonia;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;

namespace DivisionEngine.Editor;

/// <summary>
/// Shared view for node editor windows. The graph itself is drawn by
/// <see cref="NodeGraphCanvas"/>; this class builds the inspector sidebar and context
/// menus from the view model, so a derived editor only has to supply a view model.
/// </summary>
public partial class NodeEditorWindow : EditorWindow
{
    private NodeEditorWindowViewModel? viewModel;
    private NodeGraphCanvas? graphCanvas;
    private StackPanel? inspectorPanel;
    private TextBox? nodeSearchBox;
    private ListBox? nodeSearchList;
    private Border? nodeSearchOverlay;
    private bool rebuildingInspector;
    private bool searchSelectionBusy;

    /// <summary>Creates the node editor view.</summary>
    public NodeEditorWindow()
    {
        InitializeComponent();

        graphCanvas = this.FindControl<NodeGraphCanvas>("GraphCanvas");
        inspectorPanel = this.FindControl<StackPanel>("InspectorPanel");
        nodeSearchBox = this.FindControl<TextBox>("NodeSearchBox");
        nodeSearchList = this.FindControl<ListBox>("NodeSearchList");
        nodeSearchOverlay = this.FindControl<Border>("NodeSearchOverlay");

        if (graphCanvas is not null)
        {
            graphCanvas.GraphContextRequested += OnGraphContextRequested;
            graphCanvas.SaveRequested += (_, _) => SaveGraph();
            graphCanvas.LoadRequested += (_, _) => OpenGraph();
        }

        DataContextChanged += (_, _) => AttachViewModel(DataContext as NodeEditorWindowViewModel);
        Loaded += (_, _) => SyncViewAfterLayout();

        // Clicking the canvas dismisses the add-node search overlay. The overlay sits
        // above the canvas, so its own clicks never reach this handler.
        if (graphCanvas is not null)
        {
            graphCanvas.AddHandler(PointerPressedEvent, (_, _) =>
            {
                if (viewModel?.IsNodeSearchOpen == true) viewModel.CloseNodeSearch();
            }, RoutingStrategies.Tunnel);
        }
    }

    private void AttachViewModel(NodeEditorWindowViewModel? vm)
    {
        if (ReferenceEquals(viewModel, vm)) return;

        if (viewModel is not null)
        {
            viewModel.SelectionChanged -= OnSelectionChanged;
            viewModel.GraphStructureChanged -= OnGraphStructureChanged;
            viewModel.NodeValueChanged -= OnNodeValueChanged;
            viewModel.NodeSearchRequested -= OnNodeSearchRequested;
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        viewModel = vm;
        if (viewModel is not null)
        {
            viewModel.SelectionChanged += OnSelectionChanged;
            viewModel.GraphStructureChanged += OnGraphStructureChanged;
            viewModel.NodeValueChanged += OnNodeValueChanged;
            viewModel.NodeSearchRequested += OnNodeSearchRequested;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        RebuildInspector();
        SyncViewAfterLayout();
    }

    private void SyncViewAfterLayout()
    {
        Dispatcher.UIThread.Post(() =>
        {
            viewModel?.RefreshView();
            RebuildInspector();
        }, DispatcherPriority.Background);
    }

    private void OnSelectionChanged(object? sender, EventArgs e) => RebuildInspector();

    private void OnGraphStructureChanged(object? sender, EventArgs e) => RebuildInspector();

    /// <summary>
    /// Keeps the inspector in sync while values are dragged directly on the canvas.
    /// Changes made from inspector controls are deliberately ignored, otherwise the
    /// focused editor would be recreated on every keystroke.
    /// </summary>
    private void OnNodeValueChanged(object? sender, NodeViewModel node)
    {
        if (viewModel is null || !ReferenceEquals(viewModel.SelectedNode, node)) return;
        if (graphCanvas?.IsFocused == true) RebuildInspector();
    }

    private void OnNodeSearchRequested(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            nodeSearchBox?.Focus();
            nodeSearchBox?.SelectAll();
        }, DispatcherPriority.Input);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NodeEditorWindowViewModel.IsNodeSearchOpen) &&
            viewModel?.IsNodeSearchOpen == false)
        {
            Dispatcher.UIThread.Post(() => graphCanvas?.Focus(), DispatcherPriority.Input);
        }
        else if (e.PropertyName == nameof(NodeEditorWindowViewModel.ZoomText) &&
                 viewModel is not null &&
                 viewModel.SelectedNodes.Count == 0 &&
                 viewModel.SelectedConnection is null)
        {
            RebuildInspector();
        }
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (viewModel is null) return;
        if (e.Key == Key.Enter)
        {
            viewModel.AddFirstSearchResult();
            graphCanvas?.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            viewModel.CloseNodeSearch();
            graphCanvas?.Focus();
            e.Handled = true;
        }
    }

    private void OnSearchSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (searchSelectionBusy || viewModel is null || nodeSearchList is null) return;
        if (e.AddedItems.Count == 0) return;
        if (nodeSearchList.SelectedItem is not NodeCatalogEntryViewModel entry) return;

        searchSelectionBusy = true;
        try
        {
            viewModel.AddCatalogEntry(entry);
            nodeSearchList.SelectedItem = null;
        }
        finally
        {
            searchSelectionBusy = false;
        }
        graphCanvas?.Focus();
    }

    private void OnSaveGraphClick(object? sender, RoutedEventArgs e) => SaveGraph();

    private void OnOpenGraphClick(object? sender, RoutedEventArgs e) => OpenGraph();

    private async void SaveGraph()
    {
        if (viewModel is null) return;
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        try
        {
            string safeName = string.Concat(viewModel.Graph.Name.Select(c =>
                Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            if (string.IsNullOrWhiteSpace(safeName)) safeName = "nodegraph";

            IStorageFile? file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Node Graph",
                SuggestedFileName = $"{safeName}.nodegraph.json",
                DefaultExtension = "nodegraph.json",
                FileTypeChoices = [NodeGraphFileType],
            });
            if (file is null) return;

            await File.WriteAllTextAsync(file.Path.LocalPath, viewModel.SerializeGraph());
            viewModel.MarkSaved();
            viewModel.StatusText = $"Saved '{Path.GetFileName(file.Path.LocalPath)}'.";
        }
        catch (Exception ex)
        {
            viewModel.StatusText = $"Could not save graph: {ex.Message}";
        }
    }

    private async void OpenGraph()
    {
        if (viewModel is null) return;
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        try
        {
            IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open Node Graph",
                AllowMultiple = false,
                FileTypeFilter = [NodeGraphFileType],
            });
            if (files.Count == 0) return;

            string json = await File.ReadAllTextAsync(files[0].Path.LocalPath);
            if (viewModel.LoadGraph(json))
            {
                viewModel.StatusText = $"Opened '{Path.GetFileName(files[0].Path.LocalPath)}'.";
                graphCanvas?.Focus();
            }
        }
        catch (Exception ex)
        {
            viewModel.StatusText = $"Could not open graph: {ex.Message}";
        }
    }

    private static readonly FilePickerFileType NodeGraphFileType = new("Node Graph")
    {
        Patterns = ["*.nodegraph.json", "*.json"],
    };

    private void OnGraphContextRequested(object? sender, NodeCanvasContextRequestedEventArgs e)
    {
        if (viewModel is null || graphCanvas is null) return;

        ContextMenu menu = new()
        {
            Background = EditorColor.FromRGB(68, 68, 68),
            BorderBrush = EditorColor.FromRGB(128, 128, 128),
            Placement = PlacementMode.Pointer,
        };

        if (e.Node is not null || e.Port is not null)
        {
            menu.Items.Add(EditorUI.CreateContextMenuItem("Duplicate", MaterialIconKind.ContentDuplicate,
                viewModel.DuplicateSelection));
            menu.Items.Add(EditorUI.CreateContextMenuItem("Delete", MaterialIconKind.Delete,
                viewModel.DeleteSelection, EditorColor.FromRGB(220, 120, 120)));
            menu.Items.Add(new Separator());
            menu.Items.Add(EditorUI.CreateContextMenuItem("Frame Selection", MaterialIconKind.FitToScreen,
                viewModel.FrameSelection));
        }
        else if (e.Connection is not null)
        {
            menu.Items.Add(EditorUI.CreateContextMenuItem("Delete Connection", MaterialIconKind.Delete,
                () => viewModel.RemoveConnection(e.Connection), EditorColor.FromRGB(220, 120, 120)));
            menu.Items.Add(EditorUI.CreateContextMenuItem("Select Source Node", MaterialIconKind.GraphOutline,
                () => viewModel.SelectNode(e.Connection.Output.Node)));
            menu.Items.Add(EditorUI.CreateContextMenuItem("Select Target Node", MaterialIconKind.GraphOutline,
                () => viewModel.SelectNode(e.Connection.Input.Node)));
        }
        else
        {
            menu.Items.Add(EditorUI.CreateContextMenuItem("Add Node…", MaterialIconKind.Plus,
                () => viewModel.OpenNodeSearch(e.WorldPoint.X, e.WorldPoint.Y)));
            menu.Items.Add(new Separator());

            foreach (NodeCatalogGroupViewModel group in viewModel.CatalogGroups)
            {
                List<MenuItem> items = [];
                foreach (NodeCatalogEntryViewModel entry in group.Entries)
                {
                    NodeCatalogEntryViewModel captured = entry;
                    items.Add(EditorUI.CreateContextMenuItem(entry.DisplayName, entry.Icon,
                        () => viewModel.AddNode(captured.Registration, e.WorldPoint.X, e.WorldPoint.Y)));
                }

                if (items.Count > 0)
                    menu.Items.Add(EditorUI.CreateSubmenu(group.Name, MaterialIconKind.Shape, items));
            }
        }

        menu.Open(graphCanvas);
    }

    private void RebuildInspector()
    {
        if (inspectorPanel is null || rebuildingInspector) return;
        rebuildingInspector = true;
        try
        {
            inspectorPanel.Children.Clear();
            if (viewModel is null)
            {
                inspectorPanel.Children.Add(TextBlock("No graph.", 11, NodeEditorTheme.TextDim));
                return;
            }

            if (viewModel.SelectedConnection is not null)
                BuildConnectionInspector(viewModel, viewModel.SelectedConnection);
            else if (viewModel.SelectedNodes.Count > 1)
                BuildMultiSelectionInspector(viewModel);
            else if (viewModel.SelectedNode is not null)
                BuildNodeInspector(viewModel, viewModel.SelectedNode);
            else
                BuildGraphInspector(viewModel);
        }
        finally
        {
            rebuildingInspector = false;
        }
    }

    private void BuildGraphInspector(NodeEditorWindowViewModel vm)
    {
        AddSectionHeader("Graph");
        inspectorPanel!.Children.Add(TextBlock(vm.Graph.Name, 17, NodeEditorTheme.TextPrimary, FontWeight.SemiBold));
        inspectorPanel.Children.Add(TextBlock("Node graph", 10, NodeEditorTheme.TextDim));

        inspectorPanel.Children.Add(Separator());
        inspectorPanel.Children.Add(KeyValueRow("Nodes", vm.Nodes.Count.ToString(CultureInfo.InvariantCulture)));
        inspectorPanel.Children.Add(KeyValueRow("Connections", vm.Connections.Count.ToString(CultureInfo.InvariantCulture)));
        inspectorPanel.Children.Add(KeyValueRow("Node types", vm.CatalogGroups.Sum(g => g.Entries.Count).ToString(CultureInfo.InvariantCulture)));
        inspectorPanel.Children.Add(KeyValueRow("Zoom", vm.ZoomText));

        inspectorPanel.Children.Add(Separator());
        AddSectionHeader("Controls");
        inspectorPanel.Children.Add(TextBlock("Right-click or Space\tAdd node", 10.5, NodeEditorTheme.TextSecondary));
        inspectorPanel.Children.Add(TextBlock("Drag port to port\tConnect", 10.5, NodeEditorTheme.TextSecondary));
        inspectorPanel.Children.Add(TextBlock("Middle / Alt + drag\tPan", 10.5, NodeEditorTheme.TextSecondary));
        inspectorPanel.Children.Add(TextBlock("Mouse wheel\tZoom", 10.5, NodeEditorTheme.TextSecondary));
        inspectorPanel.Children.Add(TextBlock("Left drag\tBox select", 10.5, NodeEditorTheme.TextSecondary));
        inspectorPanel.Children.Add(TextBlock("F / Home\tFrame selection / all", 10.5, NodeEditorTheme.TextSecondary));
        inspectorPanel.Children.Add(TextBlock("Ctrl+Z / Ctrl+Y\tUndo / redo", 10.5, NodeEditorTheme.TextSecondary));
        inspectorPanel.Children.Add(TextBlock("Delete\tDelete selection", 10.5, NodeEditorTheme.TextSecondary));

        inspectorPanel.Children.Add(Separator());
        Button evaluate = ActionButton("Evaluate Graph", MaterialIconKind.Play, () => vm.EvaluateGraph());
        evaluate.Classes.Add("node-accent");
        inspectorPanel.Children.Add(evaluate);
        inspectorPanel.Children.Add(ActionButton("Frame All", MaterialIconKind.FitToScreen, vm.FrameAll));
    }

    private void BuildMultiSelectionInspector(NodeEditorWindowViewModel vm)
    {
        AddSectionHeader("Selection");
        inspectorPanel!.Children.Add(TextBlock($"{vm.SelectedNodes.Count} nodes selected", 16,
            NodeEditorTheme.TextPrimary, FontWeight.SemiBold));

        StackPanel names = new() { Spacing = 1, Margin = new Thickness(0, 4, 0, 4) };
        foreach (NodeViewModel node in vm.SelectedNodes.Take(10))
            names.Children.Add(TextBlock(node.Name, 10.5, NodeEditorTheme.TextSecondary));
        if (vm.SelectedNodes.Count > 10)
            names.Children.Add(TextBlock($"and {vm.SelectedNodes.Count - 10} more…", 10, NodeEditorTheme.TextDim));
        inspectorPanel.Children.Add(names);

        inspectorPanel.Children.Add(Separator());
        inspectorPanel.Children.Add(ActionButton("Duplicate", MaterialIconKind.ContentDuplicate, vm.DuplicateSelection));
        inspectorPanel.Children.Add(ActionButton("Delete", MaterialIconKind.Delete, vm.DeleteSelection,
            EditorColor.FromRGB(220, 120, 120)));
        inspectorPanel.Children.Add(ActionButton("Frame Selection", MaterialIconKind.FitToScreen, vm.FrameSelection));
    }

    private void BuildConnectionInspector(NodeEditorWindowViewModel vm, ConnectionViewModel connection)
    {
        AddSectionHeader("Connection");
        inspectorPanel!.Children.Add(TextBlock("Wire", 16, NodeEditorTheme.TextPrimary, FontWeight.SemiBold));
        inspectorPanel.Children.Add(TextBlock(connection.Output.TypeName, 10, NodeEditorTheme.TextDim));

        StackPanel endpoints = new() { Spacing = 2, Margin = new Thickness(0, 6, 0, 2) };
        endpoints.Children.Add(TextBlock($"{connection.Output.Node.Name} · {connection.Output.Name}",
            10.5, NodeEditorTheme.TextPrimary));
        endpoints.Children.Add(TextBlock("↓", 11, NodeEditorTheme.TextDim));
        endpoints.Children.Add(TextBlock($"{connection.Input.Node.Name} · {connection.Input.Name}",
            10.5, NodeEditorTheme.TextPrimary));
        inspectorPanel.Children.Add(endpoints);

        inspectorPanel.Children.Add(Separator());
        inspectorPanel.Children.Add(ActionButton("Delete Connection", MaterialIconKind.Delete,
            () => vm.RemoveConnection(connection), EditorColor.FromRGB(220, 120, 120)));
        inspectorPanel.Children.Add(ActionButton("Select Source", MaterialIconKind.GraphOutline,
            () => vm.SelectNode(connection.Output.Node)));
        inspectorPanel.Children.Add(ActionButton("Select Target", MaterialIconKind.GraphOutline,
            () => vm.SelectNode(connection.Input.Node)));
    }

    private void BuildNodeInspector(NodeEditorWindowViewModel vm, NodeViewModel node)
    {
        Grid header = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 7,
        };
        MaterialIcon icon = new()
        {
            Kind = node.Icon,
            Width = 18,
            Height = 18,
            Foreground = node.AccentBrush,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(icon, 0);
        header.Children.Add(icon);

        TextBox nameBox = new()
        {
            Text = node.Name,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Foreground = NodeEditorTheme.TextPrimary,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = NodeEditorTheme.FieldBorder,
            Padding = new Thickness(0, 0, 0, 2),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        nameBox.LostFocus += (_, _) => vm.RenameNode(node, nameBox.Text ?? node.Name);
        nameBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            vm.RenameNode(node, nameBox.Text ?? node.Name);
            graphCanvas?.Focus();
            e.Handled = true;
        };
        Grid.SetColumn(nameBox, 1);
        header.Children.Add(nameBox);
        inspectorPanel!.Children.Add(header);

        inspectorPanel.Children.Add(TextBlock(node.MenuPath, 9.5, NodeEditorTheme.TextDim));
        if (!string.IsNullOrWhiteSpace(node.Description))
            inspectorPanel.Children.Add(TextBlock(node.Description!, 10, NodeEditorTheme.TextSecondary));

        if (node.Inputs.Count > 0)
        {
            AddSectionHeader("Inputs");
            foreach (NodePortViewModel port in node.Inputs)
                BuildPortEditor(vm, node, port);
        }

        if (node.Properties.Count > 0)
        {
            AddSectionHeader("Properties");
            foreach (NodePropertyViewModel property in node.Properties)
                BuildPropertyEditor(vm, node, property);
        }

        if (node.Outputs.Count > 0)
        {
            AddSectionHeader("Outputs");
            foreach (NodePortViewModel port in node.Outputs)
            {
                Grid row = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                row.Children.Add(TextBlock(port.Name, 10.5, NodeEditorTheme.TextSecondary));
                TextBlock type = TextBlock(port.TypeName, 9.5, NodeEditorTheme.TextDim);
                Grid.SetColumn(type, 1);
                row.Children.Add(type);
                inspectorPanel.Children.Add(row);
            }
        }

        inspectorPanel.Children.Add(Separator());
        inspectorPanel.Children.Add(ActionButton("Duplicate Node", MaterialIconKind.ContentDuplicate, vm.DuplicateSelection));
        inspectorPanel.Children.Add(ActionButton("Delete Node", MaterialIconKind.Delete, vm.DeleteSelection,
            EditorColor.FromRGB(220, 120, 120)));
    }

    private void BuildPortEditor(NodeEditorWindowViewModel vm, NodeViewModel node, NodePortViewModel port)
    {
        inspectorPanel!.Children.Add(TextBlock(port.Name, 10.5, NodeEditorTheme.TextSecondary,
            tooltip: port.Tooltip));

        NodeConnection? connection = vm.Graph.GetInputConnection(port.Model);
        if (connection is not null)
        {
            Grid connected = new()
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                ColumnSpacing = 6,
                Margin = new Thickness(0, 1, 0, 4),
            };
            Border dot = new()
            {
                Width = 7,
                Height = 7,
                CornerRadius = new CornerRadius(4),
                Background = port.Brush,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(dot, 0);
            connected.Children.Add(dot);
            TextBlock source = TextBlock($"{connection.Output.Node.Name} · {connection.Output.Name}",
                10, NodeEditorTheme.TextPrimary);
            source.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(source, 1);
            connected.Children.Add(source);
            Button disconnect = SmallButton("×", () =>
            {
                vm.DetachInput(port, out _);
                RebuildInspector();
            });
            ToolTip.SetTip(disconnect, "Disconnect");
            Grid.SetColumn(disconnect, 2);
            connected.Children.Add(disconnect);
            inspectorPanel.Children.Add(connected);
            return;
        }

        object? value = port.Model.DefaultValue;
        Control editor = CreateValueEditor(
            value?.GetType() ?? port.Model.ValueType ?? typeof(object),
            value,
            port.Model.Min,
            port.Model.Max,
            port.Model.Step > 0 ? port.Model.Step : null,
            newValue => vm.RecordChange($"Edit {node.Name}.{port.Name}",
                () => vm.SetPortDefault(port, newValue)));
        inspectorPanel.Children.Add(Row(editor));
    }

    private void BuildPropertyEditor(NodeEditorWindowViewModel vm, NodeViewModel node, NodePropertyViewModel property)
    {
        inspectorPanel!.Children.Add(TextBlock(property.DisplayName, 10.5, NodeEditorTheme.TextSecondary,
            tooltip: property.Tooltip));
        Control editor = CreateValueEditor(
            property.ValueType,
            property.Value,
            property.Min,
            property.Max,
            property.Step,
            newValue => vm.RecordChange($"Edit {node.Name}.{property.DisplayName}",
                () => vm.SetNodeProperty(property, newValue)));
        inspectorPanel.Children.Add(Row(editor));
    }

    private Control CreateValueEditor(Type valueType, object? value, double? min, double? max,
        double? step, Action<object?> apply)
    {
        if (valueType == typeof(bool))
        {
            CheckBox check = new() { IsChecked = value is true, Classes = { "field-editor" } };
            check.Click += (_, _) => apply(check.IsChecked == true);
            return check;
        }

        if (valueType == typeof(string))
        {
            TextBox text = new()
            {
                Text = value?.ToString() ?? string.Empty,
                FontSize = 11,
                Classes = { "field-editor" },
                Width = 150,
            };
            void Commit()
            {
                if (!Equals(text.Text, value?.ToString())) apply(text.Text);
            }
            text.LostFocus += (_, _) => Commit();
            text.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                Commit();
                graphCanvas?.Focus();
                e.Handled = true;
            };
            return text;
        }

        if (valueType.IsEnum)
        {
            ComboBox combo = new() { Classes = { "field-editor" }, Width = 150 };
            foreach (string name in Enum.GetNames(valueType)) combo.Items.Add(name);
            combo.SelectedItem = value?.ToString();
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is string selected)
                    apply(Enum.Parse(valueType, selected));
            };
            return combo;
        }

        if (valueType == typeof(float4))
        {
            float4 color = value is float4 vector ? vector : new float4(1, 1, 1, 1);
            ColorPicker picker = new()
            {
                Color = Color.FromArgb(ToByte(color.w), ToByte(color.x), ToByte(color.y), ToByte(color.z)),
                Height = 22,
                Width = 70,
                IsAlphaVisible = true,
                IsAlphaEnabled = true,
                IsColorSpectrumVisible = true,
                IsColorPreviewVisible = true,
                IsColorComponentsVisible = false,
                IsComponentTextInputVisible = false,
                IsComponentSliderVisible = false,
                IsHexInputVisible = false,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                FontSize = 11,
            };
            picker.ColorChanged += (_, _) =>
            {
                Color selected = picker.Color;
                apply(new float4(selected.R / 255f, selected.G / 255f, selected.B / 255f, selected.A / 255f));
            };
            return picker;
        }

        if (valueType == typeof(float2) || valueType == typeof(float3))
        {
            return CreateVectorEditor(valueType, value, apply);
        }

        if (IsNumericType(valueType))
        {
            double number = Convert.ToDouble(value ?? 0, CultureInfo.InvariantCulture);
            NumericUpDown numeric = new()
            {
                Value = (decimal)number,
                Minimum = (decimal)(min ?? -1000000000),
                Maximum = (decimal)(max ?? 1000000000),
                Increment = (decimal)(step ?? (valueType == typeof(int) ? 1 : 0.01)),
                FormatString = valueType == typeof(int) ? "0" : "0.###",
                Classes = { "field-editor" },
                Width = 150,
                ShowButtonSpinner = false,
                HorizontalContentAlignment = HorizontalAlignment.Left,
            };
            numeric.ValueChanged += (_, _) =>
            {
                if (numeric.Value is not decimal captured) return;
                object converted = ConvertNumeric(captured, valueType);
                if (!Equals(converted, value)) apply(converted);
            };
            return numeric;
        }

        TextBox readOnly = new()
        {
            Text = value?.ToString() ?? "—",
            IsReadOnly = true,
            FontSize = 10,
            Classes = { "field-editor" },
            Width = 150,
        };
        return readOnly;
    }

    private Control CreateVectorEditor(Type valueType, object? value, Action<object?> apply)
    {
        int components = valueType == typeof(float2) ? 2 : valueType == typeof(float3) ? 3 : 4;
        float[] values = new float[components];
        if (value is float2 v2) { values[0] = v2.x; values[1] = v2.y; }
        else if (value is float3 v3) { values[0] = v3.x; values[1] = v3.y; values[2] = v3.z; }
        else if (value is float4 v4) { values[0] = v4.x; values[1] = v4.y; values[2] = v4.z; values[3] = v4.w; }

        StackPanel panel = new() { Orientation = Orientation.Horizontal, Spacing = 3, HorizontalAlignment = HorizontalAlignment.Right };
        NumericUpDown[] boxes = new NumericUpDown[components];
        string[] labels = ["X", "Y", "Z", "W"];

        for (int i = 0; i < components; i++)
        {
            int index = i;
            TextBlock label = new() { Text = labels[i], FontSize = 9, Foreground = NodeEditorTheme.TextDim, VerticalAlignment = VerticalAlignment.Center };
            NumericUpDown box = new()
            {
                Value = (decimal)values[i],
                Minimum = -1000000,
                Maximum = 1000000,
                Increment = 0.01m,
                FormatString = "0.###",
                Classes = { "field-editor" },
                Width = 45,
                ShowButtonSpinner = false,
                Margin = new Thickness(0),
            };
            box.ValueChanged += (_, _) =>
            {
                if (box.Value is not decimal captured) return;
                values[index] = (float)captured;
                object vector = components switch
                {
                    2 => new float2(values[0], values[1]),
                    3 => new float3(values[0], values[1], values[2]),
                    _ => new float4(values[0], values[1], values[2], values[3]),
                };
                apply(vector);
            };
            boxes[i] = box;
            panel.Children.Add(label);
            panel.Children.Add(box);
        }

        return panel;
    }

    private static bool IsNumericType(Type type)
        => type == typeof(float) || type == typeof(double) || type == typeof(int) ||
           type == typeof(long) || type == typeof(short) || type == typeof(byte) ||
           type == typeof(uint) || type == typeof(ulong);

    private static object ConvertNumeric(decimal value, Type type)
    {
        if (type == typeof(float)) return (float)value;
        if (type == typeof(double)) return (double)value;
        if (type == typeof(int)) return (int)value;
        if (type == typeof(long)) return (long)value;
        if (type == typeof(short)) return (short)value;
        if (type == typeof(byte)) return (byte)value;
        if (type == typeof(uint)) return (uint)value;
        if (type == typeof(ulong)) return (ulong)value;
        return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
    }

    private static byte ToByte(float value) => (byte)Math.Clamp((int)(value * 255f), 0, 255);

    private void AddSectionHeader(string text)
    {
        TextBlock header = new()
        {
            Text = text.ToUpperInvariant(),
            FontSize = 9,
            FontWeight = FontWeight.SemiBold,
            Foreground = NodeEditorTheme.TextDim,
            Margin = new Thickness(0, 8, 0, 1),
        };
        inspectorPanel!.Children.Add(header);
    }

    private static TextBlock TextBlock(string text, double fontSize, IBrush brush,
        FontWeight weight = FontWeight.Normal, string? tooltip = null)
    {
        TextBlock block = new()
        {
            Text = text,
            FontSize = fontSize,
            Foreground = brush,
            FontWeight = weight,
            TextWrapping = TextWrapping.Wrap,
        };
        if (tooltip is not null) ToolTip.SetTip(block, tooltip);
        return block;
    }

    private static Border Separator() => new()
    {
        Height = 1,
        Background = NodeEditorTheme.ToolbarBorder,
        Margin = new Thickness(0, 7, 0, 2),
    };

    private static Grid KeyValueRow(string key, string value)
    {
        Grid row = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        row.Children.Add(TextBlock(key, 10.5, NodeEditorTheme.TextSecondary));
        TextBlock valueBlock = TextBlock(value, 10.5, NodeEditorTheme.TextPrimary);
        Grid.SetColumn(valueBlock, 1);
        row.Children.Add(valueBlock);
        return row;
    }

    private static Grid Row(Control editor)
    {
        Grid row = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 0, 0, 3),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        editor.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(editor, 1);
        row.Children.Add(editor);
        return row;
    }

    private static Button ActionButton(string text, MaterialIconKind icon, Action action, IBrush? foreground = null)
    {
        StackPanel content = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(new MaterialIcon
        {
            Kind = icon,
            Width = 13,
            Height = 13,
            Foreground = foreground ?? NodeEditorTheme.TextPrimary,
            VerticalAlignment = VerticalAlignment.Center,
        });
        content.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 10.5,
            Foreground = foreground ?? NodeEditorTheme.TextPrimary,
            VerticalAlignment = VerticalAlignment.Center,
        });
        Button button = new()
        {
            Content = content,
            Background = EditorColor.FromRGB(52, 52, 52),
            Foreground = foreground ?? NodeEditorTheme.TextPrimary,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(7, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 1, 0, 1),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        button.Click += (_, _) => action();
        return button;
    }

    private static Button SmallButton(string text, Action action)
    {
        Button button = new()
        {
            Content = text,
            FontSize = 12,
            Width = 20,
            Height = 18,
            Padding = new Thickness(0),
            Background = EditorColor.FromRGB(40, 40, 40),
            Foreground = NodeEditorTheme.TextSecondary,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(2),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        button.Click += (_, _) => action();
        return button;
    }
}
