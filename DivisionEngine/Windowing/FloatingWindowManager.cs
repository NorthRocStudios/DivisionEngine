//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE.txt file in the project root
// for full license terms.
//
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DivisionEngine.Editor.Windowing
{
    /// <summary>
    /// Keeps floating tab group windows in sync with <see cref="DockLayout.FloatingGroups"/>.
    /// The manager owns window creation, positioning, bounds capture and shutdown.
    /// </summary>
    public sealed class FloatingWindowManager
    {
        private readonly Window owner;
        private readonly DockLayout layout;
        private readonly Dictionary<DockFloatingGroup, FloatingDockWindow> windows = [];
        private bool shuttingDown;

        /// <summary>Creates a manager for the main editor window and its layout.</summary>
        public FloatingWindowManager(Window owner, DockLayout layout)
        {
            this.owner = owner;
            this.layout = layout;
        }

        /// <summary>True while the editor is shutting down; floating windows may close freely.</summary>
        public bool IsShuttingDown => shuttingDown;

        /// <summary>Starts listening to the layout and opens any already-floating groups.</summary>
        public void Attach()
        {
            layout.StructureChanged += OnStructureChanged;
            layout.FloatingGroupAdded += OnFloatingGroupAdded;
            layout.FloatingGroupRemoved += OnFloatingGroupRemoved;
            SyncAll();
        }

        /// <summary>Stops listening and closes every floating window.</summary>
        public void Detach()
        {
            layout.StructureChanged -= OnStructureChanged;
            layout.FloatingGroupAdded -= OnFloatingGroupAdded;
            layout.FloatingGroupRemoved -= OnFloatingGroupRemoved;
            shuttingDown = true;
            foreach (FloatingDockWindow window in windows.Values.ToList())
                window.ForceClose();
            windows.Clear();
        }

        /// <summary>Closes all current floating windows and opens the ones in the layout.</summary>
        public void SyncAll()
        {
            foreach (FloatingDockWindow window in windows.Values.ToList())
                window.ForceClose();
            windows.Clear();

            foreach (DockFloatingGroup floating in layout.FloatingGroups.ToList())
                CreateWindow(floating);
        }

        private void OnStructureChanged(DockStructureChange change)
        {
            if (change.Kind == DockStructureChangeKind.Reset) SyncAll();
        }

        private void OnFloatingGroupAdded(DockFloatingGroup floating) => CreateWindow(floating);

        private void OnFloatingGroupRemoved(DockFloatingGroup floating)
        {
            if (!windows.Remove(floating, out FloatingDockWindow? window)) return;
            // Stop treating the fading window as a drop target immediately.
            window.Surface.Unregister();
            window.CloseAnimated();
        }

        private void CreateWindow(DockFloatingGroup floating)
        {
            if (windows.ContainsKey(floating)) return;

            try
            {
                if (floating.X == 0 && floating.Y == 0)
                    PlaceDefault(floating);
                EnsureOnScreen(floating);

                FloatingDockWindow window = new(owner, layout, floating, this);
                windows[floating] = window;
                window.Show();
                window.Surface.Register();
                window.Activate();
            }
            catch (Exception ex)
            {
                Debug.Error("Failed to open floating tab group window", ex);
                if (windows.Remove(floating, out FloatingDockWindow? failedWindow))
                    failedWindow.ForceClose();
            }
        }

        private void PlaceDefault(DockFloatingGroup floating)
        {
            PixelPoint origin = new(owner.Position.X + 60, owner.Position.Y + 60);
            try
            {
                Screen? screen = owner.Screens.ScreenFromPoint(origin) ?? owner.Screens.Primary;
                if (screen == null) return;
                PixelRect area = screen.WorkingArea;
                int width = (int)((floating.Width + 24) * screen.Scaling);
                int height = (int)((floating.Height + 48) * screen.Scaling);
                int x = Math.Clamp(origin.X, area.X, Math.Max(area.X, area.Right - width));
                int y = Math.Clamp(origin.Y, area.Y, Math.Max(area.Y, area.Bottom - height));
                floating.X = x;
                floating.Y = y;
            }
            catch
            {
                // Screen information may be unavailable; keep the owner-relative position.
                floating.X = origin.X;
                floating.Y = origin.Y;
            }
        }

        /// <summary>
        /// Validates a restored floating group's saved bounds against the current screens.
        /// A layout saved on a disconnected monitor must not reopen off-screen.
        /// </summary>
        private void EnsureOnScreen(DockFloatingGroup floating)
        {
            try
            {
                PixelPoint target = new(floating.X, floating.Y);
                Screen? exact = owner.Screens.ScreenFromPoint(target);
                Screen? screen = exact ?? owner.Screens.Primary;
                if (screen == null) return;

                PixelRect area = screen.WorkingArea;
                int width = (int)((floating.Width + 24) * screen.Scaling);
                int height = (int)((floating.Height + 48) * screen.Scaling);

                if (exact == null)
                {
                    floating.X = area.X + 40;
                    floating.Y = area.Y + 40;
                }

                floating.X = Math.Clamp(floating.X, area.X, Math.Max(area.X, area.Right - width));
                floating.Y = Math.Clamp(floating.Y, area.Y, Math.Max(area.Y, area.Bottom - height));
            }
            catch
            {
                // Screen information is unavailable; keep the saved bounds.
            }
        }

        /// <summary>Closes a floating group's window and removes it from the layout.</summary>
        public void RequestClose(DockFloatingGroup floating)
        {
            if (!layout.RemoveFloatingGroup(floating) &&
                windows.Remove(floating, out FloatingDockWindow? window))
            {
                window.ForceClose();
            }
        }

        /// <summary>Called by a floating window when it has closed.</summary>
        public void NotifyWindowClosed(FloatingDockWindow window)
        {
            foreach (KeyValuePair<DockFloatingGroup, FloatingDockWindow> pair in windows.ToList())
            {
                if (ReferenceEquals(pair.Value, window)) windows.Remove(pair.Key);
            }
        }

        /// <summary>Finds the window that hosts a tab group, if it is floating.</summary>
        public FloatingDockWindow? WindowForGroup(DockGroupNode group)
        {
            foreach (KeyValuePair<DockFloatingGroup, FloatingDockWindow> pair in windows)
            {
                if (ReferenceEquals(pair.Key.Group, group)) return pair.Value;
            }
            return null;
        }

        /// <summary>Finds the window for a tab, if the tab is floating.</summary>
        public FloatingDockWindow? WindowForTab(ViewModels.EditorWindowViewModel tab)
        {
            DockGroupNode? group = layout.FindGroup(tab);
            return group == null ? null : WindowForGroup(group);
        }

        /// <summary>Copies live window positions and sizes back into the layout for saving.</summary>
        public void CaptureBoundsToLayout()
        {
            foreach (KeyValuePair<DockFloatingGroup, FloatingDockWindow> pair in windows)
            {
                // Minimized windows are parked at (-32000,-32000) and maximized windows
                // report the maximized client size; neither should replace the normal
                // bounds stored in the layout.
                if (!pair.Value.IsVisible || pair.Value.WindowState != Avalonia.Controls.WindowState.Normal)
                    continue;

                pair.Key.X = pair.Value.Position.X;
                pair.Key.Y = pair.Value.Position.Y;
                pair.Key.Width = pair.Value.Width;
                pair.Key.Height = pair.Value.Height;
            }
        }

        /// <summary>Captures bounds and closes every floating window for application shutdown.</summary>
        public void PrepareForShutdown()
        {
            if (shuttingDown) return;
            CaptureBoundsToLayout();
            shuttingDown = true;
            foreach (FloatingDockWindow window in windows.Values.ToList())
                window.ForceClose();
            windows.Clear();
        }
    }
}
