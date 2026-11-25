//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE.txt file in the project root
// for full license terms.
//
using System;

namespace DivisionEngine.Editor.Undo
{
    public class AddComponentCommand : IUndoCommand
    {
        private readonly uint entityId;
        private readonly Type componentType;
        private readonly IComponent component;

        public AddComponentCommand(uint entityId, IComponent component)
        {
            this.entityId = entityId;
            componentType = component.GetType();
            this.component = component.Clone();
        }

        public string Description => $"Add component {componentType}_{entityId}";

        public void Do()
        {
            var w = WorldManager.CurrentWorld;
            if (w != null && w.EntityExists(entityId) && !w.HasComponent(entityId, componentType))
                w.AddComponent(entityId, component.Clone());
        }

        public void Undo()
        {
            var w = WorldManager.CurrentWorld;
            if (w != null && w.EntityExists(entityId))
                w.RemoveComponent(entityId, componentType);
        }
    }
}
