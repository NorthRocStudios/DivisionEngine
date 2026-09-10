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
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using DivisionEngine.Editor.Tasks;
using DivisionEngine.Editor.ViewModels;
using DivisionEngine.MathLib;
using DivisionEngine.MathUtilities;
using DivisionEngine.Projects;
using Material.Icons;
using Material.Icons.Avalonia;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Vector = Avalonia.Vector;

namespace DivisionEngine.Editor
{
    /// <summary>
    /// Represents the main UI window of the Division Engine editor.
    /// </summary>
    public partial class MainWindow : Window
    {
        // All available windows per tab group are defined here
        private readonly List<(string AvaiableTabGroups, Type ViewModelType)> AvailableWindows =
        [
            ("left,right,center,bottom", typeof(WorldWindowViewModel)),
            ("left,right,center,bottom", typeof(EnvironmentWindowViewModel)),
            ("left,right,center,bottom", typeof(AssetsWindowViewModel)),
            ("left,right,center,bottom", typeof(ConsoleWindowViewModel)),
            ("left,right,center,bottom", typeof(PropertiesWindowViewModel)),
            ("left,right,center,bottom", typeof(SettingsWindowViewModel)),
            ("left,right,center,bottom", typeof(DeveloperWindowViewModel)),
        ];

        // Bottom nav bar vars
        private static Flyout? tasksFlyout;

        // Draggin vars
        private EditorWindowViewModel? draggedTab;
        private string? sourcePanel;
        private TabControl? dragSourceTabControl;
        private Point pressPoint;
        private bool isDragging;
        private Border? ghost;
        private Border? dropHighlight;
        private Border? insertionLine;

        /// <summary>
        /// Initializes a new instance of the <see cref="MainWindow"/> class.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent(); // Initialize the main window components
            if (DataContext is MainWindowViewModel vm) vm.RequestClose = Close;
            AttachContextMenus();
            SetupUniversalProgressBar(); // Build editor task system UI
            SetupAddButtons();
            SetupPlayControls();
            SubscribeToEngineEvents();
            UpdatePlayControlsUI();
            SubscribeToProjectEvents();
        }

        private void SubscribeToProjectEvents()
        {
            ProjectManager.ProjectLoaded += OnProjectLoaded;
            ProjectManager.ProjectClosing += OnProjectClosing;
        }

        private void OnProjectLoaded() => Dispatcher.UIThread.Post(async () => await LoadEditorLayoutFromProjectAsync());

        private void OnProjectClosing()
        {
            SaveEditorLayoutToProject();
            ProjectManager.SaveCurrentProject();
        }

        #region layouts

        /// <summary>
        /// Saves the current editor layout to the loaded project data.
        /// </summary>
        public void SaveEditorLayoutToProject()
        {
            if (DataContext is not MainWindowViewModel vm || ProjectManager.CurrentProjectData == null) return;

            try
            {
                EditorLayoutData layout = ProjectManager.CurrentProjectData.EditorLayout;

                // Save panel sizes
                layout.LeftPanelWidth = GetColumnWidth(LeftPanelGrid);
                layout.RightPanelWidth = GetColumnWidth(RightPanelGrid);
                layout.BottomPanelHeight = GetRowHeight(BottomPanelGrid);

                // Save selected tabs
                layout.SelectedLeftTab = vm.SelectedLeftTab?.GetType().Name.Replace("ViewModel", "") ?? "";
                layout.SelectedRightTab = vm.SelectedRightTab?.GetType().Name.Replace("ViewModel", "") ?? "";
                layout.SelectedCenterTab = vm.SelectedCenterTab?.GetType().Name.Replace("ViewModel", "") ?? "";
                layout.SelectedBottomTab = vm.SelectedBottomTab?.GetType().Name.Replace("ViewModel", "") ?? "";

                // Save open tabs as comma-separated strings
                layout.LeftTabs = string.Join(",", vm.LeftTabs.Select(t => t.GetType().Name.Replace("ViewModel", "")));
                layout.RightTabs = string.Join(",", vm.RightTabs.Select(t => t.GetType().Name.Replace("ViewModel", "")));
                layout.CenterTabs = string.Join(",", vm.CenterTabs.Select(t => t.GetType().Name.Replace("ViewModel", "")));
                layout.BottomTabs = string.Join(",", vm.BottomTabs.Select(t => t.GetType().Name.Replace("ViewModel", "")));
            }
            catch (Exception ex)
            {
                Debug.Error($"Failed to save editor layout: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads the editor layout from the loaded project data.
        /// </summary>
        public async Task LoadEditorLayoutFromProjectAsync()
        {
            if (DataContext is not MainWindowViewModel vm || ProjectManager.CurrentProjectData == null) return;

            try
            {
                EditorLayoutData layout = ProjectManager.CurrentProjectData.EditorLayout;

                // Clear existing tabs
                vm?.LeftTabs.Clear();
                vm?.RightTabs.Clear();
                vm?.CenterTabs.Clear();
                vm?.BottomTabs.Clear();

                // Parse comma-separated strings and restore tabs
                RestoreTabsFromString(vm!, layout.LeftTabs, "left");
                RestoreTabsFromString(vm!, layout.RightTabs, "right");
                RestoreTabsFromString(vm!, layout.CenterTabs, "center");
                RestoreTabsFromString(vm!, layout.BottomTabs, "bottom");

                // Restore selected tabs
                vm!.SelectedLeftTab = FindTabByTypeName(vm.LeftTabs, layout.SelectedLeftTab);
                vm.SelectedRightTab = FindTabByTypeName(vm.RightTabs, layout.SelectedRightTab);
                vm.SelectedCenterTab = FindTabByTypeName(vm.CenterTabs, layout.SelectedCenterTab);
                vm.SelectedBottomTab = FindTabByTypeName(vm.BottomTabs, layout.SelectedBottomTab);

                // Restore panel sizes
                SetColumnWidth(LeftPanelGrid, layout.LeftPanelWidth);
                SetColumnWidth(RightPanelGrid, layout.RightPanelWidth);
                SetRowHeight(BottomPanelGrid, layout.BottomPanelHeight);
            }
            catch (Exception ex)
            {
                Debug.Error($"Failed to load editor layout: {ex.Message}");
            }
        }

        private static void RestoreTabsFromString(MainWindowViewModel vm, string tabNames, string panel)
        {
            if (string.IsNullOrWhiteSpace(tabNames)) return;
            foreach (string typeName in tabNames.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                EditorWindowViewModel? viewModel = CreateViewModelFromTypeName(typeName.Trim());
                vm.AddWindowToPanel(viewModel!, panel);
            }
        }

        private static EditorWindowViewModel? CreateViewModelFromTypeName(string typeName)
        {
            string fullTypeName = $"DivisionEngine.Editor.ViewModels.{typeName}ViewModel";
            Type? type = Type.GetType(fullTypeName);
            if (type != null && Activator.CreateInstance(type) is EditorWindowViewModel editorVM) return editorVM;
            return null;
        }

        private static EditorWindowViewModel? FindTabByTypeName(ObservableCollection<EditorWindowViewModel> tabs, string typeName) =>
            tabs.FirstOrDefault(t => t.GetType().Name.Replace("ViewModel", "") == typeName);

        private static double GetColumnWidth(Grid grid)
        {
            if (grid.Parent is Grid parent && Grid.GetColumn(grid) >= 0)
            {
                ColumnDefinition colDef = parent.ColumnDefinitions[Grid.GetColumn(grid)];
                if (colDef.Width.IsAbsolute) return colDef.Width.Value;
                else if (colDef.Width.IsStar) return colDef.Width.Value;
            }
            return 200; // Default
        }

        private static double GetRowHeight(Grid grid)
        {
            if (grid.Parent is Grid parent && Grid.GetRow(grid) >= 0)
            {
                RowDefinition rowDef = parent.RowDefinitions[Grid.GetRow(grid)];
                if (rowDef.Height.IsAbsolute) return rowDef.Height.Value;
            }
            return 250; // Default
        }

        private static void SetColumnWidth(Grid grid, double width)
        {
            if (grid.Parent is Grid parent && Grid.GetColumn(grid) >= 0)
                parent.ColumnDefinitions[Grid.GetColumn(grid)].Width = new GridLength(width);
        }

        private static void SetRowHeight(Grid grid, double height)
        {
            if (grid.Parent is Grid parent && Grid.GetRow(grid) >= 0)
                parent.RowDefinitions[Grid.GetRow(grid)].Height = new GridLength(height);
        }

        #endregion layouts
        #region playMode

        /// <summary>
        /// Sets up the play controls toolbar.
        /// </summary>
        private void SetupPlayControls()
        {
            PlayButton?.Click += PlayButton_Click;
            if (PauseButton != null)
            {
                PauseButton.Click += PauseButton_Click;
                PauseButton.Click += (_, _) => UpdatePlayControlsUI();
            }
            AdvanceFrameButton?.Click += AdvanceFrameButton_Click;
        }

        /// <summary>
        /// Subscribes to engine core events.
        /// </summary>
        private void SubscribeToEngineEvents() => EngineCore.PlayModeChanged += OnPlayModeChanged;

        /// <summary>
        /// Called when play mode changes.
        /// </summary>
        /// <param name="inPlayMode">Is the engine in play mode or no</param>
        private void OnPlayModeChanged(bool inPlayMode) => Dispatcher.UIThread.Post(UpdatePlayControlsUI);

        /// <summary>
        /// Updates the play controls UI based on current engine state.
        /// </summary>
        private void UpdatePlayControlsUI()
        {
            if (EngineCore.IsInPlayMode)
            {
                FileMenu.IsEnabled = false;

                if (EngineCore.IsPaused)
                {
                    // Paused
                    PlayIcon.Kind = MaterialIconKind.Stop;
                    PauseIcon.Kind = MaterialIconKind.Play;
                    PlayIcon.Foreground = EditorColor.FromColor(ColorPalette.TomatoRed);
                    PauseIcon.Foreground = EditorColor.FromColor(ColorPalette.Azure);
                    GameModeText.Text = "Paused";
                    GameModeText.Foreground = EditorColor.FromRGB(255, 183, 77);
                    GameModeIndicator.Background = EditorColor.FromRGB(50, 40, 20);

                    AdvanceFrameButton.IsEnabled = true;
                    PauseButton.IsEnabled = true;
                }
                else
                {
                    // Playing
                    PlayIcon.Kind = MaterialIconKind.Stop;
                    PauseIcon.Kind = MaterialIconKind.Pause;
                    PlayIcon.Foreground = EditorColor.FromColor(ColorPalette.TomatoRed);
                    PauseIcon.Foreground = EditorColor.FromColor(ColorPalette.Orange);
                    GameModeText.Text = "Playing";
                    GameModeText.Foreground = EditorColor.FromRGB(139, 195, 74);
                    GameModeIndicator.Background = EditorColor.FromRGB(30, 50, 20);

                    AdvanceFrameButton.IsEnabled = false;
                    PauseButton.IsEnabled = true;
                }
            }
            else
            {
                // Editing
                PlayIcon.Kind = MaterialIconKind.Play;
                PauseIcon.Kind = MaterialIconKind.Pause;
                PlayIcon.Foreground = EditorColor.FromColor(ColorPalette.Lime);
                PauseIcon.Foreground = EditorColor.FromColor(ColorPalette.Orange);
                GameModeText.Text = "Editing";
                GameModeText.Foreground = EditorColor.FromRGB(136, 136, 136);
                GameModeIndicator.Background = EditorColor.FromRGB(40, 40, 40);

                AdvanceFrameButton.IsEnabled = false;
                PauseButton.IsEnabled = false;

                FileMenu.IsEnabled = true;
            }
        }

        /// <summary>
        /// Play button click handler.
        /// </summary>
        private void PlayButton_Click(object? sender, RoutedEventArgs e)
        {
            if (EngineCore.IsInPlayMode) EngineCore.ExitPlayMode();
            else EngineCore.EnterPlayMode();
        }

        /// <summary>
        /// Pause button click handler.
        /// </summary>
        private void PauseButton_Click(object? sender, RoutedEventArgs e)
        {
            if (EngineCore.IsInPlayMode)
            {
                if (EngineCore.IsPaused) EngineCore.Resume();
                else EngineCore.Pause();
            }
        }

        /// <summary>
        /// Advance frame button click handler.
        /// </summary>
        private void AdvanceFrameButton_Click(object? sender, RoutedEventArgs e)
        {
            if (EngineCore.IsInPlayMode && EngineCore.IsPaused) EngineCore.RunFrame();
        }

        #endregion
        #region contextMenus

        /// <summary>
        /// Attaches a context menu to each tab control.
        /// </summary>
        private void AttachContextMenus()
        {
            Loaded += (s, e) =>
            {
                TabControl leftTabsControl = this.Find<TabControl>("leftTabs")!;
                TabControl centerTabsControl = this.Find<TabControl>("centerTabs")!;
                TabControl bottomTabsControl = this.Find<TabControl>("bottomTabs")!;
                TabControl rightTabsControl = this.Find<TabControl>("rightTabs")!;
                AttachContextMenuToTabControl(leftTabsControl);
                AttachContextMenuToTabControl(centerTabsControl);
                AttachContextMenuToTabControl(bottomTabsControl);
                AttachContextMenuToTabControl(rightTabsControl);

                SetupCustomDrag(); // make sure tab drag drop is setup correctly
            };
        }

        /// <summary>
        /// Attaches a context menu to each tab control element.
        /// </summary>
        /// <param name="tabControl">Tab control to attach menu to</param>
        /// <param name="panelType">Panel type of tab control</param>
        private void AttachContextMenuToTabControl(TabControl tabControl)
        {
            tabControl.AddHandler(PointerReleasedEvent, (sender, e) =>
            {
                if (e.InitialPressMouseButton == MouseButton.Right)
                {
                    Control? source = e.Source as Control;
                    TabItem? tabItem = EditorUI.FindParentTabItem(source);

                    if (tabItem != null && tabItem.DataContext is EditorWindowViewModel viewModel)
                    {
                        ContextMenu contextMenu = CreateTabContextMenu(viewModel);
                        contextMenu.Open(tabItem);
                        e.Handled = true;
                    }
                }
            }, RoutingStrategies.Tunnel);
        }

        /// <summary>
        /// Creates the context menu for each tab.
        /// </summary>
        /// <param name="panelType">Tab panel type</param>
        /// <param name="viewModel">Tab editor view model</param>
        /// <returns>Generated tab context menu</returns>
        private ContextMenu CreateTabContextMenu(EditorWindowViewModel viewModel)
        {
            ContextMenu contextMenu = new ContextMenu
            {
                Background = EditorColor.FromRGB(68, 68, 68),
                BorderBrush = EditorColor.FromRGB(128, 128, 128),
            };
            if (DataContext is MainWindowViewModel mainViewModel)
            {
                MenuItem closeMenuItem = new MenuItem
                {
                    Header = "Close",
                    Foreground = EditorColor.FromColor(ColorPalette.White),
                    Command = mainViewModel.CloseTabCommand,
                    CommandParameter = viewModel
                };
                Separator separator = new Separator();
                MenuItem duplicateMenuItem = new MenuItem
                {
                    Header = "Duplicate Tab",
                    Foreground = EditorColor.FromColor(ColorPalette.White),
                    Command = mainViewModel.DuplicateTabCommand,
                    CommandParameter = viewModel
                };

                contextMenu.Items.Add(closeMenuItem);
                contextMenu.Items.Add(separator);
                contextMenu.Items.Add(duplicateMenuItem);
            }
            return contextMenu;
        }

        #endregion
        #region addTabButton

        private void SetupAddButtons()
        {
            SetupAddButton(LeftAddButton, "left");
            SetupAddButton(CenterAddButton, "center");
            SetupAddButton(BottomAddButton, "bottom");
            SetupAddButton(RightAddButton, "right");
        }

        private void SetupAddButton(Button button, string panelType)
        {
            if (button == null) return;
            Flyout flyout = new Flyout
            {
                Placement = PlacementMode.Bottom,
                ShowMode = FlyoutShowMode.TransientWithDismissOnPointerMoveAway,
            };

            StackPanel stackPanel = new StackPanel();
            foreach (var (AvaiableTabGroups, ViewModelType) in AvailableWindows)
            {
                if (!AvaiableTabGroups.Contains(panelType, StringComparison.InvariantCultureIgnoreCase)) continue;

                // Create a temporary instance to get window info
                EditorWindowViewModel? tempInstance = Activator.CreateInstance(ViewModelType) as EditorWindowViewModel;
                string windowTitle = tempInstance?.Title ?? ViewModelType.Name.Replace("WindowViewModel", "");
                MaterialIconKind windowIcon = tempInstance?.Icon ?? MaterialIconKind.DatabaseEdit;

                StackPanel menuPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(0),
                    Background = EditorColor.FromRGB(24, 24, 24),
                };
                MaterialIcon menuIcon = new MaterialIcon
                {
                    Kind = windowIcon,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0),
                    Padding = new Thickness(12, 4, 6, 4),
                    CornerRadius = new CornerRadius(0),
                    Foreground = EditorColor.FromRGB(200, 200, 200),
                };
                TextBlock menuLabel = new TextBlock
                {
                    Text = windowTitle,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0),
                    Padding = new Thickness(12, 4),
                    Foreground = EditorColor.FromRGB(200, 200, 200),
                };
                menuPanel.Children.Add(menuIcon);
                menuPanel.Children.Add(menuLabel);

                Type viewModelType = ViewModelType; // Store the type locally to avoid closure issues
                menuPanel.PointerPressed += (s, e) =>
                {
                    if (DataContext is MainWindowViewModel vm &&
                        Activator.CreateInstance(viewModelType) is EditorWindowViewModel viewModel)
                        vm.AddWindowToPanel(viewModel, panelType);
                    flyout.Hide();
                };
                menuPanel.PointerEntered += (s, e) =>
                    menuPanel.Background = EditorColor.FromRGB(10, 10, 10);
                menuPanel.PointerExited += (s, e) =>
                    menuPanel.Background = EditorColor.FromRGB(24, 24, 24);
                stackPanel.Children.Add(menuPanel);
            }

            flyout.Content = stackPanel;
            button.Flyout = flyout;
        }

        #endregion
        #region taskManagement

        /// <summary>
        /// Initializes the universal progress bar at the bottom of the editor.
        /// </summary>
        private void SetupUniversalProgressBar()
        {
            EditorTaskManager.TasksChanged += UpdateUniversalProgressBar;

            // Find the progress bar container
            Border? progressBarContainer = this.FindControl<Border>("ProgressBarContainer");
            if (progressBarContainer != null)
            {
                // Create editor tasks flyout
                tasksFlyout = new Flyout
                {
                    Placement = PlacementMode.Top, // Shows above progress bar
                    ShowMode = FlyoutShowMode.TransientWithDismissOnPointerMoveAway,
                    FlyoutPresenterClasses = { "tasks-foldout" }
                };
                UpdateTaskFoldoutContent(tasksFlyout); // Set flyout content

                // Attach click handler to show the flyout
                progressBarContainer.PointerPressed += (s, e) =>
                {
                    if (e.GetCurrentPoint(progressBarContainer).Properties.IsLeftButtonPressed)
                    {
                        UpdateTaskFoldoutContent(tasksFlyout); // Refresh before showing
                        tasksFlyout.ShowAt(progressBarContainer);
                    }
                };
            }
            UpdateUniversalProgressBar();
        }

        /// <summary>
        /// Updates the universal progress bar at the bottom of the editor.
        /// </summary>
        private void UpdateUniversalProgressBar()
        {
            List<EditorTask> tasks = [.. EditorTaskManager.GetAll()];
            TextBlock? progressText = this.FindControl<TextBlock>("ProgressPercentageText");
            TextBlock? taskCountText = this.FindControl<TextBlock>("TaskCountText");

            if (tasks.Count > 0)
            {
                float avgProgress = tasks.Average(t => t.Progress);
                UniversalProgressBar.Value = avgProgress * 100;
                if (progressText != null)
                {
                    progressText.Text = $"{avgProgress:P0}";
                    progressText.Foreground = avgProgress >= 1 ? Brushes.White : Brushes.Aquamarine;
                }
                if (taskCountText != null)
                {
                    taskCountText.Text = $"{tasks.Count} task{(tasks.Count != 1 ? "s" : "")}";
                    taskCountText.Foreground = avgProgress >= 1 ? Brushes.White : Brushes.Aquamarine;
                }
                UniversalProgressBar.Foreground = avgProgress >= 1 ? Brushes.SeaGreen : Brushes.Teal;
                UniversalProgressBar.IsVisible = true;
            }
            else
            {
                UniversalProgressBar.IsVisible = false;
                UniversalProgressBar.Value = 0;
                progressText?.Text = "0%";
                taskCountText?.Text = "";
            }

            if (tasksFlyout != null) UpdateTaskFoldoutContent(tasksFlyout);
        }

        /// <summary>
        /// Updates the editor task manager context menu.
        /// </summary>
        /// <param name="flyout">List of tasks in the context menu</param>
        private static void UpdateTaskFoldoutContent(Flyout flyout)
        {
            List<EditorTask> tasks = [.. EditorTaskManager.GetAll()];
            if (tasks.Count == 0)
            {
                flyout.Content = new TextBlock
                {
                    Text = "No background tasks",
                    Foreground = Brushes.Gray,
                    Padding = new Thickness(20, 10),
                    HorizontalAlignment = HorizontalAlignment.Center,
                };
                return;
            }

            // Create main container that stretches full width
            StackPanel container = new StackPanel
            {
                MinWidth = 300,
                MaxWidth = 500,
                Spacing = 4,
            };

            // Add each task as a full-width item
            foreach (EditorTask task in tasks)
            {
                Border taskBorder = new Border
                {
                    Child = CreateTaskFoldoutItem(task), // Reuse your existing CreateTaskMenuItem but modify for full width
                    BorderBrush = EditorColor.FromRGB(10, 10, 10),
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    CornerRadius = new CornerRadius(4),
                    Background = EditorColor.FromRGB(24, 24, 24),
                    Padding = new Thickness(8, 6),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };
                container.Children.Add(taskBorder);
            }

            // Add clear completed section
            List<EditorTask> completedTasks = [.. tasks.Where(t => t.Progress >= 1)];
            if (completedTasks.Count > 0)
            {
                Button clearButton = new Button
                {
                    Content = $"Clear completed ({completedTasks.Count})",
                    Background = EditorColor.FromRGB(51, 51, 51),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(10, 8),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    Cursor = new Cursor(StandardCursorType.Hand),
                };
                clearButton.Click += (s, e) =>
                {
                    List<EditorTask> currentTasks = [.. EditorTaskManager.GetAll()];
                    IEnumerable<EditorTask> toRemove = [.. currentTasks.Where(t => t.Progress >= 1)];
                    foreach (EditorTask task in toRemove) EditorTaskManager.Remove(task.Id);
                    flyout.Hide();
                };
                container.Children.Add(clearButton);
            }
            flyout.Content = container;
        }

        /// <summary>
        /// Creates a full-width task item for the foldout.
        /// </summary>
        private static Grid CreateTaskFoldoutItem(EditorTask task)
        {
            Grid grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                    new ColumnDefinition(GridLength.Auto),
                },
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto),
                },
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };

            // Icon
            MaterialIcon icon = new MaterialIcon
            {
                Kind = task.Icon,
                Width = 16,
                Height = 16,
                Foreground = task.Progress >= 1 ? Brushes.SeaGreen : Brushes.Teal,
                Margin = new Thickness(0, 2, 8, 0),
                VerticalAlignment = VerticalAlignment.Top,
            };
            Grid.SetColumn(icon, 0);
            Grid.SetRow(icon, 0);
            Grid.SetRowSpan(icon, 2);

            // Name and close button container
            Grid nameContainer = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(1, GridUnitType.Star)), // Name
                    new ColumnDefinition(GridLength.Auto), // Close button
                },
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            TextBlock nameText = new TextBlock
            {
                Text = task.Name,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(nameText, 0);
            Button removeButton = new Button
            {
                Content = "×",
                FontSize = 16,
                FontWeight = FontWeight.Bold,
                Width = 20,
                Height = 20,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Brushes.Gray,
                Padding = new Thickness(0),
                Cursor = new Cursor(StandardCursorType.Hand),
                Tag = task.Id,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0),
            };
            removeButton.PointerEntered += (s, e) => removeButton.Foreground = Brushes.White;
            removeButton.PointerExited += (s, e) => removeButton.Foreground = Brushes.Gray;
            removeButton.Click += (s, e) =>
            {
                if (removeButton.Tag is Guid taskId) EditorTaskManager.Remove(taskId);
                e.Handled = true;
            };
            Grid.SetColumn(removeButton, 1);

            nameContainer.Children.Add(nameText);
            nameContainer.Children.Add(removeButton);
            Grid.SetColumn(nameContainer, 1);
            Grid.SetRow(nameContainer, 0);

            // Description
            TextBlock descText = new TextBlock
            {
                Text = task.Description,
                FontSize = 10,
                Foreground = EditorColor.FromRGB(180, 180, 180),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 4),
            };
            Grid.SetColumn(descText, 1);
            Grid.SetColumnSpan(descText, 2);
            Grid.SetRow(descText, 1);

            // Progress tab
            TextBlock progressText = new TextBlock
            {
                Text = $"{task.Progress:P0}",
                FontSize = 10,
                Foreground = Brushes.Gray,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 4, 0),
            };
            Grid.SetColumn(progressText, 1);
            Grid.SetRow(progressText, 1);

            // Progress bar
            ProgressBar progressBar = new ProgressBar
            {
                Value = task.Progress * 100,
                Maximum = 100,
                Height = 4,
                Background = EditorColor.FromRGB(40, 40, 40),
                Foreground = task.Progress >= 1 ? Brushes.SeaGreen : Brushes.Teal,
                Margin = new Thickness(0, 4, 0, 0),
            };
            Grid.SetColumn(progressBar, 0);
            Grid.SetColumnSpan(progressBar, 3);
            Grid.SetRow(progressBar, 2);

            grid.Children.Add(icon);
            grid.Children.Add(nameContainer);
            grid.Children.Add(descText);
            grid.Children.Add(progressText);
            grid.Children.Add(progressBar);
            return grid;
        }

        #endregion
        #region tabDragDrop

        private void SetupCustomDrag()
        {
            AttachCustomDragToTabControl(leftTabs);
            AttachCustomDragToTabControl(centerTabs);
            AttachCustomDragToTabControl(bottomTabs);
            AttachCustomDragToTabControl(rightTabs);
        }

        private void AttachCustomDragToTabControl(TabControl tabControl)
        {
            if (tabControl == null) return;
            tabControl.AddHandler(PointerPressedEvent, OnTabPointerPressed, RoutingStrategies.Tunnel);
            tabControl.AddHandler(PointerMovedEvent, OnTabPointerMoved, RoutingStrategies.Tunnel);
            tabControl.AddHandler(PointerReleasedEvent, OnTabPointerReleased, RoutingStrategies.Tunnel);
            tabControl.AddHandler(PointerCaptureLostEvent, OnPointerCaptureLost, RoutingStrategies.Tunnel);
        }

        private string? GetPanelType(TabControl? tabControl)
        {
            if (tabControl == null) return null;
            if (tabControl == leftTabs) return "left";
            if (tabControl == rightTabs) return "right";
            if (tabControl == centerTabs) return "center";
            if (tabControl == bottomTabs) return "bottom";
            return null;
        }

        private void OnTabPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            // Only left-click starts a drag; ignore right-click (context menu) presses.
            if (!e.GetCurrentPoint(sender as Visual).Properties.IsLeftButtonPressed) return;
            if (e.Source is not Control source) return;

            TabItem? tabItem = FindParentTabItem(source);
            if (tabItem?.DataContext is EditorWindowViewModel vm)
            {
                draggedTab = vm;
                sourcePanel = GetPanelType(sender as TabControl);
                dragSourceTabControl = sender as TabControl;
                pressPoint = e.GetPosition(this); // Window-relative, used consistently below
                isDragging = false;
                ghost = null;
            }
        }

        private void OnTabPointerMoved(object? sender, PointerEventArgs e)
        {
            if (draggedTab == null || dragSourceTabControl == null) return;

            Point windowPoint = e.GetPosition(this);
            Vector diff = windowPoint - pressPoint;

            // Start drag if moved at least 10 pixels
            if (!isDragging && (math.abs(diff.X) > 10 || math.abs(diff.Y) > 10))
            {
                isDragging = true;
                CreateGhost();
            }

            if (!isDragging || ghost == null) return;

            // Move ghost to follow the cursor (OverlayLayer coordinates == window coordinates)
            Canvas.SetLeft(ghost, windowPoint.X - 20);
            Canvas.SetTop(ghost, windowPoint.Y - 10);

            // Geometric hit-test do NOT use e.Source here, it's unreliable once the TabItem has captured the pointer (its own selection logic does this).
            TabControl? target = GetTabControlUnderPoint(windowPoint);
            UpdateDropIndicators(target, windowPoint);
        }

        private void OnTabPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (draggedTab != null && dragSourceTabControl != null && isDragging)
            {
                Point windowPoint = e.GetPosition(this);
                TabControl? target = GetTabControlUnderPoint(windowPoint);
                if (target != null)
                {
                    string? targetPanel = GetPanelType(target);
                    if (targetPanel != null) MoveOrReorderTab(sourcePanel!, targetPanel, target, draggedTab, windowPoint);
                }
            }

            ClearDragVisuals();
            draggedTab = null;
            sourcePanel = null;
            dragSourceTabControl = null;
            isDragging = false;
        }

        private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            ClearDragVisuals();
            draggedTab = null;
            sourcePanel = null;
            dragSourceTabControl = null;
            isDragging = false;
        }

        private void CreateGhost()
        {
            if (draggedTab == null) return;
            OverlayLayer? overlay = OverlayLayer.GetOverlayLayer(this);
            if (overlay == null) return;

            ghost = new Border
            {
                Background = EditorColor.FromRGB(24, 24, 24),
                Padding = new Thickness(8, 4),
                CornerRadius = new CornerRadius(2),
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(0, 0, 2, 2),
                Opacity = 0.85,
                Width = 120,
                Height = 28,
                ZIndex = 1000,
                IsHitTestVisible = false, // don't let the ghost itself block hit-testing
            };

            StackPanel content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            content.Children.Add(new MaterialIcon
            {
                Kind = draggedTab.Icon,
                Width = 12,
                Height = 12,
                Foreground = Brushes.LightGray,
                VerticalAlignment = VerticalAlignment.Center,
            });
            content.Children.Add(new TextBlock
            {
                Text = draggedTab.Title,
                FontSize = 12,
                Foreground = Brushes.White,
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            ghost.Child = content;
            overlay.Children.Add(ghost);

            dropHighlight = new Border
            {
                Background = EditorColor.FromColor(ColorPalette.Azure),
                Opacity = 0.15,
                IsHitTestVisible = false,
                ZIndex = 999,
            };
            overlay.Children.Add(dropHighlight);

            insertionLine = new Border
            {
                Background = Brushes.White,
                Width = 2,
                IsHitTestVisible = false,
                ZIndex = 1001,
            };
            overlay.Children.Add(insertionLine);
        }

        private void ClearDragVisuals()
        {
            OverlayLayer? overlay = OverlayLayer.GetOverlayLayer(this);
            if (overlay != null)
            {
                if (ghost != null) overlay.Children.Remove(ghost);
                if (dropHighlight != null) overlay.Children.Remove(dropHighlight);
                if (insertionLine != null) overlay.Children.Remove(insertionLine);
            }
            ghost = null;
            dropHighlight = null;
            insertionLine = null;
        }

        /// <summary>
        /// Finds which of the four tab controls (if any) contains the given
        /// window-relative point. Uses real geometry, not event routing/e.Source,
        /// since pointer capture makes e.Source unreliable during a drag.
        /// </summary>
        private TabControl? GetTabControlUnderPoint(Point windowPoint)
        {
            foreach (TabControl? tc in new[] { leftTabs, centerTabs, bottomTabs, rightTabs })
            {
                if (tc == null) continue;
                Point? topLeft = tc.TranslatePoint(new Point(0, 0), this);
                if (topLeft == null) continue;
                Rect rect = new Rect(topLeft.Value, tc.Bounds.Size);
                if (rect.Contains(windowPoint)) return tc;
            }
            return null;
        }

        /// <summary>
        /// Figures out where in the target tab strip a drop would insert the tab,
        /// based on the midpoints of the realized tab headers.
        /// </summary>
        private int GetInsertionIndex(TabControl tabControl, ObservableCollection<EditorWindowViewModel> tabs, Point windowPoint)
        {
            for (int i = 0; i < tabs.Count; i++)
            {
                if (tabControl.ContainerFromIndex(i) is not Control container) continue;
                Point? topLeft = container.TranslatePoint(new Point(0, 0), this);
                if (topLeft == null) continue;
                double midX = topLeft.Value.X + container.Bounds.Width / 2;
                if (windowPoint.X < midX) return i;
            }
            return tabs.Count;
        }

        private void UpdateDropIndicators(TabControl? target, Point windowPoint)
        {
            if (dropHighlight == null || insertionLine == null) return;

            if (target == null)
            {
                dropHighlight.IsVisible = false;
                insertionLine.IsVisible = false;
                return;
            }

            Point? targetTopLeft = target.TranslatePoint(new Point(0, 0), this);
            if (targetTopLeft == null) return;

            dropHighlight.IsVisible = true;
            dropHighlight.Width = target.Bounds.Width;
            dropHighlight.Height = target.Bounds.Height;
            Canvas.SetLeft(dropHighlight, targetTopLeft.Value.X);
            Canvas.SetTop(dropHighlight, targetTopLeft.Value.Y);

            ObservableCollection<EditorWindowViewModel>? targetTabs = GetTabCollection(GetPanelType(target)!);
            if (targetTabs == null) return;

            int insertIndex = GetInsertionIndex(target, targetTabs, windowPoint);
            double lineX;
            if (insertIndex < targetTabs.Count && target.ContainerFromIndex(insertIndex) is Control c &&
                c.TranslatePoint(new Point(0, 0), this) is Point p)
            {
                lineX = p.X;
            }
            else
            {
                lineX = targetTopLeft.Value.X + target.Bounds.Width; // insert at end
            }

            insertionLine.IsVisible = true;
            insertionLine.Height = 28;
            Canvas.SetLeft(insertionLine, lineX - 1);
            Canvas.SetTop(insertionLine, targetTopLeft.Value.Y);
        }

        private void MoveOrReorderTab(string sourcePanel, string targetPanel, TabControl targetControl, EditorWindowViewModel tab, Point windowPoint)
        {
            if (DataContext is not MainWindowViewModel vm) return;

            ObservableCollection<EditorWindowViewModel>? sourceTabs = GetTabCollection(sourcePanel);
            ObservableCollection<EditorWindowViewModel>? targetTabs = GetTabCollection(targetPanel);
            if (sourceTabs == null || targetTabs == null) return;

            int insertIndex = GetInsertionIndex(targetControl, targetTabs, windowPoint);
            if (sourcePanel == targetPanel)
            {
                // Reordering within the same panel
                int oldIndex = sourceTabs.IndexOf(tab);
                if (oldIndex < 0) return;
                if (insertIndex > oldIndex) insertIndex--; // account for the item's own removal shift
                insertIndex = System.Math.Clamp(insertIndex, 0, sourceTabs.Count - 1);
                if (insertIndex != oldIndex) sourceTabs.Move(oldIndex, insertIndex);
            }
            else
            {
                // Moving to a different panel
                if (!sourceTabs.Remove(tab)) return;
                insertIndex = System.Math.Clamp(insertIndex, 0, targetTabs.Count);
                targetTabs.Insert(insertIndex, tab);

                switch (targetPanel)
                {
                    case "left": vm.SelectedLeftTab = tab; break;
                    case "right": vm.SelectedRightTab = tab; break;
                    case "center": vm.SelectedCenterTab = tab; break;
                    case "bottom": vm.SelectedBottomTab = tab; break;
                }
            }
        }

        private ObservableCollection<EditorWindowViewModel>? GetTabCollection(string panelType)
        {
            if (DataContext is not MainWindowViewModel vm) return null;
            return panelType switch
            {
                "left" => vm.LeftTabs,
                "right" => vm.RightTabs,
                "center" => vm.CenterTabs,
                "bottom" => vm.BottomTabs,
                _ => null,
            };
        }

        private static TabItem? FindParentTabItem(Control? element)
        {
            while (element != null)
            {
                if (element is TabItem tabItem) return tabItem;
                element = element.Parent as Control;
            }
            return null;
        }

        #endregion

        protected override void OnClosed(EventArgs e)
        {
            EditorTaskManager.TasksChanged -= UpdateUniversalProgressBar;
            base.OnClosed(e);
        }
    }
}
