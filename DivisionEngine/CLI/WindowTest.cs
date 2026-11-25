//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE.txt file in the project root
// for full license terms.
//
using Avalonia;
using Avalonia.Layout;
using DivisionEngine.Editor.ViewModels;
using DivisionEngine.Editor.Windowing;
using DivisionEngine.Projects;
using DivisionEngine.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DivisionEngine.Editor
{
    /// <summary>
    /// Headless checks for the dockable window layout: tree operations, floating groups,
    /// drop geometry and project persistence. Invoked as
    /// <c>DivisionEngine.Editor.exe --cli windowtest</c>.
    /// </summary>
    internal static class WindowTest
    {
        private static int passed;
        private static int failed;

        /// <summary>Runs every windowing check. Exits 1 when any check fails.</summary>
        public static bool Run(string[] args)
        {
            Console.WriteLine("divisioncli windowtest: dockable window layout checks");

            Check("default layout matches the classic four panels", DefaultLayoutShape);
            Check("legacy empty panels collapse to one group", MinimalLegacyLayout);
            Check("legacy layout conversion keeps panel sizes and selection", LegacyConversionDetails);
            Check("legacy bottom panel keeps its height without a centre panel", LegacyBottomWithoutCentre);
            Check("split right group above", SplitAbove);
            Check("split left group to the left", SplitLeft);
            Check("split centre group below", SplitBelowCentre);
            Check("move tabs between groups", MoveTabsBetweenGroups);
            Check("reorder tabs within a group", ReorderWithinGroup);
            Check("closing tabs collapses empty groups", CloseCollapsesGroups);
            Check("closing the last tab of the last group keeps an empty group", LastGroupStaysEmpty);
            Check("float a tab and dock it back", FloatAndDockBack);
            Check("dock-back target is never a floating group", DockBackTargetIsDocked);
            Check("floating groups keep independent selections", FloatingSelections);
            Check("layout survives JSON project round-trip", PersistenceRoundTrip);
            Check("saved layout also refreshes the legacy panel fields", LegacyFieldsWritten);
            Check("unknown window types are skipped when loading", UnknownWindowTypeSkipped);
            Check("empty floating groups are ignored when loading", EmptyFloatingGroupSkipped);
            Check("geometry: drop zones", DropZoneGeometry);
            Check("geometry: tab insertion index", InsertionIndexGeometry);
            Check("geometry: same-group reorder compensation", ReorderCompensation);
            Check("window catalog duplicates concrete types", CatalogDuplication);
            Check("layout invariants hold after mixed operations", MixedOperationInvariants);

            Console.WriteLine($"windowtest: {passed} passed, {failed} failed");
            DivisionCLI.SetExitCode(failed == 0 ? 0 : 1);
            return true;
        }

        private static void Check(string name, Func<string?> check)
        {
            try
            {
                string? error = check();
                if (error == null)
                {
                    passed++;
                    Console.WriteLine($"  PASS  {name}");
                }
                else
                {
                    failed++;
                    Console.WriteLine($"  FAIL  {name}: {error}");
                }
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"  FAIL  {name}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static string? Expect(bool condition, string message) => condition ? null : message;

        private static string? Invariants(DockLayout layout)
        {
            List<string> problems = layout.Validate();
            return problems.Count == 0 ? null : string.Join("; ", problems);
        }

        private static DockLayout NewLayout()
        {
            DockLayout layout = new();
            layout.Load(DockLayoutSerialization.BuildLegacy(DefaultProjectData()));
            return layout;
        }

        private static EditorLayoutData DefaultProjectData() => new()
        {
            LeftPanelWidth = 200,
            RightPanelWidth = 300,
            BottomPanelHeight = 250,
            LeftTabs = "WorldWindow",
            CenterTabs = "EnvironmentWindow",
            RightTabs = "PropertiesWindow,SettingsWindow",
            BottomTabs = "AssetsWindow,ConsoleWindow",
            SelectedLeftTab = "WorldWindow",
            SelectedCenterTab = "EnvironmentWindow",
            SelectedRightTab = "PropertiesWindow",
            SelectedBottomTab = "AssetsWindow",
        };

        private static DockGroupNode Zone(DockLayout layout, string zone) =>
            layout.Groups.First(group => string.Equals(group.Zone, zone, StringComparison.OrdinalIgnoreCase));

        private static EditorWindowViewModel Tab(DockLayout layout, string typeName) =>
            layout.AllTabs.First(tab => DockLayoutSerialization.GetTypeName(tab) == typeName);

        private static string? DefaultLayoutShape()
        {
            DockLayout layout = NewLayout();
            string? invariant = Invariants(layout);
            if (invariant != null) return invariant;

            if (layout.Root is not DockSplitNode root || root.Orientation != Orientation.Horizontal)
                return "root should be a horizontal split";
            if (root.Children.Count != 2) return $"expected left+centre region and right panel, got {root.Children.Count} root children";

            if (root.Children[0] is not DockSplitNode region || region.Orientation != Orientation.Vertical)
                return "the left+centre region should be a vertical split";
            if (region.Children.Count != 2) return "the left+centre region should have a top row and a bottom row";

            if (region.Children[0] is not DockSplitNode topRow || topRow.Orientation != Orientation.Horizontal)
                return "the top row should be a horizontal split";
            if (topRow.Children.Count != 2) return "the top row should contain left and centre";

            DockGroupNode left = Zone(layout, "left");
            DockGroupNode center = Zone(layout, "center");
            DockGroupNode bottom = Zone(layout, "bottom");
            DockGroupNode right = Zone(layout, "right");

            if (!ReferenceEquals(topRow.Children[0], left)) return "left should be the first top-row column";
            if (!ReferenceEquals(topRow.Children[1], center)) return "centre should be the second top-row column";
            if (!ReferenceEquals(region.Children[1], bottom)) return "bottom should span under the left+centre top row";
            if (!ReferenceEquals(root.Children[1], right)) return "right should sit beside the whole left+centre region";

            if (left.Tabs.Count != 1 || DockLayoutSerialization.GetTypeName(left.Tabs[0]) != "WorldWindow")
                return "left group should contain WorldWindow";
            if (!left.Size.Equals(DockSize.Pixels(200))) return $"left size should be 200px, got {left.Size}";
            if (!center.Size.Equals(DockSize.Star(1))) return $"centre size should be 1*, got {center.Size}";
            if (!bottom.Size.Equals(DockSize.Pixels(250))) return $"bottom size should be 250px, got {bottom.Size}";
            if (bottom.Tabs.Count != 2) return "bottom group should contain two tabs";
            if (right.Tabs.Count != 2) return "right group should contain two tabs";
            if (!right.Size.Equals(DockSize.Pixels(300))) return $"right size should be 300px, got {right.Size}";
            if (layout.FloatingGroups.Count != 0) return "no floating groups expected";
            if (layout.AllTabs.Count() != 6) return $"expected 6 tabs, got {layout.AllTabs.Count()}";
            return null;
        }

        private static string? MinimalLegacyLayout()
        {
            EditorLayoutData data = new()
            {
                LeftTabs = "",
                RightTabs = "",
                CenterTabs = "EnvironmentWindow",
                BottomTabs = "",
                SelectedCenterTab = "EnvironmentWindow",
            };
            DockLayout layout = new();
            layout.Load(DockLayoutSerialization.BuildLegacy(data));

            if (layout.Root is not DockGroupNode group) return "minimal layout should be a single group root";
            if (group.Tabs.Count != 1) return "minimal layout should have one tab";
            return Invariants(layout);
        }

        private static string? LegacyConversionDetails()
        {
            EditorLayoutData data = new()
            {
                LeftPanelWidth = 180,
                RightPanelWidth = 260,
                BottomPanelHeight = 220,
                LeftTabs = "WorldWindow,AssetsWindow",
                CenterTabs = "ConsoleWindow",
                RightTabs = "PropertiesWindow",
                BottomTabs = "DeveloperWindow",
                SelectedLeftTab = "AssetsWindow",
                SelectedCenterTab = "ConsoleWindow",
                SelectedRightTab = "PropertiesWindow",
                SelectedBottomTab = "DeveloperWindow",
            };
            DockLayout layout = new();
            layout.Load(DockLayoutSerialization.BuildLegacy(data));

            DockGroupNode left = Zone(layout, "left");
            if (left.Tabs.Count != 2) return "left group should have two tabs";
            if (DockLayoutSerialization.GetTypeName(left.SelectedTab!) != "AssetsWindow")
                return "left group should restore the saved selected tab";
            if (!left.Size.Equals(DockSize.Pixels(180))) return $"left size should be 180px, got {left.Size}";
            if (!Zone(layout, "right").Size.Equals(DockSize.Pixels(260))) return "right size was not restored";
            if (!Zone(layout, "bottom").Size.Equals(DockSize.Pixels(220))) return "bottom size was not restored";
            return Invariants(layout);
        }

        private static string? LegacyBottomWithoutCentre()
        {
            EditorLayoutData data = new()
            {
                LeftTabs = "",
                RightTabs = "",
                CenterTabs = "",
                BottomTabs = "ConsoleWindow",
                SelectedBottomTab = "ConsoleWindow",
                BottomPanelHeight = 220,
            };
            DockLayout layout = new();
            layout.Load(DockLayoutSerialization.BuildLegacy(data));

            if (layout.Root is not DockSplitNode root || root.Orientation != Orientation.Vertical)
                return "bottom-without-centre should produce a vertical split";
            if (root.Children.Count != 2) return "expected an empty centre group above the bottom group";
            if (root.Children[0] is not DockGroupNode centre || centre.Tabs.Count != 0)
                return "the upper group should be empty";
            if (root.Children[1] is not DockGroupNode bottom || bottom.Tabs.Count != 1)
                return "the bottom group should contain Console";
            if (!bottom.Size.Equals(DockSize.Pixels(220)))
                return $"bottom height should be 220px, got {bottom.Size}";
            return Invariants(layout);
        }

        private static string? SplitAbove()
        {
            DockLayout layout = NewLayout();
            DockGroupNode right = Zone(layout, "right");
            EditorWindowViewModel settings = right.Tabs[1];
            DockGroupNode newGroup = layout.SplitGroupWithTab(right, DockZone.Top, settings);

            if (right.Tabs.Count != 1 || right.Tabs[0].Title != "Properties") return "original group should keep Properties";
            if (newGroup.Tabs.Count != 1 || newGroup.Tabs[0] != settings) return "new group should contain Settings";
            if (right.Parent is not DockSplitNode split || split.Orientation != Orientation.Vertical)
                return "right group should now be in a vertical split";
            if (!ReferenceEquals(split.Children[0], newGroup) || !ReferenceEquals(split.Children[1], right))
                return "new group should be above the original group";
            if (!newGroup.Size.Equals(DockSize.Pixels(150)) || !right.Size.Equals(DockSize.Pixels(150)))
                return $"split halves should each be 150px, got {newGroup.Size}/{right.Size}";
            if (!split.Size.Equals(DockSize.Pixels(300))) return $"split should keep the old 300px size, got {split.Size}";
            return Invariants(layout);
        }

        private static string? SplitLeft()
        {
            DockLayout layout = NewLayout();
            DockGroupNode left = Zone(layout, "left");
            EditorWindowViewModel world = left.Tabs[0];
            DockGroupNode newGroup = layout.SplitGroupWithTab(left, DockZone.Left, world);

            if (left.Tabs.Count != 0) return "original group should be left empty";
            if (newGroup.Parent is not DockSplitNode split || split.Orientation != Orientation.Horizontal)
                return "split should be horizontal";
            if (!ReferenceEquals(split.Children[0], newGroup) || !ReferenceEquals(split.Children[1], left))
                return "new group should be to the left";
            if (!newGroup.Size.Equals(DockSize.Pixels(100))) return $"new group should be 100px, got {newGroup.Size}";
            return Invariants(layout);
        }

        private static string? SplitBelowCentre()
        {
            DockLayout layout = NewLayout();
            DockGroupNode center = Zone(layout, "center");
            EditorWindowViewModel environment = center.Tabs[0];
            DockGroupNode newGroup = layout.SplitGroupWithTab(center, DockZone.Bottom, environment);

            if (center.Parent is not DockSplitNode split || split.Orientation != Orientation.Vertical)
                return "centre group should now be in a vertical split";
            if (!ReferenceEquals(split.Children[0], center) || !ReferenceEquals(split.Children[1], newGroup))
                return "new group should be below the centre";
            if (!center.Size.Equals(DockSize.Star(0.5))) return $"star halves expected, got {center.Size}";
            return Invariants(layout);
        }

        private static string? MoveTabsBetweenGroups()
        {
            DockLayout layout = NewLayout();
            DockGroupNode left = Zone(layout, "left");
            DockGroupNode right = Zone(layout, "right");

            EditorWindowViewModel properties = Tab(layout, "PropertiesWindow");
            if (!layout.MoveTab(properties, left, 1)) return "move failed";
            if (!left.Tabs.Contains(properties)) return "properties should be in the left group";
            if (right.Tabs.Count != 1) return "right group should keep Settings";
            if (!ReferenceEquals(left.SelectedTab, properties)) return "moved tab should be selected";

            EditorWindowViewModel settings = Tab(layout, "SettingsWindow");
            if (!layout.MoveTab(settings, left)) return "second move failed";
            if (layout.Groups.Any(group => ReferenceEquals(group, right))) return "empty right group should have been removed";
            if (!left.Tabs.Contains(settings) || !left.Tabs.Contains(properties)) return "left should contain both moved tabs";
            return Invariants(layout);
        }

        private static string? ReorderWithinGroup()
        {
            DockLayout layout = NewLayout();
            DockGroupNode bottom = Zone(layout, "bottom");
            EditorWindowViewModel assets = bottom.Tabs[0];
            EditorWindowViewModel console = bottom.Tabs[1];

            if (!layout.MoveTab(assets, bottom, 1)) return "reorder failed";
            if (!ReferenceEquals(bottom.Tabs[0], console) || !ReferenceEquals(bottom.Tabs[1], assets))
                return $"unexpected order {string.Join(",", bottom.Tabs.Select(t => t.Title))}";
            if (!ReferenceEquals(bottom.SelectedTab, assets)) return "reordered tab should be selected";
            return Invariants(layout);
        }

        private static string? CloseCollapsesGroups()
        {
            DockLayout layout = NewLayout();
            DockGroupNode right = Zone(layout, "right");
            EditorWindowViewModel settings = Tab(layout, "SettingsWindow");
            EditorWindowViewModel properties = Tab(layout, "PropertiesWindow");

            layout.CloseTab(settings);
            if (!layout.Groups.Contains(right) || right.Tabs.Count != 1) return "right group should remain with Properties";
            layout.CloseTab(properties);
            if (layout.Groups.Contains(right)) return "right group should be gone after its last tab closes";
            if (layout.Groups.Count() != 3) return $"expected 3 groups, got {layout.Groups.Count()}";
            return Invariants(layout);
        }

        private static string? LastGroupStaysEmpty()
        {
            DockLayout layout = NewLayout();
            foreach (EditorWindowViewModel tab in layout.AllTabs.ToList())
                layout.CloseTab(tab);

            if (layout.Root is not DockGroupNode group) return "closing everything should leave one group root";
            if (group.Tabs.Count != 0) return "root group should be empty";
            if (layout.FloatingGroups.Count != 0) return "no floating groups expected";
            if (group.SelectedTab != null) return "empty group should not select a tab";
            return Invariants(layout);
        }

        private static string? FloatAndDockBack()
        {
            DockLayout layout = NewLayout();
            EditorWindowViewModel environment = Tab(layout, "EnvironmentWindow");
            DockFloatingGroup? floating = layout.FloatTab(environment, 400, 300, 460, 340);

            if (floating == null) return "floating a tab returned null";
            if (layout.FloatingGroups.Count != 1) return "expected one floating group";
            if (!floating.Group.Tabs.Contains(environment)) return "floating group should contain the tab";
            if (layout.Root!.Contains(floating.Group)) return "floating group must not be part of the dock tree";
            if (layout.AllTabs.Count() != 6) return "floating must not lose tabs";
            if (layout.Validate().Count != 0) return string.Join("; ", layout.Validate());

            DockGroupNode left = Zone(layout, "left");
            if (!layout.MoveTab(environment, left, 0)) return "docking back failed";
            if (layout.FloatingGroups.Count != 0) return "floating group should be removed after docking back";
            if (!ReferenceEquals(left.Tabs[0], environment)) return "docked tab should be first in the left group";
            return Invariants(layout);
        }

        private static string? DockBackTargetIsDocked()
        {
            DockLayout layout = NewLayout();
            layout.FloatTab(Tab(layout, "EnvironmentWindow"), 100, 100);

            DockGroupNode? target = layout.PreferredDockGroup();
            if (target == null) return "no dock-back target";
            if (!layout.IsDocked(target)) return "dock-back target must be in the main dock tree";
            if (layout.FindGroupByZone("center") is { } zoneGroup && !layout.IsDocked(zoneGroup))
                return "new windows should still prefer a docked group";
            return Invariants(layout);
        }

        private static string? FloatingSelections()
        {
            DockLayout layout = NewLayout();
            EditorWindowViewModel assets = Tab(layout, "AssetsWindow");
            EditorWindowViewModel console = Tab(layout, "ConsoleWindow");
            DockFloatingGroup? first = layout.FloatTab(assets, 100, 100);
            DockFloatingGroup? second = layout.FloatTab(console, 500, 300);
            if (first == null || second == null) return "floating failed";
            if (ReferenceEquals(first.Group, second.Group)) return "floating two tabs should create two groups";
            if (!ReferenceEquals(first.Group.SelectedTab, assets) || !ReferenceEquals(second.Group.SelectedTab, console))
                return "each floating group should select its own tab";
            if (layout.AllTabs.Count() != 6) return "tabs were lost";
            return Invariants(layout);
        }

        private static string? PersistenceRoundTrip()
        {
            DockLayout original = NewLayout();
            original.FloatTab(Tab(original, "PropertiesWindow"), 120, 160, 420, 300);
            original.FloatTab(Tab(original, "ConsoleWindow"), 700, 260, 500, 360);
            DockGroupNode bottom = Zone(original, "bottom");
            original.SplitGroupWithTab(bottom, DockZone.Right, Tab(original, "AssetsWindow"));

            string before = DockLayoutSerialization.Describe(original.Root);
            string beforeFloating = string.Join(";", original.FloatingGroups.Select(f =>
                $"{DockLayoutSerialization.Describe(f.Group)}@{f.X},{f.Y},{f.Width:0}x{f.Height:0}"));

            EditorLayoutData data = new();
            DockLayoutSerialization.Capture(data, original);
            string json = Serialize.Default(data);
            EditorLayoutData? reloaded = Deserialize.Default<EditorLayoutData>(json);
            if (reloaded == null) return "JSON deserialization returned null";
            if (reloaded.DockTree == null) return "captured data has no dock tree";

            DockLayout restored = new();
            DockLayoutSerialization.Apply(reloaded, restored);

            string after = DockLayoutSerialization.Describe(restored.Root);
            string afterFloating = string.Join(";", restored.FloatingGroups.Select(f =>
                $"{DockLayoutSerialization.Describe(f.Group)}@{f.X},{f.Y},{f.Width:0}x{f.Height:0}"));

            if (before != after) return $"dock tree changed:\n  before {before}\n  after  {after}";
            if (beforeFloating != afterFloating)
                return $"floating groups changed:\n  before {beforeFloating}\n  after  {afterFloating}";
            if (restored.AllTabs.Count() != original.AllTabs.Count()) return "tab count changed";
            return Invariants(restored);
        }

        private static string? LegacyFieldsWritten()
        {
            DockLayout layout = NewLayout();
            layout.MoveTab(Tab(layout, "SettingsWindow"), Zone(layout, "left"), 1);
            EditorLayoutData data = new();
            DockLayoutSerialization.Capture(data, layout);

            if (data.DockTree == null) return "dock tree was not captured";
            if (!data.LeftTabs.Contains("SettingsWindow", StringComparison.Ordinal))
                return $"left legacy tabs should include Settings, got '{data.LeftTabs}'";
            if (data.RightTabs != "PropertiesWindow")
                return $"right legacy tabs should be Properties only, got '{data.RightTabs}'";
            if (data.SelectedLeftTab != "SettingsWindow")
                return $"selected left tab should be Settings, got '{data.SelectedLeftTab}'";

            // The old fields must still produce the same layout if a legacy editor opens
            // the project and ignores DockTree.
            DockLayout legacy = new();
            EditorLayoutData legacyOnly = new()
            {
                LeftPanelWidth = data.LeftPanelWidth,
                RightPanelWidth = data.RightPanelWidth,
                BottomPanelHeight = data.BottomPanelHeight,
                LeftTabs = data.LeftTabs,
                CenterTabs = data.CenterTabs,
                RightTabs = data.RightTabs,
                BottomTabs = data.BottomTabs,
                SelectedLeftTab = data.SelectedLeftTab,
                SelectedCenterTab = data.SelectedCenterTab,
                SelectedRightTab = data.SelectedRightTab,
                SelectedBottomTab = data.SelectedBottomTab,
            };
            legacy.Load(DockLayoutSerialization.BuildLegacy(legacyOnly));

            string expected = DockLayoutSerialization.Describe(layout.Root);
            string actual = DockLayoutSerialization.Describe(legacy.Root);
            return expected == actual ? null : $"legacy view differs: {expected} vs {actual}";
        }

        private static string? UnknownWindowTypeSkipped()
        {
            EditorLayoutData data = new()
            {
                DockTree = new EditorDockNodeData
                {
                    Type = "group",
                    Zone = "center",
                    Tabs = ["WorldWindow", "ThisWindowDoesNotExist"],
                    SelectedTab = "ThisWindowDoesNotExist",
                },
            };
            DockLayout layout = new();
            DockLayoutSerialization.Apply(data, layout);

            if (layout.Root is not DockGroupNode group) return "expected a group root";
            if (group.Tabs.Count != 1) return $"expected one valid tab, got {group.Tabs.Count}";
            if (DockLayoutSerialization.GetTypeName(group.Tabs[0]) != "WorldWindow") return "wrong tab survived";
            if (!ReferenceEquals(group.SelectedTab, group.Tabs[0])) return "selection should fall back to the first tab";
            return Invariants(layout);
        }

        private static string? EmptyFloatingGroupSkipped()
        {
            EditorLayoutData data = new()
            {
                DockTree = new EditorDockNodeData
                {
                    Type = "group",
                    Zone = "center",
                    Tabs = ["EnvironmentWindow"],
                    SelectedTab = "EnvironmentWindow",
                },
                FloatingGroups =
                [
                    new EditorFloatingGroupData
                    {
                        Node = new EditorDockNodeData { Type = "group", Tabs = [] },
                        X = 200,
                        Y = 100,
                    },
                ],
            };
            DockLayout layout = new();
            DockLayoutSerialization.Apply(data, layout);

            if (layout.FloatingGroups.Count != 0) return "an empty floating group should not be restored";
            if (layout.AllTabs.Count() != 1) return "unexpected tab count";
            return Invariants(layout);
        }

        private static string? DropZoneGeometry()
        {
            Rect bounds = new(100, 50, 400, 300);
            const double header = 28;

            if (DockGeometry.GetZone(bounds, new Point(300, 200), header) != DockZone.Center)
                return "content centre should be Centre";
            if (DockGeometry.GetZone(bounds, new Point(110, 200), header) != DockZone.Left)
                return "left edge should be Left";
            if (DockGeometry.GetZone(bounds, new Point(490, 200), header) != DockZone.Right)
                return "right edge should be Right";
            if (DockGeometry.GetZone(bounds, new Point(300, 330), header) != DockZone.Bottom)
                return "bottom edge should be Bottom";
            if (DockGeometry.GetZone(bounds, new Point(300, 85), header) != DockZone.Top)
                return "top content edge should be Top";
            if (DockGeometry.GetZone(bounds, new Point(105, 60), header) != DockZone.Center)
                return "the tab strip should always be a Centre target";
            if (DockGeometry.GetZone(default, new Point(0, 0), header) != DockZone.Center)
                return "empty bounds should fall back to Centre";
            return null;
        }

        private static string? InsertionIndexGeometry()
        {
            List<Rect> headers = [new(0, 0, 80, 28), new(80, 0, 80, 28), new(160, 0, 80, 28)];
            if (DockGeometry.GetInsertionIndex(headers, 10) != 0) return "x=10 should insert at 0";
            if (DockGeometry.GetInsertionIndex(headers, 39) != 0) return "x=39 should insert at 0";
            if (DockGeometry.GetInsertionIndex(headers, 41) != 1) return "x=41 should insert at 1";
            if (DockGeometry.GetInsertionIndex(headers, 199) != 2) return "x=199 should insert at 2";
            if (DockGeometry.GetInsertionIndex(headers, 200) != 3) return "the exact midpoint should append";
            if (DockGeometry.GetInsertionIndex(headers, 500) != 3) return "past the end should append";
            if (DockGeometry.GetInsertionIndex([], 50) != 0) return "empty headers should insert at 0";
            return null;
        }

        private static string? ReorderCompensation()
        {
            // [A, B, C]; dragging each item to each gap.
            if (DockGeometry.AdjustIndexForSameGroupRemoval(2, 0) != 1) return "dropping A after B should become index 1";
            if (DockGeometry.AdjustIndexForSameGroupRemoval(1, 2) != 1) return "dropping C before B should stay at index 1";
            if (DockGeometry.AdjustIndexForSameGroupRemoval(1, 0) != 0) return "dropping A before B should be a no-op";
            if (DockGeometry.AdjustIndexForSameGroupRemoval(0, 0) != 0) return "index 0 should not shift";
            if (DockGeometry.AdjustIndexForSameGroupRemoval(3, 1) != 2) return "append should shift by one";
            if (DockGeometry.AdjustIndexForSameGroupRemoval(2, -1) != 2) return "different group must not shift";
            return null;
        }

        private static string? CatalogDuplication()
        {
            EditorWindowViewModel original = new ConsoleWindowViewModel();
            EditorWindowViewModel? duplicate = EditorWindowCatalog.Duplicate(original);
            if (duplicate == null) return "duplicate returned null";
            if (duplicate.GetType() != original.GetType()) return "duplicate has the wrong type";
            if (ReferenceEquals(duplicate, original)) return "duplicate should be a new instance";
            if (duplicate.Title != original.Title) return "duplicate should keep the window title";
            return null;
        }

        private static string? MixedOperationInvariants()
        {
            DockLayout layout = NewLayout();

            // Split, move, float, close, re-split, dock back, reorder.
            DockGroupNode center = Zone(layout, "center");
            layout.SplitGroupWithTab(center, DockZone.Left, Tab(layout, "ConsoleWindow"));
            layout.MoveTab(Tab(layout, "WorldWindow"), center, 0);
            layout.FloatTab(Tab(layout, "SettingsWindow"), 200, 200);
            layout.CloseTab(Tab(layout, "ConsoleWindow"));
            DockGroupNode bottom = Zone(layout, "bottom");
            layout.SplitGroupWithTab(bottom, DockZone.Right, Tab(layout, "AssetsWindow"));
            layout.DockTabToMain(Tab(layout, "SettingsWindow"));
            layout.MoveTab(Tab(layout, "PropertiesWindow"), center);

            if (layout.AllTabs.Count() < 3) return "mixed operations lost too many tabs";
            return Invariants(layout);
        }
    }
}
