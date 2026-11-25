//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE file in the project root
// for full license terms.
//
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using DivisionEngine.Editor.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DivisionEngine.Editor.Windowing
{
    /// <summary>
    /// One top-level window that can participate in docking: the main editor window or a
    /// floating tab group window. Surfaces know how to turn a screen point into a drop
    /// target and expose the commands used by tab context menus.
    /// </summary>
    public sealed class DockSurface
    {
        private static readonly List<DockSurface> active = [];

        /// <summary>Every live dock surface, in registration order (main window first).</summary>
        public static IReadOnlyList<DockSurface> Active => active;

        /// <summary>Creates a surface for a window and its dock host.</summary>
        public DockSurface(Window window, DockHost host, DockLayout layout,
            DockFloatingGroup? floating, FloatingWindowManager? manager)
        {
            Window = window;
            Host = host;
            Layout = layout;
            Floating = floating;
            FloatingManager = manager;
        }

        /// <summary>The top-level window that owns this surface.</summary>
        public Window Window { get; }

        /// <summary>The dock host visual inside the window.</summary>
        public DockHost Host { get; }

        /// <summary>The shared dock layout model.</summary>
        public DockLayout Layout { get; }

        /// <summary>The floating group when this surface is a floating window.</summary>
        public DockFloatingGroup? Floating { get; }

        /// <summary>Manager that owns all floating windows, when available.</summary>
        public FloatingWindowManager? FloatingManager { get; }

        /// <summary>True when this surface belongs to a floating window.</summary>
        public bool IsFloating => Floating != null;

        /// <summary>
        /// Adds this surface to the global hit-test list. Activation order is maintained
        /// so an overlapping drop target resolves to the window the user last activated.
        /// </summary>
        public void Register()
        {
            if (!active.Contains(this))
            {
                active.Add(this);
                Window.Activated += OnWindowActivated;
            }
        }

        /// <summary>Removes this surface from the global hit-test list.</summary>
        public void Unregister()
        {
            if (active.Remove(this)) Window.Activated -= OnWindowActivated;
        }

        private void OnWindowActivated(object? sender, EventArgs e)
        {
            // Move the active surface to the end; DockDragSession scans the list backwards.
            if (!active.Remove(this)) return;
            active.Add(this);
        }

        /// <summary>Activates the owning window.</summary>
        public void Activate()
        {
            if (!Window.IsVisible) return;
            Window.Activate();
        }

        /// <summary>True when a screen point lies inside this window's client area.</summary>
        public bool ContainsScreenPoint(PixelPoint screenPoint)
        {
            if (!Window.IsVisible) return false;
            try
            {
                Point client = Window.PointToClient(screenPoint);
                return client.X >= 0 && client.Y >= 0 &&
                       client.X <= Window.ClientSize.Width && client.Y <= Window.ClientSize.Height;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Finds the tab group under a screen point, if any.</summary>
        public DockGroupNode? GroupAtScreenPoint(PixelPoint screenPoint) => Host.GroupAtScreenPoint(screenPoint);

        /// <summary>Opens a catalog window in a group.</summary>
        public void AddWindowToGroup(EditorWindowViewModel viewModel, DockGroupNode group)
        {
            Layout.AddTab(group, viewModel);
            group.SelectedTab = viewModel;
            Activate();
        }

        /// <summary>Closes a tab.</summary>
        public void CloseTab(EditorWindowViewModel tab) => Layout.CloseTab(tab);

        /// <summary>Closes every tab of a group.</summary>
        public void CloseGroup(DockGroupNode group) => Layout.CloseGroup(group);

        /// <summary>Duplicates a tab directly after the original, in the same group.</summary>
        public void DuplicateTab(EditorWindowViewModel tab)
        {
            DockGroupNode? group = Layout.FindGroup(tab);
            if (group == null) return;

            EditorWindowViewModel? duplicate = EditorWindowCatalog.Duplicate(tab);
            if (duplicate == null)
            {
                Debug.Error($"Failed to duplicate tab: could not create instance of {tab.GetType().Name}");
                return;
            }

            int index = group.Tabs.IndexOf(tab);
            Layout.AddTab(group, duplicate, index + 1);
            group.SelectedTab = duplicate;
        }

        /// <summary>
        /// Moves a tab into a new floating window. When <paramref name="screenPoint"/> is
        /// given (a drag released over the desktop) the window appears there, otherwise it
        /// is offset from the source window.
        /// </summary>
        public DockFloatingGroup? FloatTab(EditorWindowViewModel tab, PixelPoint? screenPoint = null)
        {
            PixelPoint origin;
            if (screenPoint.HasValue)
            {
                origin = new PixelPoint(screenPoint.Value.X - 70, screenPoint.Value.Y - 16);
            }
            else
            {
                origin = new PixelPoint(Window.Position.X + 64, Window.Position.Y + 64);
            }

            ClampToWorkingArea(ref origin, 440, 320);
            return Layout.FloatTab(tab, origin.X, origin.Y, 440, 320);
        }

        /// <summary>Moves a tab from a floating window back into the main dock tree.</summary>
        public void DockTab(EditorWindowViewModel tab)
        {
            if (!Layout.DockTabToMain(tab)) return;
            DockSurface? main = active.FirstOrDefault(surface => !surface.IsFloating);
            main?.Activate();
        }

        /// <summary>Splits the tab's own group and places the tab in the new group.</summary>
        public void MoveTabToNewGroup(EditorWindowViewModel tab, DockZone zone)
        {
            DockGroupNode? group = Layout.FindGroup(tab);
            if (group == null) return;

            if (Layout.IsDocked(group))
            {
                Layout.SplitGroupWithTab(group, zone, tab);
                Activate();
            }
            else
            {
                // Floating windows host one group each: moving a tab "out" of one simply
                // creates another floating window for it.
                FloatTab(tab);
            }
        }

        private void ClampToWorkingArea(ref PixelPoint point, double width, double height)
        {
            try
            {
                Screen? screen = Window.Screens.ScreenFromPoint(point) ?? Window.Screens.Primary;
                if (screen == null) return;
                PixelRect area = screen.WorkingArea;
                // Include room for the window frame/title bar, not just the content size.
                int w = (int)((width + 24) * screen.Scaling);
                int h = (int)((height + 48) * screen.Scaling);
                int x = Math.Clamp(point.X, area.X, Math.Max(area.X, area.Right - w));
                int y = Math.Clamp(point.Y, area.Y, Math.Max(area.Y, area.Bottom - h));
                point = new PixelPoint(x, y);
            }
            catch
            {
                // Screen information is unavailable in some headless configurations.
            }
        }
    }
}
