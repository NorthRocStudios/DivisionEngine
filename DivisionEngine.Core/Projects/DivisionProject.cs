//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE file in the project root
// for full license terms.
//
namespace DivisionEngine.Projects
{
    /// <summary>
    /// Represents a project in the Division Engine, used for serializing project data.
    /// </summary>
    public class DivisionProject
    {
        public string Name { get; set; }
        public string Version { get; set; }
        public DateTime LastSaved { get; set; }

        private EditorLayoutData? editorLayout;

        public EditorLayoutData EditorLayout
        {
            get => editorLayout ??= new EditorLayoutData();
            set => editorLayout = value;
        }

        public DivisionProject()
        {
            Name = string.Empty;
            Version = string.Empty;
            editorLayout = new EditorLayoutData();
        }

        public DivisionProject(string name)
        {
            Name = name;
            Version = "1.0.0";
            LastSaved = DateTime.Now;
            editorLayout = new EditorLayoutData();
        }
    }

    /// <summary>
    /// Stores editor layout configuration for a project.
    /// </summary>
    /// <remarks>
    /// <see cref="DockTree"/> and <see cref="FloatingGroups"/> are the current layout
    /// format. The older fixed-panel strings below are kept for projects written by
    /// earlier builds: when <see cref="DockTree"/> is null the editor converts the old
    /// four-panel layout into a dock tree, so existing projects keep opening with the
    /// layout they were saved with.
    /// </remarks>
    public class EditorLayoutData
    {
        // Grid splitter positions (legacy four-panel layout)
        public double LeftPanelWidth { get; set; } = 200;
        public double RightPanelWidth { get; set; } = 300;
        public double BottomPanelHeight { get; set; } = 250;
        public double CenterTopHeight { get; set; } = -1; // -1 means automatic/not set

        // Selected tabs per panel (legacy four-panel layout)
        public string SelectedLeftTab { get; set; } = "WorldWindow";
        public string SelectedRightTab { get; set; } = "PropertiesWindow";
        public string SelectedCenterTab { get; set; } = "EnvironmentWindow";
        public string SelectedBottomTab { get; set; } = "AssetsWindow";

        // Comma-separated tab lists (legacy four-panel layout)
        public string LeftTabs { get; set; } = "WorldWindow";
        public string RightTabs { get; set; } = "PropertiesWindow,SettingsWindow";
        public string CenterTabs { get; set; } = "EnvironmentWindow";
        public string BottomTabs { get; set; } = "AssetsWindow,ConsoleWindow,DeveloperWindow";

        /// <summary>
        /// The full dock tree: arbitrary tab groups arranged in horizontal and vertical
        /// splits. Null for projects saved before the dockable layout existed.
        /// </summary>
        public EditorDockNodeData? DockTree { get; set; }

        /// <summary>Tab groups that were floating in their own windows when the project was saved.</summary>
        public List<EditorFloatingGroupData> FloatingGroups { get; set; } = [];
    }

    /// <summary>
    /// Serialized form of one dock layout node (a tab group or a split).
    /// </summary>
    public class EditorDockNodeData
    {
        /// <summary>Node kind: "group" or "split".</summary>
        public string Type { get; set; } = "group";

        /// <summary>For splits: "horizontal" (columns) or "vertical" (rows).</summary>
        public string Orientation { get; set; } = "horizontal";

        /// <summary>Size within the parent split.</summary>
        public double Size { get; set; } = 1;

        /// <summary>Size unit: "star" (proportional) or "pixel".</summary>
        public string SizeType { get; set; } = "star";

        /// <summary>
        /// For groups: the logical panel kind (left/center/right/bottom). New groups
        /// inherit the kind of the group they were split from.
        /// </summary>
        public string Zone { get; set; } = "center";

        /// <summary>Child nodes when this is a split.</summary>
        public List<EditorDockNodeData> Children { get; set; } = [];

        /// <summary>Window type names when this is a group, in tab order.</summary>
        public List<string> Tabs { get; set; } = [];

        /// <summary>Selected window type name when this is a group.</summary>
        public string? SelectedTab { get; set; }
    }

    /// <summary>
    /// Serialized form of a floating tab group and its window bounds.
    /// </summary>
    public class EditorFloatingGroupData
    {
        /// <summary>The floating tab group.</summary>
        public EditorDockNodeData Node { get; set; } = new();

        /// <summary>Screen X position of the floating window.</summary>
        public int X { get; set; }

        /// <summary>Screen Y position of the floating window.</summary>
        public int Y { get; set; }

        /// <summary>Window width.</summary>
        public double Width { get; set; } = 440;

        /// <summary>Window height.</summary>
        public double Height { get; set; } = 320;
    }
}
