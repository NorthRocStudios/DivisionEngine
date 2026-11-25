//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE file in the project root
// for full license terms.
//
namespace DivisionEngine.Components.FieldAttributes
{
    /// <summary>
    /// Overrides the label the properties window shows for a field.
    /// </summary>
    /// <remarks>
    /// Field labels are normally derived from the field name, which cannot produce
    /// all-caps acronyms ("debugCTSS" becomes "Debug CTSS"). This attribute supplies
    /// the exact display text without renaming the field, so serialized projects and
    /// existing code keep working.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    public class DisplayNameAttribute : Attribute
    {
        /// <summary>
        /// Text shown next to the field in the properties window.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Creates a display-name override for a field.
        /// </summary>
        /// <param name="name">Text shown next to the field</param>
        public DisplayNameAttribute(string name)
        {
            Name = name;
        }
    }
}
