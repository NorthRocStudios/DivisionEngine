//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE.txt file in the project root
// for full license terms.
//
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using DivisionEngine.MathUtilities;

namespace DivisionEngine.Editor;

/// <summary>
/// A dialog that can be used to confirm or deny actions in Division Engine.
/// </summary>
public partial class ConfirmationDialog : Window
{
    public string Message { get; private set; } = "Are you sure?";

    public ConfirmationDialog(string title, string message)
    {
        InitializeComponent();
        Width = 350;
        Height = 150;

        Title = title;
        Message = message;

        BuildContent();
    }

    private void BuildContent()
    {
        StackPanel mainPanel = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 10,
        };
        TextBlock messageText = new TextBlock
        {
            Text = Message,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.White,
        };
        StackPanel buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
        };
        Button noButton = new Button
        {
            Content = "No",
            Width = 80,
            Background = EditorColor.FromRGB(60, 60, 60),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        noButton.Click += OnNoClicked;
        Button yesButton = new Button
        {
            Content = "Yes",
            Width = 80,
            Background = EditorColor.FromColor(ColorPalette.ForestGreen),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        yesButton.Click += OnYesClicked;

        buttonPanel.Children.Add(noButton);
        buttonPanel.Children.Add(yesButton);
        mainPanel.Children.Add(messageText);
        mainPanel.Children.Add(buttonPanel);

        // Add to window
        Border? border = this.FindControl<Border>("MainBorder");
        if (border != null) border.Child = mainPanel;
    }

    private void OnYesClicked(object? obj, RoutedEventArgs args) => Close(true);
    private void OnNoClicked(object? obj, RoutedEventArgs args) => Close(false);
}