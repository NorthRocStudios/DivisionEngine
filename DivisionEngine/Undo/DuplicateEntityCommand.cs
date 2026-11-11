//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.Components;
using System;
using System.Collections.Generic;

namespace DivisionEngine.Editor.Undo
{
    public class DuplicateEntityCommand : IUndoCommand
    {
        private readonly uint sourceEntityId;
        private readonly uint newEntityId;
        private readonly Dictionary<Type, IComponent> clonedComponents;
        private readonly string? entityName;

        public DuplicateEntityCommand(uint sourceEntityId, World world)
        {
            this.sourceEntityId = sourceEntityId;
            clonedComponents = world.GetClonedComponents(sourceEntityId); // Clone all components from source
            Name? nameComp = world.GetComponent<Name>(sourceEntityId);
            entityName = nameComp?.name;
            newEntityId = world.NextEntityId; // reserve the ID but create it in Do
        }

        public string Description => $"Duplicate entity {sourceEntityId} → {newEntityId}";

        // The world is resolved when the command runs, like every other command: capturing it
        // here would let a redo write into a world that has since been replaced.
        public void Do()
        {
            World? world = WorldManager.CurrentWorld;
            if (world == null) return;

            if (!world.EntityExists(newEntityId)) // Create the duplicate entity with the reserved ID
            {
                world.AddEntityWithId(newEntityId, entityName);
                foreach (var kv in clonedComponents) world.AddComponent(newEntityId, kv.Value.Clone());
            }
        }

        public void Undo()
        {
            World? world = WorldManager.CurrentWorld;
            if (world == null) return;

            if (world.EntityExists(newEntityId)) world.DestroyEntity(newEntityId); // Remove the duplicated entity
        }
    }
}
