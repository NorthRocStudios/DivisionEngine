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
    /// Used for specifying float4 fields as colors.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    public class ColorAttribute : Attribute
    {
        /// <summary>
        /// Whether or not to show the alpha (transparency) channel.
        /// </summary>
        /// <remarks>Only applies to float4</remarks>
        public bool ShowAlpha { get; set; } = true;

        /// <summary>
        /// Creates a new ColorAttribute with default settings.
        /// </summary>
        public ColorAttribute() { }

        /// <summary>
        /// Creates a new ColorAttribute with specified alpha visibility.
        /// </summary>
        /// <param name="showAlpha">Whether to show alpha channel</param>
        public ColorAttribute(bool showAlpha)
        {
            ShowAlpha = showAlpha;
        }
    }
}
