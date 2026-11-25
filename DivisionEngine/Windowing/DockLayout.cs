//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE file in the project root
// for full license terms.
//
using Avalonia.Controls;
using Avalonia.Layout;
using DivisionEngine.Editor.ViewModels;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;

namespace DivisionEngine.Editor.Windowing
{
    /// <summary>
    /// Where a dragged tab is dropped relative to a tab group. <see cref="Center"/>
    /// merges the tab into the group, every other value creates a new group on that
    /// side of the target.
    /// </summary>
    public enum DockZone
    {
        Center,
        Left,
        Right,
        Top,
        Bottom,
    }

    /// <summary>
    /// The size of one child of a <see cref="DockSplitNode"/>. Sizes are either fixed
    /// pixels (the side panels of the default layout) or proportional stars (the
    /// centre viewport and splits created by dragging).
    /// </summary>
    public readonly struct DockSize : IEquatable<DockSize>
    {
        /// <summary>Creates a sized child.</summary>
        public DockSize(double value, bool isPixel)
        {
            Value = isPixel ? Math.Max(1, value) : Math.Max(MinimumStar, value);
            IsPixel = isPixel;
        }

        /// <summary>Smallest legal star value, so a collapsing animation can start near zero.</summary>
        public const double MinimumStar = 0.0001;

        /// <summary>The numeric size. Pixels when <see cref="IsPixel"/> is true, otherwise star units.</summary>
        public double Value { get; }

        /// <summary>Whether <see cref="Value"/> is in pixels rather than proportional star units.</summary>
        public bool IsPixel { get; }

        /// <summary>Creates a fixed pixel size.</summary>
        public static DockSize Pixels(double value) => new(value, true);

        /// <summary>Creates a proportional star size.</summary>
        public static DockSize Star(double value) => new(value, false);

        /// <summary>Converts this size to a grid definition length.</summary>
        public GridLength ToGridLength() =>
            new(Value, IsPixel ? GridUnitType.Pixel : GridUnitType.Star);

        /// <summary>Creates a copy of this size with another value.</summary>
        public DockSize WithValue(double value) => new(value, IsPixel);

        /// <summary>Halves this size, keeping its unit, with a sensible minimum.</summary>
        public DockSize Half() => IsPixel
            ? Pixels(Math.Max(24, Value / 2))
            : Star(Math.Max(0.02, Value / 2));

        /// <inheritdoc/>
        public bool Equals(DockSize other) => IsPixel == other.IsPixel && Math.Abs(Value - other.Value) < 0.0001;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is DockSize other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(Value, IsPixel);

        /// <inheritdoc/>
        public override string ToString() => IsPixel ? $"{Value:0.#}px" : $"{Value:0.###}*";
    }

    /// <summary>
    /// Base type for every node in a dock layout tree: either a <see cref="DockGroupNode"/>
    /// (a tab group) or a <see cref="DockSplitNode"/> (a horizontal or vertical split).
    /// </summary>
    public abstract class DockNode : ViewModelBase
    {
        /// <summary>The split that contains this node, or null when this node is a root.</summary>
        public DockSplitNode? Parent { get; internal set; }

        /// <summary>This node's size within its parent split. Ignored for a root node.</summary>
        public DockSize Size { get; internal set; } = DockSize.Star(1);

        /// <summary>Enumerates the tab groups below this node, in visual order.</summary>
        public abstract IEnumerable<DockGroupNode> Groups { get; }

        /// <summary>The first tab group below this node, if any.</summary>
        public DockGroupNode? FirstGroup => Groups.FirstOrDefault();

        /// <summary>True when no tab groups below this node contain any tabs.</summary>
        public abstract bool IsEmpty { get; }

        /// <summary>True when <paramref name="candidate"/> is this node or lives below it.</summary>
        public bool Contains(DockNode candidate)
        {
            for (DockNode? node = candidate; node != null; node = node.Parent)
                if (ReferenceEquals(node, this)) return true;
            return false;
        }

        /// <summary>Enumerates every node below and including this one.</summary>
        public IEnumerable<DockNode> Descendants()
        {
            yield return this;
            if (this is DockSplitNode split)
            {
                foreach (DockNode child in split.Children)
                {
                    foreach (DockNode descendant in child.Descendants())
                        yield return descendant;
                }
            }
        }
    }

    /// <summary>
    /// A tab group: the same visual unit as the editor's original left/centre/right/
    /// bottom panels, but any number of them can now exist at any position in a layout.
    /// </summary>
    public sealed class DockGroupNode : DockNode
    {
        private EditorWindowViewModel? selectedTab;

        /// <summary>Creates an empty group that belongs to a logical panel kind.</summary>
        /// <param name="zone">One of left/center/right/bottom, or a custom name.</param>
        public DockGroupNode(string zone = "center")
        {
            Zone = zone;
            Tabs.CollectionChanged += OnTabsChanged;
        }

        /// <summary>The logical panel kind this group belongs to (left/center/right/bottom).</summary>
        public string Zone { get; set; }

        /// <summary>The windows shown as tabs in this group.</summary>
        public ObservableCollection<EditorWindowViewModel> Tabs { get; } = [];

        /// <summary>The active tab, or null when the group is empty.</summary>
        public EditorWindowViewModel? SelectedTab
        {
            get => selectedTab;
            set
            {
                // A group can only select a tab it actually contains.
                if (value != null && !Tabs.Contains(value)) return;
                this.RaiseAndSetIfChanged(ref selectedTab, value);
            }
        }

        /// <inheritdoc/>
        public override IEnumerable<DockGroupNode> Groups
        {
            get { yield return this; }
        }

        /// <inheritdoc/>
        public override bool IsEmpty => Tabs.Count == 0;

        /// <summary>Adds a tab and selects it.</summary>
        /// <param name="tab">Window view model to add.</param>
        /// <param name="index">Insertion index; -1 appends.</param>
        public void AddTab(EditorWindowViewModel tab, int index = -1)
        {
            if (index < 0 || index >= Tabs.Count) Tabs.Add(tab);
            else Tabs.Insert(index, tab);
            SelectedTab = tab;
        }

        /// <summary>Removes a tab, selecting its neighbour when it was active.</summary>
        /// <returns>True when the tab was present.</returns>
        public bool RemoveTab(EditorWindowViewModel tab)
        {
            int index = Tabs.IndexOf(tab);
            if (index < 0) return false;

            bool wasSelected = ReferenceEquals(selectedTab, tab);
            Tabs.RemoveAt(index);
            if (wasSelected)
            {
                if (Tabs.Count == 0) SelectedTab = null;
                else SelectedTab = Tabs[Math.Min(index, Tabs.Count - 1)];
            }
            return true;
        }

        private void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (selectedTab != null && Tabs.Contains(selectedTab)) return;

            // Collection changed without going through RemoveTab (for example a direct
            // RemoveAt during layout loading): fall back to a valid selection.
            if (Tabs.Count == 0) selectedTab = null;
            else if (selectedTab == null || !Tabs.Contains(selectedTab))
                selectedTab = Tabs[0];

            this.RaisePropertyChanged(nameof(SelectedTab));
        }
    }

    /// <summary>
    /// A horizontal or vertical split that arranges child groups and nested splits,
    /// separated by resizable splitters.
    /// </summary>
    public sealed class DockSplitNode : DockNode
    {
        /// <summary>Creates an empty split.</summary>
        /// <param name="orientation">Horizontal lays children out in columns; vertical in rows.</param>
        public DockSplitNode(Orientation orientation)
        {
            Orientation = orientation;
        }

        /// <summary>Layout direction of the children.</summary>
        public Orientation Orientation { get; internal set; }

        /// <summary>Child nodes, in visual order.</summary>
        public ObservableCollection<DockNode> Children { get; } = [];

        /// <inheritdoc/>
        public override IEnumerable<DockGroupNode> Groups => Children.SelectMany(child => child.Groups);

        /// <inheritdoc/>
        public override bool IsEmpty => Children.All(child => child.IsEmpty);

        /// <summary>Adds a child at the given index (-1 appends).</summary>
        public void AddChild(DockNode child, int index = -1)
        {
            if (child.Parent != null)
                throw new InvalidOperationException("A dock node can only have one parent.");

            child.Parent = this;
            if (index < 0 || index >= Children.Count) Children.Add(child);
            else Children.Insert(index, child);
        }

        /// <summary>Removes a child and clears its parent link.</summary>
        public bool RemoveChild(DockNode child)
        {
            if (!Children.Remove(child)) return false;
            child.Parent = null;
            return true;
        }
    }

    /// <summary>
    /// A tab group hosted in its own top-level window rather than in the main dock tree.
    /// </summary>
    public sealed class DockFloatingGroup : ViewModelBase
    {
        private int x;
        private int y;
        private double width = 440;
        private double height = 320;

        /// <summary>Creates a floating group around <paramref name="group"/>.</summary>
        public DockFloatingGroup(DockGroupNode group, int x = 0, int y = 0, double width = 440, double height = 320)
        {
            Group = group;
            this.x = x;
            this.y = y;
            this.width = Math.Max(260, width);
            this.height = Math.Max(160, height);
        }

        /// <summary>The tab group shown in the floating window.</summary>
        public DockGroupNode Group { get; }

        /// <summary>Screen X position of the floating window.</summary>
        public int X
        {
            get => x;
            set => this.RaiseAndSetIfChanged(ref x, value);
        }

        /// <summary>Screen Y position of the floating window.</summary>
        public int Y
        {
            get => y;
            set => this.RaiseAndSetIfChanged(ref y, value);
        }

        /// <summary>Width of the floating window.</summary>
        public double Width
        {
            get => width;
            set => this.RaiseAndSetIfChanged(ref width, Math.Max(260, value));
        }

        /// <summary>Height of the floating window.</summary>
        public double Height
        {
            get => height;
            set => this.RaiseAndSetIfChanged(ref height, Math.Max(160, value));
        }
    }

    /// <summary>Describes the structural change that just happened, for insertion animations.</summary>
    public sealed class DockStructureChange
    {
        /// <summary>Creates a change record.</summary>
        public DockStructureChange(DockStructureChangeKind kind, DockSplitNode? split = null,
            DockNode? child = null, DockGroupNode? group = null)
        {
            Kind = kind;
            Split = split;
            Child = child;
            Group = group;
        }

        /// <summary>Kind of change.</summary>
        public DockStructureChangeKind Kind { get; }

        /// <summary>The split that gained a child, when <see cref="Kind"/> is <see cref="DockStructureChangeKind.SplitInserted"/>.</summary>
        public DockSplitNode? Split { get; }

        /// <summary>The new child of <see cref="Split"/>.</summary>
        public DockNode? Child { get; }

        /// <summary>The new or removed tab group, when one is involved.</summary>
        public DockGroupNode? Group { get; }
    }

    /// <summary>Kinds of structural dock layout change.</summary>
    public enum DockStructureChangeKind
    {
        /// <summary>The whole layout was replaced or normalized.</summary>
        Reset,
        /// <summary>A new split was inserted around an existing group.</summary>
        SplitInserted,
        /// <summary>A tab group was removed from the tree.</summary>
        GroupRemoved,
    }

    /// <summary>
    /// The dockable window layout: a tree of tab groups plus the floating groups that
    /// live in their own windows. All operations keep the tree valid: no split ever has
    /// a single child, a tab is never shown in two groups, and an empty group is only
    /// kept when it is the one remaining group of a window.
    /// </summary>
    public sealed class DockLayout : ViewModelBase
    {
        private DockNode? root;

        /// <summary>Raised after a structural change (not for plain tab insert/remove/reorder).</summary>
        public event Action<DockStructureChange>? StructureChanged;

        /// <summary>Raised when a floating group must get its own window.</summary>
        public event Action<DockFloatingGroup>? FloatingGroupAdded;

        /// <summary>Raised when a floating group's window must be closed.</summary>
        public event Action<DockFloatingGroup>? FloatingGroupRemoved;

        /// <summary>The last structural change, used by the view to animate insertions.</summary>
        public DockStructureChange? LastChange { get; private set; }

        /// <summary>The root of the dock tree. Always a group or split when set.</summary>
        public DockNode? Root => root;

        /// <summary>Tab groups that are hosted in their own windows.</summary>
        public ObservableCollection<DockFloatingGroup> FloatingGroups { get; } = [];

        /// <summary>Creates a layout with a single empty centre group.</summary>
        public DockLayout()
        {
            root = new DockGroupNode("center");
        }

        /// <summary>All docked groups, top to bottom and left to right.</summary>
        public IEnumerable<DockGroupNode> Groups => root?.Groups ?? [];

        /// <summary>All groups including floating ones, in creation order.</summary>
        public IEnumerable<DockGroupNode> AllGroups =>
            (root?.Groups ?? []).Concat(FloatingGroups.Select(floating => floating.Group));

        /// <summary>All open windows across the dock tree and floating groups.</summary>
        public IEnumerable<EditorWindowViewModel> AllTabs =>
            AllGroups.SelectMany(group => group.Tabs);

        /// <summary>Finds the group that currently contains <paramref name="tab"/>.</summary>
        public DockGroupNode? FindGroup(EditorWindowViewModel tab) =>
            AllGroups.FirstOrDefault(group => group.Tabs.Contains(tab));

        /// <summary>
        /// Finds a group of the given logical zone. Docked groups are preferred over
        /// floating ones, so opening a window never dives into a floating window while a
        /// matching docked group exists.
        /// </summary>
        public DockGroupNode? FindGroupByZone(string zone)
        {
            DockGroupNode? docked = root?.Groups.FirstOrDefault(
                group => string.Equals(group.Zone, zone, StringComparison.OrdinalIgnoreCase));
            if (docked != null) return docked;

            // New windows open in the main window; floating groups are only used when
            // there is no docked group at all.
            return root?.FirstGroup
                ?? FloatingGroups.Select(entry => entry.Group).FirstOrDefault(
                    group => string.Equals(group.Zone, zone, StringComparison.OrdinalIgnoreCase))
                ?? AllGroups.FirstOrDefault();
        }

        /// <summary>The docked group a "dock back" action should target (never a floating one).</summary>
        public DockGroupNode? PreferredDockGroup()
        {
            return root?.Groups.FirstOrDefault(
                       group => string.Equals(group.Zone, "center", StringComparison.OrdinalIgnoreCase))
                   ?? root?.FirstGroup;
        }

        /// <summary>True when <paramref name="group"/> belongs to the dock tree rather than a floating window.</summary>
        public bool IsDocked(DockGroupNode group) => root != null && root.Contains(group);

        /// <summary>Replaces the whole layout, including floating groups.</summary>
        public void Load(DockNode newRoot, IEnumerable<DockFloatingGroup>? floatingGroups = null)
        {
            foreach (DockFloatingGroup floating in FloatingGroups.ToList())
                RemoveFloatingGroupInternal(floating, notify: false);
            FloatingGroups.Clear();

            root = newRoot;
            root.Parent = null;
            root.Size = DockSize.Star(1);

            LastChange = new DockStructureChange(DockStructureChangeKind.Reset);
            StructureChanged?.Invoke(LastChange);

            if (floatingGroups != null)
            {
                foreach (DockFloatingGroup floating in floatingGroups)
                {
                    floating.Group.Parent = null;
                    FloatingGroups.Add(floating);
                    FloatingGroupAdded?.Invoke(floating);
                }
            }
        }

        /// <summary>Adds a tab to a group (no structural change).</summary>
        public void AddTab(DockGroupNode group, EditorWindowViewModel tab, int index = -1)
        {
            if (group.Tabs.Contains(tab)) return;
            group.AddTab(tab, index);
        }

        /// <summary>
        /// Moves a tab to another group, collapsing the source group when it becomes empty.
        /// </summary>
        /// <param name="tab">Tab to move.</param>
        /// <param name="target">Destination group.</param>
        /// <param name="index">Insertion index in the destination; -1 appends.</param>
        /// <returns>True when the tab was found and moved.</returns>
        public bool MoveTab(EditorWindowViewModel tab, DockGroupNode target, int index = -1)
        {
            DockGroupNode? source = FindGroup(tab);
            if (source == null) return false;

            DockStructureChange? change = null;
            if (ReferenceEquals(source, target))
            {
                int oldIndex = target.Tabs.IndexOf(tab);
                int newIndex = index < 0 ? target.Tabs.Count - 1 : Math.Clamp(index, 0, target.Tabs.Count - 1);
                if (oldIndex != newIndex) target.Tabs.Move(oldIndex, newIndex);
                target.SelectedTab = tab;
                return true;
            }

            source.RemoveTab(tab);
            change = CleanupEmptySource(source);

            int insertIndex = index < 0 ? target.Tabs.Count : Math.Clamp(index, 0, target.Tabs.Count);
            target.AddTab(tab, insertIndex);

            if (change != null) Notify(change);
            return true;
        }

        /// <summary>
        /// Splits <paramref name="target"/> on the requested side and moves the given tab
        /// into the new group. This is the operation behind an edge drop.
        /// </summary>
        public DockGroupNode SplitGroupWithTab(DockGroupNode target, DockZone zone, EditorWindowViewModel tab)
        {
            if (zone == DockZone.Center)
                throw new ArgumentException("Center is not a split zone.", nameof(zone));

            DockGroupNode? source = FindGroup(tab);
            DockStructureChange? removal = null;

            // Detach first. The target is protected from auto-collapse so dragging the
            // only tab of a group onto its own edge leaves the original group empty.
            if (source != null)
            {
                source.RemoveTab(tab);
                if (!ReferenceEquals(source, target)) removal = CleanupEmptySource(source);
            }

            DockGroupNode newGroup = InsertSplit(target, zone, out DockSplitNode split, out DockNode splitChild);
            newGroup.Tabs.Add(tab);
            newGroup.SelectedTab = tab;

            Notify(new DockStructureChange(DockStructureChangeKind.SplitInserted, split, splitChild, newGroup));
            return newGroup;
        }

        /// <summary>
        /// Closes a tab. Empty side groups are removed; the final group of the main window
        /// is kept so the editor always has a place to open windows.
        /// </summary>
        public bool CloseTab(EditorWindowViewModel tab)
        {
            DockGroupNode? group = FindGroup(tab);
            if (group == null) return false;

            group.RemoveTab(tab);
            if (group.Tabs.Count > 0) return true;

            DockFloatingGroup? floating = FloatingGroups.FirstOrDefault(f => ReferenceEquals(f.Group, group));
            if (floating != null)
            {
                RemoveFloatingGroupInternal(floating, notify: true);
                Notify(new DockStructureChange(DockStructureChangeKind.GroupRemoved, group: group));
                return true;
            }

            if (group.Parent != null)
            {
                DockStructureChange change = RemoveGroupInternal(group);
                Notify(change);
            }
            return true;
        }

        /// <summary>Closes every tab of a group.</summary>
        public void CloseGroup(DockGroupNode group)
        {
            foreach (EditorWindowViewModel tab in group.Tabs.ToList())
                CloseTab(tab);
        }

        /// <summary>
        /// Moves a tab into a new floating window.
        /// </summary>
        /// <returns>The created floating group, or null when the tab was not found or was
        /// already alone in a floating window.</returns>
        public DockFloatingGroup? FloatTab(EditorWindowViewModel tab, int x = 0, int y = 0,
            double width = 440, double height = 320)
        {
            DockGroupNode? source = FindGroup(tab);
            if (source == null) return null;

            DockFloatingGroup? sourceFloating = FloatingGroups.FirstOrDefault(f => ReferenceEquals(f.Group, source));
            if (sourceFloating != null && source.Tabs.Count == 1) return sourceFloating;

            source.RemoveTab(tab);
            DockStructureChange? removal = CleanupEmptySource(source);

            // Rebuild the source surface before the new floating window takes activation.
            // Otherwise the dock tree would be restructured while its window is inactive,
            // leaving the repaint until the user clicks back into it.
            if (removal != null) Notify(removal);

            DockGroupNode floatingGroup = new("center");
            floatingGroup.Tabs.Add(tab);
            floatingGroup.SelectedTab = tab;
            DockFloatingGroup floating = new(floatingGroup, x, y, width, height);
            FloatingGroups.Add(floating);
            FloatingGroupAdded?.Invoke(floating);
            return floating;
        }

        /// <summary>Moves a tab from a floating window back into the main dock tree.</summary>
        public bool DockTabToMain(EditorWindowViewModel tab)
        {
            DockGroupNode? target = PreferredDockGroup();
            if (target == null) return false;
            return MoveTab(tab, target);
        }

        /// <summary>Docks every tab of a floating group back into the main tree, then removes it.</summary>
        public void DockFloatingGroupToMain(DockFloatingGroup floating)
        {
            DockGroupNode? target = PreferredDockGroup();
            if (target == null) return;
            foreach (EditorWindowViewModel tab in floating.Group.Tabs.ToList())
                MoveTab(tab, target);
        }

        /// <summary>Removes a floating group entry and asks the UI to close its window.</summary>
        public bool RemoveFloatingGroup(DockFloatingGroup floating)
        {
            if (!FloatingGroups.Contains(floating)) return false;
            RemoveFloatingGroupInternal(floating, notify: true);
            Notify(new DockStructureChange(DockStructureChangeKind.GroupRemoved, group: floating.Group));
            return true;
        }

        /// <summary>
        /// Updates the model sizes of a split from the live grid definitions after the
        /// user drags a splitter, so the change is persisted.
        /// </summary>
        public void CommitSplitSizes(DockSplitNode split, IReadOnlyList<DockSize> sizes)
        {
            for (int i = 0; i < split.Children.Count && i < sizes.Count; i++)
                split.Children[i].Size = sizes[i];
        }

        private DockGroupNode InsertSplit(DockGroupNode target, DockZone zone,
            out DockSplitNode split, out DockNode newChild)
        {
            Orientation orientation = zone is DockZone.Left or DockZone.Right
                ? Orientation.Horizontal
                : Orientation.Vertical;
            bool before = zone is DockZone.Left or DockZone.Top;

            split = new DockSplitNode(orientation);
            DockSize newGroupSize;
            DockSize targetSize;
            bool wasRoot = target.Parent == null;

            if (wasRoot)
            {
                // Splitting the root: two equal shares of the window.
                split.Size = DockSize.Star(1);
                targetSize = DockSize.Star(1);
                newGroupSize = DockSize.Star(1);
            }
            else
            {
                DockSplitNode parent = target.Parent!;
                int index = parent.Children.IndexOf(target);
                DockSize oldSize = target.Size;

                targetSize = oldSize.Half();
                newGroupSize = oldSize.Half();
                split.Size = oldSize;
                split.Parent = parent;
                parent.Children[index] = split;
                target.Parent = null;
            }

            DockGroupNode newGroup = new(target.Zone);
            target.Size = targetSize;
            newGroup.Size = newGroupSize;

            if (before)
            {
                split.AddChild(newGroup);
                split.AddChild(target);
            }
            else
            {
                split.AddChild(target);
                split.AddChild(newGroup);
            }

            if (wasRoot)
            {
                root = split;
                split.Parent = null;
                split.Size = DockSize.Star(1);
            }

            newChild = newGroup;
            return newGroup;
        }

        /// <summary>
        /// Removes a group that just became empty, whether it lived in the dock tree or
        /// in a floating window. The single root group of the main window is kept.
        /// </summary>
        private DockStructureChange? CleanupEmptySource(DockGroupNode source)
        {
            if (source.Tabs.Count > 0) return null;

            DockFloatingGroup? floating = FloatingGroups.FirstOrDefault(f => ReferenceEquals(f.Group, source));
            if (floating != null)
            {
                RemoveFloatingGroupInternal(floating, notify: true);
                return new DockStructureChange(DockStructureChangeKind.GroupRemoved, group: source);
            }

            if (source.Parent != null) return RemoveGroupInternal(source);
            return null;
        }

        private DockStructureChange RemoveGroupInternal(DockGroupNode group)
        {
            DockSplitNode? parent = group.Parent;
            if (parent == null) return new DockStructureChange(DockStructureChangeKind.GroupRemoved, group: group);

            parent.RemoveChild(group);

            if (parent.Children.Count == 1)
            {
                DockNode survivor = parent.Children[0];
                parent.RemoveChild(survivor);
                survivor.Size = parent.Parent == null ? DockSize.Star(1) : parent.Size;

                if (parent.Parent == null)
                {
                    root = survivor;
                    survivor.Parent = null;
                }
                else
                {
                    DockSplitNode grandparent = parent.Parent;
                    int index = grandparent.Children.IndexOf(parent);
                    grandparent.Children[index] = survivor;
                    survivor.Parent = grandparent;
                    parent.Parent = null;
                }
            }
            else if (parent.Children.Count == 0 && parent.Parent == null)
            {
                root = new DockGroupNode("center");
            }

            return new DockStructureChange(DockStructureChangeKind.GroupRemoved, group: group);
        }

        private void RemoveFloatingGroupInternal(DockFloatingGroup floating, bool notify)
        {
            FloatingGroups.Remove(floating);
            if (notify) FloatingGroupRemoved?.Invoke(floating);
        }

        private void Notify(DockStructureChange change)
        {
            LastChange = change;
            StructureChanged?.Invoke(change);
        }

        /// <summary>Validates the tree invariants. Used by the headless windowing tests.</summary>
        /// <returns>A list of human-readable invariant violations; empty when valid.</returns>
        public List<string> Validate()
        {
            List<string> problems = [];
            if (root == null) problems.Add("root is null");
            else
            {
                if (root.Parent != null) problems.Add("root has a parent");
                foreach (DockNode node in root.Descendants())
                {
                    if (node is DockSplitNode split)
                    {
                        if (split.Children.Count < 2)
                            problems.Add($"split {split.GetHashCode()} has {split.Children.Count} children");
                        foreach (DockNode child in split.Children)
                        {
                            if (!ReferenceEquals(child.Parent, split))
                                problems.Add($"child {child.GetHashCode()} has the wrong parent");
                        }
                    }
                    else if (node is DockGroupNode group)
                    {
                        if (group.Parent != null && !group.Parent.Children.Contains(group))
                            problems.Add($"group {group.GetHashCode()} is not in its parent's children");
                        if (group.SelectedTab != null && !group.Tabs.Contains(group.SelectedTab))
                            problems.Add($"group {group.GetHashCode()} selects a tab it does not contain");
                        if (group.SelectedTab == null && group.Tabs.Count > 0)
                            problems.Add($"group {group.GetHashCode()} has tabs but no selection");
                    }
                }
            }

            HashSet<EditorWindowViewModel> seen = [];
            foreach (DockGroupNode group in AllGroups)
            {
                foreach (EditorWindowViewModel tab in group.Tabs)
                {
                    if (!seen.Add(tab))
                        problems.Add($"tab '{tab.Title}' appears in more than one group");
                }
            }

            foreach (DockFloatingGroup floating in FloatingGroups)
            {
                if (floating.Group.Parent != null)
                    problems.Add("a floating group still has a tree parent");
                if (root != null && root.Contains(floating.Group))
                    problems.Add("a floating group is also part of the dock tree");
            }
            return problems;
        }
    }
}
