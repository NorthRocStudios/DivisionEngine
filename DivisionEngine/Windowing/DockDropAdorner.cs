//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using System;

namespace DivisionEngine.Editor.Windowing
{
    /// <summary>
    /// The five-zone docking compass shown over the tab group under the pointer while a
    /// tab is dragged, plus the insertion line used for tab reordering.
    /// </summary>
    public sealed class DockDropAdorner : Canvas
    {
        private static readonly IBrush ZoneFill = new SolidColorBrush(Color.FromArgb(26, 90, 160, 220));
        private static readonly IBrush ZoneBorder = new SolidColorBrush(Color.FromArgb(110, 130, 190, 240));
        private static readonly IBrush ActiveFill = new SolidColorBrush(Color.FromArgb(92, 70, 150, 220));
        private static readonly IBrush ActiveBorder = new SolidColorBrush(Color.FromArgb(230, 170, 215, 255));
        private static readonly IBrush OutlineBrush = new SolidColorBrush(Color.FromArgb(160, 100, 165, 225));

        private readonly Border outline;
        private readonly Border leftZone;
        private readonly Border rightZone;
        private readonly Border topZone;
        private readonly Border bottomZone;
        private readonly Border centerZone;
        private readonly Border insertionLine;
        private readonly Border[] allZones;

        private DockZone? activeZone;
        private Rect currentBounds;

        /// <summary>Creates an empty, hidden adorner.</summary>
        public DockDropAdorner()
        {
            IsHitTestVisible = false;
            Opacity = 0;
            Transitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = OpacityProperty,
                    Duration = TimeSpan.FromMilliseconds(90),
                },
            };

            outline = CreateZone();
            leftZone = CreateZone();
            rightZone = CreateZone();
            topZone = CreateZone();
            bottomZone = CreateZone();
            centerZone = CreateZone();
            allZones = [leftZone, rightZone, topZone, bottomZone, centerZone];

            insertionLine = new Border
            {
                Background = Brushes.White,
                Width = 2,
                IsVisible = false,
            };

            Children.Add(outline);
            Children.Add(leftZone);
            Children.Add(rightZone);
            Children.Add(topZone);
            Children.Add(bottomZone);
            Children.Add(centerZone);
            Children.Add(insertionLine);
        }

        /// <summary>Shows the compass over a tab group.</summary>
        /// <param name="bounds">Group bounds in dock host coordinates.</param>
        /// <param name="zone">The zone currently under the pointer, or null for no highlight.</param>
        /// <param name="insertionX">Insertion line X for a centre drop, when reordering tabs.</param>
        /// <param name="tabStripHeight">Tab header strip height.</param>
        public void Show(Rect bounds, DockZone? zone, double? insertionX, double tabStripHeight)
        {
            if (bounds.Width < 4 || bounds.Height < 4)
            {
                Hide();
                return;
            }

            currentBounds = bounds;
            activeZone = zone;

            Place(outline, bounds.X, bounds.Y, bounds.Width, bounds.Height);
            outline.Background = null;
            outline.BorderBrush = OutlineBrush;
            outline.BorderThickness = new Thickness(1);

            double header = Math.Clamp(tabStripHeight, 20, Math.Max(20, bounds.Height * 0.45));
            double contentTop = bounds.Y + header;
            double contentHeight = Math.Max(1, bounds.Height - header);
            double bandWidth = Math.Max(10, bounds.Width * 0.26);
            double bandHeight = Math.Max(8, contentHeight * 0.26);

            Place(leftZone, bounds.X, contentTop, bandWidth, contentHeight);
            Place(rightZone, bounds.X + bounds.Width - bandWidth, contentTop, bandWidth, contentHeight);
            Place(topZone, bounds.X + bandWidth, contentTop, Math.Max(1, bounds.Width - bandWidth * 2), bandHeight);
            Place(bottomZone, bounds.X + bandWidth, bounds.Y + bounds.Height - bandHeight,
                Math.Max(1, bounds.Width - bandWidth * 2), bandHeight);
            Place(centerZone, bounds.X + bandWidth, contentTop + bandHeight,
                Math.Max(1, bounds.Width - bandWidth * 2), Math.Max(1, contentHeight - bandHeight * 2));

            UpdateHighlight();
            UpdateInsertionLine(insertionX, header);
            Opacity = 1;
        }

        /// <summary>Hides the compass.</summary>
        public void Hide()
        {
            activeZone = null;
            Opacity = 0;
            insertionLine.IsVisible = false;
        }

        /// <summary>The zone currently highlighted, or null when hidden.</summary>
        public DockZone? ActiveZone => activeZone;

        /// <summary>The bounds passed to the last <see cref="Show"/> call.</summary>
        public Rect CurrentBounds => currentBounds;

        private void UpdateHighlight()
        {
            foreach (Border zone in allZones)
            {
                zone.Background = ZoneFill;
                zone.BorderBrush = ZoneBorder;
            }

            Border? active = activeZone switch
            {
                DockZone.Left => leftZone,
                DockZone.Right => rightZone,
                DockZone.Top => topZone,
                DockZone.Bottom => bottomZone,
                DockZone.Center => centerZone,
                _ => null,
            };

            if (active == null) return;
            active.Background = ActiveFill;
            active.BorderBrush = ActiveBorder;
        }

        private void UpdateInsertionLine(double? insertionX, double headerHeight)
        {
            if (activeZone != DockZone.Center || insertionX == null)
            {
                insertionLine.IsVisible = false;
                return;
            }

            insertionLine.IsVisible = true;
            insertionLine.Height = Math.Max(20, headerHeight);
            Canvas.SetLeft(insertionLine, insertionX.Value - 1);
            Canvas.SetTop(insertionLine, currentBounds.Y);
        }

        private static Border CreateZone() => new()
        {
            Background = ZoneFill,
            BorderBrush = ZoneBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            IsHitTestVisible = false,
        };

        private static void Place(Border border, double x, double y, double width, double height)
        {
            border.Width = Math.Max(0, width);
            border.Height = Math.Max(0, height);
            Canvas.SetLeft(border, x);
            Canvas.SetTop(border, y);
        }
    }
}
