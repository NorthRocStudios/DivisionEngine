//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE file in the project root
// for full license terms.
//
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using System;
using System.Diagnostics;

namespace DivisionEngine.Editor.Windowing
{
    /// <summary>
    /// Small, dependency-free animations for docking actions. Everything here is
    /// best-effort: if a control is detached mid-animation the animation simply ends.
    /// </summary>
    public static class DockAnimations
    {
        /// <summary>Fades a control in from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public static void FadeIn(Visual target, double from = 0, double to = 1, int milliseconds = 140)
        {
            if (milliseconds <= 0)
            {
                target.Opacity = to;
                return;
            }

            target.Opacity = from;
            Animation animation = new()
            {
                Duration = TimeSpan.FromMilliseconds(milliseconds),
                Easing = new CubicEaseOut(),
                FillMode = FillMode.Forward,
                Children =
                {
                    new KeyFrame
                    {
                        Cue = new Cue(0),
                        Setters = { new Setter(Visual.OpacityProperty, from) },
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(1),
                        Setters = { new Setter(Visual.OpacityProperty, to) },
                    },
                },
            };
            _ = animation.RunAsync(target);
        }

        /// <summary>Fades a top-level window's opacity.</summary>
        public static async System.Threading.Tasks.Task FadeWindowAsync(Window window, double from, double to, int milliseconds)
        {
            window.Opacity = from;
            if (milliseconds <= 0)
            {
                window.Opacity = to;
                return;
            }

            Animation animation = new()
            {
                Duration = TimeSpan.FromMilliseconds(milliseconds),
                Easing = new CubicEaseOut(),
                FillMode = FillMode.Forward,
                Children =
                {
                    new KeyFrame
                    {
                        Cue = new Cue(0),
                        Setters = { new Setter(Visual.OpacityProperty, from) },
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(1),
                        Setters = { new Setter(Visual.OpacityProperty, to) },
                    },
                },
            };
            try
            {
                await animation.RunAsync(window);
            }
            catch
            {
                // The window may have been closed while animating; that is fine.
            }
            window.Opacity = to;
        }

        /// <summary>
        /// Animates a freshly inserted split child from almost nothing to its target
        /// size, which makes a new tab group visibly grow out of the drop point instead
        /// of appearing instantly.
        /// </summary>
        /// <param name="grid">The split grid whose definition changed.</param>
        /// <param name="index">Child index (grid column or row index).</param>
        /// <param name="horizontal">True for columns, false for rows.</param>
        /// <param name="target">Final size.</param>
        /// <param name="milliseconds">Animation duration.</param>
        public static void GrowCell(Grid grid, int index, bool horizontal, DockSize target, int milliseconds = 170)
        {
            if (index < 0) return;

            Stopwatch watch = Stopwatch.StartNew();
            DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(15) };
            double startValue = target.IsPixel ? Math.Min(target.Value, 16) : DockSize.MinimumStar;

            void Apply(double value)
            {
                GridLength length = new(value, target.IsPixel ? GridUnitType.Pixel : GridUnitType.Star);
                if (horizontal)
                {
                    if (index < grid.ColumnDefinitions.Count) grid.ColumnDefinitions[index].Width = length;
                }
                else
                {
                    if (index < grid.RowDefinitions.Count) grid.RowDefinitions[index].Height = length;
                }
            }

            Apply(startValue);
            timer.Tick += (_, _) =>
            {
                double t = Math.Clamp(watch.Elapsed.TotalMilliseconds / milliseconds, 0, 1);
                double eased = 1 - Math.Pow(1 - t, 3);
                double value = startValue + (target.Value - startValue) * eased;
                Apply(value);

                if (t >= 1 || TopLevel.GetTopLevel(grid) == null)
                {
                    timer.Stop();
                    Apply(target.Value);
                }
            };
            timer.Start();
        }
    }
}
