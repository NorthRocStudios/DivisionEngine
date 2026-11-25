//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE.txt file in the project root
// for full license terms.
//
using DivisionEngine.Components.FieldAttributes;

namespace DivisionEngine.Components
{
    /// <summary>
    /// Special tag component allowing the naming of entities.
    /// </summary>
    public class Name : IComponent
    {
        /// <summary>
        /// Creates a new null name component.
        /// </summary>
        public Name()
        {
            name = null;
        }

        /// <summary>
        /// Builds a name component with name.
        /// </summary>
        /// <param name="name">Name to set component to</param>
        public Name(string name)
        {
            this.name = name;
        }

        /// <summary>
        /// Names an entity.
        /// </summary>
        /// <remarks>Used throughout the engine to apply readable identifiers to entities</remarks>
        [Tooltip("Name this object!")]
        public string? name = null;

        public IComponent Clone() => new Name
        {
            name = name,
        };
    }
}
