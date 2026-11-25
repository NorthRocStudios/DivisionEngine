//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE file in the project root
// for full license terms.
//
using DivisionEngine.Components;
using System;
using System.Collections.Generic;

namespace DivisionEngine.Editor.Undo
{
    public class RemoveEntityCommand : IUndoCommand
    {
        private readonly uint entityId;
        private readonly Dictionary<Type, IComponent> components;
        private readonly string? entityName;

        public RemoveEntityCommand(uint entityId, World world)
        {
            this.entityId = entityId;
            components = world.GetClonedComponents(entityId);
            Name? nameComp = world.GetComponent<Name>(entityId);
            entityName = nameComp?.name;
        }

        public string Description => $"Remove entity {entityName}_{entityId}";

        public void Do()
        {
            var w = WorldManager.CurrentWorld;
            if (w != null && w.EntityExists(entityId))
                w.DestroyEntity(entityId);
        }

        public void Undo()
        {
            var w = WorldManager.CurrentWorld;
            if (w == null || w.EntityExists(entityId)) return;
            w.AddEntityWithId(entityId, entityName);
            foreach (var kv in components)
                w.AddComponent(entityId, kv.Value.Clone());
        }
    }
}
