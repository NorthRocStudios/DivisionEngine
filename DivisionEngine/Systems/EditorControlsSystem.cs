//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.Editor.Undo;
using DivisionEngine.Editor.ViewModels;
using DivisionEngine.Input;
using DivisionEngine.Projects;
using System.Collections.Generic;

namespace DivisionEngine.Editor.Systems
{
    /// <summary>
    /// Used to handle editor controls and shortcuts.
    /// </summary>
    internal class EditorControlsSystem : SystemBase
    {
        private readonly Dictionary<KeyCode, bool> previousKeyStates = new();

        public override void EditorUpdate()
        {
            // Keys we care about
            var keys = new[] { KeyCode.Delete, KeyCode.D, KeyCode.Z, KeyCode.Y, KeyCode.S };

            foreach (var key in keys)
            {
                bool current = InputSystem.IsPressed(key);
                bool previous = previousKeyStates.TryGetValue(key, out bool prev) && prev;
                bool justPressed = current && !previous;
                previousKeyStates[key] = current;

                if (!justPressed) continue;

                // Handle each key press
                switch (key)
                {
                    case KeyCode.Delete:
                        // Only ever act on an explicitly selected entity: Selection.Entity
                        // defaults to uint.MaxValue and an entity selection sets the type.
                        if (Selection.SelectedType == SelectionType.Entity && W.EntityExists(Selection.Entity))
                        {
                            var w = WorldManager.CurrentWorld;
                            if (w != null)
                            {
                                uint id = Selection.Entity;
                                UndoManager.Execute(new RemoveEntityCommand(id, w));
                                Selection.Clear();
                                MainWindowViewModel.vm!.RecentControlsText = $"(Delete) : Removed Entity {id}";
                            }
                        }
                        break;
                    case KeyCode.D:
                        if (InputSystem.IsCtrlPressed() && Selection.SelectedType == SelectionType.Entity &&
                            Selection.Entity != uint.MaxValue)
                        {
                            var w = WorldManager.CurrentWorld;
                            if (w != null && w.EntityExists(Selection.Entity))
                            {
                                UndoManager.Execute(new DuplicateEntityCommand(Selection.Entity, w));
                                MainWindowViewModel.vm!.RecentControlsText = $"(Ctrl + D) : Duplicate Entity {Selection.Entity}";
                            }
                        }
                        break;
                    case KeyCode.Z:
                        if (InputSystem.IsCtrlPressed())
                        {
                            UndoManager.Undo();
                            MainWindowViewModel.vm!.RecentControlsText = "(Ctrl + Z) : Undo";
                        }
                        break;
                    case KeyCode.Y:
                        if (InputSystem.IsCtrlPressed())
                        {
                            UndoManager.Redo();
                            MainWindowViewModel.vm!.RecentControlsText = "(Ctrl + Y) : Redo";
                        }
                        break;
                    case KeyCode.S:
                        if (InputSystem.IsCtrlPressed())
                        {
                            ProjectManager.SaveCurrentProject();
                            MainWindowViewModel.vm!.RecentControlsText = "(Ctrl + S) : Saved Project";
                        }
                        break;
                }
            }
        }
    }
}
