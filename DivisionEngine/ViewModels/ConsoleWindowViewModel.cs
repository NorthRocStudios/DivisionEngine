//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE.txt file in the project root
// for full license terms.
//
using Material.Icons;

namespace DivisionEngine.Editor.ViewModels
{
    public partial class ConsoleWindowViewModel : EditorWindowViewModel
    {
        public ConsoleWindowViewModel()
        {
            Title = "Console";
            Icon = MaterialIconKind.Console;
        }
    }
}
