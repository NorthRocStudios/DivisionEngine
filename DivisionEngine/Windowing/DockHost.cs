//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE file in the project root
// for full license terms.
//
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DivisionEngine.Editor.Windowing
{
    /// <summary>
    /// Renders a dock layout tree into ordinary grids and tab controls. Existing tab
    /// group views are reused when the tree is restructured, so moving a group (or
    /// docking another group next to it) does not recreate the window contents.
    /// </summary>
    public sealed class DockHost : UserControl, IDisposable
    {
        private readonly Grid treeRoot = new();
        private readonly Dictionary<DockGroupNode, DockGroupView> groupViews = [];
        private readonly Dictionary<DockNode, (Grid Grid, int Index, bool Horizontal)> cells = [];

        private DockSurface? surface;
        private DockLayout? layout;
        private DockNode? node;
        private bool disposed;

        /// <summary>Creates an empty dock host; call <see cref="Attach"/> to show a layout.</summary>
        public DockHost()
        {
            MinWidth = 0;
            MinHeight = 0;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            VerticalAlignment = VerticalAlignment.Stretch;

            Grid container = new();
            container.Children.Add(treeRoot);
            Adorner = new DockDropAdorner
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            container.Children.Add(Adorner);
            Content = container;
        }

        /// <summary>The five-zone drop compass rendered above the tab groups.</summary>
        public DockDropAdorner Adorner { get; }

        /// <summary>The node rendered by this host.</summary>
        public DockNode? Node => node;

        /// <summary>The surface this host belongs to.</summary>
        public DockSurface? Surface => surface;

        /// <summary>Binds the host to a surface and starts rendering the layout root.</summary>
        public void Attach(DockSurface dockSurface)
        {
            if (disposed) return;

            if (layout != null) layout.StructureChanged -= OnStructureChanged;
            surface = dockSurface;
            layout = dockSurface.Layout;
            layout.StructureChanged += OnStructureChanged;
            node = dockSurface.Floating?.Group ?? layout.Root;
            Rebuild(null);
        }

        private void OnStructureChanged(DockStructureChange change)
        {
            if (disposed || surface == null || layout == null) return;

            DockNode? next = surface.Floating?.Group ?? layout.Root;
            node = next;

            if (!ShouldRebuild(change)) return;
            Rebuild(change);
        }

        private bool ShouldRebuild(DockStructureChange change)
        {
            if (node == null) return true;
            if (change.Kind == DockStructureChangeKind.Reset) return true;

            // Only rebuild when the change touches something this host displays. A
            // floating window elsewhere in the workspace must not force the main dock
            // to rebuild (which would recreate its visible window views).
            if (change.Split != null && node.Contains(change.Split)) return true;
            if (change.Group != null && groupViews.ContainsKey(change.Group)) return true;
            if (change.Group != null && node.Contains(change.Group)) return true;
            return false;
        }

        private void Rebuild(DockStructureChange? change)
        {
            // Detach the old visual tree first: group views may move to a new parent in
            // the rebuilt tree and a control cannot have two visual parents. Clearing the
            // root only detaches the outer grid, so each cached group view must also be
            // removed from the nested grid that directly owns it.
            foreach (DockGroupView view in groupViews.Values)
            {
                if (view.Parent is Panel panel) panel.Children.Remove(view);
            }
            treeRoot.Children.Clear();
            cells.Clear();

            if (node == null)
            {
                Adorner.Hide();
                return;
            }

            Control visual = BuildView(node);
            treeRoot.Children.Add(visual);

            HashSet<DockGroupNode> usedGroups = node.Groups.ToHashSet();
            foreach (KeyValuePair<DockGroupNode, DockGroupView> pair in groupViews.ToList())
            {
                if (usedGroups.Contains(pair.Key)) continue;
                pair.Value.Dispose();
                groupViews.Remove(pair.Key);
            }

            if (change == null || change.Kind != DockStructureChangeKind.SplitInserted) return;

            if (change.Group != null && groupViews.TryGetValue(change.Group, out DockGroupView? newView))
                DockAnimations.FadeIn(newView, 0, 1, 150);

            if (change.Split != null && change.Child != null &&
                cells.TryGetValue(change.Child, out (Grid Grid, int Index, bool Horizontal) cell))
            {
                DockAnimations.GrowCell(cell.Grid, cell.Index, cell.Horizontal, change.Child.Size);
            }
        }

        private Control BuildView(DockNode current)
        {
            if (current is DockGroupNode group)
                return GetOrCreateGroupView(group);

            return BuildSplitView((DockSplitNode)current);
        }

        private DockGroupView GetOrCreateGroupView(DockGroupNode group)
        {
            if (groupViews.TryGetValue(group, out DockGroupView? view)) return view;
            if (surface == null) throw new InvalidOperationException("DockHost is not attached to a surface.");

            view = new DockGroupView(surface, group);
            groupViews[group] = view;
            return view;
        }

        private Control BuildSplitView(DockSplitNode split)
        {
            bool horizontal = split.Orientation == Orientation.Horizontal;
            Grid grid = new()
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };

            for (int i = 0; i < split.Children.Count; i++)
            {
                if (i > 0)
                {
                    GridSplitter splitter = new()
                    {
                        ResizeDirection = horizontal ? GridResizeDirection.Columns : GridResizeDirection.Rows,
                        ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                        Background = EditorColor.FromRGB(68, 68, 68),
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Stretch,
                        Cursor = new Cursor(horizontal ? StandardCursorType.SizeWestEast : StandardCursorType.SizeNorthSouth),
                    };
                    splitter.DragCompleted += (_, _) => CommitSplitSizes(split, grid, horizontal);

                    if (horizontal)
                    {
                        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(2, GridUnitType.Pixel)));
                        Grid.SetColumn(splitter, i * 2 - 1);
                    }
                    else
                    {
                        grid.RowDefinitions.Add(new RowDefinition(new GridLength(2, GridUnitType.Pixel)));
                        Grid.SetRow(splitter, i * 2 - 1);
                    }
                    grid.Children.Add(splitter);
                }

                DockNode child = split.Children[i];
                Control childView = BuildView(child);

                if (horizontal)
                {
                    grid.ColumnDefinitions.Add(new ColumnDefinition
                    {
                        Width = child.Size.ToGridLength(),
                        MinWidth = 60,
                    });
                    Grid.SetColumn(childView, i * 2);
                }
                else
                {
                    grid.RowDefinitions.Add(new RowDefinition
                    {
                        Height = child.Size.ToGridLength(),
                        MinHeight = 40,
                    });
                    Grid.SetRow(childView, i * 2);
                }

                grid.Children.Add(childView);
                cells[child] = (grid, i * 2, horizontal);
            }

            return grid;
        }

        private void CommitSplitSizes(DockSplitNode split, Grid grid, bool horizontal)
        {
            if (layout == null) return;

            List<DockSize> sizes = [];
            for (int i = 0; i < split.Children.Count; i++)
            {
                GridLength length = horizontal
                    ? grid.ColumnDefinitions[i * 2].Width
                    : grid.RowDefinitions[i * 2].Height;
                sizes.Add(length.IsAbsolute ? DockSize.Pixels(length.Value) : DockSize.Star(length.Value));
            }
            layout.CommitSplitSizes(split, sizes);
        }

        /// <summary>True when this host currently displays the given group.</summary>
        public bool ContainsGroup(DockGroupNode group) =>
            groupViews.TryGetValue(group, out DockGroupView? view) && view.Parent != null;

        /// <summary>Finds the tab group under a screen point.</summary>
        public DockGroupNode? GroupAtScreenPoint(PixelPoint screenPoint)
        {
            try
            {
                Point local = this.PointToClient(screenPoint);
                return GroupAt(local);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Finds the tab group under a host-relative point.</summary>
        public DockGroupNode? GroupAt(Point point)
        {
            foreach (KeyValuePair<DockGroupNode, DockGroupView> pair in groupViews)
            {
                if (pair.Value.Parent == null) continue;
                Point? origin = pair.Value.TranslatePoint(new Point(0, 0), this);
                if (origin == null) continue;
                if (new Rect(origin.Value, pair.Value.Bounds.Size).Contains(point)) return pair.Key;
            }
            return null;
        }

        /// <summary>Gets a group's bounds in host coordinates.</summary>
        public Rect GetGroupBounds(DockGroupNode group)
        {
            if (!groupViews.TryGetValue(group, out DockGroupView? view)) return default;
            Point? origin = view.TranslatePoint(new Point(0, 0), this);
            return origin == null ? default : new Rect(origin.Value, view.Bounds.Size);
        }

        /// <summary>Height of a group's tab header strip.</summary>
        public double GetTabStripHeight(DockGroupNode group)
        {
            if (!groupViews.TryGetValue(group, out DockGroupView? view)) return 28;
            if (view.Tabs.ContainerFromIndex(0) is Control header && header.Bounds.Height > 0)
                return header.Bounds.Height;
            return 28;
        }

        /// <summary>Gets the realized tab header bounds of a group in host coordinates.</summary>
        public List<Rect> GetTabHeaderBounds(DockGroupNode group)
        {
            List<Rect> bounds = [];
            if (!groupViews.TryGetValue(group, out DockGroupView? view)) return bounds;

            for (int i = 0; i < group.Tabs.Count; i++)
            {
                if (view.Tabs.ContainerFromIndex(i) is not Control header) continue;
                Point? origin = header.TranslatePoint(new Point(0, 0), this);
                if (origin == null) continue;
                bounds.Add(new Rect(origin.Value, header.Bounds.Size));
            }
            return bounds;
        }

        /// <summary>Shows the docking compass over a target group.</summary>
        public void ShowDropTarget(DockGroupNode group, DockZone zone, int insertionIndex)
        {
            Rect bounds = GetGroupBounds(group);
            double header = GetTabStripHeight(group);
            double? insertionX = null;

            if (zone == DockZone.Center)
            {
                List<Rect> headers = GetTabHeaderBounds(group);
                if (headers.Count > 0)
                {
                    insertionX = insertionIndex >= headers.Count
                        ? headers[^1].X + headers[^1].Width
                        : headers[Math.Clamp(insertionIndex, 0, headers.Count)].X;
                }
            }

            Adorner.Show(bounds, zone, insertionX, header);
        }

        /// <summary>Hides the docking compass.</summary>
        public void HideDropTarget() => Adorner.Hide();

        /// <inheritdoc/>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (layout != null) layout.StructureChanged -= OnStructureChanged;
            foreach (DockGroupView view in groupViews.Values) view.Dispose();
            groupViews.Clear();
            cells.Clear();
            treeRoot.Children.Clear();
            Content = null;
        }
    }
}
