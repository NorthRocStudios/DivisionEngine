//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.Nodes;
using Material.Icons;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace DivisionEngine.Editor.ViewModels
{
    /// <summary>
    /// One entry in the add-node catalog, built from a registered node type.
    /// </summary>
    public sealed class NodeCatalogEntryViewModel
    {
        internal NodeCatalogEntryViewModel(NodeTypeRegistration registration)
        {
            Registration = registration;
            Icon = Enum.TryParse(registration.Icon, ignoreCase: true, out MaterialIconKind parsed)
                ? parsed
                : MaterialIconKind.Shape;
            SearchText = $"{registration.MenuPath} {registration.DisplayName} {registration.Description}".ToLowerInvariant();
        }

        /// <summary>Registry metadata for the node type.</summary>
        public NodeTypeRegistration Registration { get; }

        /// <summary>Node type id.</summary>
        public string TypeId => Registration.TypeId;

        /// <summary>Full menu path.</summary>
        public string MenuPath => Registration.MenuPath;

        /// <summary>Display name.</summary>
        public string DisplayName => Registration.DisplayName;

        /// <summary>Top level category.</summary>
        public string Category => Registration.Category;

        /// <summary>Optional description.</summary>
        public string? Description => Registration.Description;

        /// <summary>Icon shown in menus and search results.</summary>
        public MaterialIconKind Icon { get; }

        /// <summary>Lower-cased text used for search filtering.</summary>
        public string SearchText { get; }

        /// <inheritdoc />
        public override string ToString() => MenuPath;
    }

    /// <summary>
    /// A top level category in the add-node catalog.
    /// </summary>
    public sealed class NodeCatalogGroupViewModel
    {
        internal NodeCatalogGroupViewModel(string name, IEnumerable<NodeCatalogEntryViewModel> entries)
        {
            Name = name;
            Entries = new ObservableCollection<NodeCatalogEntryViewModel>(entries);
        }

        /// <summary>Category name.</summary>
        public string Name { get; }

        /// <summary>Entries in this category, ordered by menu path.</summary>
        public ObservableCollection<NodeCatalogEntryViewModel> Entries { get; }
    }
}
