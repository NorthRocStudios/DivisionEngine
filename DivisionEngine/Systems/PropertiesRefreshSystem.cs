//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using System;
using System.Collections.Generic;
using System.Reflection;

namespace DivisionEngine.Editor.Systems
{
    /// <summary>
    /// Allows the properties window to dynamically update component fields in the editor if entities are modified in the world.
    /// </summary>
    public class PropertiesRefreshSystem : SystemBase
    {
        public override int Priority => -100;

        private static uint lastSelectedEntity = uint.MaxValue;
        private static readonly HashSet<Type> componentsToRefresh = [];
        private static int framesSinceLastRefresh = 0;
        private static bool needsRefresh = false;

        public override void EditorUpdate()
        {
            if (lastSelectedEntity == uint.MaxValue) return;
            if (!needsRefresh && componentsToRefresh.Count == 0) return;

            // During a project transition, the world may not exist. Skip this tick
            // rather than letting the properties windows query a null world.
            if (WorldManager.CurrentWorld == null)
            {
                componentsToRefresh.Clear();
                needsRefresh = false;
                return;
            }

            framesSinceLastRefresh++;
            if (framesSinceLastRefresh >= 2)
            {
                framesSinceLastRefresh = 0;
                needsRefresh = false;

                HashSet<Type> refreshesToProcess = [.. componentsToRefresh];
                componentsToRefresh.Clear();

                foreach (Type compType in refreshesToProcess)
                    foreach (PropertiesWindow? window in PropertiesWindow.GetCurrentWindows())
                        window?.RefreshComponent(compType);
            }
        }

        public static void OnEntitySelected(uint entityId)
        {
            if (!W.EntityExists(entityId))
            {
                ClearSelection();
                return;
            }

            lastSelectedEntity = entityId;
            componentsToRefresh.Clear();
            needsRefresh = false;
            framesSinceLastRefresh = 0;
        }

        /// <summary>
        /// Call this to update the properties window for a specific component type.
        /// </summary>
        /// <param name="entityId">Entity to update properties for</param>
        /// <param name="componentType">Component type to update properties for</param>
        public static void OnFieldChanged(uint entityId, string? componentType)
        {
            if (entityId != lastSelectedEntity || string.IsNullOrEmpty(componentType)) return;

            Type? compType = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                compType = assembly.GetType(componentType);
                if (compType != null) break;
            }

            if (compType == null) return;
            componentsToRefresh.Add(compType);
            needsRefresh = true;
            framesSinceLastRefresh = 0;
        }

        public static void ClearSelection()
        {
            lastSelectedEntity = uint.MaxValue;
            componentsToRefresh.Clear();
            needsRefresh = false;
            framesSinceLastRefresh = 0;
        }
    }
}
