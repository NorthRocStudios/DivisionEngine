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
    /// Defines a maximum value for a float or int field.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    public class MaxAttribute : Attribute
    {
        /// <summary>
        /// Maximum value.
        /// </summary>
        public float Max { get; }

        /// <summary>
        /// Applies a maximum value to a field.
        /// </summary>
        /// <param name="max">Maximum value</param>
        public MaxAttribute(float max)
        {
            Max = max;
        }
    }
}
