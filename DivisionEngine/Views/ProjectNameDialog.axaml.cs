//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE file in the project root
// for full license terms.
//
using Avalonia.Controls;

namespace DivisionEngine.Editor;

/// <summary>
/// Used when creating a new project.
/// </summary>
public partial class ProjectNameDialog : Window
{
    /// <summary>
    /// Name of new project.
    /// </summary>
    public string ProjectName { get; set; } = "NewProject";

    /// <summary>
    /// Create a new project name dialog.
    /// </summary>
    public ProjectNameDialog()
    {
        InitializeComponent();
        DataContext = this;
        Width = 350;
        Height = 150;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }

    private void Ok_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(ProjectName);
    }

    private void Cancel_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(null);
    }
}