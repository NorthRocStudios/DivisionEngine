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
    public class RemoveComponentCommand(uint entityId, Type componentType, IComponent component) : IUndoCommand
    {
        private readonly IComponent component = component.Clone();

        public string Description => $"Remove component {componentType}_{entityId}";

        public void Do()
        {
            World? w = WorldManager.CurrentWorld;
            if (w != null && w.EntityExists(entityId)) w.RemoveComponent(entityId, componentType);
        }

        public void Undo()
        {
            World? w = WorldManager.CurrentWorld;
            if (w != null && w.EntityExists(entityId) && !w.HasComponent(entityId, componentType))
                w.AddComponent(entityId, component.Clone());
        }
    }
}
