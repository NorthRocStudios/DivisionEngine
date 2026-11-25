//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE file in the project root
// for full license terms.
//
namespace DivisionEngine.Editor.Systems
{
    /// <summary>
    /// Manages render window state (visible, focus, position, etc).
    /// </summary>
    public class RenderWindowManagementSystem : SystemBase
    {
        /// <summary>
        /// Editor window is in focus.
        /// </summary>
        public static bool EditorFocused { get; set; }

        /// <summary>
        /// Renderer window is in focus.
        /// </summary>
        public static bool RendererFocused { get; set; } = true;

        public override void AppStart()
        {
            App.AppFocused += (f) => EditorFocused = f;
            EntitySelectionSystem.CanSelect = true;
            EntitySelectionSystem.OnEntitySelected += Selection.SelectEntity;
            EntitySelectionSystem.OnNoEntityFound += () =>
            {
                Selection.Clear();
                PropertiesWindow.LoadWorldData(WorldManager.CurrentWorld);
            };
        }
    }
}
