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
    /// Used for specifying float4 fields as quaternions.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    public class RotationAttribute : Attribute
    {
        /// <summary>
        /// Whether to display in degrees or radians.
        /// </summary>
        public bool Degrees { get; set; } = true;

        /// <summary>
        /// Creates a new RotationAttribute with default settings.
        /// </summary>
        public RotationAttribute() { }

        /// <summary>
        /// Creates a new RotationAttribute with degrees parameter.
        /// </summary>
        /// <param name="degrees">Whether to display in degrees or radians</param>
        public RotationAttribute(bool degrees)
        {
            Degrees = degrees;
        }
    }
}
