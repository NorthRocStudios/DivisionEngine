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
    /// Defines a minimum value for a float or int field.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    public class MinAttribute : Attribute
    {
        /// <summary>
        /// Minimum value.
        /// </summary>
        public float Min { get; }

        /// <summary>
        /// Applies a minimum value to a float or int field.
        /// </summary>
        /// <param name="min">Minimum value</param>
        public MinAttribute(float min)
        {
            Min = min;
        }
    }
}
