//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE.txt file in the project root
// for full license terms.
//
namespace DivisionEngine.Components.FieldAttributes
{
    /// <summary>
    /// Used for hiding fields in the properties window.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    public class HideInEditorAttribute : Attribute
    {
        /// <summary>
        /// Creates a new HideInEditorAttribute with default settings.
        /// </summary>
        public HideInEditorAttribute() { }
    }
}
