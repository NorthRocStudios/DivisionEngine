//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.Components;
using DivisionEngine.Projects;
using DivisionEngine.Rendering;

namespace DivisionEngine.Systems
{
    /// <summary>
    /// Renders screen-space icons for entities with specific components.
    /// </summary>
    public class IconRendererSystem : SystemBase
    {
        public static bool Enabled { get; set; } = true;

        private static readonly Dictionary<uint, (float3 pos, IconType icon, float3 dir)> iconsToRender = [];

        public override void AppStart()
        {
            ProjectManager.ProjectLoaded += ProjectManager_ProjectLoaded;
        }

        private void ProjectManager_ProjectLoaded()
        {
            RenderPipeline.Instance?.ClearIcons();
        }

        public override void EditorUpdate()
        {
            iconsToRender.Clear();
            if (!Enabled || RenderPipeline.Instance == null || EngineCore.IsInPlayMode || WorldManager.CurrentWorld == null)
            {
                RenderPipeline.Instance?.ClearIcons();
                return;
            }

            foreach (var entityId in W.Query<Transform>())
            {
                var transform = W.GetComponent<Transform>(entityId);
                if (transform == null) continue;

                IconType icon = IconDefinitions.GetIconForEntity(entityId, WorldManager.CurrentWorld);
                if (icon != IconType.None)
                {
                    float3 direction = float3.Zero;

                    // Get direction for directional lights
                    if (W.HasComponent<Components.Lights.DirectionalLight>(entityId))
                        direction = transform.Forward;

                    iconsToRender[entityId] = (transform.position, icon, direction);
                    RenderPipeline.Instance?.ShowIcon(transform.position, icon, direction, entityId);
                }
            }
        }
    }

    /// <summary>
    /// Represents the available editor icon types that can be rendered.
    /// </summary>
    public enum IconType : uint
    {
        None = 0,
        Camera = 100,
        DirectionalLight = 101,
        PointLight = 102,
        SpotLight = 103,
        Environment = 104,
    }

    public static class IconDefinitions
    {
        public static IconType GetIconForEntity(uint entityId, World world)
        {
            if (entityId != EditorCamera.EditorCameraId && world.HasComponent<Camera>(entityId)) return IconType.Camera;
            if (world.HasComponent<Components.Lights.DirectionalLight>(entityId)) return IconType.DirectionalLight;
            if (world.HasComponent<Components.Lights.PointLight>(entityId)) return IconType.PointLight;
            if (world.HasComponent<Components.Environment>(entityId)) return IconType.Environment;
            return IconType.None;
        }
    }
}
