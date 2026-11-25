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
    /// Displays a header on a field.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    public class HeaderAttribute : Attribute
    {
        /// <summary>
        /// Header text.
        /// </summary>
        public string Header { get; }

        /// <summary>
        /// Applies a header on a field.
        /// </summary>
        /// <param name="header">Header text</param>
        public HeaderAttribute(string header)
        {
            Header = header;
        }
    }
}
