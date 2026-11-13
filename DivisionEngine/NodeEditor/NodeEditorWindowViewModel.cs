//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using Avalonia;
using CommunityToolkit.Mvvm.Input;
using DivisionEngine.Nodes;
using Material.Icons;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace DivisionEngine.Editor.ViewModels
{
    /// <summary>
    /// Base view model for every node editor window. A concrete editor derives from
    /// this class, seeds a <see cref="NodeGraph"/> (or overrides
    /// <see cref="CreateNodeViewModel"/> for custom visuals) and is then usable with the
    /// shared <c>NodeEditorWindow</c> view.
    /// </summary>
    /// <remarks>
    /// The view model owns the editor-side state around the graph: node wrappers,
    /// camera, selection, add-node catalog, undo history and status text. Graph logic
    /// itself lives in <see cref="NodeGraph"/> so it can run outside the editor.
    /// </remarks>
    public partial class NodeEditorWindowViewModel : EditorWindowViewModel
    {
        private const int MaxUndoStates = 100;
        private const double UndoCoalesceMilliseconds = 800;

        private readonly List<UndoState> undoStates = [];
        private readonly List<UndoState> redoStates = [];
        private readonly List<NodeCatalogEntryViewModel> catalogEntries = [];
        private readonly Dictionary<string, NodeCatalogEntryViewModel> catalogById = new(StringComparer.Ordinal);

        private UndoTransaction? transaction;
        private bool restoring;
        private string? lastCommitLabel;
        private DateTime lastCommitUtc = DateTime.MinValue;
        private bool cameraInitialized;
        private bool pendingFrame;
        private double nodeSearchWorldX;
        private double nodeSearchWorldY;
        private bool isNodeSearchOpen;
        private string nodeSearchText = string.Empty;
        private string statusText = "Right-click the canvas or press Space to add a node.";
        private string? evaluationSummary;
        private bool hasEvaluationError;
        private bool isDirty;
        private bool showInspector = true;
        private bool showGrid = true;
        private bool snapToGrid;
        private double zoom = 1;
        private double panX;
        private double panY;
        private double viewportWidth = 960;
        private double viewportHeight = 600;
        private NodeViewModel? selectedNode;
        private ConnectionViewModel? selectedConnection;

        /// <summary>
        /// Creates a node editor view model with an empty graph.
        /// </summary>
        /// <param name="graphName">Initial graph name, also used as the window title.</param>
        /// <param name="icon">Window icon.</param>
        protected NodeEditorWindowViewModel(string graphName = "Node Graph", MaterialIconKind icon = MaterialIconKind.GraphOutline)
        {
            NodeTypeRegistry.EnsureAssembly(typeof(NodeEditorWindowViewModel).Assembly);
            Graph = new NodeGraph(graphName);
            Title = graphName;
            Icon = icon;
            BuildCatalog();
            RefreshSearchResults();
        }

        /// <summary>Raised when the selection changes.</summary>
        public event EventHandler? SelectionChanged;

        /// <summary>Raised when nodes or connections are added, removed or reloaded.</summary>
        public event EventHandler? GraphStructureChanged;

        /// <summary>Raised when the canvas needs to repaint.</summary>
        public event EventHandler? CanvasInvalidated;

        /// <summary>Raised when a node value, name or property changed.</summary>
        public event EventHandler<NodeViewModel>? NodeValueChanged;

        /// <summary>Raised when the add-node search panel should take focus.</summary>
        public event EventHandler? NodeSearchRequested;

        /// <summary>The graph being edited.</summary>
        public NodeGraph Graph { get; private set; }

        /// <summary>Node wrappers, in editor stacking order.</summary>
        public ObservableCollection<NodeViewModel> Nodes { get; } = [];

        /// <summary>Connection wrappers.</summary>
        public ObservableCollection<ConnectionViewModel> Connections { get; } = [];

        /// <summary>Currently selected nodes.</summary>
        public ObservableCollection<NodeViewModel> SelectedNodes { get; } = [];

        /// <summary>Add-node catalog grouped by category.</summary>
        public ObservableCollection<NodeCatalogGroupViewModel> CatalogGroups { get; } = [];

        /// <summary>Search results for the add-node panel.</summary>
        public ObservableCollection<NodeCatalogEntryViewModel> SearchResults { get; } = [];

        /// <summary>Graph display name.</summary>
        public string GraphName
        {
            get => Graph.Name;
            set
            {
                if (string.Equals(Graph.Name, value, StringComparison.Ordinal)) return;
                Graph.Name = string.IsNullOrWhiteSpace(value) ? "Node Graph" : value;
                this.RaisePropertyChanged();
                this.RaisePropertyChanged(nameof(WindowSubtitle));
            }
        }

        /// <summary>Secondary title text shown in the toolbar.</summary>
        public string WindowSubtitle => $"{Graph.Name} · {Nodes.Count} nodes · {Connections.Count} links";

        /// <summary>Short summary used by the toolbar and status bar.</summary>
        public string GraphSummary => $"{Nodes.Count} nodes · {Connections.Count} connections";

        /// <summary>Primary selected node, or null.</summary>
        public NodeViewModel? SelectedNode
        {
            get => selectedNode;
            private set
            {
                if (ReferenceEquals(selectedNode, value)) return;
                selectedNode = value;
                this.RaisePropertyChanged();
                this.RaisePropertyChanged(nameof(SelectionSummary));
                this.RaisePropertyChanged(nameof(HasNodeSelection));
            }
        }

        /// <summary>Selected connection, or null.</summary>
        public ConnectionViewModel? SelectedConnection
        {
            get => selectedConnection;
            private set
            {
                if (ReferenceEquals(selectedConnection, value)) return;
                if (selectedConnection is not null) selectedConnection.IsSelected = false;
                selectedConnection = value;
                if (selectedConnection is not null) selectedConnection.IsSelected = true;
                this.RaisePropertyChanged();
                this.RaisePropertyChanged(nameof(HasSelection));
                this.RaisePropertyChanged(nameof(SelectionSummary));
            }
        }

        /// <summary>True when at least one node or connection is selected.</summary>
        public bool HasSelection => SelectedNodes.Count > 0 || SelectedConnection is not null;

        /// <summary>True when at least one node is selected.</summary>
        public bool HasNodeSelection => SelectedNodes.Count > 0;

        /// <summary>Human readable selection summary.</summary>
        public string SelectionSummary => SelectedNodes.Count switch
        {
            0 => SelectedConnection is not null ? "1 connection selected" : "Nothing selected",
            1 => SelectedNodes[0].Name,
            _ => $"{SelectedNodes.Count} nodes selected",
        };

        /// <summary>Current graph zoom factor.</summary>
        public double Zoom
        {
            get => zoom;
            set
            {
                double clamped = Math.Clamp(value, NodeEditorTheme.MinZoom, NodeEditorTheme.MaxZoom);
                if (Math.Abs(zoom - clamped) < 1e-9) return;
                zoom = clamped;
                this.RaisePropertyChanged();
                this.RaisePropertyChanged(nameof(ZoomText));
                InvalidateCanvas();
            }
        }

        /// <summary>Zoom shown as a percentage.</summary>
        public string ZoomText => $"{Math.Round(Zoom * 100)}%";

        /// <summary>Horizontal camera offset in screen pixels.</summary>
        public double PanX
        {
            get => panX;
            set
            {
                if (Math.Abs(panX - value) < 1e-9) return;
                panX = value;
                this.RaisePropertyChanged();
                InvalidateCanvas();
            }
        }

        /// <summary>Vertical camera offset in screen pixels.</summary>
        public double PanY
        {
            get => panY;
            set
            {
                if (Math.Abs(panY - value) < 1e-9) return;
                panY = value;
                this.RaisePropertyChanged();
                InvalidateCanvas();
            }
        }

        /// <summary>Last known canvas width, used for framing commands.</summary>
        public double ViewportWidth
        {
            get => viewportWidth;
            private set => this.RaiseAndSetIfChanged(ref viewportWidth, value);
        }

        /// <summary>Last known canvas height, used for framing commands.</summary>
        public double ViewportHeight
        {
            get => viewportHeight;
            private set => this.RaiseAndSetIfChanged(ref viewportHeight, value);
        }

        /// <summary>Whether the inspector sidebar is visible.</summary>
        public bool ShowInspector
        {
            get => showInspector;
            set => this.RaiseAndSetIfChanged(ref showInspector, value);
        }

        /// <summary>Whether the background grid is drawn.</summary>
        public bool ShowGrid
        {
            get => showGrid;
            set
            {
                if (showGrid == value) return;
                showGrid = value;
                this.RaisePropertyChanged();
                InvalidateCanvas();
            }
        }

        /// <summary>Whether node dragging snaps to the grid.</summary>
        public bool SnapToGrid
        {
            get => snapToGrid;
            set
            {
                if (snapToGrid == value) return;
                snapToGrid = value;
                this.RaisePropertyChanged();
                InvalidateCanvas();
            }
        }

        /// <summary>Status line shown below the canvas.</summary>
        public string StatusText
        {
            get => statusText;
            set => this.RaiseAndSetIfChanged(ref statusText, value);
        }

        /// <summary>Latest evaluation summary.</summary>
        public string? EvaluationSummary
        {
            get => evaluationSummary;
            set
            {
                if (evaluationSummary == value) return;
                evaluationSummary = value;
                this.RaisePropertyChanged();
            }
        }

        /// <summary>Whether the latest evaluation reported errors.</summary>
        public bool HasEvaluationError
        {
            get => hasEvaluationError;
            set
            {
                if (hasEvaluationError == value) return;
                hasEvaluationError = value;
                this.RaisePropertyChanged();
            }
        }

        /// <summary>Whether the graph has unsaved edits.</summary>
        public bool IsDirty
        {
            get => isDirty;
            set
            {
                if (isDirty == value) return;
                isDirty = value;
                this.RaisePropertyChanged();
                this.RaisePropertyChanged(nameof(DirtyMarker));
            }
        }

        /// <summary>Marker shown next to the title when there are unsaved edits.</summary>
        public string DirtyMarker => IsDirty ? "●" : string.Empty;

        /// <summary>Whether the add-node search panel is open.</summary>
        public bool IsNodeSearchOpen
        {
            get => isNodeSearchOpen;
            private set
            {
                if (isNodeSearchOpen == value) return;
                isNodeSearchOpen = value;
                this.RaisePropertyChanged();
            }
        }

        /// <summary>Search filter for the add-node panel.</summary>
        public string NodeSearchText
        {
            get => nodeSearchText;
            set
            {
                if (nodeSearchText == value) return;
                nodeSearchText = value ?? string.Empty;
                this.RaisePropertyChanged();
                RefreshSearchResults();
            }
        }

        /// <summary>Number of matching node types.</summary>
        public string NodeSearchStatus => SearchResults.Count == 0
            ? "No matching nodes"
            : $"{SearchResults.Count} node type{(SearchResults.Count == 1 ? string.Empty : "s")}";

        /// <summary>Whether an undo step is available.</summary>
        public bool CanUndo => undoStates.Count > 0;

        /// <summary>Whether a redo step is available.</summary>
        public bool CanRedo => redoStates.Count > 0;

        /// <summary>Last undo label, for menus and tooltips.</summary>
        public string? UndoLabel => undoStates.Count > 0 ? undoStates[^1].Label : null;

        /// <summary>Last redo label, for menus and tooltips.</summary>
        public string? RedoLabel => redoStates.Count > 0 ? redoStates[^1].Label : null;

        /// <summary>
        /// Creates the view model wrapper for a model node. Override this to return
        /// custom <see cref="NodeViewModel"/> subclasses for specific node types.
        /// </summary>
        /// <param name="node">Model node.</param>
        protected virtual NodeViewModel CreateNodeViewModel(Node node) => new(node, this);

        /// <summary>
        /// Replaces the edited graph, rebuilding every wrapper and optionally resetting
        /// the undo history. Intended for derived editor constructors and loaders.
        /// </summary>
        /// <param name="graph">New graph.</param>
        /// <param name="markDirty">Whether to mark the graph as modified.</param>
        /// <param name="resetHistory">Whether to clear undo/redo history.</param>
        protected void SetGraphModel(NodeGraph graph, bool markDirty = false, bool resetHistory = true)
        {
            ArgumentNullException.ThrowIfNull(graph);

            foreach (NodeViewModel node in Nodes) node.Dispose();
            Nodes.Clear();
            Connections.Clear();
            SelectedNodes.Clear();
            SelectConnectionInternal(null, notify: false);
            SelectedNode = null;

            Graph = graph;
            this.RaisePropertyChanged(nameof(Graph));
            this.RaisePropertyChanged(nameof(GraphName));
            RebuildFromGraph();

            if (resetHistory)
            {
                undoStates.Clear();
                redoStates.Clear();
                transaction = null;
            }

            IsDirty = markDirty;
            cameraInitialized = false;
            pendingFrame = true;
            if (ViewportWidth > 10 && ViewportHeight > 10) FrameAll();

            StatusText = $"Loaded '{graph.Name}' with {Nodes.Count} nodes.";
            RaiseUndoProperties();
            NotifyGraphStructureChanged();
        }

        /// <summary>
        /// Rebuilds every node and connection wrapper from <see cref="Graph"/>.
        /// </summary>
        protected void RebuildFromGraph()
        {
            foreach (NodeViewModel node in Nodes.ToArray()) node.Dispose();
            Nodes.Clear();
            Connections.Clear();

            foreach (Node node in Graph.Nodes)
                Nodes.Add(CreateNodeViewModel(node));

            RebuildConnections();

            foreach (NodeViewModel node in Nodes)
            {
                foreach (NodePortViewModel port in node.Inputs) port.NotifyConnectionChanged();
                foreach (NodePortViewModel port in node.Outputs) port.NotifyConnectionChanged();
            }

            this.RaisePropertyChanged(nameof(GraphSummary));
            this.RaisePropertyChanged(nameof(WindowSubtitle));
            this.RaisePropertyChanged(nameof(SelectionSummary));
        }

        /// <summary>
        /// Recreates connection wrappers from the model graph.
        /// </summary>
        public void RebuildConnections()
        {
            Connections.Clear();
            foreach (NodeConnection connection in Graph.Connections)
            {
                NodePortViewModel? output = FindPortView(connection.Output);
                NodePortViewModel? input = FindPortView(connection.Input);
                if (output is null || input is null) continue;
                Connections.Add(new ConnectionViewModel(this, connection, output, input));
            }

            foreach (NodeViewModel node in Nodes)
            {
                foreach (NodePortViewModel port in node.Inputs) port.NotifyConnectionChanged();
                foreach (NodePortViewModel port in node.Outputs) port.NotifyConnectionChanged();
            }

            this.RaisePropertyChanged(nameof(GraphSummary));
            this.RaisePropertyChanged(nameof(WindowSubtitle));
            InvalidateCanvas();
        }

        /// <summary>
        /// Called by the canvas when its size changes.
        /// </summary>
        /// <param name="width">Canvas width in pixels.</param>
        /// <param name="height">Canvas height in pixels.</param>
        public void SetViewport(double width, double height)
        {
            if (width <= 0 || height <= 0) return;
            ViewportWidth = width;
            ViewportHeight = height;
            if (!cameraInitialized || pendingFrame)
            {
                cameraInitialized = true;
                pendingFrame = false;
                FrameAll();
            }
        }

        /// <summary>Converts a graph-space point to screen space.</summary>
        public Point WorldToScreen(Point point) => new(point.X * Zoom + PanX, point.Y * Zoom + PanY);

        /// <summary>Converts a graph-space rectangle to screen space.</summary>
        public Rect WorldToScreen(Rect rect)
        {
            Point topLeft = WorldToScreen(rect.TopLeft);
            return new Rect(topLeft.X, topLeft.Y, rect.Width * Zoom, rect.Height * Zoom);
        }

        /// <summary>Converts a screen-space point to graph space.</summary>
        public Point ScreenToWorld(Point point) => new((point.X - PanX) / Zoom, (point.Y - PanY) / Zoom);

        /// <summary>Zooms by a factor around a screen-space anchor.</summary>
        /// <param name="screenAnchor">Point that stays fixed while zooming.</param>
        /// <param name="factor">Multiplicative zoom factor.</param>
        public void ZoomAt(Point screenAnchor, double factor)
        {
            Point world = ScreenToWorld(screenAnchor);
            double newZoom = Math.Clamp(Zoom * factor, NodeEditorTheme.MinZoom, NodeEditorTheme.MaxZoom);
            if (Math.Abs(newZoom - Zoom) < 1e-9) return;
            Zoom = newZoom;
            PanX = screenAnchor.X - world.X * newZoom;
            PanY = screenAnchor.Y - world.Y * newZoom;
        }

        /// <summary>Frames the whole graph in the viewport.</summary>
        public void FrameAll()
        {
            if (Nodes.Count == 0)
            {
                Zoom = 1;
                PanX = ViewportWidth / 2;
                PanY = ViewportHeight / 2;
                InvalidateCanvas();
                return;
            }

            Rect bounds = Nodes[0].Bounds;
            foreach (NodeViewModel node in Nodes)
                bounds = bounds.Union(node.Bounds);

            FrameBounds(bounds);
        }

        /// <summary>Frames the current selection in the viewport.</summary>
        public void FrameSelection()
        {
            if (SelectedNodes.Count == 0)
            {
                FrameAll();
                return;
            }

            Rect bounds = SelectedNodes[0].Bounds;
            foreach (NodeViewModel node in SelectedNodes) bounds = bounds.Union(node.Bounds);
            FrameBounds(bounds);
        }

        private void FrameBounds(Rect bounds)
        {
            const double padding = 90;
            double width = Math.Max(1, bounds.Width + padding * 2);
            double height = Math.Max(1, bounds.Height + padding * 2);
            double availableWidth = Math.Max(200, ViewportWidth - 32);
            double availableHeight = Math.Max(150, ViewportHeight - 32);
            double newZoom = Math.Clamp(Math.Min(availableWidth / width, availableHeight / height),
                NodeEditorTheme.MinZoom, 1.35);
            Zoom = newZoom;
            PanX = ViewportWidth / 2 - (bounds.X + bounds.Width / 2) * newZoom;
            PanY = ViewportHeight / 2 - (bounds.Y + bounds.Height / 2) * newZoom;
            InvalidateCanvas();
        }

        /// <summary>Resets the camera to 100% with the graph centred.</summary>
        public void ResetView()
        {
            Zoom = 1;
            FrameAll();
        }

        /// <summary>Requests a canvas repaint.</summary>
        public void InvalidateCanvas() => CanvasInvalidated?.Invoke(this, EventArgs.Empty);

        /// <summary>
        /// Re-raises the display-only properties after the view has finished loading.
        /// Bindings created after the initial camera frame would otherwise keep the
        /// pre-layout zoom/summary values until the next user action.
        /// </summary>
        public void RefreshView()
        {
            this.RaisePropertyChanged(nameof(ZoomText));
            this.RaisePropertyChanged(nameof(GraphSummary));
            this.RaisePropertyChanged(nameof(WindowSubtitle));
            this.RaisePropertyChanged(nameof(SelectionSummary));
            this.RaisePropertyChanged(nameof(CanUndo));
            this.RaisePropertyChanged(nameof(CanRedo));
            this.RaisePropertyChanged(nameof(UndoLabel));
            this.RaisePropertyChanged(nameof(RedoLabel));
            InvalidateCanvas();
        }

        /// <summary>Selects a single node, optionally toggling or extending the selection.</summary>
        /// <param name="node">Node to select, or null to clear.</param>
        /// <param name="additive">Whether to keep the current selection.</param>
        /// <param name="toggle">Whether clicking an already selected node removes it.</param>
        public void SelectNode(NodeViewModel? node, bool additive = false, bool toggle = false)
        {
            if (node is null)
            {
                ClearSelection();
                return;
            }

            List<NodeViewModel> selection = [.. SelectedNodes];
            if (!additive)
            {
                foreach (NodeViewModel existing in selection)
                {
                    existing.IsSelected = false;
                    SelectedNodes.Remove(existing);
                }
                selection.Clear();
            }

            if (toggle && SelectedNodes.Contains(node))
            {
                SelectedNodes.Remove(node);
                node.IsSelected = false;
            }
            else if (!SelectedNodes.Contains(node))
            {
                SelectedNodes.Add(node);
                node.IsSelected = true;
            }

            SelectConnectionInternal(null, notify: false);
            SelectedNode = SelectedNodes.Count > 0 ? SelectedNodes[^1] : null;
            NotifySelectionChanged();
        }

        /// <summary>Selects several nodes at once.</summary>
        /// <param name="nodes">Nodes to add to the selection.</param>
        /// <param name="additive">Whether to keep the current selection.</param>
        public void SelectNodes(IEnumerable<NodeViewModel> nodes, bool additive = false)
        {
            if (!additive) ClearSelectionCore();
            foreach (NodeViewModel node in nodes)
            {
                if (SelectedNodes.Contains(node)) continue;
                SelectedNodes.Add(node);
                node.IsSelected = true;
            }
            SelectConnectionInternal(null, notify: false);
            SelectedNode = SelectedNodes.Count > 0 ? SelectedNodes[^1] : null;
            NotifySelectionChanged();
        }

        /// <summary>Selects a connection.</summary>
        /// <param name="connection">Connection to select, or null.</param>
        public void SelectConnection(ConnectionViewModel? connection)
        {
            SelectConnectionInternal(connection, notify: true);
        }

        private void SelectConnectionInternal(ConnectionViewModel? connection, bool notify)
        {
            if (ReferenceEquals(SelectedConnection, connection)) return;
            if (SelectedConnection is not null) SelectedConnection.IsSelected = false;
            if (!ReferenceEquals(SelectedConnection, connection)) SelectedConnection = connection;
            if (connection is null) return;

            foreach (NodeViewModel node in SelectedNodes) node.IsSelected = false;
            SelectedNodes.Clear();
            SelectedNode = null;
            if (notify) NotifySelectionChanged();
        }

        /// <summary>Clears the node and connection selection.</summary>
        public void ClearSelection()
        {
            ClearSelectionCore();
            SelectConnectionInternal(null, notify: false);
            NotifySelectionChanged();
        }

        private void ClearSelectionCore()
        {
            foreach (NodeViewModel node in SelectedNodes) node.IsSelected = false;
            SelectedNodes.Clear();
            SelectedNode = null;
        }

        private void NotifySelectionChanged()
        {
            this.RaisePropertyChanged(nameof(HasSelection));
            this.RaisePropertyChanged(nameof(HasNodeSelection));
            this.RaisePropertyChanged(nameof(SelectionSummary));
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateCanvas();
        }

        private void NotifyGraphStructureChanged()
        {
            EvaluationSummary = null;
            HasEvaluationError = false;
            this.RaisePropertyChanged(nameof(GraphSummary));
            this.RaisePropertyChanged(nameof(WindowSubtitle));
            this.RaisePropertyChanged(nameof(SelectionSummary));
            GraphStructureChanged?.Invoke(this, EventArgs.Empty);
            InvalidateCanvas();
        }

        /// <summary>Builds the add-node catalog from <see cref="NodeTypeRegistry"/>.</summary>
        protected void BuildCatalog()
        {
            catalogEntries.Clear();
            catalogById.Clear();
            CatalogGroups.Clear();

            foreach (NodeTypeRegistration registration in NodeTypeRegistry.All)
            {
                NodeCatalogEntryViewModel entry = new(registration);
                catalogEntries.Add(entry);
                catalogById[registration.TypeId] = entry;
            }

            foreach (IGrouping<string, NodeCatalogEntryViewModel> group in catalogEntries
                         .GroupBy(e => e.Category)
                         .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                CatalogGroups.Add(new NodeCatalogGroupViewModel(group.Key,
                    group.OrderBy(e => e.MenuPath, StringComparer.OrdinalIgnoreCase)));
            }

            RefreshSearchResults();
        }

        /// <summary>Filters the add-node search results.</summary>
        protected void RefreshSearchResults()
        {
            SearchResults.Clear();
            string[] tokens = NodeSearchText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            IEnumerable<NodeCatalogEntryViewModel> matches = catalogEntries;
            foreach (string token in tokens)
                matches = matches.Where(e => e.SearchText.Contains(token, StringComparison.OrdinalIgnoreCase));

            foreach (NodeCatalogEntryViewModel entry in matches
                         .OrderBy(e => e.MenuPath, StringComparer.OrdinalIgnoreCase)
                         .Take(200))
                SearchResults.Add(entry);

            this.RaisePropertyChanged(nameof(NodeSearchStatus));
        }

        /// <summary>
        /// Opens the add-node search panel. The node is created at the supplied graph
        /// position when one is given, otherwise at the viewport centre.
        /// </summary>
        /// <param name="worldX">Optional graph-space X position.</param>
        /// <param name="worldY">Optional graph-space Y position.</param>
        public void OpenNodeSearch(double? worldX = null, double? worldY = null)
        {
            Point center = ScreenToWorld(new Point(ViewportWidth / 2, ViewportHeight / 2));
            nodeSearchWorldX = worldX ?? center.X;
            nodeSearchWorldY = worldY ?? center.Y;

            nodeSearchText = string.Empty;
            this.RaisePropertyChanged(nameof(NodeSearchText));
            RefreshSearchResults();
            IsNodeSearchOpen = true;
            NodeSearchRequested?.Invoke(this, EventArgs.Empty);
            StatusText = "Search for a node type. Enter adds the first result.";
        }

        /// <summary>Closes the add-node search panel.</summary>
        public void CloseNodeSearch()
        {
            if (!IsNodeSearchOpen) return;
            IsNodeSearchOpen = false;
            StatusText = "Right-click the canvas or press Space to add a node.";
        }

        /// <summary>Adds the first matching search result.</summary>
        public bool AddFirstSearchResult()
        {
            if (SearchResults.Count == 0) return false;
            AddCatalogEntry(SearchResults[0]);
            return true;
        }

        /// <summary>Adds a catalog entry at the position captured when search opened.</summary>
        /// <param name="entry">Catalog entry.</param>
        public void AddCatalogEntry(NodeCatalogEntryViewModel? entry)
        {
            if (entry is null) return;
            AddNode(entry.Registration, nodeSearchWorldX, nodeSearchWorldY);
            CloseNodeSearch();
        }

        /// <summary>
        /// Adds a node of the given type at a graph position.
        /// </summary>
        /// <param name="registration">Registered node type.</param>
        /// <param name="x">Graph-space X position.</param>
        /// <param name="y">Graph-space Y position.</param>
        /// <param name="recordUndo">Whether to record an undo step.</param>
        /// <returns>The created node wrapper, or null when creation failed.</returns>
        public NodeViewModel? AddNode(NodeTypeRegistration registration, double x, double y, bool recordUndo = true)
        {
            ArgumentNullException.ThrowIfNull(registration);

            Node node;
            try
            {
                node = registration.CreateInstance();
            }
            catch (Exception ex)
            {
                StatusText = $"Could not create {registration.DisplayName}: {ex.Message}";
                return null;
            }

            node.Name = CreateUniqueName(registration.DisplayName);
            node.X = Snap(x);
            node.Y = Snap(y);

            if (recordUndo) BeginChange($"Add {registration.DisplayName}");
            Graph.AddNode(node);
            NodeViewModel view = CreateNodeViewModel(node);
            Nodes.Add(view);
            if (recordUndo) EndChange();

            SelectNode(view);
            RebuildConnections();
            IsDirty = true;
            StatusText = $"Added {registration.DisplayName}.";
            NodeValueChanged?.Invoke(this, view);
            NotifyGraphStructureChanged();
            return view;
        }

        /// <summary>
        /// Adds a node by stable type id.
        /// </summary>
        /// <param name="typeId">Registered type id.</param>
        /// <param name="x">Graph-space X position.</param>
        /// <param name="y">Graph-space Y position.</param>
        /// <param name="recordUndo">Whether to record an undo step.</param>
        public NodeViewModel? AddNode(string typeId, double x, double y, bool recordUndo = true)
        {
            if (!catalogById.TryGetValue(typeId, out NodeCatalogEntryViewModel? entry))
            {
                StatusText = $"No registered node type '{typeId}'.";
                return null;
            }
            return AddNode(entry.Registration, x, y, recordUndo);
        }

        /// <summary>Adds a node at the viewport centre with a small cascade offset.</summary>
        /// <param name="entry">Catalog entry.</param>
        public NodeViewModel? AddNodeAtViewportCenter(NodeCatalogEntryViewModel entry)
        {
            if (entry is null) return null;
            Point world = ScreenToWorld(new Point(ViewportWidth / 2, ViewportHeight / 2));
            double cascade = (Nodes.Count % 6) * 24;
            return AddNode(entry.Registration, world.X - NodeEditorTheme.NodeWidth / 2 + cascade,
                world.Y - 60 + cascade);
        }

        /// <summary>
        /// Connects two ports, choosing the output/input orientation automatically.
        /// </summary>
        /// <param name="a">First port.</param>
        /// <param name="b">Second port.</param>
        /// <returns>True when a connection was created or already existed.</returns>
        public bool ConnectPorts(NodePortViewModel a, NodePortViewModel b)
        {
            NodePortViewModel? output = a.IsInput ? b : a;
            NodePortViewModel? input = a.IsInput ? a : b;
            if (output.IsInput || input.IsOutput)
            {
                StatusText = "Connections must run from an output to an input.";
                return false;
            }

            bool connected = false;
            string? failure = null;
            RecordChange("Connect", () =>
            {
                if (Graph.TryConnect(output.Model, input.Model, out _, out string? error)) connected = true;
                else failure = error;
            });

            if (!connected)
            {
                StatusText = failure ?? "Those ports cannot be connected.";
                return false;
            }

            RebuildConnections();
            IsDirty = true;
            StatusText = $"Connected {output.Node.Name}.{output.Name} to {input.Node.Name}.{input.Name}.";
            NodeValueChanged?.Invoke(this, input.Node);
            NotifyGraphStructureChanged();
            return true;
        }

        /// <summary>
        /// Detaches the connection feeding an input port and returns its source output,
        /// so the canvas can continue dragging a new wire from it.
        /// </summary>
        /// <param name="input">Input port to detach.</param>
        /// <param name="output">Source output port, when a connection existed.</param>
        /// <returns>True when a connection was removed.</returns>
        public bool DetachInput(NodePortViewModel input, out NodePortViewModel? output)
        {
            output = null;
            NodeConnection? connection = Graph.GetInputConnection(input.Model);
            if (connection is null) return false;
            output = FindPortView(connection.Output);
            if (output is null) return false;

            RecordChange("Disconnect", () => Graph.RemoveConnection(connection));
            RebuildConnections();
            IsDirty = true;
            StatusText = "Disconnected input.";
            NotifyGraphStructureChanged();
            return true;
        }

        /// <summary>Removes a connection wrapper from the graph.</summary>
        /// <param name="connection">Connection to remove.</param>
        public void RemoveConnection(ConnectionViewModel connection)
        {
            if (connection is null) return;
            RecordChange("Remove Connection", () => Graph.RemoveConnection(connection.Model));
            if (ReferenceEquals(SelectedConnection, connection)) SelectConnectionInternal(null, notify: false);
            RebuildConnections();
            IsDirty = true;
            StatusText = "Removed connection.";
            NotifyGraphStructureChanged();
            NotifySelectionChanged();
        }

        /// <summary>Deletes every selected node and connection.</summary>
        public void DeleteSelection()
        {
            if (!HasSelection) return;

            RecordChange("Delete Selection", () =>
            {
                ConnectionViewModel? connection = SelectedConnection;
                if (connection is not null && Graph.Connections.Contains(connection.Model))
                    Graph.RemoveConnection(connection.Model);

                foreach (NodeViewModel node in SelectedNodes.ToArray())
                {
                    Graph.RemoveNode(node.Model);
                    Nodes.Remove(node);
                    node.Dispose();
                }

                ClearSelectionCore();
                SelectConnectionInternal(null, notify: false);
            });

            RebuildConnections();
            IsDirty = true;
            StatusText = "Deleted selection.";
            NotifySelectionChanged();
            NotifyGraphStructureChanged();
        }

        /// <summary>Duplicates selected nodes and the connections between them.</summary>
        public void DuplicateSelection()
        {
            if (SelectedNodes.Count == 0) return;

            NodeConnection[] sourceConnections = [.. Graph.Connections];
            NodeViewModel[] sources = [.. SelectedNodes];
            Dictionary<string, NodeViewModel> duplicateByNodeId = new(StringComparer.Ordinal);

            BeginChange("Duplicate Selection");
            foreach (NodeViewModel source in sources)
            {
                NodeTypeRegistration? registration = NodeTypeRegistry.GetRegistration(source.Model.GetType());
                if (registration is null)
                {
                    StatusText = $"{source.Model.GetType().Name} is not registered, so it cannot be duplicated.";
                    continue;
                }

                Node clone = registration.CreateInstance();
                clone.Name = CreateUniqueName(source.Name);
                clone.X = Snap(source.Model.X + 32);
                clone.Y = Snap(source.Model.Y + 32);

                CopyPortDefaults(source.Model, clone);
                CopyProperties(source.Model, clone);

                Graph.AddNode(clone);
                NodeViewModel view = CreateNodeViewModel(clone);
                Nodes.Add(view);
                duplicateByNodeId[source.Model.Id] = view;
            }

            foreach (NodeConnection connection in sourceConnections)
            {
                if (!duplicateByNodeId.TryGetValue(connection.Output.Node.Id, out NodeViewModel? outputNode) ||
                    !duplicateByNodeId.TryGetValue(connection.Input.Node.Id, out NodeViewModel? inputNode))
                    continue;

                NodePort? output = outputNode.Model.FindPort(connection.Output.Name, NodePortDirection.Output);
                NodePort? input = inputNode.Model.FindPort(connection.Input.Name, NodePortDirection.Input);
                if (output is not null && input is not null)
                    Graph.TryConnect(output, input, out _, out _);
            }

            ClearSelectionCore();
            foreach (NodeViewModel duplicate in duplicateByNodeId.Values)
            {
                SelectedNodes.Add(duplicate);
                duplicate.IsSelected = true;
            }
            SelectedNode = SelectedNodes.Count > 0 ? SelectedNodes[^1] : null;
            EndChange();

            RebuildConnections();
            IsDirty = true;
            StatusText = duplicateByNodeId.Count == 1
                ? "Duplicated 1 node."
                : $"Duplicated {duplicateByNodeId.Count} nodes.";
            NotifySelectionChanged();
            NotifyGraphStructureChanged();
        }

        /// <summary>Renames a node and records an undo step.</summary>
        /// <param name="node">Node to rename.</param>
        /// <param name="name">New name.</param>
        public void RenameNode(NodeViewModel node, string name)
        {
            if (node is null) return;
            string clean = string.IsNullOrWhiteSpace(name) ? node.Model.GetType().Name : name.Trim();
            if (string.Equals(node.Model.Name, clean, StringComparison.Ordinal)) return;

            RecordChange("Rename Node", () => node.Name = clean);
            IsDirty = true;
            StatusText = $"Renamed node to '{node.Name}'.";
            NodeValueChanged?.Invoke(this, node);
        }

        /// <summary>
        /// Sets an input port's default value and invalidates previews.
        /// </summary>
        /// <param name="port">Input port.</param>
        /// <param name="value">New value.</param>
        public void SetPortDefault(NodePortViewModel port, object? value)
        {
            if (port is null || !port.IsInput) return;
            object? coerced = CoercePortValue(port, value);
            if (Equals(port.Model.DefaultValue, coerced)) return;

            port.Model.DefaultValue = coerced;
            port.Node.Model.NotifyChanged();
            OnNodeValueChanged(port.Node);
        }

        /// <summary>
        /// Sets a reflected node property and invalidates previews.
        /// </summary>
        /// <param name="property">Property wrapper.</param>
        /// <param name="value">New value.</param>
        public void SetNodeProperty(NodePropertyViewModel property, object? value)
        {
            if (property is null || !property.CanWrite) return;
            object? current = property.Descriptor.GetValue(property.Node.Model);
            if (Equals(current, value)) return;

            property.Descriptor.SetValue(property.Node.Model, value);
            property.NotifyValueChanged();
            property.Node.Model.NotifyChanged();
            OnNodeValueChanged(property.Node);
        }

        private void OnNodeValueChanged(NodeViewModel node)
        {
            IsDirty = true;
            EvaluationSummary = null;
            HasEvaluationError = false;
            StatusText = $"Modified {node.Name}.";
            NodeValueChanged?.Invoke(this, node);
            InvalidateCanvas();
        }

        private static object? CoercePortValue(NodePortViewModel port, object? value)
        {
            if (value is null) return null;
            Type target = port.Model.ValueType ?? typeof(object);
            if (target.IsInstanceOfType(value)) return ClampPort(port, value);

            try
            {
                if (target.IsEnum) return Enum.ToObject(target, value);
                if (target == typeof(float)) return ClampPort(port, Convert.ToSingle(value, CultureInfo.InvariantCulture));
                if (target == typeof(int)) return ClampPort(port, Convert.ToInt32(value, CultureInfo.InvariantCulture));
                if (target == typeof(bool)) return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
                if (target == typeof(string)) return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
            {
                return port.Model.DefaultValue;
            }

            return value;
        }

        private static object? ClampPort(NodePortViewModel port, object value)
        {
            if (value is float f) return (float)port.Model.ClampNumeric(f);
            if (value is double d) return port.Model.ClampNumeric(d);
            if (value is int i) return (int)port.Model.ClampNumeric(i);
            return value;
        }

        /// <summary>Returns the view wrapper for a model node.</summary>
        /// <param name="node">Model node.</param>
        public NodeViewModel? FindNodeView(Node node)
            => node is null ? null : Nodes.FirstOrDefault(n => ReferenceEquals(n.Model, node));

        /// <summary>Returns the view wrapper for a model port.</summary>
        /// <param name="port">Model port.</param>
        public NodePortViewModel? FindPortView(NodePort port)
        {
            if (port is null) return null;
            NodeViewModel? node = FindNodeView(port.Node);
            return node?.GetPort(port);
        }

        /// <summary>Returns true when a port has at least one connection.</summary>
        /// <param name="port">Model port.</param>
        public bool IsPortConnected(NodePort port)
            => port is not null && Graph.GetConnections(port).Count > 0;

        /// <summary>Returns true when two ports are currently connected.</summary>
        /// <param name="output">Output port.</param>
        /// <param name="input">Input port.</param>
        public bool AreConnected(NodePortViewModel output, NodePortViewModel input)
            => Graph.GetInputConnections(input.Model).Any(c => ReferenceEquals(c.Output, output.Model));

        /// <summary>Evaluates every node and updates the status line.</summary>
        public NodeGraphEvaluationResult EvaluateGraph()
        {
            NodeGraphEvaluationResult result = Graph.EvaluateAll();
            HasEvaluationError = !result.Succeeded;
            EvaluationSummary = result.Succeeded
                ? $"Evaluated {result.NodesEvaluated} nodes in {result.Duration.TotalMilliseconds:0.0} ms"
                : $"{result.Errors.Count} evaluation error{(result.Errors.Count == 1 ? "" : "s")}: {result.Errors[0]}";
            StatusText = EvaluationSummary;
            return result;
        }

        /// <summary>Starts an undo transaction if one is not already active.</summary>
        /// <param name="label">Human readable label for the change.</param>
        public void BeginChange(string label)
        {
            if (restoring || transaction is not null) return;
            transaction = new UndoTransaction(label, NodeGraphSerializer.Serialize(Graph), SelectedNode?.Model.Id);
        }

        /// <summary>Completes the active undo transaction, recording a step if needed.</summary>
        public void EndChange()
        {
            if (transaction is null) return;
            UndoTransaction active = transaction;
            transaction = null;

            string current = NodeGraphSerializer.Serialize(Graph);
            if (string.Equals(current, active.BeforeJson, StringComparison.Ordinal)) return;

            bool coalesce = lastCommitLabel == active.Label &&
                            (DateTime.UtcNow - lastCommitUtc).TotalMilliseconds < UndoCoalesceMilliseconds &&
                            undoStates.Count > 0;
            if (!coalesce)
            {
                undoStates.Add(new UndoState(active.BeforeJson, active.Label, active.SelectedNodeId));
                if (undoStates.Count > MaxUndoStates) undoStates.RemoveAt(0);
            }

            redoStates.Clear();
            lastCommitLabel = active.Label;
            lastCommitUtc = DateTime.UtcNow;
            IsDirty = true;
            RaiseUndoProperties();
        }

        /// <summary>Runs an action inside an undo transaction.</summary>
        /// <param name="label">Human readable label for the change.</param>
        /// <param name="action">Action to run.</param>
        public void RecordChange(string label, Action action)
        {
            BeginChange(label);
            try
            {
                action();
            }
            finally
            {
                EndChange();
            }
        }

        /// <summary>Undoes the last change.</summary>
        [RelayCommand]
        public void Undo()
        {
            if (transaction is not null) EndChange();
            if (undoStates.Count == 0) return;

            UndoState state = undoStates[^1];
            undoStates.RemoveAt(undoStates.Count - 1);
            redoStates.Add(new UndoState(NodeGraphSerializer.Serialize(Graph), state.Label, SelectedNode?.Model.Id));
            RestoreUndoState(state);
            StatusText = $"Undo: {state.Label}";
            RaiseUndoProperties();
        }

        /// <summary>Redoes the last undone change.</summary>
        [RelayCommand]
        public void Redo()
        {
            if (redoStates.Count == 0) return;

            UndoState state = redoStates[^1];
            redoStates.RemoveAt(redoStates.Count - 1);
            undoStates.Add(new UndoState(NodeGraphSerializer.Serialize(Graph), state.Label, SelectedNode?.Model.Id));
            RestoreUndoState(state);
            StatusText = $"Redo: {state.Label}";
            RaiseUndoProperties();
        }

        private void RestoreUndoState(UndoState state)
        {
            restoring = true;
            try
            {
                NodeGraph graph = NodeGraphSerializer.Deserialize(state.Json);
                ApplyGraphForRestore(graph);
                if (!string.IsNullOrWhiteSpace(state.SelectedNodeId))
                {
                    NodeViewModel? node = Nodes.FirstOrDefault(n => n.Model.Id == state.SelectedNodeId);
                    if (node is not null) SelectNode(node);
                }
            }
            catch (Exception ex)
            {
                StatusText = $"Could not restore the graph: {ex.Message}";
            }
            finally
            {
                restoring = false;
            }

            IsDirty = true;
            RaiseUndoProperties();
            NotifySelectionChanged();
            NotifyGraphStructureChanged();
        }

        private void ApplyGraphForRestore(NodeGraph graph)
        {
            foreach (NodeViewModel node in Nodes) node.Dispose();
            Nodes.Clear();
            Connections.Clear();
            SelectedNodes.Clear();
            SelectedNode = null;
            SelectConnectionInternal(null, notify: false);

            Graph = graph;
            this.RaisePropertyChanged(nameof(Graph));
            this.RaisePropertyChanged(nameof(GraphName));
            RebuildFromGraph();
            cameraInitialized = true;
        }

        private void RaiseUndoProperties()
        {
            this.RaisePropertyChanged(nameof(CanUndo));
            this.RaisePropertyChanged(nameof(CanRedo));
            this.RaisePropertyChanged(nameof(UndoLabel));
            this.RaisePropertyChanged(nameof(RedoLabel));
        }

        /// <summary>Clears undo and redo history.</summary>
        public void ClearHistory()
        {
            undoStates.Clear();
            redoStates.Clear();
            transaction = null;
            RaiseUndoProperties();
        }

        /// <summary>Serializes the edited graph.</summary>
        public string SerializeGraph() => NodeGraphSerializer.Serialize(Graph);

        /// <summary>Marks the current graph as saved and updates the status line.</summary>
        public void MarkSaved()
        {
            IsDirty = false;
            StatusText = $"Saved '{Graph.Name}'.";
        }

        /// <summary>
        /// Loads a graph from JSON, replacing the current content with an undoable step.
        /// </summary>
        /// <param name="json">Serialized graph.</param>
        /// <returns>True when the graph was loaded.</returns>
        public bool LoadGraph(string json)
        {
            try
            {
                NodeGraph graph = NodeGraphSerializer.Deserialize(json);
                RecordChange("Load Graph", () => ApplyGraphForRestore(graph));
                RebuildConnections();
                IsDirty = false;
                StatusText = $"Loaded graph '{Graph.Name}'.";
                NotifyGraphStructureChanged();
                return true;
            }
            catch (Exception ex)
            {
                StatusText = $"Could not load graph: {ex.Message}";
                return false;
            }
        }

        /// <summary>Returns a unique node name based on a requested name.</summary>
        /// <param name="requested">Requested base name.</param>
        public string CreateUniqueName(string requested)
        {
            string baseName = string.IsNullOrWhiteSpace(requested) ? "Node" : requested.Trim();
            if (!Nodes.Any(n => string.Equals(n.Name, baseName, StringComparison.OrdinalIgnoreCase)))
                return baseName;

            int suffix = 2;
            while (Nodes.Any(n => string.Equals(n.Name, $"{baseName} {suffix}", StringComparison.OrdinalIgnoreCase)))
                suffix++;
            return $"{baseName} {suffix}";
        }

        private double Snap(double value)
        {
            if (!SnapToGrid) return value;
            const double grid = 20;
            return Math.Round(value / grid) * grid;
        }

        private static void CopyPortDefaults(Node source, Node destination)
        {
            foreach (NodePort input in destination.Inputs)
            {
                NodePort? sourcePort = source.FindPort(input.Name, NodePortDirection.Input);
                if (sourcePort is null) continue;
                input.DefaultValue = sourcePort.DefaultValue;
            }
        }

        private static void CopyProperties(Node source, Node destination)
        {
            IReadOnlyList<NodePropertyDescriptor> sourceProperties = NodeReflection.GetProperties(source);
            foreach (NodePropertyDescriptor destinationProperty in NodeReflection.GetProperties(destination))
            {
                NodePropertyDescriptor? sourceProperty = sourceProperties.FirstOrDefault(p => p.Name == destinationProperty.Name);
                if (sourceProperty is null) continue;
                destinationProperty.SetValue(destination, sourceProperty.GetValue(source));
            }
        }

        /// <summary>Zooms in around the viewport centre.</summary>
        [RelayCommand]
        private void ZoomIn() => ZoomAt(new Point(ViewportWidth / 2, ViewportHeight / 2), 1.2);

        /// <summary>Zooms out around the viewport centre.</summary>
        [RelayCommand]
        private void ZoomOut() => ZoomAt(new Point(ViewportWidth / 2, ViewportHeight / 2), 1 / 1.2);

        /// <summary>Resets the camera and frames the graph.</summary>
        [RelayCommand]
        private void ResetZoom() => ResetView();

        /// <summary>Frames the whole graph.</summary>
        [RelayCommand]
        private void FrameAllNodes() => FrameAll();

        /// <summary>Frames the current selection.</summary>
        [RelayCommand]
        private void FrameSelected() => FrameSelection();

        /// <summary>Opens the add-node search panel at the viewport centre.</summary>
        [RelayCommand]
        private void OpenNodeSearchPanel() => OpenNodeSearch();

        /// <summary>Closes the add-node search panel.</summary>
        [RelayCommand]
        private void CloseNodeSearchPanel() => CloseNodeSearch();

        /// <summary>Evaluates the graph.</summary>
        [RelayCommand]
        private void Evaluate() => EvaluateGraph();

        /// <summary>Toggles the inspector sidebar.</summary>
        [RelayCommand]
        private void ToggleInspector() => ShowInspector = !ShowInspector;

        /// <summary>Toggles grid snapping.</summary>
        [RelayCommand]
        private void ToggleSnap() => SnapToGrid = !SnapToGrid;

        /// <summary>Deletes the current selection.</summary>
        [RelayCommand]
        private void Delete() => DeleteSelection();

        /// <summary>Duplicates the selected nodes.</summary>
        [RelayCommand]
        private void Duplicate() => DuplicateSelection();

        /// <summary>Selects every node.</summary>
        [RelayCommand]
        private void SelectAll() => SelectNodes(Nodes, additive: false);

        private sealed record UndoState(string Json, string Label, string? SelectedNodeId);

        private sealed record UndoTransaction(string Label, string BeforeJson, string? SelectedNodeId);
    }
}
