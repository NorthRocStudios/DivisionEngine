//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE.txt file in the project root
// for full license terms.
//
using System.Text.Json.Serialization;

namespace DivisionEngine.Serialization
{
    /// <summary>
    /// Stores entity data for serializing project entities.
    /// </summary>
    public class EntityData
    {
        /// <summary>
        /// ID of entity.
        /// </summary>
        public uint Id { get; set; }

        /// <summary>
        /// Serialized list of components on entity.
        /// </summary>
        public List<ComponentData> Components { get; set; }

        [JsonConstructor]
        public EntityData() => Components = [];

        /// <summary>
        /// Creates serialized entity data.
        /// </summary>
        /// <param name="entity">Entity ID</param>
        /// <param name="world">World to pull from</param>
        public EntityData(uint entity, World world)
        {
            Id = entity;
            Components = [];
            List<IComponent> comps = world.GetAllComponents(entity);
            for (int i = 0; i < comps.Count; i++)
                Components.Add(new ComponentData(comps[i]));
        }
    }
}
