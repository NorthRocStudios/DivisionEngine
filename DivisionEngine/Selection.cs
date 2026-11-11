//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using System;

namespace DivisionEngine.Editor
{
    /// <summary>
    /// Represents the type of object selected.
    /// </summary>
    public enum SelectionType
    {
        None, Entity, Asset, Other
    }

    /// <summary>
    /// Stores the current selection in the editor.
    /// </summary>
    public static class Selection
    {
        /// <summary>
        /// Called when the selection is changed.
        /// </summary>
        public static Action<object?>? OnSelectionChanged { get; set; }

        /// <summary>
        /// Selected object reference.
        /// </summary>
        public static object? SelectedObj { get; private set; }

        /// <summary>
        /// Selected type of object.
        /// </summary>
        public static SelectionType SelectedType { get; private set; } = SelectionType.None;

        /// <summary>
        /// Shortcut to get selected entity, or null if none exists.
        /// </summary>
        /// <remarks>Defaults to <see cref="uint.MaxValue"/>: 0 is a real entity ID, so a
        /// zero default would silently act on the first entity before anything is selected.</remarks>
        public static uint Entity { get; private set; } = uint.MaxValue;

        /// <summary>
        /// Shortcut to get selected asset, or null if none exists.
        /// </summary>
        public static string? Asset { get; private set; }

        /// <summary>
        /// Clears the current selection.
        /// </summary>
        public static void Clear()
        {
            Entity = uint.MaxValue;
            Asset = null;

            SelectedObj = null;
            SelectedType = SelectionType.None;
            OnSelectionChanged?.Invoke(null);
        }

        /// <summary>
        /// Selects the uint entity ID.
        /// </summary>
        /// <param name="entityID">ID of an entity</param>
        public static void SelectEntity(uint entityID)
        {
            Entity = entityID;
            Asset = null;

            SelectedObj = entityID;
            SelectedType = SelectionType.Entity;
            OnSelectionChanged?.Invoke(entityID);
        }

        /// <summary>
        /// Selects the GUID of an asset.
        /// </summary>
        /// <param name="assetID">Asset GUID</param>
        public static void SelectAsset(string assetID)
        {
            Entity = uint.MaxValue;
            Asset = assetID;

            SelectedObj = assetID;
            SelectedType = SelectionType.Asset;
            OnSelectionChanged?.Invoke(assetID);
        }

        /// <summary>
        /// Selects an object.
        /// </summary>
        /// <param name="selection">Selected object</param>
        public static void SelectObject(object selection)
        {
            Entity = uint.MaxValue;
            Asset = null;

            SelectedObj = selection;
            SelectedType = SelectionType.Asset;
            OnSelectionChanged?.Invoke(selection);
        }
    }
}
