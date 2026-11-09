//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using System;
using Avalonia;

namespace DivisionEngine.Editor
{
    /// <summary>
    /// Startup class for the Division Engine Avalonia editor application.
    /// </summary>
    internal sealed class EditorProgram
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static int Main(string[] args)
        {
            // `--cli <command>` runs headless commands (benchmark, info) without opening the editor.
            if (DivisionCLI.TryRun(args)) return DivisionCLI.ExitCode;

            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}

