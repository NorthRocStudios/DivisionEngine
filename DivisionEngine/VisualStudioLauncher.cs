//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;

namespace DivisionEngine.Editor
{
    /// <summary>
    /// Locates an installed Visual Studio and opens project solutions and
    /// source files, reusing a running IDE instance when one already has the
    /// solution open.
    /// </summary>
    public static class VisualStudioLauncher
    {
        private static string? cachedDevenvPath;

        #region Win32

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetShortPathName(string lpszLongPath, StringBuilder lpszShortPath, uint cchBuffer);

        /// <summary>
        /// Returns the 8.3 (MS-DOS) form of a path, or null if conversion fails.
        /// devenv's /Command parser splits on spaces regardless of quoting, so a path
        /// with spaces has to be shortened before being passed through /Command.
        /// </summary>
        private static string? GetShortPath(string longPath)
        {
            try
            {
                StringBuilder buffer = new(512);
                uint result = GetShortPathName(longPath, buffer, (uint)buffer.Capacity);
                if (result == 0 || result > buffer.Capacity) return null;
                return buffer.ToString();
            }
            catch
            {
                return null;
            }
        }

        #endregion

        /// <summary>
        /// Opens the project's .sln, optionally navigating to a specific file.
        /// Reuses an existing Visual Studio instance if one already has the
        /// solution open; otherwise launches a new one.
        /// </summary>
        /// <param name="projectDirectory">Directory containing the .sln.</param>
        /// <param name="fileToOpen">Optional absolute path to a file to open.</param>
        public static bool OpenSolution(string projectDirectory, string? fileToOpen = null)
        {
            if (string.IsNullOrWhiteSpace(projectDirectory)) return false;

            string? slnPath = FindSolutionFile(projectDirectory);
            if (slnPath == null)
            {
                Debug.Warning($"Visual Studio Launcher: no .sln found in '{projectDirectory}'");
                return false;
            }

            string? devenv = GetDevenvPath();

            // Try to reuse an existing instance that already has this solution open
            Process? existing = FindRunningInstance(slnPath);
            if (existing != null && devenv != null)
            {
                Debug.Info("Visual Studio Launcher: reusing running instance");
                FocusProcessWindow(existing);

                if (!string.IsNullOrEmpty(fileToOpen) && File.Exists(fileToOpen))
                    OpenFileInRunningInstance(devenv, fileToOpen);

                return true;
            }

            // No existing instance launch a new one
            if (devenv == null)
            {
                Debug.Warning("Visual Studio Launcher: no Visual Studio installation found; " +
                              "falling back to file association.");
                Process.Start(new ProcessStartInfo(slnPath) { UseShellExecute = true });
                return true;
            }

            return LaunchNewInstance(devenv, slnPath, fileToOpen, projectDirectory);
        }

		#region instanceDiscovery

		/// <summary>
		/// Scans running devenv.exe processes and returns the one whose command
		/// line references the given solution path. Returns null if none match.
		/// </summary>
		private static Process? FindRunningInstance(string slnPath)
        {
            try
            {
                string normalizedSln = Path.GetFullPath(slnPath);

                foreach (Process proc in Process.GetProcessesByName("devenv"))
                {
                    string? cmdLine = TryGetProcessCommandLine(proc.Id);
                    if (cmdLine == null) continue;

                    // VS records the solution path on the command line. Match on
                    // the full path (case-insensitive on Windows).
                    if (cmdLine.Contains(normalizedSln, StringComparison.OrdinalIgnoreCase))
                        return proc;
                }
            }
            catch (Exception ex)
            {
                Debug.Warning("Visual Studio Launcher: process scan failed", ex);
            }
            return null;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "<Pending>")]
        private static string? TryGetProcessCommandLine(int pid)
        {
            try
            {
                using ManagementObjectSearcher searcher = new(
                    $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
                foreach (ManagementObject obj in searcher.Get().Cast<ManagementObject>())
                {
                    string? cmdLine = obj["CommandLine"] as string;
                    if (!string.IsNullOrEmpty(cmdLine)) return cmdLine;
                }
            }
            catch (Exception ex)
            {
                Debug.Warning($"Visual Studio Launcher: WMI lookup failed for PID {pid}", ex);
            }
            return null;
        }

        private static void FocusProcessWindow(Process proc)
        {
            try
            {
                proc.Refresh();
                IntPtr hwnd = proc.MainWindowHandle;
                if (hwnd == IntPtr.Zero) return;

                ShowWindow(hwnd, SW_RESTORE); // un-minimize if needed
                SetForegroundWindow(hwnd);
            }
            catch (Exception ex)
            {
                Debug.Warning("Visual Studio Launcher: failed to focus instance", ex);
            }
        }

		#endregion
		#region fileOpening

		/// <summary>
		/// Sends a File.OpenFile command to the running VS instance. With the
		/// default "Reuse windows when launching solutions" setting, devenv.exe
		/// routes the command to the existing instance rather than starting a
		/// new one.
		/// </summary>
		private static void OpenFileInRunningInstance(string devenv, string filePath)
        {
            try
            {
                // devenv's /Command parser doesn't respect quoted arguments - it splits
                // on the first space and tries to open the truncated path, producing a
                // "Could not open <partial path>, operation failed" error. Using the 8.3
                // short form avoids spaces entirely
                string shortPath = GetShortPath(filePath) ?? filePath;

                if (shortPath.Contains(' '))
                {
                    // 8.3 name generation is disabled on this volume. We can't safely
                    // send the path through /Command, so just focus the running
                    // instance and let the user navigate to the file themselves
                    Debug.Warning($"Visual Studio Launcher: cannot shorten '{filePath}' - " +
                                  "skipping auto-open, focusing running instance instead.");
                    return;
                }

                ProcessStartInfo psi = new(devenv)
                {
                    Arguments = $"/Command \"File.OpenFile {shortPath}\"",
                    UseShellExecute = false,
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                Debug.Error("Visual Studio Launcher: failed to send open-file command", ex);
            }
        }

        private static bool LaunchNewInstance(string devenv, string slnPath,
            string? fileToOpen, string projectDirectory)
        {
            try
            {
                string arguments = $"\"{slnPath}\"";

                if (!string.IsNullOrEmpty(fileToOpen) && File.Exists(fileToOpen))
                {
                    string shortFile = GetShortPath(fileToOpen) ?? fileToOpen;
                    if (!shortFile.Contains(' '))
                        arguments += $" /Command \"File.OpenFile {shortFile}\"";
                }

                ProcessStartInfo psi = new(devenv)
                {
                    Arguments = arguments,
                    UseShellExecute = false,
                    WorkingDirectory = projectDirectory,
                };
                Process.Start(psi);
                Debug.Info($"Visual Studio Launcher: opened '{slnPath}' in a new instance");
                return true;
            }
            catch (Exception ex)
            {
                Debug.Error("Visual Studio Launcher: failed to launch devenv.exe", ex);
                return false;
            }
        }

		#endregion
		#region discovery

		private static string? FindSolutionFile(string projectDirectory)
        {
            string dirName = Path.GetFileName(
                Path.TrimEndingDirectorySeparator(projectDirectory));
            string preferred = Path.Combine(projectDirectory, $"{dirName}.sln");
            if (File.Exists(preferred)) return preferred;

            string[] candidates = Directory.GetFiles(projectDirectory, "*.sln",
                SearchOption.TopDirectoryOnly);
            return candidates.Length > 0 ? candidates[0] : null;
        }

        private static string? GetDevenvPath()
        {
            if (cachedDevenvPath != null) return cachedDevenvPath;

            cachedDevenvPath = TryRegistryAppPath()
                ?? TryVsWhere()
                ?? TryCommonPaths();
            return cachedDevenvPath;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "<Pending>")]
        private static string? TryRegistryAppPath()
        {
            try
            {
                using RegistryKey? key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\devenv.exe");
                if (key?.GetValue(null) is string machinePath && File.Exists(machinePath))
                    return machinePath;

                using RegistryKey? userKey = Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\devenv.exe");
                if (userKey?.GetValue(null) is string userPath && File.Exists(userPath))
                    return userPath;
            }
            catch (Exception ex)
            {
                Debug.Warning("Visual Studio Launcher: registry lookup failed", ex);
            }
            return null;
        }

        private static string? TryVsWhere()
        {
            try
            {
                string programFilesX86 = Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFilesX86);
                string vswhere = Path.Combine(programFilesX86,
                    @"Microsoft Visual Studio\Installer\vswhere.exe");
                if (!File.Exists(vswhere)) return null;

                ProcessStartInfo psi = new(vswhere)
                {
                    Arguments = "-latest -prerelease -property productPath",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using Process? proc = Process.Start(psi);
                if (proc == null) return null;

                string output = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit(5000);
                if (string.IsNullOrWhiteSpace(output)) return null;
                return File.Exists(output) ? output : null;
            }
            catch (Exception ex)
            {
                Debug.Warning("Visual Studio Launcher: vswhere lookup failed", ex);
                return null;
            }
        }

        private static string? TryCommonPaths()
        {
            string programFiles = Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFiles);
            string[] candidates =
            [
                Path.Combine(programFiles, @"Microsoft Visual Studio\2022\Enterprise\Common7\IDE\devenv.exe"),
                Path.Combine(programFiles, @"Microsoft Visual Studio\2022\Professional\Common7\IDE\devenv.exe"),
                Path.Combine(programFiles, @"Microsoft Visual Studio\2022\Community\Common7\IDE\devenv.exe"),
                Path.Combine(programFiles, @"Microsoft Visual Studio\2022\BuildTools\Common7\IDE\devenv.exe"),
            ];
            foreach (string path in candidates)
                if (File.Exists(path)) return path;
            return null;
        }

		#endregion
	}
}
