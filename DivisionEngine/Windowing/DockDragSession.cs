//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE.txt file in the project root
// for full license terms.
//
using Avalonia;
using DivisionEngine.Editor.ViewModels;
using System;
using System.Collections.Generic;

namespace DivisionEngine.Editor.Windowing
{
    /// <summary>
    /// Tracks one tab drag across every dock window. The dragged tab's source view owns
    /// the pointer capture and forwards screen coordinates here; the session decides
    /// which window/group/zone is under the pointer, updates the target window's drop
    /// compass, and performs the dock, split or float on release.
    /// </summary>
    public sealed class DockDragSession
    {
        private readonly DockSurface source;
        private readonly DockGroupNode sourceGroup;
        private readonly EditorWindowViewModel tab;
        private readonly DragGhostWindow? ghost;

        private DockSurface? targetSurface;
        private DockGroupNode? targetGroup;
        private DockZone targetZone = DockZone.Center;
        private int targetIndex = -1;
        private int displayIndex = -1;
        private bool finished;

        private DockDragSession(DockSurface source, DockGroupNode sourceGroup, EditorWindowViewModel tab, PixelPoint start)
        {
            this.source = source;
            this.sourceGroup = sourceGroup;
            this.tab = tab;

            try
            {
                ghost = new DragGhostWindow(tab);
                ghost.MoveTo(start);
                ghost.Show();
            }
            catch (Exception ex)
            {
                Debug.Warning($"Could not create the tab drag ghost: {ex.Message}");
                ghost = null;
            }
        }

        /// <summary>The active drag, if any.</summary>
        public static DockDragSession? Current { get; private set; }

        /// <summary>Starts a drag. Any previous drag is cancelled.</summary>
        public static void Start(DockSurface source, DockGroupNode sourceGroup,
            EditorWindowViewModel tab, PixelPoint start)
        {
            Current?.Cancel();
            Current = new DockDragSession(source, sourceGroup, tab, start);
            Current.Update(start);
        }

        /// <summary>Updates the hovered drop target and the drag ghost.</summary>
        public void Update(PixelPoint screenPoint)
        {
            if (finished) return;

            try
            {
                ghost?.MoveTo(screenPoint);
            }
            catch
            {
                // The ghost window can disappear if the app is losing its window; the drag
                // itself keeps working.
            }

            DockSurface? surface = FindSurfaceAt(screenPoint);
            DockGroupNode? group = surface?.GroupAtScreenPoint(screenPoint);

            if (surface == null || group == null)
            {
                targetSurface = surface;
                targetGroup = null;
                HideAllAdorners();
                return;
            }

            targetSurface = surface;
            targetGroup = group;

            Rect bounds = surface.Host.GetGroupBounds(group);
            Point local;
            try
            {
                local = surface.Host.PointToClient(screenPoint);
            }
            catch
            {
                HideAllAdorners();
                return;
            }

            double headerHeight = surface.Host.GetTabStripHeight(group);
            DockZone zone = DockGeometry.GetZone(bounds, local, headerHeight);

            // Floating windows host exactly one tab group, so an edge drop there simply
            // merges the tab into that group rather than rendering a split that the window
            // cannot show.
            if (surface.IsFloating && zone != DockZone.Center) zone = DockZone.Center;

            targetZone = zone;
            targetIndex = -1;
            displayIndex = -1;

            if (zone == DockZone.Center)
            {
                List<Rect> headers = surface.Host.GetTabHeaderBounds(group);
                int raw = DockGeometry.GetInsertionIndex(headers, local.X);
                displayIndex = raw;
                targetIndex = ReferenceEquals(group, sourceGroup)
                    ? DockGeometry.AdjustIndexForSameGroupRemoval(raw, sourceGroup.Tabs.IndexOf(tab))
                    : raw;
            }

            foreach (DockSurface other in DockSurface.Active)
            {
                if (!ReferenceEquals(other, surface)) other.Host.HideDropTarget();
            }

            surface.Host.ShowDropTarget(group, zone, zone == DockZone.Center ? Math.Max(displayIndex, 0) : 0);
        }

        /// <summary>Finishes the drag, applying the dock/split/float operation.</summary>
        public void Complete(PixelPoint screenPoint)
        {
            if (finished) return;

            // Refresh the target for the exact release position.
            Update(screenPoint);

            DockSurface? releaseSurface = targetSurface;
            DockGroupNode? releaseGroup = targetGroup;
            DockZone zone = targetZone;
            int index = targetIndex;
            DockSurface sourceSurface = source;
            EditorWindowViewModel dragged = tab;

            finished = true;
            Current = null;
            HideAllAdorners();
            CloseGhost();

            try
            {
                if (releaseSurface != null && releaseGroup != null)
                {
                    if (zone == DockZone.Center)
                    {
                        sourceSurface.Layout.MoveTab(dragged, releaseGroup, index);
                    }
                    else
                    {
                        sourceSurface.Layout.SplitGroupWithTab(releaseGroup, zone, dragged);
                    }
                    releaseSurface.Activate();
                }
                else if (releaseSurface == null)
                {
                    // Released over the desktop: detach the tab into its own window.
                    sourceSurface.FloatTab(dragged, screenPoint);
                }
                // Otherwise the pointer is over a dock window but outside every tab
                // group (toolbar, status bar, splitter): cancel the drag, snapping back.
            }
            catch (Exception ex)
            {
                Debug.Error("Failed to complete tab drag", ex);
            }
        }

        /// <summary>Cancels the drag without changing the layout.</summary>
        public void Cancel()
        {
            if (finished) return;
            finished = true;
            Current = null;
            HideAllAdorners();
            CloseGhost();
        }

        private void CloseGhost()
        {
            try
            {
                ghost?.Close();
            }
            catch
            {
                // Already closed.
            }
        }

        private static DockSurface? FindSurfaceAt(PixelPoint screenPoint)
        {
            IReadOnlyList<DockSurface> surfaces = DockSurface.Active;
            for (int i = surfaces.Count - 1; i >= 0; i--)
            {
                if (surfaces[i].ContainsScreenPoint(screenPoint)) return surfaces[i];
            }
            return null;
        }

        private static void HideAllAdorners()
        {
            foreach (DockSurface surface in DockSurface.Active)
                surface.Host.HideDropTarget();
        }
    }
}
