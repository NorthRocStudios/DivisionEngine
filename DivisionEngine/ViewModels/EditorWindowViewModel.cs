//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE.txt file in the project root
// for full license terms.
//
using Material.Icons;
using ReactiveUI;

namespace DivisionEngine.Editor.ViewModels
{
    /// <summary>
    /// Base class for all editor windows.
    /// </summary>
    public partial class EditorWindowViewModel : ViewModelBase
    {
        private string title = "Untitled";

        /// <summary>
        /// Title of this editor window.
        /// </summary>
        public string Title
        {
            get => title;
            set => this.RaiseAndSetIfChanged(ref title, value);
        }

        private MaterialIconKind icon = MaterialIconKind.DatabaseEdit;

        /// <summary>
        /// Icon of this editor window.
        /// </summary>
        public MaterialIconKind Icon
        {
            get => icon;
            set => this.RaiseAndSetIfChanged(ref icon, value);
        }
    }
}
