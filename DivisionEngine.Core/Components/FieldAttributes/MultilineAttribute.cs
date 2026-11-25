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
    /// Allows text fields to wrap their text and have multiple lines.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    public class MultilineAttribute : Attribute
    {
        /// <summary>
        /// Allows text fields to wrap their text and have multiple lines.
        /// </summary>
        public MultilineAttribute() { }
    }
}
