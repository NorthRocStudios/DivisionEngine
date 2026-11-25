//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE.txt file in the project root
// for full license terms.
//
using Material.Icons;
using System;

namespace DivisionEngine.Editor.Tasks
{
    /// <summary>
    /// Represents a task in the editor.
    /// </summary>
    /// <remarks>
    /// Creates a new editor task object.
    /// </remarks>
    /// <param name="name">Name of task</param>
    /// <param name="description">Description of task</param>
    /// <param name="initProgress">Initial progress of task from 0 - 1</param>
    /// <param name="icon">Icon to represent task</param>
    internal class EditorTask(string name, string description, float initProgress = 0f, MaterialIconKind icon = MaterialIconKind.TaskAuto)
    {
        /// <summary>
        /// Name of this task.
        /// </summary>
        public string Name { get; set; } = name;

        /// <summary>
        /// ID of this task.
        /// </summary>
        public Guid Id { get; set; } = Guid.CreateVersion7();

        /// <summary>
        /// Description of this task.
        /// </summary>
        public string Description { get; set; } = description;

        /// <summary>
        /// Progress of this task.
        /// </summary>
        public float Progress { get; set; } = initProgress;

        /// <summary>
        /// Icon representing this task.
        /// </summary>
        public MaterialIconKind Icon { get; set; } = icon;

        /// <summary>
        /// Called when the task is completed.
        /// </summary>
        public Action? OnComplete { get; set; }

        /// <summary>
        /// If the task has been completed.
        /// </summary>
        public bool IsComplete => Progress >= 1.0f;
    }
}
