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
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using DivisionEngine.MathLib;
using DivisionEngine.MathUtilities;
using DivisionEngine.Projects.Scripting;
using Material.Icons;
using Material.Icons.Avalonia;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace DivisionEngine.Editor;

/// <summary>
/// Class that represents a console window in Division.
/// </summary>
public partial class ConsoleWindow : EditorWindow
{
    public const int MaxDisplayedLogEntries = 1000;

    /// <summary>
    /// Rendering style for the log list. Persisted statically across all
    /// console windows so closing and reopening keeps the same mode.
    /// </summary>
    public enum ConsoleView
    {
        /// <summary>
        /// Detailed cards with caller info, expandable multi-line messages.
        /// </summary>
        Verbose,
        /// <summary>
        /// Compact monospace lines, one per entry. Pairs with the command bar.
        /// </summary>
        Terminal,
    }

    // Display panels
    private readonly StackPanel logList;
    private readonly StackPanel controlsPanel;
    private readonly ScrollViewer scrollViewer;
    private readonly CheckBox autoscrollCheckbox;
    private readonly CheckBox collapseCheckbox;
    private readonly ComboBox filterLogTypeBox;
    private readonly Button clearButton;
    private readonly Button viewToggleButton;
    private readonly MaterialIcon viewToggleIcon;
    private readonly TextBox searchBox;
    private readonly MaterialIcon searchIcon;
    private readonly Border commandBar;
    private readonly TextBox commandInput;

    // State
    private bool autoScroll;
    private bool collapseEnabled;
    private string searchFilter = string.Empty;

    // Static so the view mode persists across console window instances
    private static ConsoleView currentView = ConsoleView.Verbose;

    private readonly Lock threadLock;

    // Command history for up/down navigation
    private readonly List<string> commandHistory = [];
    private int historyIndex = -1;

    /// <summary>
    /// Represents a group of identical log entries.
    /// </summary>
    private class GroupedLogEntry
    {
        public LogEntry FirstLog { get; set; } = null!;
        public int Count { get; set; } = 1;
        public Border? Control { get; set; }
    }

    private readonly Dictionary<string, GroupedLogEntry> groupedLogs = [];

    public ConsoleWindow()
    {
        InitializeComponent();

        autoScroll = true;
        collapseEnabled = false;
        threadLock = new Lock();

        clearButton = new Button
        {
            Content = "Clear",
            FontSize = 12,
            Height = 25,
            FontStretch = FontStretch.SemiExpanded,
            Background = EditorColor.FromRGB(17, 17, 17),
            Foreground = EditorColor.FromColor(ColorPalette.White),
            BorderBrush = EditorColor.FromRGB(28, 28, 28),
            BorderThickness = new Thickness(1, 1, 0, 0),
            Margin = new Thickness(4, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(clearButton, "Clear logs (script diagnostics are preserved)");
        clearButton.Click += ClearButton_Click;

        autoscrollCheckbox = new CheckBox
        {
            Content = "Auto Scroll",
            Foreground = Brushes.White,
            IsChecked = autoScroll,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
        autoscrollCheckbox.IsCheckedChanged += (_, _) => autoScroll = autoscrollCheckbox.IsChecked == true;

        collapseCheckbox = new CheckBox
        {
            Content = "Collapse",
            Foreground = Brushes.White,
            IsChecked = collapseEnabled,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
        collapseCheckbox.IsCheckedChanged += (_, _) =>
        {
            collapseEnabled = collapseCheckbox.IsChecked == true;
            ReloadLogs();
        };

        searchIcon = new MaterialIcon
        {
            Kind = MaterialIconKind.Search,
            Foreground = EditorColor.FromRGB(128, 128, 128),
            Margin = new Thickness(6, 0, 0, 0),
            Width = 12,
            Height = 12,
        };
        searchBox = new TextBox
        {
            InnerLeftContent = searchIcon,
            PlaceholderText = "Search Logs...",
            FontSize = 12,
            Foreground = EditorColor.FromRGB(220, 220, 220),
            Background = EditorColor.FromRGB(17, 17, 17),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            MinWidth = 150,
            Margin = new Thickness(8, 0, 0, 0),
        };
        searchBox.TextChanged += SearchBox_TextChanged;

        // Log type filter dropdown
        filterLogTypeBox = BuildFilterDropdown();
        filterLogTypeBox.SelectionChanged += (_, _) => ReloadLogs();

        // View mode toggle - initialized from the static field so the mode
        // is consistent regardless of which window instance is opening.
        viewToggleIcon = new MaterialIcon
        {
            Kind = currentView == ConsoleView.Verbose ? MaterialIconKind.Terminal : MaterialIconKind.ViewList,
            Width = 16,
            Height = 16,
            Foreground = EditorColor.FromRGB(200, 200, 200),
            VerticalAlignment = VerticalAlignment.Center,
        };
        viewToggleButton = new Button
        {
            Content = viewToggleIcon,
            Background = EditorColor.FromRGB(17, 17, 17),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 2),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(viewToggleButton, currentView == ConsoleView.Verbose
            ? "Switch to terminal view"
            : "Switch to verbose view");
        viewToggleButton.Click += (_, _) => ToggleView();

        logList = new StackPanel { Orientation = Orientation.Vertical };
        controlsPanel = new StackPanel
        {
            Background = EditorColor.FromRGB(28, 28, 28),
            Orientation = Orientation.Horizontal,
            Spacing = 0,
            Height = 30,
            VerticalAlignment = VerticalAlignment.Top,
        };
        scrollViewer = new ScrollViewer
        {
            Content = logList,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        controlsPanel.Children.Add(clearButton);
        controlsPanel.Children.Add(autoscrollCheckbox);
        controlsPanel.Children.Add(collapseCheckbox);
        controlsPanel.Children.Add(viewToggleButton);
        controlsPanel.Children.Add(searchBox);
        controlsPanel.Children.Add(filterLogTypeBox);

        commandBar = BuildCommandBar(out commandInput);
        commandBar.IsVisible = currentView == ConsoleView.Terminal;

        DockPanel mainPanel = new DockPanel { Background = EditorColor.FromRGB(45, 45, 45) };
        DockPanel.SetDock(controlsPanel, Dock.Top);
        DockPanel.SetDock(commandBar, Dock.Bottom);
        mainPanel.Children.Add(controlsPanel);
        mainPanel.Children.Add(commandBar);
        mainPanel.Children.Add(scrollViewer);

        AttachBackgroundContextMenu();

        // ---- Subscriptions ----------------------------------------------
        Debug.OnLogUpdate += Debug_OnLogUpdate;

        // We re-query the pipeline's LastResult on every render, so missing
        // this event while unloaded is harmless - but subscribing keeps the
        // view live while the window is open.
        ScriptCompilationPipeline.CompilationCompleted += OnCompilationCompleted;

        Unloaded += (_, _) =>
        {
            Debug.OnLogUpdate -= Debug_OnLogUpdate;
            ScriptCompilationPipeline.CompilationCompleted -= OnCompilationCompleted;
        };

        ReloadLogs();
        Border? border = this.FindControl<Border>("MainBorder");
        border?.Child = mainPanel;
    }

    #region viewMode

    private void ToggleView()
    {
        currentView = currentView == ConsoleView.Verbose
            ? ConsoleView.Terminal
            : ConsoleView.Verbose;

        viewToggleIcon.Kind = currentView == ConsoleView.Verbose
            ? MaterialIconKind.Terminal
            : MaterialIconKind.ViewList;

        ToolTip.SetTip(viewToggleButton, currentView == ConsoleView.Verbose
            ? "Switch to terminal view"
            : "Switch to verbose view");

        commandBar.IsVisible = currentView == ConsoleView.Terminal;

        ReloadLogs();

        if (currentView == ConsoleView.Terminal)
            Dispatcher.UIThread.Post(() => commandInput.Focus(), DispatcherPriority.Background);
    }

    #endregion
    #region commandBar

    private Border BuildCommandBar(out TextBox input)
    {
        TextBlock prompt = new()
        {
            Text = ">",
            FontFamily = new FontFamily("Consolas, Menlo, monospace"),
            FontSize = 13,
            FontWeight = FontWeight.Bold,
            Foreground = EditorColor.FromRGB(120, 200, 120),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 6, 0),
        };

        // Help icon with command list tooltip
        MaterialIcon helpIcon = new()
        {
            Kind = MaterialIconKind.HelpCircleOutline,
            Width = 14,
            Height = 14,
            Foreground = EditorColor.FromRGB(140, 140, 140),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
        };
        ToolTip.SetTip(helpIcon, BuildCommandsTooltip());
        ToolTip.SetPlacement(helpIcon, PlacementMode.Top);
        ToolTip.SetShowDelay(helpIcon, 200);

        input = new TextBox
        {
            FontFamily = new FontFamily("Consolas, Menlo, monospace"),
            FontSize = 12,
            Foreground = EditorColor.FromRGB(220, 220, 220),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        input.KeyDown += CommandInput_KeyDown;

        Border bar = new()
        {
            Background = EditorColor.FromRGB(14, 14, 14),
            BorderBrush = EditorColor.FromRGB(30, 30, 30),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Height = 30,
        };

        DockPanel layout = new();
        DockPanel.SetDock(prompt, Dock.Left);
        DockPanel.SetDock(helpIcon, Dock.Right);
        layout.Children.Add(prompt);
        layout.Children.Add(helpIcon);
        layout.Children.Add(input);
        bar.Child = layout;
        return bar;
    }

    private static Border BuildCommandsTooltip()
    {
        const string commands =
            "help ---------------------- Show this list\n" +
            "clear | cls --------------- Clear non-diagnostic logs\n" +
            "recompile | build --------- Recompile project scripts\n" +
            "filter <level> ------------ Set log filter (all|info|debug|warning|error)\n" +
            "search <text> ------------- Set search filter\n" +
            "count --------------------- Show log counts\n" +
            "diagnostics | diag -------- Show build diagnostic summary\n" +
            "view <mode> --------------- Switch view (verbose|terminal)\n" +
            "collapse <on|off> --------- Toggle collapse\n" +
            "autoscroll <on|off> ------- Toggle auto-scroll\n" +
            "log <open|dir> ------------ Open log file or directory";

        return new Border
        {
            Background = EditorColor.FromRGB(24, 24, 24),
            BorderBrush = EditorColor.FromRGB(80, 80, 80),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 6),
            Child = new TextBlock
            {
                Text = commands,
                FontFamily = new FontFamily("Consolas, Menlo, monospace"),
                FontSize = 11,
                Foreground = EditorColor.FromRGB(220, 220, 220),
            },
        };
    }

    private void CommandInput_KeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                ExecuteCommand(commandInput.Text ?? string.Empty);
                commandInput.Text = string.Empty;
                historyIndex = -1;
                e.Handled = true;
                break;

            case Key.Up:
                if (commandHistory.Count == 0) break;
                historyIndex = historyIndex < 0
                    ? commandHistory.Count - 1
                    : math.max(0, historyIndex - 1);
                commandInput.Text = commandHistory[historyIndex];
                commandInput.CaretIndex = commandInput.Text.Length;
                e.Handled = true;
                break;

            case Key.Down:
                if (historyIndex < 0) break;
                historyIndex++;
                if (historyIndex >= commandHistory.Count)
                {
                    historyIndex = -1;
                    commandInput.Text = string.Empty;
                }
                else
                {
                    commandInput.Text = commandHistory[historyIndex];
                }
                commandInput.CaretIndex = commandInput.Text.Length;
                e.Handled = true;
                break;

            case Key.Escape:
                commandInput.Text = string.Empty;
                historyIndex = -1;
                e.Handled = true;
                break;
        }
    }

    private void ExecuteCommand(string rawInput)
    {
        string input = rawInput.Trim();
        if (string.IsNullOrEmpty(input)) return;

        commandHistory.Add(input);
        if (commandHistory.Count > 100) commandHistory.RemoveAt(0);
        historyIndex = -1;

        Debug.Log($"> {input}", LogLevel.Info);

        string[] parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string verb = parts[0].ToLowerInvariant();
        string[] args = parts.Length > 1 ? parts[1..] : [];

        try
        {
            switch (verb)
            {
                case "help": CommandHelp(); break;
                case "clear": CommandClear(); break;
                case "cls": CommandClear(); break;
                case "recompile":
                case "build": CommandRecompile(); break;
                case "filter": CommandFilter(args); break;
                case "search": CommandSearch(args); break;
                case "count": CommandCount(); break;
                case "diagnostics":
                case "diag": CommandDiagnostics(); break;
                case "view": CommandView(args); break;
                case "collapse": CommandCollapse(args); break;
                case "autoscroll": CommandAutoscroll(args); break;
                case "log": CommandLog(args); break;
                case "open":
                case "open-log": Debug.OpenLogFile(); break;
                case "dir":
                case "open-dir": Debug.OpenLogDirectory(); break;
                default:
                    Debug.Warning($"Unknown command: '{verb}'. Type 'help' for a list.");
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.Error($"Command failed: {ex.Message}");
        }
    }

    private static void CommandHelp()
    {
        Debug.Info("Available commands:");
        Debug.Info("  help                        Show this list");
        Debug.Info("  clear | cls                 Clear non-diagnostic logs");
        Debug.Info("  recompile | build           Recompile project scripts");
        Debug.Info("  filter <all|info|debug|warning|error>   Set log filter");
        Debug.Info("  search <text>               Set search filter (empty to clear)");
        Debug.Info("  count                       Show total log count");
        Debug.Info("  diagnostics | diag          Show current build diagnostics");
        Debug.Info("  view <verbose|terminal>     Switch view mode");
        Debug.Info("  collapse <on|off>           Toggle collapse");
        Debug.Info("  autoscroll <on|off>         Toggle auto-scroll");
        Debug.Info("  log <open|dir>              Open log file or directory");
        Debug.Info("  exit | close                Close this console window");
    }

    private void CommandClear()
    {
        Debug.ClearLogs();
        ReloadLogs();
    }

    private static async void CommandRecompile()
    {
        Debug.Info("Triggering script recompile...");
        await ScriptCompilationPipeline.RefreshAndCompileAsync();
    }

    private void CommandFilter(string[] args)
    {
        if (args.Length == 0)
        {
            Debug.Warning("Usage: filter <all|info|debug|warning|error>");
            return;
        }
        int index = args[0].ToLowerInvariant() switch
        {
            "all" => 0,
            "info" => 1,
            "debug" => 2,
            "warning" or "warn" => 3,
            "error" => 4,
            _ => -1,
        };
        if (index < 0)
        {
            Debug.Warning($"Unknown filter level: '{args[0]}'");
            return;
        }
        filterLogTypeBox.SelectedIndex = index;
    }

    private void CommandSearch(string[] args) => searchBox.Text = args.Length == 0 ? string.Empty : string.Join(' ', args);

    private static void CommandCount()
    {
        int logCount = Debug.Logs.Count;
        int diagCount = ScriptCompilationPipeline.LastResult?.Diagnostics.Count(d => d.Severity != ScriptDiagnosticSeverity.Info) ?? 0;
        Debug.Info($"Logs: {logCount} | Diagnostics: {diagCount}");
    }

    private static void CommandDiagnostics()
    {
        ScriptCompileResult? last = ScriptCompilationPipeline.LastResult;
        if (last == null)
        {
            Debug.Info("No compile has run yet.");
            return;
        }

        int errors = last.Diagnostics.Count(d => d.Severity == ScriptDiagnosticSeverity.Error);
        int warnings = last.Diagnostics.Count(d => d.Severity == ScriptDiagnosticSeverity.Warning);

        if (errors == 0 && warnings == 0)
        {
            Debug.Info($"Last build: clean ({last.Duration.TotalMilliseconds:F0}ms)");
            return;
        }

        Debug.Info($"Last build: {errors} error(s), {warnings} warning(s)");
        foreach (ScriptDiagnostic diag in last.Diagnostics)
        {
            if (diag.Severity == ScriptDiagnosticSeverity.Info) continue;
            string location = string.IsNullOrEmpty(diag.FilePath)
                ? string.Empty
                : $"{Path.GetFileName(diag.FilePath)}({diag.Line},{diag.Column}): ";
            string level = diag.Severity.ToString().ToLowerInvariant();
            Debug.Info($"  {location}{level}: {diag.Message}");
        }
    }

    private void CommandView(string[] args)
    {
        if (args.Length == 0)
        {
            Debug.Warning("Usage: view <verbose|terminal>");
            return;
        }
        ConsoleView target = args[0].ToLowerInvariant() switch
        {
            "verbose" => ConsoleView.Verbose,
            "terminal" => ConsoleView.Terminal,
            _ => (ConsoleView)(-1),
        };
        if ((int)target == -1)
        {
            Debug.Warning($"Unknown view: '{args[0]}'");
            return;
        }
        if (target != currentView) ToggleView();
    }

    private void CommandCollapse(string[] args)
    {
        if (args.Length == 0)
        {
            Debug.Warning("Usage: collapse <on|off>");
            return;
        }
        collapseCheckbox.IsChecked = args[0].Equals("on", StringComparison.InvariantCultureIgnoreCase);
    }

    private void CommandAutoscroll(string[] args)
    {
        if (args.Length == 0)
        {
            Debug.Warning("Usage: autoscroll <on|off>");
            return;
        }
        autoscrollCheckbox.IsChecked = args[0].Equals("on", StringComparison.InvariantCultureIgnoreCase);
    }

    private static void CommandLog(string[] args)
    {
        if (args.Length == 0)
        {
            Debug.Warning("Usage: log <open|dir>");
            return;
        }
        switch (args[0].ToLowerInvariant())
        {
            case "open": Debug.OpenLogFile(); break;
            case "dir": Debug.OpenLogDirectory(); break;
            default: Debug.Warning($"Unknown log subcommand: '{args[0]}'"); break;
        }
    }

    #endregion
    #region compilationDiagnostics

    private void OnCompilationCompleted(ScriptCompileResult result) => Dispatcher.UIThread.Post(ReloadLogs);

    #endregion
    #region contextMenu

    private void AttachBackgroundContextMenu()
    {
        ContextMenu menu = new()
        {
            Background = EditorColor.FromRGB(68, 68, 68),
            BorderBrush = EditorColor.FromRGB(128, 128, 128),
        };
        menu.Items.Add(MakeMenuItem("Clear All", MaterialIconKind.Delete,
            () => ClearButton_Click(null, null!), Brushes.OrangeRed));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem("Copy All", MaterialIconKind.ContentCopy, CopyAllLogs, Brushes.White));
        menu.Items.Add(MakeMenuItem("Open Log File", MaterialIconKind.FileDocument, Debug.OpenLogFile, Brushes.White));
        menu.Items.Add(MakeMenuItem("Open Log Directory", MaterialIconKind.FolderOpen, Debug.OpenLogDirectory, Brushes.White));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem("Toggle Terminal View", MaterialIconKind.Terminal, ToggleView, Brushes.White));
        scrollViewer.ContextMenu = menu;
    }

    private static MenuItem MakeMenuItem(string header, MaterialIconKind icon, Action onClick, IBrush foreground)
    {
        MenuItem item = new()
        {
            Header = header,
            Icon = new MaterialIcon { Kind = icon, Width = 16, Height = 16 },
            Foreground = foreground,
        };
        item.Click += (_, _) => onClick();
        return item;
    }

    private async void CopyAllLogs()
    {
        try
        {
            IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null) return;

            // Logs first, then diagnostics, matching visual order
            List<string> lines = [.. Debug.Logs.Select(l => l.ToFileString())];

            ScriptCompileResult? last = ScriptCompilationPipeline.LastResult;
            if (last != null)
            {
                foreach (ScriptDiagnostic diag in last.Diagnostics)
                {
                    if (diag.Severity == ScriptDiagnosticSeverity.Info) continue;
                    string location = string.IsNullOrEmpty(diag.FilePath)
                        ? string.Empty
                        : $"{diag.FilePath}({diag.Line},{diag.Column}): ";
                    lines.Add($"[{diag.Severity}] {location}{diag.Message}");
                }
            }

            string all = string.Join(Environment.NewLine, lines);
            DataTransfer data = new DataTransfer();
            data.Add(DataTransferItem.CreateText(all));
            await clipboard.SetDataAsync(data);
            Debug.Info($"Copied {lines.Count} entries to clipboard");
        }
        catch (Exception ex)
        {
            Debug.Error($"Failed to copy logs: {ex.Message}");
        }
    }

    #endregion
    #region rendering

    private static string GetCollapseKey(LogEntry log) =>
        $"{log.CallerInfo}|{log.Message}|{log.Level}";

    /// <summary>
    /// Rebuilds the entire log list: regular Debug logs followed by the
    /// current pipeline diagnostics. Called on every event that could change
    /// the view - window load, compile completion, filter change, clear.
    /// </summary>
    private void ReloadLogs()
    {
        logList.Children.Clear();
        groupedLogs.Clear();

        if (collapseEnabled)
        {
            foreach (LogEntry log in Debug.Logs)
            {
                string key = GetCollapseKey(log);
                if (!groupedLogs.TryGetValue(key, out GroupedLogEntry? group))
                    groupedLogs[key] = new GroupedLogEntry { FirstLog = log, Count = 1 };
                else
                    group.Count++;
            }

            foreach (GroupedLogEntry group in groupedLogs.Values)
            {
                if (!ShouldShowLog(group.FirstLog)) continue;
                Border control = CreateLogControl(group.FirstLog, group.Count);
                group.Control = control;
                logList.Children.Add(control);
            }
        }
        else
        {
            lock (threadLock)
            {
                foreach (LogEntry log in Debug.Logs.ToArray())
                    if (ShouldShowLog(log))
                        logList.Children.Add(CreateLogControl(log, 1));
            }
        }

        RenderDiagnostics();
        if (autoScroll) Dispatcher.UIThread.Post(scrollViewer.ScrollToEnd, DispatcherPriority.Background);
    }

    private void RenderDiagnostics()
    {
        ScriptCompileResult? last = ScriptCompilationPipeline.LastResult;
        if (last == null) return;

        foreach (ScriptDiagnostic diag in last.Diagnostics)
        {
            if (diag.Severity == ScriptDiagnosticSeverity.Info) continue;
            if (!ShouldShowDiagnostic(diag)) continue;

            logList.Children.Add(currentView == ConsoleView.Terminal
                ? CreateTerminalDiagnosticLine(diag)
                : CreateVerboseDiagnosticControl(diag));
        }
    }

    private bool ShouldShowLog(LogEntry log)
    {
        bool matchesLevel = filterLogTypeBox.SelectedIndex == 0 || log.Level == (LogLevel)(filterLogTypeBox.SelectedIndex - 1);
        if (!matchesLevel) return false;

        if (!string.IsNullOrWhiteSpace(searchFilter))
        {
            return log.Message.Contains(searchFilter, StringComparison.OrdinalIgnoreCase)
                || log.Timestamp.ToString().Contains(searchFilter, StringComparison.OrdinalIgnoreCase)
                || log.Level.ToString().Contains(searchFilter, StringComparison.OrdinalIgnoreCase)
                || log.CallerInfo.Contains(searchFilter, StringComparison.OrdinalIgnoreCase);
        }
        return true;
    }

    private bool ShouldShowDiagnostic(ScriptDiagnostic diag)
    {
        LogLevel level = diag.Severity switch
        {
            ScriptDiagnosticSeverity.Error => LogLevel.Error,
            ScriptDiagnosticSeverity.Warning => LogLevel.Warning,
            _ => LogLevel.Info,
        };

        bool matchesLevel = filterLogTypeBox.SelectedIndex == 0 || level == (LogLevel)(filterLogTypeBox.SelectedIndex - 1);
        if (!matchesLevel) return false;
        if (!string.IsNullOrWhiteSpace(searchFilter))
        {
            return diag.Message.Contains(searchFilter, StringComparison.OrdinalIgnoreCase)
                || (diag.FilePath?.Contains(searchFilter, StringComparison.OrdinalIgnoreCase) ?? false)
                || level.ToString().Contains(searchFilter, StringComparison.OrdinalIgnoreCase);
        }
        return true;
    }

    private void ClearButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Clears only Debug.Logs diagnostics come from the pipeline and are not affected
        int diagCount = ScriptCompilationPipeline.LastResult?.Diagnostics
            .Count(d => d.Severity != ScriptDiagnosticSeverity.Info) ?? 0;
        Debug.ClearLogs();
        ReloadLogs();
    }

    private void Debug_OnLogUpdate(LogEntry entry) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (collapseEnabled)
            {
                string key = GetCollapseKey(entry);
                if (groupedLogs.TryGetValue(key, out GroupedLogEntry? group))
                {
                    group.Count++;
                    if (group.Control != null && ShouldShowLog(entry)) UpdateLogCount(group.Control, group.Count);
                }
                else if (ShouldShowLog(entry))
                {
                    GroupedLogEntry newGroup = new() { FirstLog = entry, Count = 1 };
                    Border control = CreateLogControl(entry, 1);
                    newGroup.Control = control;
                    groupedLogs[key] = newGroup;
                    logList.Children.Add(control);
                    if (autoScroll) scrollViewer.ScrollToEnd();
                }
            }
            else if (ShouldShowLog(entry))
            {
                logList.Children.Add(CreateLogControl(entry, 1));
                if (autoScroll) scrollViewer.ScrollToEnd();
            }
        });

    private static void UpdateLogCount(Border control, int count)
    {
        if (control.Child is not StackPanel mainPanel) return;
        foreach (Control child in mainPanel.Children)
        {
            if (child is not Grid grid) continue;
            foreach (Control gridChild in grid.Children)
            {
                if (gridChild is Border badge && badge.Classes.Contains("count-badge"))
                {
                    if (badge.Child is TextBlock text) text.Text = count.ToString();
                    return;
                }
            }
            return;
        }
    }

    private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        searchFilter = searchBox.Text?.Trim() ?? string.Empty;
        ReloadLogs();
    }

    private Border CreateLogControl(LogEntry log, int count) => currentView switch
    {
        ConsoleView.Terminal => CreateTerminalLine(log, count),
        _ => CreateVerboseLogControl(log, count),
    };

    private Border CreateTerminalLine(LogEntry log, int count)
    {
        IBrush lineColor = log.Level switch
        {
            LogLevel.Error => new SolidColorBrush(Color.FromRgb(240, 100, 100)),
            LogLevel.Warning => new SolidColorBrush(Color.FromRgb(230, 200, 90)),
            LogLevel.Info => new SolidColorBrush(Color.FromRgb(180, 200, 220)),
            _ => new SolidColorBrush(Color.FromRgb(150, 150, 150)),
        };

        string levelTag = log.Level switch
        {
            LogLevel.Error => "ERR",
            LogLevel.Warning => "WRN",
            LogLevel.Info => "INF",
            LogLevel.Debug => "DBG",
            _ => "LOG",
        };

        string countSuffix = count > 1 ? $" (×{count})" : "";

        Border wrapper = new()
        {
            Padding = new Thickness(6, 0, 6, 0),
            Background = Brushes.Transparent,
            Tag = log,
            Child = new SelectableTextBlock
            {
                Text = $"[{log.Timestamp:HH:mm:ss}] [{levelTag}] {log.Message}{countSuffix}",
                FontFamily = new FontFamily("Consolas, Menlo, monospace"),
                FontSize = 11.5,
                Foreground = lineColor,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
            },
        };

        ContextMenu lineMenu = new()
        {
            Background = EditorColor.FromRGB(68, 68, 68),
            BorderBrush = EditorColor.FromRGB(128, 128, 128),
        };
        MenuItem copyItem = new()
        {
            Header = "Copy Line",
            Icon = new MaterialIcon { Kind = MaterialIconKind.ContentCopy, Width = 14, Height = 14 },
            Foreground = Brushes.White,
        };
        copyItem.Click += async (_, _) =>
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null) return;
            var data = new Avalonia.Input.DataTransfer();
            data.Add(Avalonia.Input.DataTransferItem.CreateText(log.ToFileString()));
            await clipboard.SetDataAsync(data);
        };
        lineMenu.Items.Add(copyItem);

        lineMenu.Items.Add(new Separator());
        MenuItem deleteItem = new()
        {
            Header = "Delete",
            Icon = new MaterialIcon { Kind = MaterialIconKind.Delete, Width = 14, Height = 14 },
            Foreground = Brushes.OrangeRed,
        };
        deleteItem.Click += (_, _) => ClickDeleteButton(log);
        lineMenu.Items.Add(deleteItem);

        wrapper.ContextMenu = lineMenu;
        return wrapper;
    }

    private Border CreateVerboseLogControl(LogEntry log, int count)
    {
        bool isMultiLine = log.Message.Contains('\n') || log.Message.Length > 100;
        bool hasCallerInfo = !string.IsNullOrEmpty(log.CallerInfo);
        bool isGrouped = count > 1;

        Border logBorder = new()
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(10, 10, 10)),
            BorderThickness = new Thickness(0, 0, 2, 2),
            Padding = new Thickness(4),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(6, 2, 6, 2),
            Background = EditorColor.FromRGB(17, 17, 17),
            Tag = log,
        };

        StackPanel mainPanel = new() { Orientation = Orientation.Vertical, Spacing = 2 };

        Grid headerGrid = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                new ColumnDefinition(GridLength.Auto),
            },
        };

        Button? expandButton = null;
        if (isMultiLine)
        {
            expandButton = new Button
            {
                Content = new MaterialIcon { Kind = MaterialIconKind.ChevronRight, Width = 12, Height = 12, Foreground = Brushes.Gray },
                Background = Brushes.Transparent,
                Padding = new Thickness(2),
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                BorderThickness = new Thickness(0),
                Width = 20,
                Height = 20,
            };
            headerGrid.Children.Add(expandButton);
            Grid.SetColumn(expandButton, 0);
        }

        int columnOffset = isMultiLine ? 1 : 0;

        headerGrid.Children.Add(new TextBlock
        {
            Text = $"[{log.Timestamp.TimeOfDay:hh':'mm':'ss'.'fff}]",
            FontSize = 11,
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetColumn(headerGrid.Children[^1], columnOffset);

        headerGrid.Children.Add(new TextBlock
        {
            Text = $"[{log.Level}]",
            FontSize = 11,
            Foreground = GetLogColor(log.Level),
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetColumn(headerGrid.Children[^1], columnOffset + 1);

        if (isGrouped)
        {
            Border countBadge = new()
            {
                Classes = { "count-badge" },
                Background = EditorColor.FromRGB(68, 68, 68),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(6, 1),
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = count.ToString(), FontSize = 10, Foreground = Brushes.White, FontWeight = FontWeight.Medium },
            };
            headerGrid.Children.Add(countBadge);
            Grid.SetColumn(headerGrid.Children[^1], columnOffset + 2);
        }

        string displayMessage = log.Message;
        if (isMultiLine)
        {
            int firstNewline = log.Message.IndexOf('\n');
            if (firstNewline >= 0)
                displayMessage = string.Concat(log.Message.AsSpan(0, math.min(firstNewline, 100)), "...");
            else if (log.Message.Length > 100)
                displayMessage = string.Concat(log.Message.AsSpan(0, 100), "...");
        }

        headerGrid.Children.Add(new SelectableTextBlock
        {
            Text = displayMessage,
            FontSize = 11,
            Foreground = Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
        });
        Grid.SetColumn(headerGrid.Children[^1], columnOffset + 3);

        Button deleteButton = new()
        {
            Content = new MaterialIcon { Kind = MaterialIconKind.Delete, Width = 12, Height = 12, Foreground = EditorColor.FromRGB(200, 200, 200) },
            Background = EditorColor.FromRGB(10, 10, 10),
            Padding = new Thickness(2),
            Margin = new Thickness(4, 0, 0, 0),
            BorderBrush = EditorColor.FromRGB(28, 28, 28),
            BorderThickness = new Thickness(1, 1, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Width = 24,
            Height = 24,
        };
        deleteButton.Click += (_, _) => ClickDeleteButton(log);
        headerGrid.Children.Add(deleteButton);
        Grid.SetColumn(headerGrid.Children[^1], columnOffset + 4);

        mainPanel.Children.Add(headerGrid);

        if (hasCallerInfo)
        {
            mainPanel.Children.Add(new TextBlock
            {
                Text = $"└─ {log.CallerInfo}",
                FontSize = 10,
                Foreground = EditorColor.FromRGB(80, 80, 80),
                Margin = new Thickness(isMultiLine ? 24 : 0, 0, 0, 2),
                FontFamily = FontFamily.Parse("Consolas, Courier New, monospace"),
            });
        }

        if (isMultiLine)
        {
            Border expandedContent = new()
            {
                Background = EditorColor.FromRGB(6, 6, 6),
                BorderBrush = EditorColor.FromRGB(28, 28, 28),
                BorderThickness = new Thickness(1, 1, 0, 0),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 6),
                Margin = new Thickness(24, 4, 0, 0),
                IsVisible = false,
                Child = new SelectableTextBlock
                {
                    Text = log.Message,
                    FontSize = 11,
                    Foreground = Brushes.White,
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = FontFamily.Parse("Consolas, Courier New, monospace"),
                }
            };
            mainPanel.Children.Add(expandedContent);

            bool isExpanded = false;
            expandButton!.Click += (_, _) =>
            {
                isExpanded = !isExpanded;
                expandedContent.IsVisible = isExpanded;
                if (expandButton.Content is MaterialIcon icon)
                    icon.Kind = isExpanded ? MaterialIconKind.ChevronDown : MaterialIconKind.ChevronRight;
            };
        }

        logBorder.Child = mainPanel;
        return logBorder;
    }

    /// <summary>
    /// Compact terminal-style diagnostic line. No timestamp (diagnostics
    /// don't have one), file(line,col) prefix, colored left bar.
    /// </summary>
    private Border CreateTerminalDiagnosticLine(ScriptDiagnostic diag)
    {
        Color accent = diag.Severity switch
        {
            ScriptDiagnosticSeverity.Error => Color.FromRgb(240, 100, 100),
            ScriptDiagnosticSeverity.Warning => Color.FromRgb(230, 200, 90),
            _ => Color.FromRgb(150, 150, 150),
        };

        string levelTag = diag.Severity switch
        {
            ScriptDiagnosticSeverity.Error => "ERR",
            ScriptDiagnosticSeverity.Warning => "WRN",
            _ => "INF",
        };

        string location = string.IsNullOrEmpty(diag.FilePath)
            ? string.Empty
            : $"{Path.GetFileName(diag.FilePath)}({diag.Line},{diag.Column}): ";

        Border wrapper = new()
        {
            Padding = new Thickness(8, 1, 6, 1),
            Background = new SolidColorBrush(Color.FromArgb(24, accent.R, accent.G, accent.B)),
            BorderBrush = new SolidColorBrush(accent),
            BorderThickness = new Thickness(3, 0, 0, 0),
            Child = new SelectableTextBlock
            {
                Text = $"[{levelTag}] {location}{diag.Message}",
                FontFamily = new FontFamily("Consolas, Menlo, monospace"),
                FontSize = 11.5,
                Foreground = new SolidColorBrush(accent),
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
            },
        };

        AttachDiagnosticContextMenu(wrapper, diag);
        return wrapper;
    }

    /// <summary>
    /// Card-style diagnostic entry. Distinct from regular log cards: colored
    /// left edge, prominent file/line header, message body, no delete button.
    /// </summary>
    private Border CreateVerboseDiagnosticControl(ScriptDiagnostic diag)
    {
        Color accent = diag.Severity switch
        {
            ScriptDiagnosticSeverity.Error => Color.FromRgb(220, 80, 80),
            ScriptDiagnosticSeverity.Warning => Color.FromRgb(220, 180, 60),
            _ => Color.FromRgb(150, 150, 150),
        };
        string levelLabel = diag.Severity switch
        {
            ScriptDiagnosticSeverity.Error => "error",
            ScriptDiagnosticSeverity.Warning => "warning",
            _ => "info",
        };
        Border card = new()
        {
            Background = EditorColor.FromRGB(20, 20, 20),
            BorderBrush = new SolidColorBrush(accent),
            BorderThickness = new Thickness(3, 0, 1, 1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 6),
            Margin = new Thickness(6, 2, 6, 2),
        };

        StackPanel content = new() { Orientation = Orientation.Vertical, Spacing = 3 };
        StackPanel header = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
        header.Children.Add(new TextBlock
        {
            Text = levelLabel,
            FontFamily = new FontFamily("Consolas, Menlo, monospace"),
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(accent),
            VerticalAlignment = VerticalAlignment.Center,
        });

        if (!string.IsNullOrEmpty(diag.FilePath))
        {
            header.Children.Add(new TextBlock
            {
                Text = $"{Path.GetFileName(diag.FilePath)}({diag.Line},{diag.Column})",
                FontFamily = new FontFamily("Consolas, Menlo, monospace"),
                FontSize = 11,
                Foreground = EditorColor.FromRGB(160, 160, 160),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        content.Children.Add(header);
        content.Children.Add(new SelectableTextBlock
        {
            Text = diag.Message,
            FontSize = 12,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
        });

        card.Child = content;
        AttachDiagnosticContextMenu(card, diag);
        return card;
    }

    private void AttachDiagnosticContextMenu(Control target, ScriptDiagnostic diag)
    {
        ContextMenu menu = new()
        {
            Background = EditorColor.FromRGB(68, 68, 68),
            BorderBrush = EditorColor.FromRGB(128, 128, 128),
        };

        MenuItem copyItem = new()
        {
            Header = "Copy",
            Icon = new MaterialIcon { Kind = MaterialIconKind.ContentCopy, Width = 14, Height = 14 },
            Foreground = Brushes.White,
        };
        copyItem.Click += async (_, _) =>
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null) return;
            string location = string.IsNullOrEmpty(diag.FilePath)
                ? string.Empty
                : $"{diag.FilePath}({diag.Line},{diag.Column}): ";
            string text = $"{location}{diag.Severity.ToString().ToLowerInvariant()}: {diag.Message}";
            var data = new DataTransfer();
            data.Add(DataTransferItem.CreateText(text));
            await clipboard.SetDataAsync(data);
        };
        menu.Items.Add(copyItem);

        // No delete item - diagnostics can't be removed manually. They
        // disappear only when the next compile resolves them.
        target.ContextMenu = menu;
    }

    #endregion
    #region deleteButton

    /// <summary>
    /// Called when a log delete button is clicked. Only regular log entries
    /// reach this method - diagnostics don't have delete buttons.
    /// </summary>
    private void ClickDeleteButton(LogEntry logEntry)
    {
        if (collapseEnabled)
        {
            string key = GetCollapseKey(logEntry);
            if (groupedLogs.TryGetValue(key, out GroupedLogEntry? group))
            {
                if (group.Control != null) logList.Children.Remove(group.Control);
                groupedLogs.Remove(key);
            }

            List<LogEntry> toRemove = [.. Debug.Logs.Where(l => GetCollapseKey(l) == key)];
            foreach (LogEntry log in toRemove) Debug.ClearLog(log);
        }
        else
        {
            Debug.ClearLog(logEntry);
            foreach (Control child in logList.Children.ToArray())
            {
                if (child is Border border && ReferenceEquals(border.Tag, logEntry))
                {
                    logList.Children.Remove(border);
                    break;
                }
            }
        }
    }

    #endregion
    #region helpers

    private static IBrush GetLogColor(LogLevel level) => level switch
    {
        LogLevel.Debug => Brushes.White,
        LogLevel.Info => Brushes.White,
        LogLevel.Warning => Brushes.Yellow,
        LogLevel.Error => Brushes.Red,
        _ => Brushes.Green,
    };

    private static ComboBox BuildFilterDropdown()
    {
        static StackPanel MakeItem(MaterialIconKind icon, string label, IBrush? iconColor = null)
        {
            StackPanel panel = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
            panel.Children.Add(new MaterialIcon { Kind = icon, Foreground = iconColor ?? Brushes.White });
            panel.Children.Add(new TextBlock { Text = label });
            return panel;
        }

        return new ComboBox
        {
            Items =
            {
                new ComboBoxItem { Content = MakeItem(MaterialIconKind.AllInclusive, "All") },
                new ComboBoxItem { Content = MakeItem(MaterialIconKind.Info, "Info") },
                new ComboBoxItem { Content = MakeItem(MaterialIconKind.DebugStepOver, "Debug") },
                new ComboBoxItem { Content = MakeItem(MaterialIconKind.Warning, "Warning", EditorColor.FromRGB(200, 200, 0)) },
                new ComboBoxItem { Content = MakeItem(MaterialIconKind.Error, "Error", EditorColor.FromRGB(200, 0, 0)) },
            },
            SelectedIndex = 0,
            Foreground = Brushes.White,
            Background = EditorColor.FromRGB(17, 17, 17),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
    }

    #endregion
}
