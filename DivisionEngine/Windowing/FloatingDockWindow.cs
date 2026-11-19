//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using DivisionEngine.Editor.ViewModels;
using System;
using System.ComponentModel;

namespace DivisionEngine.Editor.Windowing
{
    /// <summary>
    /// A top-level window that hosts one floating tab group. Dragging a tab out of the
    /// main window creates one of these; dragging its tab back onto the dock closes it.
    /// </summary>
    public sealed class FloatingDockWindow : Window
    {
        private readonly DockFloatingGroup floating;
        private readonly FloatingWindowManager manager;
        private readonly DockHost host;
        private bool allowClose;
        private bool closingAnimated;
        private EditorWindowViewModel? titleTab;

        /// <summary>Creates and wires the floating window. Call <see cref="Window.Show"/> afterwards.</summary>
        public FloatingDockWindow(Window owner, DockLayout layout, DockFloatingGroup floating,
            FloatingWindowManager manager)
        {
            this.floating = floating;
            this.manager = manager;

            Title = floating.Group.SelectedTab?.Title ?? "Division Engine";
            Icon = owner.Icon;
            Width = floating.Width;
            Height = floating.Height;
            MinWidth = 260;
            MinHeight = 160;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(floating.X, floating.Y);
            Background = EditorColor.FromRGB(51, 51, 51);
            ShowInTaskbar = true;
            Opacity = 0;

            host = new DockHost();
            Surface = new DockSurface(this, host, layout, floating, manager);
            Content = new Border
            {
                Background = EditorColor.FromRGB(51, 51, 51),
                Child = host,
            };
            host.Attach(Surface);

            floating.Group.PropertyChanged += OnGroupPropertyChanged;
            floating.Group.Tabs.CollectionChanged += OnTabsChanged;
            UpdateTitle();

            Opened += OnOpened;
            Closing += OnClosing;
            Closed += OnClosed;
            PositionChanged += OnPositionChanged;
            Resized += OnResized;
        }

        /// <summary>The dock surface for this window.</summary>
        public DockSurface Surface { get; }

        /// <summary>The floating group shown by this window.</summary>
        public DockFloatingGroup FloatingGroup => floating;

        private void OnOpened(object? sender, EventArgs e)
        {
            _ = DockAnimations.FadeWindowAsync(this, 0, 1, 140);
        }

        private void OnClosing(object? sender, WindowClosingEventArgs e)
        {
            if (allowClose || manager.IsShuttingDown) return;

            // Cancel this close and close the tabs instead. The last tab removal removes
            // the floating group from the layout, which asks the manager to close this
            // window with the closing animation.
            e.Cancel = true;
            if (floating.Group.Tabs.Count > 0) Surface.CloseGroup(floating.Group);
            else manager.RequestClose(floating);
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            floating.Group.PropertyChanged -= OnGroupPropertyChanged;
            floating.Group.Tabs.CollectionChanged -= OnTabsChanged;
            if (titleTab != null) titleTab.PropertyChanged -= OnTitleTabPropertyChanged;
            Surface.Unregister();
            manager.NotifyWindowClosed(this);
            host.Dispose();
        }

        private void OnPositionChanged(object? sender, PixelPointEventArgs e)
        {
            // Maximizing/minimizing can raise position changes before Avalonia reports the
            // new WindowState (and minimized windows are parked off-screen), so re-check
            // once the notification pass settles and never persist a non-normal position.
            Dispatcher.UIThread.Post(() =>
            {
                if (WindowState != Avalonia.Controls.WindowState.Normal) return;
                floating.X = Position.X;
                floating.Y = Position.Y;
            }, DispatcherPriority.Background);
        }

        private void OnResized(object? sender, WindowResizedEventArgs e)
        {
            // Maximizing raises WM_SIZE before Avalonia reports the new WindowState, so
            // re-check after the current layout/notification pass has settled.
            Dispatcher.UIThread.Post(() =>
            {
                if (WindowState != Avalonia.Controls.WindowState.Normal) return;
                floating.Width = Width;
                floating.Height = Height;
            }, DispatcherPriority.Background);
        }

        private void OnTabsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
            UpdateTitle();

        private void OnGroupPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DockGroupNode.SelectedTab)) UpdateTitle();
        }

        private void UpdateTitle()
        {
            if (titleTab != null) titleTab.PropertyChanged -= OnTitleTabPropertyChanged;

            titleTab = floating.Group.SelectedTab;
            if (titleTab != null) titleTab.PropertyChanged += OnTitleTabPropertyChanged;

            Title = titleTab?.Title ?? "Division Engine";
        }

        private void OnTitleTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(EditorWindowViewModel.Title))
                Title = titleTab?.Title ?? "Division Engine";
        }

        /// <summary>Closes the window with a short fade-out.</summary>
        public void CloseAnimated()
        {
            if (closingAnimated) return;
            closingAnimated = true;
            allowClose = true;

            // No more tabs can be added to or dragged out of a closing window.
            IsHitTestVisible = false;
            Surface.Unregister();

            // Not shown yet (layout reset during creation): there is nothing to fade.
            if (!IsVisible)
            {
                ForceClose();
                return;
            }

            _ = CloseAnimatedAsync();
        }

        private async System.Threading.Tasks.Task CloseAnimatedAsync()
        {
            try
            {
                await DockAnimations.FadeWindowAsync(this, Opacity, 0, 90);
            }
            catch
            {
                // Ignore animation failures; the window still needs to close.
            }

            try
            {
                Close();
            }
            catch
            {
                // Already closed.
            }
        }

        /// <summary>Closes immediately, used during shutdown and layout resets.</summary>
        public void ForceClose()
        {
            allowClose = true;
            try
            {
                Close();
            }
            catch
            {
                // Already closed.
            }
        }
    }
}
