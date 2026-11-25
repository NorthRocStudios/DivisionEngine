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
    /// Class used for serializing the world data to a project file.
    /// </summary>
    public class WorldData
    {
        /// <summary>
        /// Name of world.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Next entity ID in world.
        /// </summary>
        public uint NextEntityId { get; set; }

        /// <summary>
        /// Serialized entities list in from world data.
        /// </summary>
        public List<EntityData> Entities { get; set; }

        /// <summary>
        /// Shortcut for "new WorldData(WorldManager.CurrentWorld!)".
        /// </summary>
        [JsonIgnore] public static WorldData Current => new WorldData(WorldManager.CurrentWorld!);

        [JsonConstructor]
        public WorldData()
        {
            Name = string.Empty;
            Entities = [];
        }

        /// <summary>
        /// Builds serializable world data object automatically from ECS world.
        /// </summary>
        /// <param name="world">World object to serialize</param>
        public WorldData(World world)
        {
            Name = world.Name;
            NextEntityId = world.NextEntityId;
            Entities = [];
            foreach (uint entity in world.entities) Entities.Add(new EntityData(entity, world));
        }
    }
}
