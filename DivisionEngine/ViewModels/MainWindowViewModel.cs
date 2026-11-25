//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE.txt file in the project root
// for full license terms.
//
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using DivisionEngine.Editor.Settings;
using DivisionEngine.Editor.Systems;
using DivisionEngine.Editor.Tasks;
using DivisionEngine.Editor.Undo;
using DivisionEngine.Editor.Windowing;
using DivisionEngine.Projects;
using DivisionEngine.Settings;
using DivisionEngine.Systems;
using Material.Icons.Avalonia;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace DivisionEngine.Editor.ViewModels
{
    /// <summary>
    /// The view model for the Division Engine editor's parent window.
    /// </summary>
    public partial class MainWindowViewModel : ViewModelBase
    {
        // Window storage
        public static MainWindowViewModel? vm;
        private readonly Window mainWindow;

        // Main window menu bindings
        public Action? RequestClose { get; set; }
        public static bool CanUndo => UndoManager.CanUndo;
        public static bool CanRedo => UndoManager.CanRedo;
        public static bool LayoutsEnabled => ProjectManager.IsCurrentLoaded;

        // Dockable editor window layout (tab groups, splits and floating groups)
        public DockLayout Layout { get; } = new();

        // Recent projects
        private ObservableCollection<string> recentProjects = [];
        public ObservableCollection<string> RecentProjects
        {
            get => recentProjects;
            set => this.RaiseAndSetIfChanged(ref recentProjects, value);
        }
        private ObservableCollection<object> recentProjectMenuItems = [];
        public ObservableCollection<object> RecentProjectMenuItems
        {
            get => recentProjectMenuItems;
            set => this.RaiseAndSetIfChanged(ref recentProjectMenuItems, value);
        }

        public bool HasRecentProjects => RecentProjects.Count > 0;

        // Main Window API

        private string? recentControlsText;

        /// <summary>
        /// States what the recent controls text should be.
        /// </summary>
        public string? RecentControlsText
        {
            get => recentControlsText;
            set => this.RaiseAndSetIfChanged(ref recentControlsText, value);
        }

        /// <summary>
        /// Builds the main window view model and initializes default tabs.
        /// </summary>
        public MainWindowViewModel(Window mainWindow)
        {
            this.mainWindow = mainWindow;
            vm = this;

            // The startup layout mirrors the classic four-panel editor. Project load
            // replaces it with the project's saved dock tree.
            Layout.Load(DockLayoutSerialization.BuildLegacy(CreateStartupLayoutData()));

            LoadRecentProjects(); // Load recent projects

            // Undo setup
            UndoManager.UndoStackChanged += OnUndoStackChanged;
            UndoManager.UndoStateRestored += OnUndoStateRestored;
            ProjectManager.ProjectLoaded += ProjectStateChanged;
            ProjectManager.ProjectClosed += ProjectStateChanged;
            EngineCore.PlayModeChanged += OnPlayModeChanged;
        }

        /// <summary>
        /// Drops undo history and selection when the world is replaced (play mode restores a
        /// snapshot on stop, project transitions create a new world). Entity IDs are world
        /// local, so commands and selections from the previous world must not carry over.
        /// </summary>
        private static void ResetEditorStateForNewWorld()
        {
            UndoManager.Clear();
            Selection.Clear();
        }

        private static void OnPlayModeChanged(bool inPlayMode)
        {
            // Entering play mode leaves the edited world intact; leaving it restores the
            // pre-play snapshot, which replaces every component instance.
            if (!inPlayMode) ResetEditorStateForNewWorld();
        }

        private void ProjectStateChanged() => this.RaisePropertyChanged(nameof(LayoutsEnabled));

        private void OnUndoStackChanged()
        {
            // Update menu item enabled states. Do NOT rebuild the properties panel here:
            // this also runs for every live field edit (slider drags, typing in number
            // boxes), and rebuilding would take focus away from the control being edited.
            this.RaisePropertyChanged(nameof(CanUndo));
            this.RaisePropertyChanged(nameof(CanRedo));
        }

        private static void OnUndoStateRestored() => RefreshEditorUI();

        private static void RefreshEditorUI()
        {
            if (Selection.SelectedType == SelectionType.Entity && Selection.Entity != uint.MaxValue)
            {
                // If the entity no longer exists, clear the selection
                if (WorldManager.CurrentWorld == null || !WorldManager.CurrentWorld.EntityExists(Selection.Entity)) Selection.Clear();
                else PropertiesWindow.LoadEntityComponents(Selection.Entity);
            }
            else if (Selection.SelectedType == SelectionType.Asset && !string.IsNullOrEmpty(Selection.Asset))
            {
                // Refresh asset properties
                // PropertiesWindow.LoadAssetProperties(Selection.Asset);
            }
            else PropertiesWindow.LoadWorldData(WorldManager.CurrentWorld);
        }

        private void LoadRecentProjects()
        {
            EditorSettings settings = EditorSettings.Instance;
            RecentProjects = new ObservableCollection<string>(settings.RecentProjects);
            this.RaisePropertyChanged(nameof(HasRecentProjects));
            UpdateRecentProjectMenuItems();

            Debug.Info($"Loaded {RecentProjects.Count} recent projects");
        }

        private void AddToRecentProjects(string path)
        {
            EditorSettings.Instance.AddRecentProject(path);
            SettingsManager.SaveSettings(EditorSettings.Instance);
            LoadRecentProjects(); // Reload to update the collection
        }

        private void UpdateRecentProjectMenuItems()
        {
            ObservableCollection<object> items = [];

            // Add project items
            foreach (string project in RecentProjects)
            {
                MenuItem menuItem = new MenuItem
                {
                    Foreground = Brushes.White,
                    Command = OpenRecentProjectCommand,
                    CommandParameter = project,
                    Header = CreateRecentProjectHeader(project),
                };
                items.Add(menuItem);
            }

            // Add separator and clear all if there are items
            if (items.Count > 0)
            {
                items.Add(new Separator());
                MenuItem clearMenuItem = new MenuItem
                {
                    Foreground = Brushes.White,
                    Command = ClearRecentProjectsCommand,
                    Header = CreateClearAllHeader(),
                };
                items.Add(clearMenuItem);
            }
            RecentProjectMenuItems = items;
        }

        private static StackPanel CreateRecentProjectHeader(string projectPath)
        {
            StackPanel displayPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(2, 2, 10, 2),
            };
            displayPanel.Children.Add(new MaterialIcon
            {
                Kind = Material.Icons.MaterialIconKind.Folder,
                Width = 16,
                Height = 16,
                Foreground = EditorColor.FromRGB(200, 200, 200),
                VerticalAlignment = VerticalAlignment.Center,
            });

            StackPanel textPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 0,
            };
            textPanel.Children.Add(new TextBlock
            {
                Text = Path.GetFileName(projectPath),
                FontWeight = FontWeight.SemiBold,
                FontSize = 12,
                Foreground = Brushes.White,
            });
            textPanel.Children.Add(new TextBlock
            {
                Text = Path.GetDirectoryName(projectPath) ?? "",
                FontSize = 10,
                Foreground = Brushes.Gray,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 300,
            });

            displayPanel.Children.Add(textPanel);
            return displayPanel;
        }

        private static StackPanel CreateClearAllHeader()
        {
            StackPanel panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
            };
            panel.Children.Add(new MaterialIcon
            {
                Kind = Material.Icons.MaterialIconKind.Delete,
                Width = 14,
                Height = 14,
                Foreground = EditorColor.FromRGB(200, 128, 128),
            });
            panel.Children.Add(new TextBlock
            {
                Text = "Clear All",
                Foreground = Brushes.White,
            });
            return panel;
        }

        [RelayCommand]
        private async Task NewProject()
        {
            try
            {
                ConfirmationDialog confirmDialog = new ConfirmationDialog("New Project",
                        $"Would you like to create a new project?");
                if (await confirmDialog.ShowDialog<bool>(mainWindow))
                {
                    ProjectManager.CloseProject();
                    WorldManager.CreateDefaultWorld(true);
                    ResetEditorStateForNewWorld();
                }
            }
            catch (Exception ex)
            {
                Debug.Error($"Error opening project", ex);
            }
        }

        [RelayCommand]
        private async Task OpenProject()
        {
            try
            {
                var suggestedLocation = await GetSuggestedProjectLocation(); // Get the starting folder
                var result = await mainWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Open Project Folder",
                    AllowMultiple = false,
                    SuggestedStartLocation = suggestedLocation
                });

                if (result.Count > 0 && !string.IsNullOrEmpty(result[0].Path.LocalPath))
                {
                    string projectPath = result[0].Path.LocalPath;
                    projectPath = Path.TrimEndingDirectorySeparator(projectPath);
                    await OpenProjectAtPath(projectPath);
                }
            }
            catch (Exception ex)
            {
                Debug.Error($"Error opening project", ex);
            }
        }

        /// <summary>
        /// Determines the best starting folder for the open project dialog.
        /// </summary>
        private async Task<IStorageFolder?> GetSuggestedProjectLocation()
        {
            try
            {
                // Use the most recent project's folder (if it exists)
                if (RecentProjects.Count > 0)
                {
                    string mostRecentProject = RecentProjects[0];
                    string? parentDirectory = Path.GetDirectoryName(mostRecentProject);
                    if (!string.IsNullOrEmpty(parentDirectory) && Directory.Exists(parentDirectory))
                    {
                        var parentFolder = await mainWindow.StorageProvider.TryGetFolderFromPathAsync(parentDirectory);
                        if (parentFolder != null)
                        {
                            Debug.Info($"Using parent folder of most recent project: {parentDirectory}");
                            return parentFolder;
                        }
                    }
                }

                // Fallback to Documents folder
                var documents = await mainWindow.StorageProvider.TryGetWellKnownFolderAsync(WellKnownFolder.Documents);
                if (documents != null)
                {
                    Debug.Info("Using Documents folder as fallback");
                    return documents;
                }
            }
            catch (Exception ex)
            {
                Debug.Error($"Error determining suggested project location", ex);
            }

            return null;
        }

        [RelayCommand]
        private async Task OpenRecentProject(string path)
        {
            try
            {
                await OpenProjectAtPath(path);
            }
            catch (Exception ex)
            {
                Debug.Error($"Error opening recent project: {path}", ex);
            }
        }

        private async Task OpenProjectAtPath(string projectPath)
        {
            if (ProjectManager.IsDivisionProject(projectPath)) // Check if this is a valid project directory
            {
                bool success = ProjectManager.LoadProject(projectPath);
                if (success) // Check if loaded project
                {
                    // The new project has its own world and entity IDs: drop the previous
                    // project's undo history and selection before anything can act on them.
                    ResetEditorStateForNewWorld();
                    AddToRecentProjects(projectPath);
                    AssetsWindow.LoadAssetsForCurrentProject();
                }
                else Debug.Error($"Failed to load project: {projectPath}");
            }
            else Debug.Info("Selected folder is not a valid Division Engine project");
        }

        [RelayCommand]
        private void ClearRecentProjects()
        {
            EditorSettings.Instance.ClearRecentProjects();
            SettingsManager.SaveSettings(EditorSettings.Instance); // Save immediately 
            LoadRecentProjects(); // Reload in ViewModel
            this.RaisePropertyChanged(nameof(HasRecentProjects)); // Force UI update
        }

        [RelayCommand]
        private async Task SaveProject()
        {
            try
            {
                if (ProjectManager.IsCurrentLoaded)
                {
                    // Keep the dock tree and floating-window bounds in step with the
                    // project file when the user saves manually, not only on close.
                    if (mainWindow is MainWindow editorWindow) editorWindow.SaveEditorLayoutToProject();
                    ProjectManager.SaveCurrentProject();
                }
                else await SaveProjectAs();
            }
            catch (Exception ex)
            {
                Debug.Error($"Error saving project", ex);
            }
        }

        [RelayCommand]
        private async Task SaveProjectAs()
        {
            EditorTask t = EditorTaskManager.Create("Saving Project", "Creating new project...", 0f);
            try
            {
                EditorTaskManager.Update(t.Id, 0.25f);

                // Get project name
                ProjectNameDialog projectNameDialog = new ProjectNameDialog();
                string? projectName = await projectNameDialog.ShowDialog<string>(mainWindow);
                EditorTaskManager.Update(t.Id, 0.5f);

                if (string.IsNullOrWhiteSpace(projectName))
                {
                    EditorTaskManager.Complete(t.Id);
                    return;
                }

                // Get suggested location for saving
                var suggestedLocation = await GetSuggestedProjectLocation();

                // Choose folder location
                var folderResult = await mainWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Select Project Location",
                    AllowMultiple = false,
                    SuggestedStartLocation = suggestedLocation
                });
                EditorTaskManager.Update(t.Id, 0.75f);

                if (folderResult.Count == 0 || string.IsNullOrEmpty(folderResult[0].Path.LocalPath))
                {
                    EditorTaskManager.Complete(t.Id);
                    return;
                }

                string selectedFolder = folderResult[0].Path.LocalPath;
                string projectPath = Path.Combine(selectedFolder, projectName);

                // Check if folder already exists
                if (Directory.Exists(projectPath) && Directory.GetFiles(projectPath, "*.divp").Length > 0)
                {
                    ConfirmationDialog confirmDialog = new ConfirmationDialog("Project Exists",
                        $"A project named '{projectName}' already exists at this location. Overwrite?");
                    bool overwrite = await confirmDialog.ShowDialog<bool>(mainWindow);
                    EditorTaskManager.Update(t.Id, 1f);
                    if (!overwrite)
                    {
                        EditorTaskManager.Complete(t.Id);
                        return;
                    }
                }

                // SaveNewProject raises ProjectLoaded; the current layout is already the
                // one we want, so skip the automatic reload and write it into the new
                // project instead (rather than rebuilding every editor window).
                if (mainWindow is MainWindow layoutWindow) layoutWindow.SkipNextProjectLayoutLoad();

                // Save project
                bool success = ProjectManager.SaveNewProject(projectName, projectPath);
                if (success)
                {
                    // SaveNewProject writes a default layout; replace it with the layout
                    // the user is currently looking at.
                    if (mainWindow is MainWindow editorWindow)
                    {
                        editorWindow.SaveEditorLayoutToProject();
                        ProjectManager.SaveCurrentProject();
                    }
                    AddToRecentProjects(projectPath);
                    AssetsWindow.LoadAssetsForCurrentProject();
                }
                else
                {
                    if (mainWindow is MainWindow failedWindow) failedWindow.CancelSkipNextProjectLayoutLoad();
                    Debug.Error("Failed to save project");
                }
                EditorTaskManager.Complete(t.Id);
            }
            catch (Exception ex)
            {
                Debug.Error($"Error saving project", ex);
                EditorTaskManager.Complete(t.Id);
            }
        }

        [RelayCommand]
        private void Exit()
        {
            Debug.Info("Exiting Division Engine Editor");
            RequestClose?.Invoke();
        }

        [RelayCommand]
        private static void Undo() => UndoManager.Undo();

        [RelayCommand]
        private static void Redo() => UndoManager.Redo();

        [RelayCommand]
        private static void ReloadTextures() => TextureSystem.MarkDirty();

        [RelayCommand]
        private static void ReloadAllShaders() => ShaderReload.Run();

        [RelayCommand]
        private static void About()
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/DivisionEngine/DivisionEngine",
                UseShellExecute = true,
            });
        }

        [RelayCommand]
        private static void Roadmap()
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://trello.com/b/mWtyHBMf/division-engine",
                UseShellExecute = true,
            });
        }

        [RelayCommand]
        private static void Forum()
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/orgs/DivisionEngine/discussions",
                UseShellExecute = true,
            });
        }

        [RelayCommand]
        private static void NorthRoc()
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://northroc.org/",
                UseShellExecute = true,
            });
        }

        [RelayCommand]
        private static void OpenLogDirectory() => Debug.OpenLogDirectory();

        [RelayCommand]
        private static void OpenLogFile() => Debug.OpenLogFile();

        /// <summary>
        /// Creates an entity straight from the "Add" menu.
        /// </summary>
        /// <param name="entityType">Entity type to add (as camel case string key)</param>
        [RelayCommand]
        private static void CreateEntity(string entityType) => EditorUI.CreateEntityStatic(entityType);

        /// <summary>
        /// Opens a window in the first tab group of a logical zone, creating no new group.
        /// Used by the View menu and by the tab group plus buttons.
        /// </summary>
        public void OpenWindow(EditorWindowViewModel viewModel, string zone)
        {
            DockGroupNode? group = Layout.FindGroupByZone(zone);
            if (group == null) return;
            Layout.AddTab(group, viewModel);
            group.SelectedTab = viewModel;
        }

        /// <summary>Selects a tab wherever it currently lives (docked or floating).</summary>
        public void FocusTab(EditorWindowViewModel tab)
        {
            DockGroupNode? group = Layout.FindGroup(tab);
            if (group != null) group.SelectedTab = tab;
        }

        /// <summary>
        /// The layout the editor starts with before a project is loaded: the classic
        /// left/centre/right/bottom arrangement.
        /// </summary>
        private static EditorLayoutData CreateStartupLayoutData() => new()
        {
            LeftPanelWidth = 200,
            RightPanelWidth = 300,
            BottomPanelHeight = 250,
            LeftTabs = "WorldWindow",
            CenterTabs = "EnvironmentWindow",
            RightTabs = "PropertiesWindow,SettingsWindow",
            BottomTabs = "AssetsWindow,ConsoleWindow",
            SelectedLeftTab = "WorldWindow",
            SelectedCenterTab = "EnvironmentWindow",
            SelectedRightTab = "PropertiesWindow",
            SelectedBottomTab = "AssetsWindow",
        };

        [RelayCommand]
        private async Task ApplyDefaultLayout()
        {
            EditorLayoutData layout = new EditorLayoutData
            {
                LeftPanelWidth = 200,
                RightPanelWidth = 300,
                BottomPanelHeight = 250,
                LeftTabs = "WorldWindow",
                RightTabs = "PropertiesWindow,SettingsWindow",
                CenterTabs = "EnvironmentWindow",
                BottomTabs = "AssetsWindow,ConsoleWindow,DeveloperWindow",
                SelectedLeftTab = "WorldWindow",
                SelectedRightTab = "PropertiesWindow",
                SelectedCenterTab = "EnvironmentWindow",
                SelectedBottomTab = "AssetsWindow"
            };
            await ApplyLayout(layout);
        }

        [RelayCommand]
        private async Task ApplyFocusEditorLayout()
        {
            // Focus on code/script editing
            EditorLayoutData layout = new EditorLayoutData
            {
                LeftPanelWidth = 250,
                RightPanelWidth = 350,
                BottomPanelHeight = 200,
                LeftTabs = "WorldWindow,AssetsWindow",
                RightTabs = "PropertiesWindow",
                CenterTabs = "EnvironmentWindow",
                BottomTabs = "ConsoleWindow,DeveloperWindow",
                SelectedLeftTab = "AssetsWindow",
                SelectedRightTab = "PropertiesWindow",
                SelectedCenterTab = "EnvironmentWindow",
                SelectedBottomTab = "ConsoleWindow"
            };
            await ApplyLayout(layout);
        }

        [RelayCommand]
        private async Task ApplyFocusAssetsLayout()
        {
            // Focus on asset browsing and management
            EditorLayoutData layout = new EditorLayoutData
            {
                LeftPanelWidth = 150,
                RightPanelWidth = 400,
                BottomPanelHeight = 200,
                LeftTabs = "WorldWindow",
                RightTabs = "PropertiesWindow",
                CenterTabs = "AssetsWindow",
                BottomTabs = "ConsoleWindow,DeveloperWindow",
                SelectedLeftTab = "WorldWindow",
                SelectedRightTab = "PropertiesWindow",
                SelectedCenterTab = "AssetsWindow",
                SelectedBottomTab = "ConsoleWindow"
            };
            await ApplyLayout(layout);
        }

        [RelayCommand]
        private async Task ApplyWideViewportLayout()
        {
            // Maximize the viewport for scene editing
            EditorLayoutData layout = new EditorLayoutData
            {
                LeftPanelWidth = 180,
                RightPanelWidth = 250,
                BottomPanelHeight = 180,
                LeftTabs = "WorldWindow",
                RightTabs = "PropertiesWindow,SettingsWindow",
                CenterTabs = "EnvironmentWindow",
                BottomTabs = "AssetsWindow,ConsoleWindow",
                SelectedLeftTab = "WorldWindow",
                SelectedRightTab = "PropertiesWindow",
                SelectedCenterTab = "EnvironmentWindow",
                SelectedBottomTab = "AssetsWindow"
            };
            await ApplyLayout(layout);
        }

        [RelayCommand]
        private async Task ApplyMinimalLayout()
        {
            // Minimal UI for pure viewing
            EditorLayoutData layout = new EditorLayoutData
            {
                LeftPanelWidth = 0,
                RightPanelWidth = 0,
                BottomPanelHeight = 0,
                LeftTabs = "",
                RightTabs = "",
                CenterTabs = "EnvironmentWindow",
                BottomTabs = "",
                SelectedLeftTab = "",
                SelectedRightTab = "",
                SelectedCenterTab = "EnvironmentWindow",
                SelectedBottomTab = ""
            };
            await ApplyLayout(layout);
        }

        [RelayCommand]
        private async Task ApplyDebugLayout()
        {
            // Layout optimized for debugging (console and developer tools visible)
            EditorLayoutData layout = new EditorLayoutData
            {
                LeftPanelWidth = 200,
                RightPanelWidth = 350,
                BottomPanelHeight = 300,
                LeftTabs = "WorldWindow",
                RightTabs = "PropertiesWindow",
                CenterTabs = "EnvironmentWindow",
                BottomTabs = "ConsoleWindow,DeveloperWindow,AssetsWindow",
                SelectedLeftTab = "WorldWindow",
                SelectedRightTab = "PropertiesWindow",
                SelectedCenterTab = "EnvironmentWindow",
                SelectedBottomTab = "ConsoleWindow"
            };
            await ApplyLayout(layout);
        }

        /// <summary>
        /// Update the project's layout data.
        /// </summary>
        /// <param name="layout">New layout to switch to</param>
        /// <returns>Async layout change operation task</returns>
        private async Task ApplyLayout(EditorLayoutData layout)
        {
            if (ProjectManager.CurrentProjectData != null)
            {
                // A preset is described by the legacy fields, so make sure a previously
                // saved dock tree cannot take precedence over it.
                layout.DockTree = null;
                layout.FloatingGroups.Clear();
                ProjectManager.CurrentProjectData.EditorLayout = layout;
                if (mainWindow is MainWindow window) await window.LoadEditorLayoutFromProjectAsync();
            }
        }
    }
}
