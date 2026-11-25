//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE.txt file in the project root
// for full license terms.
//
using Material.Icons;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DivisionEngine.Editor.Tasks
{
    /// <summary>
    /// Used for accessing the editor task system.
    /// </summary>
    internal static class EditorTaskManager
    {
        private static readonly ConcurrentDictionary<Guid, EditorTask> tasks = new();

        /// <summary>
        /// Called when tasks have been updated.
        /// </summary>
        public static event Action? TasksChanged;

        /// <summary>
        /// Creates a task in the task manager.
        /// </summary>
        /// <param name="name">Task display name</param>
        /// <param name="description">Description of task to display</param>
        /// <param name="initProgress">Initial progress value (0.0 - 1.0)</param>
        /// <param name="icon">Task icon to display</param>
        /// <returns>New editor task instance data</returns>
        public static EditorTask Create(string name, string description, float initProgress = 0f, MaterialIconKind icon = MaterialIconKind.TaskAuto)
        {
            EditorTask task = new EditorTask(name, description, initProgress, icon);
            tasks[task.Id] = task;
            TasksChanged?.Invoke();
            return task;
        }

        /// <summary>
        /// Updates a task in the task manager.
        /// </summary>
        /// <remarks>Creates a task if none exist already</remarks>
        /// <param name="id">Task GUID to update</param>
        /// <param name="progress">Progress value to set (0.0 - 1.0)</param>
        public static void Update(Guid id, float progress)
        {
            if (tasks.TryGetValue(id, out EditorTask? task))
            {
                task.Progress = progress;
                if (task.IsComplete) task.OnComplete?.Invoke(); // make sure this isnt called multiple times in the future
                TasksChanged?.Invoke();
            }
        }

        /// <summary>
        /// Checks if a task exists in the task manager.
        /// </summary>
        /// <param name="id">GUID of the task to check</param>
        /// <returns>If a task with this GUID exists</returns>
        public static bool TaskExists(Guid id) => tasks.ContainsKey(id);

        /// <summary>
        /// Complete a task in the task manager.
        /// </summary>
        /// <remarks>Functionally the same as "Update(id, 1)"</remarks>
        /// <param name="id">GUID of task to mark complete</param>
        public static void Complete(Guid id) => Update(id, 1);

        /// <summary>
        /// Gets an enumerable of all the editor tasks.
        /// </summary>
        /// <returns>Enumerable of all editor tasks</returns>
        public static IEnumerable<EditorTask> GetAll() => tasks.Values;

        /// <summary>
        /// Removes a task from the editor task manager.
        /// </summary>
        /// <param name="id">GUID of task to remove</param>
        /// <returns>If the task was successfully removed</returns>
        public static bool Remove(Guid id)
        {
            bool removed = tasks.TryRemove(id, out _);
            if (removed) TasksChanged?.Invoke(); // Make sure this line exists!
            return removed;
        }
    }
}
