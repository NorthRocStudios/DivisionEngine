//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using System.Runtime.CompilerServices;

namespace DivisionEngine
{
    /// <summary>
    /// Represents the severity level of a log entry.
    /// </summary>
    public enum LogLevel
    {
        /// <summary>
        /// General info log.
        /// </summary>
        Info = 0,

        /// <summary>
        /// Debug log (default).
        /// </summary>
        Debug = 1,

        /// <summary>
        /// Warning log.
        /// </summary>
        /// <remarks>Can be from exception</remarks>
        Warning = 2,

        /// <summary>
        /// Error log.
        /// </summary>
        /// <remarks>Can be from exception</remarks>
        Error = 3,
    }

    /// <summary>
    /// Represents a single log entry in the debug log.
    /// </summary>
    public class LogEntry(
        string message,
        LogLevel level,
        [CallerFilePath] string filePath = "",
        [CallerMemberName] string methodName = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        /// <summary>
        /// Time log was created.
        /// </summary>
        public DateTime Timestamp { get; } = DateTime.Now;

        /// <summary>
        /// Log message.
        /// </summary>
        public string Message { get; } = message;

        /// <summary>
        /// Log level.
        /// </summary>
        public LogLevel Level { get; } = level;

        /// <summary>
        /// File path where the log was called from.
        /// </summary>
        public string? FilePath { get; } = filePath;

        /// <summary>
        /// Method name where the log was called from.
        /// </summary>
        public string? MethodName { get; } = methodName;

        /// <summary>
        /// Line number where the log was called from.
        /// </summary>
        public int LineNumber { get; } = lineNumber;

        /// <summary>
        /// Full caller info string (file:line method).
        /// </summary>
        public string CallerInfo =>
            string.IsNullOrEmpty(FilePath) ? string.Empty :
            $"{Path.GetFileName(FilePath)}:{LineNumber} {MethodName}()";

        /// <summary>
        /// Formats the log entry for file output.
        /// </summary>
        public string ToFileString() =>
            $"[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level}] {Message} ({CallerInfo})";

        public override string ToString() =>
            $"[{Level}] {Timestamp}: {Message} ({CallerInfo})";

        public override bool Equals(object? obj) =>
            obj is LogEntry entry && entry.Message == Message && entry.Level == Level;

        public override int GetHashCode() =>
            Message.GetHashCode() + Level.GetHashCode();
    }

    /// <summary>
    /// Debugging and logging utility for the Division Engine.
    /// </summary>
    public class Debug
    {
        private static readonly Debug instance = new Debug();
        private readonly List<LogEntry> debugLog = [];
        private readonly Lock logLock = new();
        private readonly Lock fileLock = new();
        private readonly string logFilePath;
        private readonly string logDirectoryPath;
        private const int MAX_LOG_FILE_SIZE_MB = 50; // Maximum size before rotating
        private const int MAX_LOG_FILES = 5; // Number of rotated log files to keep
        private const int MAX_LOG_ENTRIES = 5000; // In-memory history cap
        private bool headerWritten = false;

        /// <summary>
        /// Callback invoked when a new log entry is added.
        /// </summary>
        public static event Action<LogEntry>? OnLogUpdate;

        /// <summary>
        /// Snapshot of all logs stored in the debug system.
        /// </summary>
        /// <remarks>
        /// Returns a copy taken under the log lock: log entries are appended from the render
        /// thread and background tasks while the console enumerates this list on the UI thread.
        /// </remarks>
        public static IReadOnlyList<LogEntry> Logs
        {
            get
            {
                lock (instance.logLock) return [.. instance.debugLog];
            }
        }

        /// <summary>
        /// Whether file logging is enabled.
        /// </summary>
        public static bool FileLoggingEnabled { get; set; } = true;

        public Debug()
        {
            // Setup log file path in AppData
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string companyFolder = Path.Combine(appDataPath, "DivisionEngine");
            logDirectoryPath = Path.Combine(companyFolder, "Logs");

            if (!Directory.Exists(logDirectoryPath))
                Directory.CreateDirectory(logDirectoryPath);

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            logFilePath = Path.Combine(logDirectoryPath, $"Editor_{timestamp}.log");

            CleanupOldLogFiles();
            WriteHeader();

            debugLog.Add(new LogEntry("Debug system initialized.", LogLevel.Info));
        }

        /// <summary>
        /// Writes the log file header directly.
        /// </summary>
        private void WriteHeader()
        {
            if (!FileLoggingEnabled) return;

            lock (fileLock)
            {
                try
                {
                    // Ensure directory exists
                    if (!Directory.Exists(logDirectoryPath))
                        Directory.CreateDirectory(logDirectoryPath);

                    // Build header content
                    var headerLines = new[]
                    {
                        "========================================",
                        $"Division Engine Editor Log",
                        $"Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}",
                        $"Version: {System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0)}",
                        $"OS: {Environment.OSVersion}",
                        $"Runtime: {Environment.Version}",
                        $"64-bit OS: {Environment.Is64BitOperatingSystem}",
                        $"Processors: {Environment.ProcessorCount}",
                        $"Log File: {Path.GetFileName(logFilePath)}",
                        "========================================",
                        ""
                    };

                    // Write header directly to file
                    File.WriteAllText(logFilePath, string.Join(Environment.NewLine, headerLines) + Environment.NewLine);
                    headerWritten = true;

                    Console.WriteLine($"Log file created: {logFilePath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to write log header: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Cleans up old log files, keeping only the most recent ones.
        /// </summary>
        private void CleanupOldLogFiles()
        {
            try
            {
                if (!Directory.Exists(logDirectoryPath)) return;
                List<string> logFiles = [.. Directory.GetFiles(logDirectoryPath, "Editor_*.log").OrderByDescending(File.GetCreationTime)];

                // Remove old files beyond the limit
                for (int i = MAX_LOG_FILES; i < logFiles.Count; i++)
                {
                    try
                    {
                        File.Delete(logFiles[i]);
                        Console.WriteLine($"Deleted old log file: {Path.GetFileName(logFiles[i])}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to delete old log file: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to clean up old log files: {ex.Message}");
            }
        }

        /// <summary>
        /// Writes a string to the log file with automatic rotation.
        /// </summary>
        private void WriteToFile(string content)
        {
            if (!FileLoggingEnabled) return;

            lock (fileLock)
            {
                try
                {
                    // Check if need to rotate the log file
                    if (File.Exists(logFilePath))
                    {
                        FileInfo fileInfo = new FileInfo(logFilePath);
                        if (fileInfo.Length > MAX_LOG_FILE_SIZE_MB * 1024 * 1024) RotateLogFile();
                    }

                    if (!Directory.Exists(logDirectoryPath)) Directory.CreateDirectory(logDirectoryPath); // Ensure directory exists
                    if (!headerWritten && !File.Exists(logFilePath)) WriteHeader(); // Ensure header is written
                    File.AppendAllText(logFilePath, content + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    // If we can't write to the file, at least log to console
                    Console.WriteLine($"Failed to write to log file: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Rotates the log file when it gets too large.
        /// </summary>
        private void RotateLogFile()
        {
            try
            {
                if (!File.Exists(logFilePath)) return;

                string fileName = Path.GetFileNameWithoutExtension(logFilePath);
                string extension = Path.GetExtension(logFilePath);
                string directory = Path.GetDirectoryName(logFilePath)!;

                // Generate a new name for the rotated file
                string date = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string rotatedPath = Path.Combine(directory, $"{fileName}_rotated_{date}{extension}");

                // Move current log to rotated
                File.Move(logFilePath, rotatedPath);
                headerWritten = false; // Reset header flag so new header is written
                WriteHeader(); // Write new header

                // Add rotation notice
                WriteToFile($"Previous log file was rotated at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                WriteToFile($"Rotated file: {Path.GetFileName(rotatedPath)}");
                WriteToFile("");
                CleanupOldLogFiles(); // Clean up old rotated logs
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to rotate log file: {ex.Message}");
            }
        }

        /// <summary>
        /// Creates an info log entry.
        /// </summary>
        public static void Info(string message,
            [CallerFilePath] string filePath = "",
            [CallerMemberName] string methodName = "",
            [CallerLineNumber] int lineNumber = 0) =>
            Log(message, LogLevel.Info, filePath, methodName, lineNumber);

        /// <summary>
        /// Creates an error log entry.
        /// </summary>
        public static void Error(string message,
            [CallerFilePath] string filePath = "",
            [CallerMemberName] string methodName = "",
            [CallerLineNumber] int lineNumber = 0) =>
            Log(message, LogLevel.Error, filePath, methodName, lineNumber);

        /// <summary>
        /// Creates an error log entry with exception.
        /// </summary>
        public static void Error(string message, Exception ex,
            [CallerFilePath] string filePath = "",
            [CallerMemberName] string methodName = "",
            [CallerLineNumber] int lineNumber = 0) =>
            Log(message + $" | {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}",
                LogLevel.Error, filePath, methodName, lineNumber);

        /// <summary>
        /// Creates a warning log entry.
        /// </summary>
        public static void Warning(string message,
            [CallerFilePath] string filePath = "",
            [CallerMemberName] string methodName = "",
            [CallerLineNumber] int lineNumber = 0) =>
            Log(message, LogLevel.Warning, filePath, methodName, lineNumber);

        /// <summary>
        /// Creates a warning log entry with exception.
        /// </summary>
        public static void Warning(string message, Exception ex,
            [CallerFilePath] string filePath = "",
            [CallerMemberName] string methodName = "",
            [CallerLineNumber] int lineNumber = 0) =>
            Log(message + $" | {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}",
                LogLevel.Warning, filePath, methodName, lineNumber);

        /// <summary>
        /// Creates a debug log entry.
        /// </summary>
        public static void Log(string message, LogLevel level = LogLevel.Debug,
            [CallerFilePath] string filePath = "",
            [CallerMemberName] string methodName = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            string prefix = level switch
            {
                LogLevel.Debug => "[DEBUG]",
                LogLevel.Info => "[INFO]",
                LogLevel.Warning => "[WARNING]",
                LogLevel.Error => "[ERROR]",
                _ => "[LOG]"
            };

#if DEBUG
            if (System.Diagnostics.Debugger.IsAttached)
            {
                string callerInfo = string.IsNullOrEmpty(filePath) ? "" :
                    $"{Path.GetFileName(filePath)}:{lineNumber} {methodName}()";
                System.Diagnostics.Debug.WriteLine($"{prefix} {message} ({callerInfo})");
            }
#endif

            LogEntry entry = new LogEntry(message, level, filePath, methodName, lineNumber);
            Console.WriteLine(entry.ToString());

            // Write to file
            instance.WriteToFile(entry.ToFileString());

            // Add to in-memory log (locked: written from every thread, read by the UI)
            lock (instance.logLock)
            {
                instance.debugLog.Add(entry);
                if (instance.debugLog.Count > MAX_LOG_ENTRIES)
                    instance.debugLog.RemoveRange(0, instance.debugLog.Count - MAX_LOG_ENTRIES);
            }

            OnLogUpdate?.Invoke(entry);
        }

        /// <summary>
        /// Clears all debug logs.
        /// </summary>
        public static void ClearLogs()
        {
            lock (instance.logLock) instance.debugLog.Clear();
        }

        /// <summary>
        /// Removes a specific log entry.
        /// </summary>
        public static void ClearLog(LogEntry e)
        {
            lock (instance.logLock) instance.debugLog.Remove(e);
        }

        /// <summary>
        /// Gets the path to the current log file.
        /// </summary>
        public static string GetLogFilePath() => instance.logFilePath;

        /// <summary>
        /// Gets the log directory path.
        /// </summary>
        public static string GetLogDirectory() => instance.logDirectoryPath;

        /// <summary>
        /// Opens the log directory in the file explorer.
        /// </summary>
        public static void OpenLogDirectory()
        {
            string dir = GetLogDirectory();
            if (Directory.Exists(dir)) System.Diagnostics.Process.Start("explorer.exe", dir);
        }

        /// <summary>
        /// Opens the current log file in the default text editor.
        /// </summary>
        public static void OpenLogFile()
        {
            string path = GetLogFilePath();
            if (File.Exists(path))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
        }
    }
}
