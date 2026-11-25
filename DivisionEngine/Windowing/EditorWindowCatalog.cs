//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE.txt file in the project root
// for full license terms.
//
using DivisionEngine.Editor.ViewModels;
using Material.Icons;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DivisionEngine.Editor.Windowing
{
    /// <summary>
    /// Metadata for one editor window type: what to call it, where it opens by default,
    /// and which logical panel kinds accept it through the tab group's plus button.
    /// </summary>
    public sealed record EditorWindowCatalogEntry(
        Type ViewModelType,
        string Title,
        MaterialIconKind Icon,
        string[] AllowedZones,
        string DefaultZone,
        bool IsExample);

    /// <summary>
    /// Single source of truth for the View menu and for every tab group's add-window
    /// flyout. Kept static because floating windows need the same catalog as the main
    /// window.
    /// </summary>
    public static class EditorWindowCatalog
    {
        private static readonly string[] AnyZone = ["left", "center", "right", "bottom"];

        /// <summary>All window types the editor can host.</summary>
        public static IReadOnlyList<EditorWindowCatalogEntry> Entries { get; } = Build();

        /// <summary>Windows allowed in a tab group of the given logical zone.</summary>
        public static IEnumerable<EditorWindowCatalogEntry> ForZone(string zone) =>
            Entries.Where(entry => entry.AllowedZones.Contains(zone, StringComparer.OrdinalIgnoreCase));

        /// <summary>Finds an entry for a window view model instance.</summary>
        public static EditorWindowCatalogEntry? Find(EditorWindowViewModel viewModel) =>
            Entries.FirstOrDefault(entry => entry.ViewModelType == viewModel.GetType());

        /// <summary>Creates a new instance of a catalog window type.</summary>
        public static EditorWindowViewModel? Create(Type viewModelType) =>
            Activator.CreateInstance(viewModelType) as EditorWindowViewModel;

        /// <summary>Creates a fresh instance of the same concrete window type as <paramref name="viewModel"/>.</summary>
        public static EditorWindowViewModel? Duplicate(EditorWindowViewModel viewModel) =>
            Activator.CreateInstance(viewModel.GetType()) as EditorWindowViewModel;

        private static List<EditorWindowCatalogEntry> Build()
        {
            List<EditorWindowCatalogEntry> entries =
            [
                Entry<WorldWindowViewModel>(AnyZone, "left"),
                Entry<EnvironmentWindowViewModel>(AnyZone, "center"),
                Entry<AssetsWindowViewModel>(AnyZone, "bottom"),
                Entry<ConsoleWindowViewModel>(AnyZone, "bottom"),
                Entry<PropertiesWindowViewModel>(AnyZone, "right"),
                Entry<SettingsWindowViewModel>(AnyZone, "right"),
                Entry<DeveloperWindowViewModel>(AnyZone, "bottom"),
                Entry<SdfPlaygroundExampleViewModel>(["center"], "center", isExample: true),
            ];
            return entries;
        }

        private static EditorWindowCatalogEntry Entry<T>(string[] allowedZones, string defaultZone, bool isExample = false)
            where T : EditorWindowViewModel, new()
        {
            T window = new();
            return new EditorWindowCatalogEntry(typeof(T), window.Title, window.Icon, allowedZones, defaultZone, isExample);
        }
    }
}
