//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using System;
using System.Reflection;

namespace DivisionEngine.Editor.Undo
{
    /// <summary>
    /// Records a change to a single component field, so property-panel edits take part in
    /// undo/redo instead of leaving Ctrl+Z to reverse an older structural action.
    /// </summary>
    public class ModifyFieldCommand : IUndoCommand
    {
        private readonly uint entityId;
        private readonly Type componentType;
        private readonly string fieldName;
        private readonly object? oldValue;
        private object? newValue;

        public ModifyFieldCommand(uint entityId, Type componentType, string fieldName,
            object? oldValue, object? newValue)
        {
            this.entityId = entityId;
            this.componentType = componentType;
            this.fieldName = fieldName;
            this.oldValue = oldValue;
            this.newValue = newValue;
        }

        public string Description => $"Modify field {fieldName}_{entityId}";

        public void Do()
        {
            SetField(newValue);
        }

        public void Undo()
        {
            SetField(oldValue);
        }

        /// <summary>
        /// Folds a follow-up edit of the same field into this command. Sliders and colour
        /// pickers fire continuously, so without this one drag would fill the undo history
        /// with hundreds of one-tick steps.
        /// </summary>
        internal bool MergeWith(ModifyFieldCommand next)
        {
            if (next.entityId != entityId) return false;
            if (next.componentType != componentType) return false;
            if (!string.Equals(next.fieldName, fieldName, StringComparison.Ordinal)) return false;
            if (Equals(newValue, next.newValue)) return false;

            newValue = next.newValue;
            return true;
        }

        private void SetField(object? value)
        {
            World? w = WorldManager.CurrentWorld;
            if (w == null || !w.EntityExists(entityId)) return;
            IComponent? comp = w.GetComponent(entityId, componentType);
            if (comp == null) return;
            FieldInfo? field = componentType.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            field?.SetValue(comp, value);
        }
    }
}
