//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE file in the project root
// for full license terms.
//
using DivisionEngine.Input;
using DivisionEngine.Projects;
using DivisionEngine.Rendering;
using DivisionEngine.Settings;
using Silk.NET.Input;
using Silk.NET.Maths;

namespace DivisionEngine.Player;

/// <summary>
/// Represents the main entry point for the game application.
/// </summary>
public class GameStartup
{
    public static RenderPipeline? Renderer { get; private set; }
    public static InputSystem? UserInput { get; private set; }

    public const int DefaultEngineFrameTimeMS = 16; // Around 60 fps
    public const double DefaultRequestedFPS = 60;

    private static Task? engineCoreTask;
    private static CancellationTokenSource? engineCancellationTokenSource;

    /// <summary>
    /// The main entry point for the game.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    [STAThread]
    public static void Main(string[] args)
    {
        UserInput = new InputSystem();

        string projectPath = ParseCommandLineArgs(args); // Parse command line args
        LoadProjectOrDefaultWorld(projectPath);

        // Run engine loop
        engineCancellationTokenSource = new CancellationTokenSource();
        engineCoreTask = Task.Run(() => RunEngineLoop(DefaultEngineFrameTimeMS / 2, engineCancellationTokenSource.Token));

        // Run render pipeline
        Renderer = new RenderPipeline();
        Renderer.InputContextCreated += SetupInputHandlers; // Subscribe input handling at correct time!
        Renderer.Close += EngineCore.Stop; // Stop engine loop
        Renderer.Close += () => WorldManager.CurrentWorld?.CallAppExit(); // Invoke application exit callback

        Renderer.BindCurrentWorld(); // Bind loaded project
        Renderer.Run(DefaultRequestedFPS, false);

        // Cancel and stop engine loop
        engineCancellationTokenSource.Cancel();
        engineCoreTask?.Wait(1000);
        engineCancellationTokenSource.Dispose();
    }

    /// <summary>
    /// Parses command line arguments to extract project path.
    /// </summary>
    /// <param name="args">Command line arguments</param>
    /// <returns>Project path if found, empty string otherwise</returns>
    private static string ParseCommandLineArgs(string[] args)
    {
        // Supported project loading formats:
        // --project "C:\MyGame"
        // -p "C:\MyGame"  
        // "C:\MyGame" (just the path as first argument)

        for (int i = 0; i < args.Length; i++)
        {
            Debug.Info($"CMD line arg {i}: \"{args[i]}\"");
            string arg = args[i].ToLower();
            if ((arg == "--project" || arg == "-p") && i + 1 < args.Length)
                return args[i + 1];
            else if (i == 0 && Directory.Exists(args[i])) // First argument is a directory path
                return args[i];
            else if (i == 0 && File.Exists(args[i]) && args[i].EndsWith(".divp")) // First argument is a .divp file, get its directory
                return Path.GetDirectoryName(args[i])!;
        }
        return string.Empty;
    }

    /// <summary>
    /// Runs the main engine core loop.
    /// </summary>
    /// <param name="frameTime">Frame time the main loop runs at (usually 16ms for 60fps)</param>
    /// <param name="cancellationToken">Engine loop thread cancellation token</param>
    private static void RunEngineLoop(int frameTime, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                EngineCore.RunFrame();

                // Make sure engine will sleep by some frame time
                int engineSleep = MathUtilities.Math.RoundToInt(1f / EngineSettings.Instance.MaxFPS * 1000f);
                if (engineSleep > 0) Thread.Sleep(engineSleep);
                else Thread.Sleep(frameTime);
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Loads the project path provided or a default world if the path is empty into the current world.
    /// </summary>
    /// <param name="projectPath">Path to project</param>
    private static void LoadProjectOrDefaultWorld(string projectPath)
    {
        if (string.IsNullOrEmpty(projectPath)) WorldManager.CreateDefaultWorld(true);
        else
        {
            bool loadedProject = ProjectManager.LoadProject(projectPath);
            if (!loadedProject) WorldManager.CreateDefaultWorld(true);
        }
        WorldManager.CurrentWorld?.CallAppStart(); // Invoke application start callback

        // Enter play mode (for consistent state management even though this isnt the editor)
        EngineCore.EnterPlayMode();
    }

    /// <summary>
    /// Setup input handling for borderless Silk.NET threaded render GLFW window.
    /// </summary>
    private static void SetupInputHandlers(IInputContext input)
    {
        Debug.Info("Setup Input Handlers: Configuring input...");
        foreach (IKeyboard keyboard in input.Keyboards)
        {
            keyboard.KeyDown += (kb, key, code) => UserInput?.SetKeyDown(PlayerInput.SilkNetToKeyCode(key));
            keyboard.KeyUp += (kb, key, code) => UserInput?.SetKeyUp(PlayerInput.SilkNetToKeyCode(key));
        }

        foreach (IMouse mouse in input.Mice)
        {
            mouse.MouseDown += (m, code) => UserInput?.SetMouseKeyDown(PlayerInput.SilkNetToMouseCode(code));
            mouse.MouseUp += (m, code) => UserInput?.SetMouseKeyUp(PlayerInput.SilkNetToMouseCode(code));

            mouse.MouseMove += (m, pos) =>
            {
                float2 posConverted = new float2(pos.X, pos.Y);
                UserInput?.SetMousePosition(posConverted);

                if (Renderer == null || Renderer.RendererWindow == null) return;
                Vector2D<int> screenSizeInt = Renderer.RendererWindow.Size;
                float2 screenSize = new float2(screenSizeInt.X, screenSizeInt.Y);
                UserInput?.SetRelativeMousePosition(posConverted, screenSize);
            };

            mouse.Scroll += (m, wheel) =>
            {
                float2 posConverted = new float2(wheel.X, wheel.Y);
                UserInput?.SetMouseWheel(posConverted);
            };
        }
        Debug.Info("Setup Input Handlers: Input configured successfully");
    }
}