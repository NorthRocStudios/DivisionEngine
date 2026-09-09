//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.Components;
using DivisionEngine.Components.Lights;
using DivisionEngine.Components.SDFs;
using DivisionEngine.Components.SDFs.Effects;
using DivisionEngine.Components.SDFs.Primitives;
using DivisionEngine.MathUtilities;
using DivisionEngine.Rendering;
using Environment = DivisionEngine.Components.Environment;
using Random = DivisionEngine.MathUtilities.Random;

namespace DivisionEngine
{
    /// <summary>
    /// Manages all worlds loaded and currently active.
    /// </summary>
    public static class WorldManager
    {
        /// <summary>
        /// Current active world.
        /// </summary>
        public static World? CurrentWorld { get; private set; }
        private static readonly Dictionary<string, World> worlds = [];

        /// <summary>
        /// Creates a new default world.
        /// </summary>
        /// <param name="makeCurrent">Makes the newly created default world the current world</param>
        /// <returns>The new default world</returns>
        public static World CreateDefaultWorld(bool makeCurrent)
        {
            World newDefaultWorld = new World("default");
            newDefaultWorld.RegisterAllSystems();

            // (Player) Camera
            uint cameraEntity = newDefaultWorld.CreateEntity("Camera");
            newDefaultWorld.AddComponent(cameraEntity, new Transform
            {
                position = new float3(0, 2, 7),
            });
            newDefaultWorld.AddComponent(cameraEntity, new Camera());
            newDefaultWorld.AddComponent(cameraEntity, new PostProcessing());
            newDefaultWorld.AddComponent(cameraEntity, new Player());

            // Environment
            uint environmentEntity = newDefaultWorld.CreateEntity("Environment");
            newDefaultWorld.AddComponent(environmentEntity, new Environment
            {
                skyType = Environment.SkyType.Gradient,
            });

            // Sun
            uint sunEntity = newDefaultWorld.CreateEntity("Sun");
            newDefaultWorld.AddComponent(sunEntity, new Transform
            {
                rotation = new float3(140f, -70f, 0f).EulerToQuaternion(),
            });
            newDefaultWorld.AddComponent(sunEntity, new DirectionalLight());

            // Spheres
            int sphereCount = 1;
            for (int i = 0; i < sphereCount; i++)
            {
                for (int j = 0; j < sphereCount; j++)
                {
                    uint sphereEntity = newDefaultWorld.CreateEntity("Sphere");
                    newDefaultWorld.AddComponent(sphereEntity, new Transform
                    {
                        position = new float3((i - sphereCount / 2) * 5, 1, (j - sphereCount / 2) * 5),
                    });
                    newDefaultWorld.AddComponent(sphereEntity, new SDFSphere
                    {
                        radius = 2f,
                    });
                    newDefaultWorld.AddComponent(sphereEntity, new SDFMaterial
                    {
                        albedoColor = ColorPalette.Khaki,
                        roughness = 0.1f, //1f - i / (float)(sphereCount - 1),
                        metallic = 1f, //1f - j / (float)(sphereCount - 1),
                        ior = 1.5f,
                    });
                    newDefaultWorld.AddComponent(sphereEntity, new Shadows());
                    newDefaultWorld.AddComponent(sphereEntity, new Reflections());
                    newDefaultWorld.AddComponent(sphereEntity, new Refractions
                    {
                        absorptionColor = new float4(0.4f, 0.7f, 0.8f, 0.06f),
                    });
                }
            }

            // Terrain
            uint terrainEntity = newDefaultWorld.CreateEntity("Terrain");
            newDefaultWorld.AddComponent(terrainEntity, new Transform
            {
                position = new float3(0, -30, 0),
            });
            newDefaultWorld.AddComponent(terrainEntity, new SDFTerrain());
            newDefaultWorld.AddComponent(terrainEntity, new SDFMaterial
            {
                albedoColor = ColorPalette.ForestGreen,
                stepBias = 0.85f,
                metallic = 0f,
                roughness = 1f,
            });
            newDefaultWorld.AddComponent(terrainEntity, new Shadows());

            // Rounded Box
            uint boxEntity = newDefaultWorld.CreateEntity("Rounded Box");
            newDefaultWorld.AddComponent(boxEntity, new Transform
            {
                position = new float3(5, 3, -5),
                rotation = Quaternion.CreateFromYawPitchRoll(Random.NextFloat(), Random.NextFloat(), Random.NextFloat()),
            });
            newDefaultWorld.AddComponent(boxEntity, new SDFRoundedBox
            {
                size = new float3(1f, 2f, 1f),
                bevel = 0.25f,
            });
            newDefaultWorld.AddComponent(boxEntity, new SDFMaterial
            {
                albedoColor = ColorPalette.Crimson,
            });
            newDefaultWorld.AddComponent(boxEntity, new Shadows());
            newDefaultWorld.AddComponent(boxEntity, new Reflections());

            // Create world
            SetWorld(newDefaultWorld);
            if (makeCurrent)
            {
                EngineCore.Stop();
                CurrentWorld = newDefaultWorld;
            }
            return newDefaultWorld;
        }

        /// <summary>
        /// Sets / adds a world based off its name.
        /// </summary>
        /// <param name="world">World to add / set</param>
        public static void SetWorld(World world)
        {
            if (!worlds.TryAdd(world.Name, world))
                worlds[world.Name] = world;
        }

        /// <summary>
        /// Checks if a world exists in the world manager.
        /// </summary>
        /// <param name="name">Name to check for</param>
        /// <returns>If world exists</returns>
        public static bool HasWorld(string name) => worlds.ContainsKey(name);

        /// <summary>
        /// Gets a world based off a certain name.
        /// </summary>
        /// <param name="name">Name of the world to retrieve</param>
        /// <returns>The world referenced by name</returns>
        public static World? GetWorld(string name)
        {
            worlds.TryGetValue(name, out var world);
            return world;
        }

        /// <summary>
        /// Switches the current world to the one referenced by a certain name.
        /// </summary>
        /// <param name="name">Name of the world to make current</param>
        /// <returns>Whether or not the switch was successful</returns>
        public static bool SwitchWorld(string name)
        {
            if (worlds.TryGetValue(name, out var world))
            {
                EngineCore.Stop();
                CurrentWorld = world;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Removes a world from the world manager.
        /// </summary>
        /// <param name="name">Name of the world to remove</param>
        /// <returns>Whether the world could be removed or not</returns>
        public static bool RemoveWorld(string name)
        {
            if (CurrentWorld == worlds[name]) return false;
            return worlds.Remove(name);
        }

        #region playModeSpecific

        /// <summary>
        /// Restores the current world from a backup without changing world instance.
        /// </summary>
        /// <param name="backupWorld">The backup world to restore from</param>
        public static void RestoreWorldState(World backupWorld)
        {
            if (CurrentWorld == null) return;

            bool wasRunning = EngineCore.IsRunning;
            if (wasRunning) EngineCore.Stop();
            CurrentWorld.RestoreFrom(backupWorld); // Restore state into existing world
            RenderPipeline.Instance?.BindCurrentWorld(); // Rebind renderer
            if (wasRunning) EngineCore.Start();

            Debug.Info($"Restored world state from: {backupWorld.Name}");
        }

        #endregion playModeSpecific
    }
}
