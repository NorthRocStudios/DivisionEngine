//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE file in the project root
// for full license terms.
//
namespace DivisionEngine
{
    /// <summary>
    /// Base interface for defining components.
    /// </summary>
    public interface IComponent
    {
        /// <summary>
        /// Creates a deep copy of the component.
        /// </summary>
        /// <returns>A new instance with the same values</returns>
        IComponent Clone();
    }
}
