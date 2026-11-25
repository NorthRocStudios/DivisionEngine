//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE file in the project root
// for full license terms.
//
using Avalonia.Layout;
using DivisionEngine.Editor.ViewModels;
using DivisionEngine.Projects;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DivisionEngine.Editor.Windowing
{
    /// <summary>
    /// Converts between the runtime dock layout and its serialized project form, and
    /// upgrades the old fixed four-panel layout into a dock tree.
    /// </summary>
    public static class DockLayoutSerialization
    {
        /// <summary>Type name stored for a window view model (the class name without "ViewModel").</summary>
        public static string GetTypeName(EditorWindowViewModel viewModel) =>
            viewModel.GetType().Name.Replace("ViewModel", "", StringComparison.Ordinal);

        /// <summary>
        /// Creates a window view model from its stored type name. Unknown or abstract
        /// types are reported and skipped so an old project cannot stop the editor from
        /// opening.
        /// </summary>
        public static EditorWindowViewModel? CreateViewModel(string? typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName)) return null;

            string fullName = typeName.Contains('.', StringComparison.Ordinal)
                ? typeName
                : $"DivisionEngine.Editor.ViewModels.{typeName}ViewModel";

            Type? type = typeof(EditorWindowViewModel).Assembly.GetType(fullName, throwOnError: false);
            if (type == null || type.IsAbstract || !typeof(EditorWindowViewModel).IsAssignableFrom(type))
            {
                Debug.Warning($"Editor layout references unknown window type '{typeName}'; skipping it.");
                return null;
            }

            return Activator.CreateInstance(type) as EditorWindowViewModel;
        }

        /// <summary>
        /// Replaces the layout from project data, using the dock tree when present and
        /// converting the legacy four-panel layout otherwise.
        /// </summary>
        public static void Apply(EditorLayoutData data, DockLayout layout)
        {
            DockNode root = data.DockTree != null ? BuildNode(data.DockTree) ?? new DockGroupNode("center") : BuildLegacy(data);
            EnsureRootCanFill(root);
            List<DockFloatingGroup> floating = [];
            foreach (EditorFloatingGroupData floatingData in data.FloatingGroups ?? [])
            {
                DockNode? node = BuildNode(floatingData.Node);
                // A floating group with no valid windows should not produce an empty
                // floating window on load.
                if (node is not DockGroupNode group || group.Tabs.Count == 0) continue;
                floating.Add(new DockFloatingGroup(group, floatingData.X, floatingData.Y,
                    floatingData.Width, floatingData.Height));
            }
            layout.Load(root, floating);
        }

        /// <summary>Writes the live layout to project data.</summary>
        public static void Capture(EditorLayoutData data, DockLayout layout)
        {
            data.DockTree = layout.Root == null ? null : ToData(layout.Root);
            data.FloatingGroups = layout.FloatingGroups
                .Select(floating => new EditorFloatingGroupData
                {
                    Node = ToData(floating.Group),
                    X = floating.X,
                    Y = floating.Y,
                    Width = floating.Width,
                    Height = floating.Height,
                })
                .ToList();

            CaptureLegacyFields(data, layout);
        }

        /// <summary>Builds a dock tree from the legacy fixed-panel fields.</summary>
        public static DockNode BuildLegacy(EditorLayoutData data)
        {
            DockGroupNode? left = CreateGroup(data.LeftTabs, "left", data.SelectedLeftTab);
            DockGroupNode? center = CreateGroup(data.CenterTabs, "center", data.SelectedCenterTab);
            DockGroupNode? right = CreateGroup(data.RightTabs, "right", data.SelectedRightTab);
            DockGroupNode? bottom = CreateGroup(data.BottomTabs, "bottom", data.SelectedBottomTab);

            // The classic editor put left and centre side by side in the top row, with
            // the bottom row spanning underneath both of them. The right panel ran the
            // full height beside that whole left+centre region.
            DockNode? topRow;
            if (left != null)
            {
                left.Size = DockSize.Pixels(Math.Max(80, data.LeftPanelWidth));
                if (center != null)
                {
                    center.Size = DockSize.Star(1);
                    DockSplitNode sideBySide = new(Orientation.Horizontal);
                    sideBySide.AddChild(left);
                    sideBySide.AddChild(center);
                    topRow = sideBySide;
                }
                else
                {
                    topRow = left;
                }
            }
            else
            {
                topRow = center;
                if (center != null) center.Size = DockSize.Star(1);
            }

            DockNode core;
            if (bottom != null)
            {
                DockSplitNode rows = new(Orientation.Vertical);
                // An empty top row preserves the bottom row's position and height even
                // when both the left and centre panels are empty.
                topRow ??= new DockGroupNode("center");
                topRow.Size = DockSize.Star(1);
                bottom.Size = DockSize.Pixels(Math.Max(80, data.BottomPanelHeight));
                rows.AddChild(topRow);
                rows.AddChild(bottom);
                core = rows;
            }
            else
            {
                core = topRow ?? new DockGroupNode("center");
            }

            if (right != null)
            {
                right.Size = DockSize.Pixels(Math.Max(80, data.RightPanelWidth));
                core.Size = DockSize.Star(1);
                DockSplitNode root = new(Orientation.Horizontal);
                root.AddChild(core);
                root.AddChild(right);
                root.Size = DockSize.Star(1);
                return root;
            }

            core.Size = DockSize.Star(1);
            return core;
        }

        private static DockGroupNode? CreateGroup(string? typeNames, string zone, string? selectedTypeName)
        {
            if (string.IsNullOrWhiteSpace(typeNames)) return null;

            DockGroupNode group = new(zone);
            foreach (string name in SplitTypeNames(typeNames))
            {
                EditorWindowViewModel? viewModel = CreateViewModel(name);
                if (viewModel != null) group.Tabs.Add(viewModel);
            }
            if (group.Tabs.Count == 0) return null;

            EditorWindowViewModel? selected = group.Tabs.FirstOrDefault(
                tab => string.Equals(GetTypeName(tab), selectedTypeName, StringComparison.Ordinal));
            group.SelectedTab = selected ?? group.Tabs[0];
            return group;
        }

        private static DockNode? BuildNode(EditorDockNodeData data)
        {
            DockSize size = ParseSize(data.Size, data.SizeType);

            if (string.Equals(data.Type, "split", StringComparison.OrdinalIgnoreCase))
            {
                Orientation orientation = string.Equals(data.Orientation, "vertical", StringComparison.OrdinalIgnoreCase)
                    ? Orientation.Vertical
                    : Orientation.Horizontal;

                List<DockNode> children = [];
                foreach (EditorDockNodeData childData in data.Children ?? [])
                {
                    DockNode? child = BuildNode(childData);
                    if (child != null) children.Add(child);
                }

                if (children.Count == 0) return null;
                if (children.Count == 1)
                {
                    children[0].Size = size;
                    return children[0];
                }

                DockSplitNode split = new(orientation) { Size = size };
                foreach (DockNode child in children) split.AddChild(child);
                return split;
            }

            DockGroupNode group = new(string.IsNullOrWhiteSpace(data.Zone) ? "center" : data.Zone) { Size = size };
            foreach (string typeName in data.Tabs ?? [])
            {
                EditorWindowViewModel? viewModel = CreateViewModel(typeName);
                if (viewModel != null) group.Tabs.Add(viewModel);
            }
            if (group.Tabs.Count == 0) return group;

            EditorWindowViewModel? selected = group.Tabs.FirstOrDefault(
                tab => string.Equals(GetTypeName(tab), data.SelectedTab, StringComparison.Ordinal));
            group.SelectedTab = selected ?? group.Tabs[0];
            return group;
        }

        private static EditorDockNodeData ToData(DockNode node)
        {
            if (node is DockSplitNode split)
            {
                return new EditorDockNodeData
                {
                    Type = "split",
                    Orientation = split.Orientation == Orientation.Vertical ? "vertical" : "horizontal",
                    Size = split.Size.Value,
                    SizeType = split.Size.IsPixel ? "pixel" : "star",
                    Children = split.Children.Select(ToData).ToList(),
                };
            }

            DockGroupNode group = (DockGroupNode)node;
            return new EditorDockNodeData
            {
                Type = "group",
                Zone = group.Zone,
                Size = group.Size.Value,
                SizeType = group.Size.IsPixel ? "pixel" : "star",
                Tabs = group.Tabs.Select(GetTypeName).ToList(),
                SelectedTab = group.SelectedTab == null ? null : GetTypeName(group.SelectedTab),
            };
        }

        /// <summary>
        /// A root split whose every child is a fixed pixel size would leave unused space
        /// when the editor window grows. Nested all-pixel splits are valid (a split inside
        /// a fixed panel), so only the root is adjusted.
        /// </summary>
        private static void EnsureRootCanFill(DockNode root)
        {
            if (root is DockSplitNode split && split.Children.Count > 0 &&
                !split.Children.Any(child => !child.Size.IsPixel))
            {
                split.Children[^1].Size = DockSize.Star(1);
            }
        }

        private static DockSize ParseSize(double value, string? sizeType)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0) return DockSize.Star(1);
            return string.Equals(sizeType, "pixel", StringComparison.OrdinalIgnoreCase)
                ? DockSize.Pixels(value)
                : DockSize.Star(value);
        }

        private static IEnumerable<string> SplitTypeNames(string typeNames) =>
            typeNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        /// <summary>
        /// Fills the old layout fields from the dock tree, so a project opened by an older
        /// editor build still shows a sensible four-panel layout.
        /// </summary>
        private static void CaptureLegacyFields(EditorLayoutData data, DockLayout layout)
        {
            List<DockGroupNode> docked = layout.Groups.ToList();

            DockGroupNode? Zone(string zone) => docked.FirstOrDefault(
                group => string.Equals(group.Zone, zone, StringComparison.OrdinalIgnoreCase));

            DockGroupNode? left = Zone("left");
            DockGroupNode? center = Zone("center");
            DockGroupNode? right = Zone("right");
            DockGroupNode? bottom = Zone("bottom");

            data.LeftTabs = JoinTabs(left);
            data.CenterTabs = JoinTabs(center);
            data.RightTabs = JoinTabs(right);
            data.BottomTabs = JoinTabs(bottom);

            data.SelectedLeftTab = SelectedName(left);
            data.SelectedCenterTab = SelectedName(center);
            data.SelectedRightTab = SelectedName(right);
            data.SelectedBottomTab = SelectedName(bottom);

            if (left?.Size.IsPixel == true) data.LeftPanelWidth = left.Size.Value;
            if (right?.Size.IsPixel == true) data.RightPanelWidth = right.Size.Value;
            if (bottom?.Size.IsPixel == true) data.BottomPanelHeight = bottom.Size.Value;
        }

        private static string JoinTabs(DockGroupNode? group) =>
            group == null ? string.Empty : string.Join(",", group.Tabs.Select(GetTypeName));

        private static string SelectedName(DockGroupNode? group) =>
            group?.SelectedTab == null ? string.Empty : GetTypeName(group.SelectedTab);

        /// <summary>
        /// Describes a layout as a compact parenthesized string. Used by the CLI windowing
        /// test to compare trees structurally.
        /// </summary>
        public static string Describe(DockNode? node)
        {
            if (node == null) return "<null>";
            if (node is DockGroupNode group)
            {
                string size = group.Size.IsPixel
                    ? group.Size.Value.ToString("0", CultureInfo.InvariantCulture) + "px"
                    : group.Size.Value.ToString("0.###", CultureInfo.InvariantCulture) + "*";
                string tabs = string.Join("|", group.Tabs.Select(GetTypeName));
                return $"G[{group.Zone};{size};{tabs};{(group.SelectedTab == null ? "-" : GetTypeName(group.SelectedTab))}]";
            }

            DockSplitNode split = (DockSplitNode)node;
            string orientation = split.Orientation == Orientation.Horizontal ? "H" : "V";
            return $"{orientation}({string.Join(",", split.Children.Select(Describe))})";
        }
    }
}
