//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE file in the project root
// for full license terms.
//
using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using DivisionEngine.Editor.ViewModels;

namespace DivisionEngine.Editor
{
    /// <summary>
    /// Used for locating the view associated with a given ViewModel.
    /// </summary>
    public class ViewLocator : IDataTemplate
    {
        public Control? Build(object? param)
        {
            if (param is null) return null;
            string name = param.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
            Type? type = Type.GetType(name);

            if (type != null) return (Control)Activator.CreateInstance(type)!;
            return new TextBlock { Text = "Not Found: " + name };
        }

        public bool Match(object? data) => data is ViewModelBase;
    }
}
