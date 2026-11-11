//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.Editor.Settings;
using DivisionEngine.Projects;

namespace DivisionEngine.Editor.Systems
{
    /// <summary>
    /// Auto-saves projects if EditorSettings.AutoSave is enabled.
    /// </summary>
    internal class AutosaveProjectSystem : SystemBase
    {
        private int saveProjectTimer;

        public override void Awake()
        {
            // World start (not app start): give the timer a full interval before the first
            // save, so loading a world never triggers an immediate save of unloaded state.
            // 60 fps = save every 2 seconds, 30 fps = save every 4 seconds, etc.
            saveProjectTimer = 120;
        }

        public override void EditorUpdate()
        {
            if (EditorSettings.Instance.AutoSave && ProjectManager.IsCurrentLoaded && !EngineCore.IsInPlayMode)
            {
                saveProjectTimer--;
                if (saveProjectTimer < 1)
                {
                    ProjectManager.SaveCurrentProject();
                    saveProjectTimer = EditorSettings.Instance.AutoSaveInterval;
                }
            }
        }

        public override void AppExit()
        {
            if (EditorSettings.Instance.AutoSave && ProjectManager.IsCurrentLoaded && !EngineCore.IsInPlayMode)
                ProjectManager.SaveCurrentProject();
        }
    }
}
