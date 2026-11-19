//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using Avalonia;
using System;
using System.Collections.Generic;

namespace DivisionEngine.Editor.Windowing
{
    /// <summary>
    /// Pure geometry helpers for docking. Separated from the controls so the headless
    /// CLI windowing test can exercise the exact zone and insertion rules the UI uses.
    /// </summary>
    public static class DockGeometry
    {
        /// <summary>
        /// Fraction of a group's content area that counts as the centre (merge) target.
        /// The outer bands are the four split targets.
        /// </summary>
        public const double CenterFraction = 0.28;

        /// <summary>
        /// Resolves a pointer position to a drop zone. Anywhere on the tab strip counts
        /// as <see cref="DockZone.Center"/> so dragging over tab headers reorders tabs
        /// instead of constantly offering a "split above" target.
        /// </summary>
        /// <param name="groupBounds">The tab group's bounds in the dock host's coordinates.</param>
        /// <param name="point">Pointer position in the same coordinates.</param>
        /// <param name="tabStripHeight">Height of the tab header strip.</param>
        public static DockZone GetZone(Rect groupBounds, Point point, double tabStripHeight)
        {
            if (groupBounds.Width <= 0 || groupBounds.Height <= 0) return DockZone.Center;

            double localY = point.Y - groupBounds.Y;
            if (localY <= Math.Max(20, tabStripHeight)) return DockZone.Center;

            double contentTop = groupBounds.Y + Math.Max(20, tabStripHeight);
            double contentHeight = Math.Max(1, groupBounds.Bottom - contentTop);
            double nx = Math.Clamp((point.X - groupBounds.X) / Math.Max(1, groupBounds.Width), 0, 1);
            double ny = Math.Clamp((point.Y - contentTop) / contentHeight, 0, 1);

            double toLeft = nx;
            double toRight = 1 - nx;
            double toTop = ny;
            double toBottom = 1 - ny;
            double nearest = Math.Min(Math.Min(toLeft, toRight), Math.Min(toTop, toBottom));

            if (nearest > CenterFraction) return DockZone.Center;
            if (nearest == toLeft) return DockZone.Left;
            if (nearest == toRight) return DockZone.Right;
            if (nearest == toTop) return DockZone.Top;
            return DockZone.Bottom;
        }

        /// <summary>
        /// Chooses where a tab would be inserted among a row of tab headers, based on
        /// their horizontal midpoints.
        /// </summary>
        /// <param name="headerBounds">Realized tab header bounds in visual order.</param>
        /// <param name="x">Pointer X in the same coordinate space.</param>
        public static int GetInsertionIndex(IReadOnlyList<Rect> headerBounds, double x)
        {
            for (int i = 0; i < headerBounds.Count; i++)
            {
                if (x < headerBounds[i].X + headerBounds[i].Width / 2) return i;
            }
            return headerBounds.Count;
        }

        /// <summary>
        /// Converts a tab insertion index measured before the dragged tab is removed into
        /// the index to use after it is removed. When the tab lived earlier in the same
        /// group, everything after it shifts left by one.
        /// </summary>
        /// <param name="index">Insertion index in the current tab list (0..count).</param>
        /// <param name="draggedIndex">Current index of the dragged tab, or -1 for another group.</param>
        public static int AdjustIndexForSameGroupRemoval(int index, int draggedIndex)
        {
            if (draggedIndex < 0) return index;
            return index > draggedIndex ? index - 1 : index;
        }
    }
}
