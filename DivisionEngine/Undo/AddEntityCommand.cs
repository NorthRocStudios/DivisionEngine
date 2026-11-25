//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE.txt file in the project root
// for full license terms.
//
using System;
using System.Collections.Generic;

namespace DivisionEngine.Editor.Undo
{
    /// <summary>
    /// Represents the command for adding entities in the undo/redo manager.
    /// </summary>
    /// <param name="entityId">Entity ID to add</param>
    /// <param name="components">Components on entity</param>
    /// <param name="name">Name of entity to add</param>
    public class AddEntityCommand(uint entityId, Dictionary<Type, IComponent> components, string? name = null) : IUndoCommand
    {
        public string Description => $"Add entity {name}_{entityId}";

        public void Do()
        {
            World? w = WorldManager.CurrentWorld;
            if (w == null) return;
            if (!w.EntityExists(entityId))
            {
                w.AddEntityWithId(entityId, name);
                foreach (var kv in components) w.AddComponent(entityId, kv.Value.Clone());
            }
        }

        public void Undo()
        {
            World? w = WorldManager.CurrentWorld;
            if (w == null) return;
            if (w.EntityExists(entityId)) w.DestroyEntity(entityId);
        }
    }
}
