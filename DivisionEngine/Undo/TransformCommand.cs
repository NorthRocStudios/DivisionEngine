//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.Components;

namespace DivisionEngine.Editor.Undo
{
    /// <summary>
    /// Records a transform gizmo drag as one undoable action covering position, rotation
    /// and scale, so Ctrl+Z after moving an object restores it instead of undoing whatever
    /// structural action came before.
    /// </summary>
    public class TransformCommand(
        uint entityId,
        float3 oldPosition,
        float4 oldRotation,
        float3 oldScaling,
        float3 newPosition,
        float4 newRotation,
        float3 newScaling) : IUndoCommand
    {
        public string Description => $"Transform entity {entityId}";

        public void Do() => Apply(newPosition, newRotation, newScaling);

        public void Undo() => Apply(oldPosition, oldRotation, oldScaling);

        private void Apply(float3 position, float4 rotation, float3 scaling)
        {
            World? world = WorldManager.CurrentWorld;
            if (world == null || !world.EntityExists(entityId)) return;

            Transform? transform = world.GetComponent<Transform>(entityId);
            if (transform == null) return;

            transform.position = position;
            transform.rotation = rotation;
            transform.scaling = scaling;
        }
    }
}
