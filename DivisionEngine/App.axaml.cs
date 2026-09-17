//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using DivisionEngine.Editor.Settings;
using DivisionEngine.Editor.ViewModels;
using DivisionEngine.Input;
using DivisionEngine.Projects;
using DivisionEngine.Projects.Scripting;
using DivisionEngine.Rendering;
using DivisionEngine.Settings;
using System;

namespace DivisionEngine.Editor
{
    /// <summary>
    /// Divsion Engine editor application backend.
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// Reference to the Division SDF render pipeline.
        /// </summary>
        public static RenderPipeline? Renderer { get; private set; }

        /// <summary>
        /// Current input system for the Division editor.
        /// </summary>
        public static InputSystem? UserInput { get; private set; }

        /// <summary>
        /// Current input system for the Division editor.
        /// </summary>
        public static Window? MainWindow { get; private set; }

        /// <summary>
        /// Whether the application is focused or not.
        /// </summary>
        public static event Action<bool>? AppFocused;

        /// <summary>
        /// Constant FPS ms Division maintains in the editor.
        /// </summary>
        public const long EngineCoreFrameTime = 16; // Around 60 fps

        /// <summary>
        /// Constant FPS rate Division maintains in the editor.
        /// </summary>
        public const double RequestedFPS = 60;

        /// <summary>
        /// Initializes the Avalonia UI base app.
        /// </summary>
        public override void Initialize() => AvaloniaXamlLoader.Load(this);

        public static void StartRenderer()
        {
            if (Renderer != null) return;

            Renderer = new RenderPipeline();
            Renderer.BindCurrentWorld();
            Renderer.RunEmbedded(RequestedFPS); // no window, no Silk.NET input context needed

            EnvironmentWindow.SyncToolValuesToRenderer();
        }

        /// <summary>
        /// Runs when Avalonia finishes initialization.
        /// </summary>
        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Install the Roslyn compiler so projects compile scripts as they
                // load. Doing this here (rather than in ProjectManager) keeps the
                // engine core free of a Roslyn dependency.
                ScriptCompilationPipeline.SetCompiler(new RoslynScriptCompiler());

                // Settings are automatically loaded when Instance is first accessed
                EditorSettings settings = EditorSettings.Instance;

                desktop.MainWindow = new MainWindow();
                MainWindowViewModel vm = new MainWindowViewModel(desktop.MainWindow);
                desktop.MainWindow.DataContext = vm;
                MainWindow = desktop.MainWindow;

                // Startup editor
                WorldManager.CreateDefaultWorld(true);
                WorldManager.CurrentWorld?.CallAppStart();
                StartEditorEngineLoop();
                UserInput = new InputSystem();
                StartRenderer(); // Start the SDFRenderer in a separate thread
                SetupAvaloniaInput(desktop);

                // Bind editor callbacks
                desktop.Exit += (s, e) =>
                {
                    Debug.Info($"Editor application exit with code: {e.ApplicationExitCode}");

                    WorldManager.CurrentWorld?.CallAppExit(); // Invoke application exit callback
                    SettingsManager.SaveSettings(settings); // Save editor settings on editor close
                    Renderer?.RendererWindow?.Close();

                    // Close project if open on exit
                    if (ProjectManager.IsCurrentLoaded) ProjectManager.CloseProject();
                };
                desktop.MainWindow.Activated += (_, _) => AppFocused!(true);
                desktop.MainWindow.Deactivated += (_, _) => AppFocused!(false);
                vm.RequestClose += () => desktop.Shutdown(); // Bind close request
            }

            base.OnFrameworkInitializationCompleted();
        }

        /// <summary>
        /// Runs the ECS main loop in the editor, referencing EngineCore.
        /// </summary>
        private void StartEditorEngineLoop()
        {
            // Create Avalonia editor integrated engine loop
            DispatcherTimer engineTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(EngineCoreFrameTime / 2)
            };
            engineTimer.Tick += EngineTimer_Tick;
            engineTimer.Start();
        }

        /// <summary>
        /// Runs each frame of the core engine in the editor.
        /// </summary>
        /// <param name="sender">Sender obj</param>
        /// <param name="e">Event args</param>
        private void EngineTimer_Tick(object? sender, EventArgs e)
        {
            WorldManager.CurrentWorld?.CallEditorUpdate(); // Only called here
            if (EngineCore.IsRunning && !EngineCore.IsPaused) EngineCore.RunFrame();
        }

        /// <summary>
        /// Sets up input handling for the Division Engine editor.
        /// </summary>
        /// <param name="desktop">The desktop application lifetime for Avalonia UI.</param>
        private static async void SetupAvaloniaInput(IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avalonia input handling
            desktop.MainWindow!.KeyUp += (s, e) => UserInput?.SetKeyUp(EditorInput.AvaloniaToKeyCode(e.Key));
            desktop.MainWindow.KeyDown += (s, e) => UserInput?.SetKeyDown(EditorInput.AvaloniaToKeyCode(e.Key));
        }
    }
}
