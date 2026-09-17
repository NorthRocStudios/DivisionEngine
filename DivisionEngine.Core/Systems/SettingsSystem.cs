//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.Settings;

namespace DivisionEngine.Systems
{
    /// <summary>
    /// Initializes engine settings.
    /// </summary>
    internal class SettingsSystem : SystemBase
    {
        private int saveTimer;
        private const int SAVE_INTERVAL = 120; // Save every 120 frames (at 60 fps about 2 seconds; 30fps, 4s)
        private static bool hasChanges;

        public static Action? ApplySettings;

        public override void AppStart() => _ = EngineSettings.Instance; // ensure loaded

        public override void EditorUpdate()
        {
            if (!hasChanges) return;

            saveTimer += 1;
            if (saveTimer >= SAVE_INTERVAL)
            {
                SettingsManager.SaveAll();
                saveTimer = 0;
                hasChanges = false;
            }

            // Invoke apply settings callback
            ApplySettings?.Invoke();
        }

        public override void AppExit() => SettingsManager.SaveAll();

        /// <summary>
        /// Call this whenever a setting changes.
        /// </summary>
        public static void MarkDirty() => hasChanges = true;
    }
}
