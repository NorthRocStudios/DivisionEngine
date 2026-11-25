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
    /// Used for specifying integer and float fields as sliders.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    public class RangeAttribute : Attribute
    {
        /// <summary>
        /// Minimum slider value.
        /// </summary>
        public float Min { get; }

        /// <summary>
        /// Maximum slider value.
        /// </summary>
        public float Max { get; }

        /// <summary>
        /// Applies a range slider.
        /// </summary>
        /// <param name="min">Minimum slider value</param>
        /// <param name="max">Maximum slider value</param>
        public RangeAttribute(float min, float max)
        {
            Min = min;
            Max = max;
        }
    }
}
