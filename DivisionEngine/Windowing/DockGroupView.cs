//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using DivisionEngine.Editor.ViewModels;
using Material.Icons;
using Material.Icons.Avalonia;
using System;
using System.Collections.Specialized;

namespace DivisionEngine.Editor.Windowing
{
    /// <summary>
    /// One tab group in the docking system: the familiar tab strip and + button, plus
    /// the drag source logic that starts a docking drag. Instances are cached by the
    /// dock host and moved with their group when the layout is restructured.
    /// </summary>
    public sealed class DockGroupView : UserControl, IDisposable
    {
        private readonly DockSurface surface;
        private readonly TabControl tabControl;
        private readonly Border emptyState;

        private EditorWindowViewModel? pressedTab;
        private IPointer? dragPointer;
        private PixelPoint pressScreen;
        private bool isDragging;
        private bool disposed;

        /// <summary>Creates the view for a tab group.</summary>
        public DockGroupView(DockSurface surface, DockGroupNode group)
        {
            this.surface = surface;
            Group = group;

            MinWidth = 60;
            MinHeight = 40;
            ClipToBounds = true;

            Grid grid = new();
            tabControl = new TabControl
            {
                ItemsSource = group.Tabs,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                ItemTemplate = CreateHeaderTemplate(),
            };
            tabControl.Bind(TabControl.SelectedItemProperty, new Binding(nameof(DockGroupNode.SelectedTab))
            {
                Source = group,
                Mode = BindingMode.TwoWay,
            });
            // Pointer handlers live on the group view rather than the inner TabControl:
            // once a drag starts, the group view captures the pointer and captured events
            // are routed to the capture target (and its ancestors), not to its children.
            AddHandler(PointerPressedEvent, OnTabPointerPressed, RoutingStrategies.Tunnel);
            AddHandler(PointerMovedEvent, OnTabPointerMoved, RoutingStrategies.Tunnel);
            AddHandler(PointerReleasedEvent, OnTabPointerReleased, RoutingStrategies.Tunnel);
            AddHandler(ContextRequestedEvent, OnTabContextRequested, RoutingStrategies.Tunnel);
            // PointerCaptureLost is a Direct routed event in Avalonia; a Tunnel handler
            // would never run and a lost capture would leak the drag session/ghost.
            AddHandler(PointerCaptureLostEvent, OnPointerCaptureLost, RoutingStrategies.Direct);
            grid.Children.Add(tabControl);

            emptyState = new Border
            {
                Name = "EmptyGroupOverlay",
                Background = EditorColor.FromRGB(27, 27, 27),
                IsHitTestVisible = false,
                IsVisible = group.Tabs.Count == 0,
                Child = new TextBlock
                {
                    Text = "Empty tab group\nDrag a tab here or use +",
                    FontSize = 11,
                    Foreground = EditorColor.FromRGB(120, 120, 120),
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            grid.Children.Add(emptyState);

            Button addButton = new()
            {
                Name = "GroupAddButton",
                Content = "+",
                FontSize = 14,
                Width = 18,
                Height = 18,
                Margin = new Thickness(6),
                Padding = new Thickness(3, -2, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Cursor = new Cursor(StandardCursorType.Hand),
                Flyout = CreateAddFlyout(),
            };
            ToolTip.SetTip(addButton, "Add a window to this tab group");
            // The add button must stay above the empty-group overlay.
            addButton.ZIndex = 10;
            grid.Children.Add(addButton);

            Content = grid;
            group.Tabs.CollectionChanged += OnTabsChanged;
        }

        /// <summary>The model group rendered by this view.</summary>
        public DockGroupNode Group { get; }

        /// <summary>The tab control used for header geometry.</summary>
        public TabControl Tabs => tabControl;

        /// <summary>Updates the empty-state overlay after tabs are added or removed.</summary>
        private void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
            emptyState.IsVisible = Group.Tabs.Count == 0;

        private static IDataTemplate CreateHeaderTemplate()
        {
            return new FuncDataTemplate<EditorWindowViewModel>((viewModel, _) =>
            {
                StackPanel panel = new()
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                };
                MaterialIcon icon = new()
                {
                    Width = 12,
                    Height = 12,
                    Foreground = EditorColor.FromRGB(136, 136, 136),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                icon.Bind(MaterialIcon.KindProperty, new Binding(nameof(EditorWindowViewModel.Icon)));
                TextBlock title = new() { VerticalAlignment = VerticalAlignment.Center };
                title.Bind(TextBlock.TextProperty, new Binding(nameof(EditorWindowViewModel.Title)));
                panel.Children.Add(icon);
                panel.Children.Add(title);
                return panel;
            });
        }

        #region add window flyout

        private Flyout CreateAddFlyout()
        {
            Flyout flyout = new()
            {
                Placement = PlacementMode.Bottom,
                ShowMode = FlyoutShowMode.TransientWithDismissOnPointerMoveAway,
            };

            StackPanel stackPanel = new();
            foreach (EditorWindowCatalogEntry entry in EditorWindowCatalog.ForZone(Group.Zone))
            {
                stackPanel.Children.Add(CreateWindowRow(entry, flyout));
            }

            if (stackPanel.Children.Count == 0)
            {
                stackPanel.Children.Add(new TextBlock
                {
                    Text = "No windows available",
                    FontSize = 11,
                    Foreground = EditorColor.FromRGB(150, 150, 150),
                    Margin = new Thickness(10, 6),
                });
            }

            flyout.Content = stackPanel;
            return flyout;
        }

        private StackPanel CreateWindowRow(EditorWindowCatalogEntry entry, Flyout flyout)
        {
            StackPanel row = new()
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0),
                Background = EditorColor.FromRGB(24, 24, 24),
            };

            MaterialIcon icon = new()
            {
                Kind = entry.Icon,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0),
                Padding = new Thickness(12, 4, 6, 4),
                CornerRadius = new CornerRadius(0),
                Foreground = EditorColor.FromRGB(200, 200, 200),
            };
            TextBlock label = new()
            {
                Text = entry.Title,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0),
                Padding = new Thickness(12, 4),
                Foreground = EditorColor.FromRGB(200, 200, 200),
            };
            row.Children.Add(icon);
            row.Children.Add(label);

            row.PointerPressed += (_, _) =>
            {
                if (EditorWindowCatalog.Create(entry.ViewModelType) is { } viewModel)
                    surface.AddWindowToGroup(viewModel, Group);
                flyout.Hide();
            };
            row.PointerEntered += (_, _) => row.Background = EditorColor.FromRGB(10, 10, 10);
            row.PointerExited += (_, _) => row.Background = EditorColor.FromRGB(24, 24, 24);
            return row;
        }

        #endregion

        #region tab context menu

        private void OnTabContextRequested(object? sender, ContextRequestedEventArgs e)
        {
            ResetDragState();
            TabItem? tabItem = FindTabItem(e.Source as Control);
            if (tabItem?.DataContext is not EditorWindowViewModel viewModel) return;

            CreateTabContextMenu(viewModel).Open(tabItem);
            e.Handled = true;
        }

        private ContextMenu CreateTabContextMenu(EditorWindowViewModel viewModel)
        {
            ContextMenu menu = new()
            {
                Background = EditorColor.FromRGB(68, 68, 68),
                BorderBrush = EditorColor.FromRGB(128, 128, 128),
            };

            menu.Items.Add(MenuItemFor("Close", () => surface.CloseTab(viewModel)));
            MenuItem duplicate = MenuItemFor("Duplicate Tab", () => surface.DuplicateTab(viewModel));
            menu.Items.Add(duplicate);
            menu.Items.Add(new Separator());

            if (surface.IsFloating)
            {
                menu.Items.Add(MenuItemFor("Dock Tab", () => surface.DockTab(viewModel)));
            }
            else
            {
                menu.Items.Add(MenuItemFor("Float Tab", () => surface.FloatTab(viewModel)));

                MenuItem moveToNewGroup = new()
                {
                    Header = "Move to New Group",
                    Foreground = Brushes.White,
                };
                moveToNewGroup.Items.Add(MenuItemFor("Left", () => surface.MoveTabToNewGroup(viewModel, DockZone.Left)));
                moveToNewGroup.Items.Add(MenuItemFor("Right", () => surface.MoveTabToNewGroup(viewModel, DockZone.Right)));
                moveToNewGroup.Items.Add(MenuItemFor("Above", () => surface.MoveTabToNewGroup(viewModel, DockZone.Top)));
                moveToNewGroup.Items.Add(MenuItemFor("Below", () => surface.MoveTabToNewGroup(viewModel, DockZone.Bottom)));
                menu.Items.Add(moveToNewGroup);
            }

            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItemFor("Close Tab Group", () => surface.CloseGroup(Group)));
            return menu;
        }

        private static MenuItem MenuItemFor(string header, Action action)
        {
            MenuItem item = new()
            {
                Header = header,
                Foreground = Brushes.White,
            };
            item.Click += (_, _) => action();
            return item;
        }

        private static TabItem? FindTabItem(Control? control)
        {
            while (control != null)
            {
                if (control is TabItem tabItem) return tabItem;
                control = control.Parent as Control;
            }
            return null;
        }

        #endregion

        #region tab dragging

        private void OnTabPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (disposed) return;

            // A right/middle press (or any other button) ends any stale left-press state
            // left behind by a release that happened outside this view.
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                ResetDragState();
                return;
            }
            if (e.Source is not Control source) return;

            TabItem? tabItem = FindTabItem(source);
            if (tabItem?.DataContext is not EditorWindowViewModel viewModel) return;

            pressedTab = viewModel;
            dragPointer = e.Pointer;
            pressScreen = SafePointToScreen(e.GetPosition(this));
            isDragging = false;
        }

        private void OnTabPointerMoved(object? sender, PointerEventArgs e)
        {
            if (disposed || pressedTab == null || dragPointer == null) return;

            // If the button was released while the pointer was outside this view we never
            // saw the release. Do not turn the next ordinary mouse move into a drag.
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                ResetDragState();
                return;
            }

            PixelPoint screen = SafePointToScreen(e.GetPosition(this));
            if (!isDragging)
            {
                int dx = Math.Abs(screen.X - pressScreen.X);
                int dy = Math.Abs(screen.Y - pressScreen.Y);
                if (dx < 8 && dy < 8) return;

                isDragging = true;
                dragPointer.Capture(this);
                DockDragSession.Start(surface, Group, pressedTab, screen);
            }

            DockDragSession.Current?.Update(screen);
            e.Handled = true;
        }

        private void OnTabPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (disposed) return;

            if (isDragging)
            {
                PixelPoint screen = SafePointToScreen(e.GetPosition(this));
                DockDragSession? session = DockDragSession.Current;
                IPointer? pointer = dragPointer;

                // Mark the drag finished before releasing capture: releasing it raises
                // PointerCaptureLost, whose handler must not cancel the session we are
                // about to complete.
                ResetDragState();
                pointer?.Capture(null);
                session?.Complete(screen);
                e.Handled = true;
                return;
            }

            ResetDragState();

            if (e.InitialPressMouseButton == MouseButton.Middle && e.Source is Control source)
            {
                if (FindTabItem(source)?.DataContext is EditorWindowViewModel viewModel)
                {
                    surface.CloseTab(viewModel);
                    e.Handled = true;
                }
            }
        }

        private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            if (!isDragging) return;

            // A child (the TabItem) losing its own capture also routes through this
            // handler because the event tunnels through ancestors. Only cancel when this
            // group view is the element that actually lost the pointer.
            if (!ReferenceEquals(e.Source, this)) return;

            DockDragSession.Current?.Cancel();
            ResetDragState();
        }

        private void ResetDragState()
        {
            pressedTab = null;
            dragPointer = null;
            isDragging = false;
        }

        private PixelPoint SafePointToScreen(Point point)
        {
            try
            {
                return this.PointToScreen(point);
            }
            catch
            {
                return new PixelPoint((int)point.X, (int)point.Y);
            }
        }

        #endregion

        /// <inheritdoc/>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Group.Tabs.CollectionChanged -= OnTabsChanged;

            // Do NOT clear ItemsSource: the TabControl's TwoWay SelectedItem binding would
            // write null back into DockGroupNode.SelectedTab while the editor is shutting
            // down and the project layout is being saved. Clearing Content detaches the
            // control without mutating the model.
            Content = null;
        }
    }
}
