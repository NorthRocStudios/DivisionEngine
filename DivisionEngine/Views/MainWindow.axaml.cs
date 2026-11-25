//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE file in the project root
// for full license terms.
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
using DivisionEngine.Editor.Windowing;
using DivisionEngine.MathLib;
using DivisionEngine.MathUtilities;
using DivisionEngine.Projects;
using DivisionEngine.Settings;
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
        // Bottom nav bar vars
        private static Flyout? tasksFlyout;

        // Docking
        private DockLayout? dockLayout;
        private DockSurface? mainSurface;
        private FloatingWindowManager? floatingManager;
        private bool dockAttached;
        private bool skipNextProjectLayoutLoad;
        private bool mainWindowClosed;

        /// <summary>
        /// Initializes a new instance of the <see cref="MainWindow"/> class.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent(); // Initialize the main window components
            if (DataContext is MainWindowViewModel vm) vm.RequestClose = Close;
            SetupViewMenu();
            SetupUniversalProgressBar(); // Build editor task system UI
            SetupPlayControls();
            SubscribeToEngineEvents();
            UpdatePlayControlsUI();
            SubscribeToProjectEvents();
        }

        /// <inheritdoc/>
        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            AttachDockWorkspace();
        }

        /// <summary>
        /// Binds the dock host, the floating window manager and the main surface to the
        /// view model's layout. Runs once the DataContext is assigned (the view model is
        /// created after the window in the application startup path).
        /// </summary>
        private void AttachDockWorkspace()
        {
            if (dockAttached || DataContext is not MainWindowViewModel vm) return;

            dockAttached = true;
            dockLayout = vm.Layout;
            floatingManager = new FloatingWindowManager(this, dockLayout);
            mainSurface = new DockSurface(this, EditorDockHost, dockLayout, null, floatingManager);
            EditorDockHost.Attach(mainSurface);
            mainSurface.Register();
            floatingManager.Attach();
        }

        private void SubscribeToProjectEvents()
        {
            ProjectManager.ProjectLoaded += OnProjectLoaded;
            ProjectManager.ProjectClosing += OnProjectClosing;
        }

        /// <summary>
        /// Fills the View menu from the shared window catalog. Regular editor windows
        /// are listed directly; example windows are grouped under an Examples submenu
        /// so they are clearly demonstrations rather than production tools.
        /// </summary>
        private void SetupViewMenu()
        {
            ViewMenu.Items.Clear();

            foreach (EditorWindowCatalogEntry entry in EditorWindowCatalog.Entries)
            {
                if (entry.IsExample) continue;
                ViewMenu.Items.Add(CreateWindowMenuItem(entry));
            }

            List<EditorWindowCatalogEntry> examples = EditorWindowCatalog.Entries
                .Where(entry => entry.IsExample)
                .ToList();
            if (examples.Count == 0) return;

            ViewMenu.Items.Add(new Separator());
            MenuItem examplesMenu = new()
            {
                Header = CreateWindowMenuHeader("Examples", MaterialIconKind.FlaskOutline),
                Foreground = EditorColor.FromRGB(200, 200, 200),
            };
            foreach (EditorWindowCatalogEntry example in examples)
                examplesMenu.Items.Add(CreateWindowMenuItem(example));
            ViewMenu.Items.Add(examplesMenu);
        }

        private MenuItem CreateWindowMenuItem(EditorWindowCatalogEntry entry)
        {
            MenuItem item = new()
            {
                Header = CreateWindowMenuHeader(entry.Title, entry.Icon),
                Foreground = EditorColor.FromRGB(200, 200, 200),
            };
            item.Click += (_, _) => OpenOrFocusWindow(entry);
            return item;
        }

        private static StackPanel CreateWindowMenuHeader(string title, MaterialIconKind icon)
        {
            StackPanel header = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
            header.Children.Add(new MaterialIcon
            {
                Kind = icon,
                Width = 14,
                Height = 14,
                Foreground = EditorColor.FromRGB(180, 180, 180),
                VerticalAlignment = VerticalAlignment.Center,
            });
            header.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 12,
                Foreground = EditorColor.FromRGB(200, 200, 200),
                VerticalAlignment = VerticalAlignment.Center,
            });
            return header;
        }

        /// <summary>
        /// Opens a window from the View menu. If that window type is already open,
        /// focuses the existing tab instead of creating a duplicate.
        /// </summary>
        private void OpenOrFocusWindow(EditorWindowCatalogEntry entry)
        {
            if (DataContext is not MainWindowViewModel vm) return;

            EditorWindowViewModel? existing = vm.Layout.AllTabs
                .FirstOrDefault(window => entry.ViewModelType.IsInstanceOfType(window));

            if (existing is not null)
            {
                vm.FocusTab(existing);
                ActivateGroup(vm.Layout.FindGroup(existing));
                return;
            }

            if (EditorWindowCatalog.Create(entry.ViewModelType) is not { } window) return;
            vm.OpenWindow(window, entry.DefaultZone);
            ActivateGroup(vm.Layout.FindGroup(window));
        }

        /// <summary>Brings the main window or the floating window that owns a group to the front.</summary>
        private void ActivateGroup(DockGroupNode? group)
        {
            if (group == null) return;
            if (mainSurface?.Host.ContainsGroup(group) == true) mainSurface.Activate();
            else floatingManager?.WindowForGroup(group)?.Activate();
        }

        private void OnProjectLoaded()
        {
            // Creating a project via Save As already displays the layout that was just
            // written to it; reloading it would rebuild every editor window for nothing.
            if (skipNextProjectLayoutLoad)
            {
                skipNextProjectLayoutLoad = false;
                return;
            }

            Dispatcher.UIThread.Post(async () => await LoadEditorLayoutFromProjectAsync());
        }

        /// <summary>
        /// Skips the next automatic project layout load. Used by Save As, where the
        /// current layout is saved into the new project and already on screen.
        /// </summary>
        public void SkipNextProjectLayoutLoad() => skipNextProjectLayoutLoad = true;

        /// <summary>Cancels a pending <see cref="SkipNextProjectLayoutLoad"/> when project creation failed.</summary>
        public void CancelSkipNextProjectLayoutLoad() => skipNextProjectLayoutLoad = false;

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
            // OnClosing captures the layout before the dock visuals are disposed. Any
            // later save (project close during application shutdown) must not recapture
            // from a torn-down window.
            if (mainWindowClosed) return;
            if (DataContext is not MainWindowViewModel vm || ProjectManager.CurrentProjectData == null) return;

            try
            {
                // Capture live floating-window positions before serializing.
                floatingManager?.CaptureBoundsToLayout();
                DockLayoutSerialization.Capture(ProjectManager.CurrentProjectData.EditorLayout, vm.Layout);
            }
            catch (Exception ex)
            {
                Debug.Error($"Failed to save editor layout: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads the editor layout from the loaded project data. Projects from before the
        /// dockable layout are converted from their four-panel representation.
        /// </summary>
        public Task LoadEditorLayoutFromProjectAsync()
        {
            if (DataContext is not MainWindowViewModel vm || ProjectManager.CurrentProjectData == null)
                return Task.CompletedTask;

            try
            {
                DockLayoutSerialization.Apply(ProjectManager.CurrentProjectData.EditorLayout, vm.Layout);
            }
            catch (Exception ex)
            {
                Debug.Error($"Failed to load editor layout: {ex.Message}");
            }
            return Task.CompletedTask;
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

            if (VoxelToggle != null)
            {
                VoxelToggle.IsChecked = EngineSettings.Instance.VoxelSdfRendering;
                VoxelToggle.IsCheckedChanged += (_, _) =>
                {
                    EngineSettings.Instance.VoxelSdfRendering = VoxelToggle.IsChecked ?? false;
                    UpdateVoxelToggleUI();
                };
                EngineSettings.VoxelRenderingChanged += () => Dispatcher.UIThread.Post(UpdateVoxelToggleUI);
                UpdateVoxelToggleUI();
            }
        }

        /// <summary>
        /// Reflects the live voxel/exact renderer choice in the toolbar toggle.
        /// </summary>
        private void UpdateVoxelToggleUI()
        {
            bool enabled = EngineSettings.Instance.VoxelSdfRendering;
            if (VoxelToggle != null && VoxelToggle.IsChecked != enabled)
                VoxelToggle.IsChecked = enabled;

            bool on = enabled;
            if (VoxelText != null)
                VoxelText.Foreground = on ? EditorColor.FromRGB(180, 255, 60) : EditorColor.FromRGB(136, 136, 136);
            if (VoxelIcon != null)
                VoxelIcon.Foreground = on ? EditorColor.FromRGB(180, 255, 60) : EditorColor.FromRGB(136, 136, 136);
            if (VoxelToggle != null)
                VoxelToggle.BorderBrush = on ? EditorColor.FromRGB(120, 180, 30) : EditorColor.FromRGB(17, 17, 17);
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
            if (EngineCore.IsInPlayMode && EngineCore.IsPaused) EngineCore.RunFrame(step: true);
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
                Content = "Ã—",
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

        /// <inheritdoc/>
        protected override void OnClosing(WindowClosingEventArgs e)
        {
            if (ProjectManager.IsCurrentLoaded) SaveEditorLayoutToProject();
            floatingManager?.PrepareForShutdown();
            base.OnClosing(e);
        }

        /// <inheritdoc/>
        protected override void OnClosed(EventArgs e)
        {
            mainWindowClosed = true;
            EditorTaskManager.TasksChanged -= UpdateUniversalProgressBar;
            floatingManager?.Detach();
            mainSurface?.Unregister();
            EditorDockHost.Dispose();
            base.OnClosed(e);
        }
    }
}
