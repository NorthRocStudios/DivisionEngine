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
    /// Defines a space between two fields.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    public class SpaceAttribute : Attribute
    {
        /// <summary>
        /// Amount of space.
        /// </summary>
        public float Space { get; } = 10f;

        /// <summary>
        /// Applies a space of 10.0 between fields.
        /// </summary>
        public SpaceAttribute() { }

        /// <summary>
        /// Applies a space between fields.
        /// </summary>
        /// <param name="min">Amount of space</param>
        public SpaceAttribute(float space)
        {
            Space = space;
        }
    }
}
